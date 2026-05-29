using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaudeTraceHub.Web.Models;
using Microsoft.Extensions.Options;

namespace ClaudeTraceHub.Web.Services;

public class CodeMergingService
{
    private readonly AzureDevOpsSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<CodeMergingService> _logger;
    private string ApiVersion => _settings.ApiVersion;

    private static readonly SemaphoreSlim _throttle = new(5, 5);

    public CodeMergingService(
        IOptionsSnapshot<AzureDevOpsSettings> settings,
        HttpClient httpClient,
        ILogger<CodeMergingService> logger)
    {
        _settings = settings.Value;
        _httpClient = httpClient;
        _logger = logger;

        if (_settings.IsConfigured)
        {
            _httpClient.BaseAddress = new Uri(_settings.OrganizationUrl.TrimEnd('/') + "/");
            var credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($":{_settings.PersonalAccessToken}"));
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", credentials);
        }
    }

    public bool IsConfigured => _settings.IsConfigured;

    /// <summary>
    /// Main entry point: fetch requirement-wise PR and commit data.
    /// All operations are READ-ONLY.
    /// </summary>
    public async Task<CodeMergingBundle> GetMergingDataAsync(
        string project, string team, string iterationPath,
        IEnumerable<string>? selectedRepoNames = null, bool deliverableOnly = true,
        Dictionary<string, string>? repoTargetBranchFilters = null)
    {
        var bundle = new CodeMergingBundle
        {
            IterationPath = iterationPath,
            TeamName = team
        };

        if (!_settings.IsConfigured)
        {
            bundle.ErrorMessage = "Azure DevOps is not configured. Go to Settings to configure.";
            return bundle;
        }

        try
        {
            // Step 1: Get team's area paths
            var teamAreaPaths = await GetTeamAreaPathsAsync(project, team);

            // Step 2: WIQL query for Requirements/User Stories under the iteration
            var workItemIds = await ExecuteWiqlForRequirementsAsync(project, iterationPath, teamAreaPaths, deliverableOnly);
            if (workItemIds.Count == 0)
            {
                bundle.ErrorMessage = "No requirements found in this iteration for the selected team.";
                return bundle;
            }

            // Step 3: Fetch requirement details
            var requirements = await FetchRequirementDetailsAsync(project, workItemIds);

            // Step 4: Fetch repos for filtering
            var repos = await GetRepositoriesAsync(project);
            var selectedRepoSet = selectedRepoNames?.ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Filter repos to only selected ones for PR search
            var searchRepos = selectedRepoSet != null && selectedRepoSet.Count > 0
                ? repos.Where(r => selectedRepoSet.Contains(r.Name)).ToList()
                : repos;

            // Step 5: Pre-fetch all PRs from selected repos (much faster than per-requirement)
            var allPRsByRepo = new Dictionary<string, List<(int PrId, string Title, string SourceBranch, string Json)>>();
            foreach (var repo in searchRepos)
            {
                var repoPrs = await FetchAllPullRequestsInRepoAsync(project, repo.Id);
                allPRsByRepo[repo.Id] = repoPrs;
            }

            var repoLookup = repos.ToDictionary(r => r.Id, r => r.Name, StringComparer.OrdinalIgnoreCase);

            // Step 6: For each requirement, find PRs via relations (primary) + branch name search (fallback)
            var lockObj = new object();
            var tasks = requirements.Select(async req =>
            {
                await _throttle.WaitAsync();
                try
                {
                    var prs = new List<PullRequestInfo>();
                    var seenPrIds = new HashSet<int>();

                    // Approach 1 (Primary): Get PRs from work item relations (direct link, always accurate)
                    var relationPrs = await GetLinkedPullRequestsAsync(project, req.WorkItemId, repos);
                    foreach (var rpr in relationPrs)
                    {
                        if (seenPrIds.Add(rpr.PullRequestId))
                            prs.Add(rpr);
                    }

                    // Approach 2 (Fallback): Search PRs by work item ID in branch name or PR title
                    var idStr = req.WorkItemId.ToString();
                    foreach (var (repoId, repoPrs) in allPRsByRepo)
                    {
                        foreach (var (prId, title, sourceBranch, prJson) in repoPrs)
                        {
                            if (seenPrIds.Contains(prId)) continue;

                            if (sourceBranch.Contains(idStr) || title.Contains(idStr))
                            {
                                var pr = ParsePullRequestFromJson(prJson, repoId, repoLookup);
                                if (pr != null && seenPrIds.Add(prId))
                                    prs.Add(pr);
                            }
                        }
                    }

                    // Filter by selected repos
                    if (selectedRepoSet != null && selectedRepoSet.Count > 0)
                        prs = prs.Where(pr => selectedRepoSet.Contains(pr.RepositoryName)).ToList();

                    // Filter by target branch per repo (e.g., FX-CPFOIA-Migration→main, CasepointARA→dev-qa-net8)
                    if (repoTargetBranchFilters != null && repoTargetBranchFilters.Count > 0)
                    {
                        prs = prs.Where(pr =>
                        {
                            if (repoTargetBranchFilters.TryGetValue(pr.RepositoryName, out var expectedTarget))
                                return pr.TargetBranch.Equals(expectedTarget, StringComparison.OrdinalIgnoreCase);
                            return true; // no filter for this repo, keep all
                        }).ToList();
                    }

                    // Fetch commits for each PR
                    foreach (var pr in prs)
                    {
                        pr.Commits = await FetchPrCommitsAsync(project, pr.RepositoryId, pr.PullRequestId);
                        var orgUrl = _settings.OrganizationUrl.TrimEnd('/');
                        foreach (var c in pr.Commits)
                            c.CommitUrl = $"{orgUrl}/{Uri.EscapeDataString(project)}/_git/{Uri.EscapeDataString(pr.RepositoryName)}/commit/{c.CommitId}";
                    }

                    // Order PRs by their first activity date (earliest commit, or creation date as fallback)
                    // so the user sees PRs in the order work actually started on the requirement.
                    var orderedPrs = prs
                        .OrderBy(pr => pr.FirstActivityDate)
                        .ThenBy(pr => pr.PullRequestId)
                        .ToList();

                    lock (lockObj)
                    {
                        req.PullRequests = orderedPrs;
                    }
                }
                finally
                {
                    _throttle.Release();
                }
            });

            await Task.WhenAll(tasks);

            bundle.Requirements = requirements.OrderBy(r => r.AssignedTo).ThenBy(r => r.WorkItemId).ToList();
            bundle.TotalRequirements = bundle.Requirements.Count;
            bundle.TotalPRs = bundle.Requirements.Sum(r => r.TotalPRs);
            bundle.TotalCommits = bundle.Requirements.Sum(r => r.TotalCommits);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching code merging data");
            bundle.ErrorMessage = $"Error: {ex.Message}";
        }

        return bundle;
    }

    /// <summary>
    /// Verify if PR commits exist on a target branch (supports cherry-pick). READ-ONLY operation.
    /// Checks each individual commit from the PR against the target branch.
    /// </summary>
    public async Task VerifyMergeToTargetAsync(CodeMergingBundle bundle, string project, string targetBranch)
    {
        bundle.TargetBranchName = targetBranch;
        bundle.MergedToTargetCount = 0;
        bundle.NotMergedToTargetCount = 0;

        // First, fetch all commits on the target branch for each repo (batched, efficient)
        var repoIds = bundle.Requirements.SelectMany(r => r.PullRequests)
            .Where(pr => pr.Status == "completed")
            .Select(pr => pr.RepositoryId)
            .Distinct()
            .ToList();

        // For each repo, get recent commit messages on the target branch
        // Cherry-picks create new SHAs but keep the same commit message
        var targetBranchCommits = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var repoId in repoIds)
        {
            var commits = await FetchBranchCommitMessagesAsync(project, repoId, targetBranch);
            targetBranchCommits[repoId] = commits;
        }

        var allPRs = bundle.Requirements.SelectMany(r => r.PullRequests)
            .Where(pr => pr.Status == "completed")
            .ToList();

        var lockObj = new object();
        var tasks = allPRs.Select(async pr =>
        {
            await _throttle.WaitAsync();
            try
            {
                var merged = await EvaluatePrMergeStatusAsync(pr, project, targetBranch, targetBranchCommits);

                lock (lockObj)
                {
                    pr.IsMergedToTargetBranch = merged;
                    if (merged) bundle.MergedToTargetCount++;
                    else bundle.NotMergedToTargetCount++;
                }
            }
            finally
            {
                _throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Determines whether a PR is merged to the target branch and records the status of each
    /// individual commit on <see cref="CommitInfo.IsMergedToTargetBranch"/>. READ-ONLY.
    ///
    /// A commit is considered present on the target when its SHA matches (normal merge) or its
    /// message matches (cherry-picks create a new SHA but keep the message). Auto-generated
    /// "Merge remote-tracking branch …" commits are optional: they don't block the PR's merged
    /// state, so a PR counts as merged once every other commit is present — even if the
    /// merge-tracking commit is absent.
    /// </summary>
    private async Task<bool> EvaluatePrMergeStatusAsync(
        PullRequestInfo pr, string project, string targetBranch,
        IReadOnlyDictionary<string, HashSet<string>> targetBranchCommits)
    {
        // PR already targets the verification branch → everything on it is merged by definition.
        if (pr.TargetBranch.Equals(targetBranch, StringComparison.OrdinalIgnoreCase) ||
            pr.TargetBranch.Equals($"refs/heads/{targetBranch}", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var c in pr.Commits) c.IsMergedToTargetBranch = true;
            return true;
        }

        if (pr.Commits.Count > 0 && targetBranchCommits.TryGetValue(pr.RepositoryId, out var targetMessages))
        {
            var allRequiredMerged = true;
            foreach (var c in pr.Commits)
            {
                var present = targetMessages.Contains(c.CommitId) ||
                              targetMessages.Contains(c.Message.Trim());
                c.IsMergedToTargetBranch = present;

                if (!present && !c.IsMergeTrackingCommit)
                    allRequiredMerged = false;
            }
            return allRequiredMerged;
        }

        bool merged;
        if (!string.IsNullOrEmpty(pr.LastMergeCommitId))
            merged = await CheckCommitOnBranchAsync(project, pr.RepositoryId, pr.LastMergeCommitId, targetBranch);
        else
            merged = false;

        foreach (var c in pr.Commits) c.IsMergedToTargetBranch = merged;
        return merged;
    }

    /// <summary>
    /// Fetch commit SHAs and messages from a branch. READ-ONLY.
    /// Returns a set containing both SHAs and trimmed commit messages for matching.
    /// </summary>
    private async Task<HashSet<string>> FetchBranchCommitMessagesAsync(string project, string repoId, string branchName)
    {
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var encodedProject = Uri.EscapeDataString(project);
            var encodedBranch = Uri.EscapeDataString(branchName);
            // Fetch last 1000 commits on the target branch
            var url = $"{encodedProject}/_apis/git/repositories/{repoId}/commits?" +
                      $"searchCriteria.itemVersion.version={encodedBranch}&" +
                      $"searchCriteria.itemVersion.versionType=branch&" +
                      $"$top=1000&api-version={ApiVersion}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return results;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var c in values.EnumerateArray())
                {
                    var sha = GetJsonString(c, "commitId");
                    var msg = GetJsonString(c, "comment").Trim();
                    if (!string.IsNullOrEmpty(sha)) results.Add(sha);
                    if (!string.IsNullOrEmpty(msg)) results.Add(msg);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching commits from branch {Branch} in repo {RepoId}", branchName, repoId);
        }

        return results;
    }

    /// <summary>
    /// Re-verify merge status for a single requirement's PRs. Used for live refresh on row expansion.
    /// </summary>
    public async Task VerifyMergeForRequirementAsync(
        CodeMergingBundle bundle, string project, string targetBranch, int workItemId)
    {
        if (string.IsNullOrEmpty(targetBranch)) return;

        bundle.TargetBranchName = targetBranch;
        var req = bundle.Requirements.FirstOrDefault(r => r.WorkItemId == workItemId);
        if (req == null) return;

        var completedPrs = req.PullRequests.Where(pr => pr.Status == "completed").ToList();
        var repoIds = completedPrs.Select(pr => pr.RepositoryId).Distinct().ToList();

        var targetBranchCommits = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var repoId in repoIds)
        {
            targetBranchCommits[repoId] = await FetchBranchCommitMessagesAsync(project, repoId, targetBranch);
        }

        foreach (var pr in completedPrs)
        {
            pr.IsMergedToTargetBranch =
                await EvaluatePrMergeStatusAsync(pr, project, targetBranch, targetBranchCommits);
        }

        var allCompleted = bundle.Requirements.SelectMany(r => r.PullRequests)
            .Where(p => p.Status == "completed")
            .ToList();
        bundle.MergedToTargetCount = allCompleted.Count(p => p.IsMergedToTargetBranch == true);
        bundle.NotMergedToTargetCount = allCompleted.Count(p => p.IsMergedToTargetBranch == false);
    }

    /// <summary>
    /// Returns sorted, deduplicated branch names across the supplied repos (by name). READ-ONLY.
    /// </summary>
    public async Task<List<string>> GetBranchesAsync(string project, IEnumerable<string> repoNames)
    {
        var nameSet = repoNames?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new();
        if (nameSet.Count == 0) return new List<string>();

        var repos = await GetRepositoriesAsync(project);
        var filteredRepos = repos.Where(r => nameSet.Contains(r.Name)).ToList();

        var tasks = filteredRepos.Select(async r =>
        {
            await _throttle.WaitAsync();
            try { return await FetchBranchesForRepoAsync(project, r.Id); }
            finally { _throttle.Release(); }
        });
        var lists = await Task.WhenAll(tasks);

        return lists.SelectMany(l => l)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<List<string>> FetchBranchesForRepoAsync(string project, string repoId)
    {
        var branches = new List<string>();
        try
        {
            var encodedProject = Uri.EscapeDataString(project);
            string? continuationToken = null;
            const int maxPages = 20; // Hard ceiling — 20 * 1000 = 20K branches.

            for (var page = 0; page < maxPages; page++)
            {
                var url = $"{encodedProject}/_apis/git/repositories/{repoId}/refs?" +
                          $"filter=heads&$top=1000&api-version={ApiVersion}";
                if (!string.IsNullOrEmpty(continuationToken))
                    url += $"&continuationToken={Uri.EscapeDataString(continuationToken)}";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) break;

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("value", out var values))
                {
                    foreach (var v in values.EnumerateArray())
                    {
                        var name = GetJsonString(v, "name").Replace("refs/heads/", "");
                        if (!string.IsNullOrEmpty(name))
                            branches.Add(name);
                    }
                }

                continuationToken = response.Headers.TryGetValues("x-ms-continuationtoken", out var tokens)
                    ? tokens.FirstOrDefault()
                    : null;
                if (string.IsNullOrEmpty(continuationToken)) break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching branches for repo {RepoId}", repoId);
        }
        return branches;
    }

    /// <summary>
    /// Gets list of repositories in a project. READ-ONLY.
    /// </summary>
    public async Task<List<(string Id, string Name)>> GetRepositoriesAsync(string project)
    {
        var repos = new List<(string Id, string Name)>();
        try
        {
            var encodedProject = Uri.EscapeDataString(project);
            var url = $"{encodedProject}/_apis/git/repositories?api-version={ApiVersion}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch repositories: HTTP {StatusCode}", (int)response.StatusCode);
                return repos;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var repo in values.EnumerateArray())
                {
                    var id = repo.TryGetProperty("id", out var idP) ? idP.GetString() ?? "" : "";
                    var name = repo.TryGetProperty("name", out var nameP) ? nameP.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(name))
                        repos.Add((id, name));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching repositories for {Project}", project);
        }

        return repos.OrderBy(r => r.Name).ToList();
    }

    // --- Private Methods ---

    private async Task<List<(string Path, bool IncludeChildren)>> GetTeamAreaPathsAsync(string project, string team)
    {
        var areaPaths = new List<(string Path, bool IncludeChildren)>();
        try
        {
            var encodedProject = Uri.EscapeDataString(project);
            var encodedTeam = Uri.EscapeDataString(team);
            var url = $"{encodedProject}/{encodedTeam}/_apis/work/teamsettings/teamfieldvalues?api-version={ApiVersion}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch team area paths for {Team}: HTTP {StatusCode}",
                    team, (int)response.StatusCode);
                return areaPaths;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("values", out var values))
            {
                foreach (var val in values.EnumerateArray())
                {
                    var path = val.TryGetProperty("value", out var pathProp) ? pathProp.GetString() ?? "" : "";
                    var includeChildren = val.TryGetProperty("includeChildren", out var incProp) && incProp.GetBoolean();
                    if (!string.IsNullOrEmpty(path))
                        areaPaths.Add((path, includeChildren));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching team area paths for {Team}", team);
        }

        return areaPaths;
    }

    private async Task<List<int>> ExecuteWiqlForRequirementsAsync(
        string project, string iterationPath,
        List<(string Path, bool IncludeChildren)> teamAreaPaths, bool deliverableOnly)
    {
        var encodedProject = Uri.EscapeDataString(project);
        var url = $"{encodedProject}/_apis/wit/wiql?api-version={ApiVersion}";

        // Bugs and Change Requests never need a 'Deliverable' tag — they're treated
        // as always-relevant alongside tagged Requirements/User Stories/PBIs.
        var workItemTypeFilter = deliverableOnly
            ? "([System.WorkItemType] IN ('Requirement', 'User Story', 'Product Backlog Item') AND [System.Tags] CONTAINS 'Deliverable') OR ([System.WorkItemType] IN ('Bug', 'Change Request'))"
            : "[System.WorkItemType] IN ('Requirement', 'User Story', 'Product Backlog Item', 'Bug', 'Change Request')";

        var conditions = new List<string>
        {
            $"({workItemTypeFilter})",
            $"[System.IterationPath] UNDER '{iterationPath}'"
        };

        if (teamAreaPaths.Count > 0)
        {
            var areaClauses = teamAreaPaths.Select(ap =>
                ap.IncludeChildren
                    ? $"[System.AreaPath] UNDER '{ap.Path}'"
                    : $"[System.AreaPath] = '{ap.Path}'");
            conditions.Add($"({string.Join(" OR ", areaClauses)})");
        }

        var wiql = $"SELECT [System.Id] FROM WorkItems WHERE {string.Join(" AND ", conditions)} ORDER BY [System.AssignedTo] ASC";

        var requestBody = JsonSerializer.Serialize(new { query = wiql });
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync(url, content);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("WIQL query failed: HTTP {StatusCode} - {Body}", (int)response.StatusCode, body);
            throw new InvalidOperationException($"WIQL query failed: HTTP {(int)response.StatusCode}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var ids = new List<int>();
        if (doc.RootElement.TryGetProperty("workItems", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp))
                    ids.Add(idProp.GetInt32());
            }
        }

        return ids;
    }

    private async Task<List<RequirementMergingRow>> FetchRequirementDetailsAsync(string project, List<int> ids)
    {
        var allItems = new List<RequirementMergingRow>();
        var fields = "System.Id,System.Title,System.State,System.AssignedTo,System.WorkItemType";
        var orgUrl = _settings.OrganizationUrl.TrimEnd('/');

        const int batchSize = 200;
        for (var i = 0; i < ids.Count; i += batchSize)
        {
            var batch = ids.Skip(i).Take(batchSize).ToList();
            var idsParam = string.Join(",", batch);
            var url = $"_apis/wit/workitems?ids={idsParam}&fields={fields}&api-version={ApiVersion}";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch work items batch: HTTP {StatusCode}", (int)response.StatusCode);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("value", out var valueArray))
                {
                    foreach (var wi in valueArray.EnumerateArray())
                    {
                        var f = wi.GetProperty("fields");
                        var id = GetIntField(f, "System.Id");
                        allItems.Add(new RequirementMergingRow
                        {
                            WorkItemId = id,
                            Title = GetStringField(f, "System.Title"),
                            State = GetStringField(f, "System.State"),
                            AssignedTo = GetAssignedToName(f),
                            WorkItemType = GetStringField(f, "System.WorkItemType"),
                            WorkItemUrl = $"{orgUrl}/{Uri.EscapeDataString(project)}/_workitems/edit/{id}"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching work items batch starting at index {Index}", i);
            }
        }

        return allItems;
    }

    private async Task<List<PullRequestInfo>> GetLinkedPullRequestsAsync(
        string project, int workItemId, List<(string Id, string Name)> repos)
    {
        var prs = new List<PullRequestInfo>();
        try
        {
            var url = $"_apis/wit/workitems/{workItemId}?$expand=relations&api-version={ApiVersion}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch relations for work item {Id}: HTTP {StatusCode}",
                    workItemId, (int)response.StatusCode);
                return prs;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("relations", out var relations))
                return prs;

            var repoLookup = repos.ToDictionary(r => r.Id, r => r.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var rel in relations.EnumerateArray())
            {
                var relType = rel.TryGetProperty("rel", out var relProp) ? relProp.GetString() ?? "" : "";
                var artifactUrl = rel.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";

                // Check attribute name for "Pull Request" as well (some TFS versions use different rel types)
                var attrName = "";
                if (rel.TryGetProperty("attributes", out var attrs) &&
                    attrs.TryGetProperty("name", out var nameProp))
                    attrName = nameProp.GetString() ?? "";

                // Accept ArtifactLink or any link with PullRequest in the URL or attribute
                if (relType != "ArtifactLink" &&
                    !attrName.Contains("Pull Request", StringComparison.OrdinalIgnoreCase) &&
                    !artifactUrl.Contains("PullRequest", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Try to extract PR info from various artifact URL formats
                // Format 1: vstfs:///Git/PullRequestId/{projectGuid}/{repoGuid}/{prId}
                // Format 2: vstfs:///Git/PullRequestId/{projectGuid}%2F{repoGuid}%2F{prId}
                // Format 3: vstfs:///CodeReview/CodeReviewId/{projectGuid}/{prId}
                var decoded = Uri.UnescapeDataString(artifactUrl);

                string? repoGuid = null;
                int prId = 0;

                if (decoded.Contains("PullRequestId", StringComparison.OrdinalIgnoreCase))
                {
                    // Find the PullRequestId segment and parse what follows
                    var idx = decoded.IndexOf("PullRequestId", StringComparison.OrdinalIgnoreCase);
                    var remainder = decoded[(idx + "PullRequestId".Length)..].TrimStart('/');
                    var segments = remainder.Split('/', StringSplitOptions.RemoveEmptyEntries);

                    if (segments.Length >= 3)
                    {
                        // {projectGuid}/{repoGuid}/{prId}
                        repoGuid = segments[1];
                        int.TryParse(segments[2], out prId);
                    }
                    else if (segments.Length >= 2)
                    {
                        // {repoGuid}/{prId} or {projectGuid}/{prId}
                        int.TryParse(segments[^1], out prId);
                        if (segments.Length > 1)
                            repoGuid = segments[^2];
                    }
                    else if (segments.Length == 1)
                    {
                        int.TryParse(segments[0], out prId);
                    }
                }

                if (prId == 0) continue;

                // Try fetching PR details - if repoGuid is known, use it directly
                // Otherwise try all repos
                if (!string.IsNullOrEmpty(repoGuid))
                {
                    var pr = await FetchPullRequestDetailsAsync(project, repoGuid, prId, repoLookup);
                    if (pr != null)
                        prs.Add(pr);
                }
                else
                {
                    // Try each repo to find the PR
                    foreach (var repo in repos)
                    {
                        var pr = await FetchPullRequestDetailsAsync(project, repo.Id, prId, repoLookup);
                        if (pr != null)
                        {
                            prs.Add(pr);
                            break;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching linked PRs for work item {Id}", workItemId);
        }

        return prs;
    }

    /// <summary>
    /// Fetch all PRs (completed + active) from a repo. Returns raw JSON for later parsing.
    /// </summary>
    private async Task<List<(int PrId, string Title, string SourceBranch, string Json)>>
        FetchAllPullRequestsInRepoAsync(string project, string repoId)
    {
        var results = new List<(int, string, string, string)>();
        try
        {
            var encodedProject = Uri.EscapeDataString(project);

            // Fetch completed PRs (merged)
            foreach (var status in new[] { "completed", "active" })
            {
                var url = $"{encodedProject}/_apis/git/repositories/{repoId}/pullrequests?" +
                          $"searchCriteria.status={status}&$top=500&api-version={ApiVersion}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch {Status} PRs from repo {RepoId}: HTTP {StatusCode}",
                        status, repoId, (int)response.StatusCode);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("value", out var values))
                {
                    foreach (var pr in values.EnumerateArray())
                    {
                        var prId = pr.TryGetProperty("pullRequestId", out var idP) ? idP.GetInt32() : 0;
                        var title = GetJsonString(pr, "title");
                        var sourceBranch = GetJsonString(pr, "sourceRefName").Replace("refs/heads/", "");

                        if (prId > 0)
                            results.Add((prId, title, sourceBranch, pr.GetRawText()));
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching PRs from repo {RepoId}", repoId);
        }

        return results;
    }

    /// <summary>
    /// Parse a PR from cached JSON string.
    /// </summary>
    private PullRequestInfo? ParsePullRequestFromJson(string prJson, string repoId, Dictionary<string, string> repoLookup)
    {
        try
        {
            var root = JsonDocument.Parse(prJson).RootElement;

            var sourceBranch = GetJsonString(root, "sourceRefName").Replace("refs/heads/", "");
            var targetBranch = GetJsonString(root, "targetRefName").Replace("refs/heads/", "");
            var status = GetJsonString(root, "status");
            var prId = root.TryGetProperty("pullRequestId", out var idP) ? idP.GetInt32() : 0;

            string? mergeCommitId = null;
            if (root.TryGetProperty("lastMergeCommit", out var mc) &&
                mc.TryGetProperty("commitId", out var mcId))
                mergeCommitId = mcId.GetString();

            string? mergeSourceCommitId = null;
            if (root.TryGetProperty("lastMergeSourceCommit", out var msc) &&
                msc.TryGetProperty("commitId", out var mscId))
                mergeSourceCommitId = mscId.GetString();

            var createdBy = "";
            if (root.TryGetProperty("createdBy", out var creator))
                createdBy = creator.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";

            DateTime.TryParse(GetJsonString(root, "creationDate"), out var creationDate);
            DateTime? closedDate = null;
            var closedStr = GetJsonString(root, "closedDate");
            if (!string.IsNullOrEmpty(closedStr) && DateTime.TryParse(closedStr, out var cd))
                closedDate = cd.ToLocalTime();

            var repoName = repoLookup.TryGetValue(repoId, out var rn) ? rn : repoId;
            var orgUrl = _settings.OrganizationUrl.TrimEnd('/');
            var prUrl = $"{orgUrl}/{Uri.EscapeDataString("")}/{Uri.EscapeDataString(repoName)}/pullrequest/{prId}";

            // Try to build a proper PR URL
            if (root.TryGetProperty("repository", out var repoObj))
            {
                var repoNameFromJson = repoObj.TryGetProperty("name", out var rnj) ? rnj.GetString() ?? repoName : repoName;
                if (repoObj.TryGetProperty("project", out var projObj))
                {
                    var projName = projObj.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "";
                    prUrl = $"{orgUrl}/{Uri.EscapeDataString(projName)}/_git/{Uri.EscapeDataString(repoNameFromJson)}/pullrequest/{prId}";
                }
            }

            return new PullRequestInfo
            {
                PullRequestId = prId,
                Title = GetJsonString(root, "title"),
                SourceBranch = sourceBranch,
                TargetBranch = targetBranch,
                Status = status,
                CreatedBy = createdBy,
                CreationDate = creationDate.ToLocalTime(),
                ClosedDate = closedDate,
                RepositoryName = repoName,
                RepositoryId = repoId,
                LastMergeCommitId = mergeCommitId,
                LastMergeSourceCommitId = mergeSourceCommitId,
                PullRequestUrl = prUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error parsing PR JSON for repo {RepoId}", repoId);
            return null;
        }
    }

    private async Task<PullRequestInfo?> FetchPullRequestDetailsAsync(
        string project, string repoId, int prId, Dictionary<string, string> repoLookup)
    {
        try
        {
            var encodedProject = Uri.EscapeDataString(project);
            var url = $"{encodedProject}/_apis/git/repositories/{repoId}/pullrequests/{prId}?api-version={ApiVersion}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch PR {PrId} from repo {RepoId}: HTTP {StatusCode}",
                    prId, repoId, (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var sourceBranch = GetJsonString(root, "sourceRefName").Replace("refs/heads/", "");
            var targetBranch = GetJsonString(root, "targetRefName").Replace("refs/heads/", "");
            var status = GetJsonString(root, "status");

            string? mergeCommitId = null;
            if (root.TryGetProperty("lastMergeCommit", out var mergeCommit) &&
                mergeCommit.TryGetProperty("commitId", out var mcId))
                mergeCommitId = mcId.GetString();

            string? mergeSourceCommitId = null;
            if (root.TryGetProperty("lastMergeSourceCommit", out var mergeSrc) &&
                mergeSrc.TryGetProperty("commitId", out var mscId))
                mergeSourceCommitId = mscId.GetString();

            var createdBy = "";
            if (root.TryGetProperty("createdBy", out var creator))
                createdBy = creator.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "" : "";

            DateTime.TryParse(GetJsonString(root, "creationDate"), out var creationDate);
            DateTime? closedDate = null;
            var closedStr = GetJsonString(root, "closedDate");
            if (!string.IsNullOrEmpty(closedStr) && DateTime.TryParse(closedStr, out var cd))
                closedDate = cd.ToLocalTime();

            var repoName = repoLookup.TryGetValue(repoId, out var rn) ? rn : repoId;

            // Build PR URL
            var orgUrl = _settings.OrganizationUrl.TrimEnd('/');
            var prUrl = $"{orgUrl}/{Uri.EscapeDataString(project)}/_git/{Uri.EscapeDataString(repoName)}/pullrequest/{prId}";

            return new PullRequestInfo
            {
                PullRequestId = prId,
                Title = GetJsonString(root, "title"),
                SourceBranch = sourceBranch,
                TargetBranch = targetBranch,
                Status = status,
                CreatedBy = createdBy,
                CreationDate = creationDate.ToLocalTime(),
                ClosedDate = closedDate,
                RepositoryName = repoName,
                RepositoryId = repoId,
                LastMergeCommitId = mergeCommitId,
                LastMergeSourceCommitId = mergeSourceCommitId,
                PullRequestUrl = prUrl
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching PR {PrId} from repo {RepoId}", prId, repoId);
            return null;
        }
    }

    private async Task<List<CommitInfo>> FetchPrCommitsAsync(string project, string repoId, int prId)
    {
        var commits = new List<CommitInfo>();
        try
        {
            var encodedProject = Uri.EscapeDataString(project);
            var url = $"{encodedProject}/_apis/git/repositories/{repoId}/pullRequests/{prId}/commits?api-version={ApiVersion}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch commits for PR {PrId}: HTTP {StatusCode}",
                    prId, (int)response.StatusCode);
                return commits;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("value", out var values))
            {
                foreach (var c in values.EnumerateArray())
                {
                    var commitId = GetJsonString(c, "commitId");
                    var comment = GetJsonString(c, "comment");

                    var author = "";
                    DateTime authorDate = default;
                    if (c.TryGetProperty("author", out var authorObj))
                    {
                        author = authorObj.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                        if (authorObj.TryGetProperty("date", out var ad) &&
                            DateTime.TryParse(ad.GetString(), out var dt))
                            authorDate = dt.ToLocalTime();
                    }

                    commits.Add(new CommitInfo
                    {
                        CommitId = commitId,
                        Message = comment,
                        Author = author,
                        AuthorDate = authorDate
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching commits for PR {PrId}", prId);
        }

        // Oldest commit first so the UI displays a natural chronological order.
        return commits.OrderBy(c => c.AuthorDate).ToList();
    }

    /// <summary>
    /// Check if a specific commit exists on a target branch. READ-ONLY.
    /// </summary>
    private async Task<bool> CheckCommitOnBranchAsync(string project, string repoId, string commitId, string targetBranch)
    {
        try
        {
            var encodedProject = Uri.EscapeDataString(project);
            var encodedBranch = Uri.EscapeDataString(targetBranch);
            var url = $"{encodedProject}/_apis/git/repositories/{repoId}/commits?" +
                      $"searchCriteria.itemVersion.version={encodedBranch}&" +
                      $"searchCriteria.itemVersion.versionType=branch&" +
                      $"searchCriteria.ids={commitId}&" +
                      $"api-version={ApiVersion}";

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                // Try alternative: check if commit is ancestor via merge base comparison
                return await CheckCommitReachableAsync(project, repoId, commitId, targetBranch);
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("count", out var countProp))
                return countProp.GetInt32() > 0;

            if (doc.RootElement.TryGetProperty("value", out var values))
                return values.GetArrayLength() > 0;

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking commit {CommitId} on branch {Branch}", commitId, targetBranch);
            return false;
        }
    }

    /// <summary>
    /// Fallback: check if commit is reachable from a branch via the diffs API. READ-ONLY.
    /// </summary>
    private async Task<bool> CheckCommitReachableAsync(string project, string repoId, string commitId, string targetBranch)
    {
        try
        {
            var encodedProject = Uri.EscapeDataString(project);
            var encodedBranch = Uri.EscapeDataString(targetBranch);
            // Use diffs/commits to check reachability
            var url = $"{encodedProject}/_apis/git/repositories/{repoId}/diffs/commits?" +
                      $"baseVersionType=commit&baseVersion={commitId}&" +
                      $"targetVersionType=branch&targetVersion={encodedBranch}&" +
                      $"api-version={ApiVersion}";

            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            // If behindCount is 0, the commit is reachable from the target branch
            if (doc.RootElement.TryGetProperty("behindCount", out var behind))
                return behind.GetInt32() == 0;

            return false;
        }
        catch
        {
            return false;
        }
    }

    // --- JSON Helper Methods ---

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? "";
        return "";
    }

    private static string GetStringField(JsonElement fields, string fieldName)
    {
        if (fields.TryGetProperty(fieldName, out var prop))
            return prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? "" : prop.ToString();
        return "";
    }

    private static int GetIntField(JsonElement fields, string fieldName)
    {
        if (fields.TryGetProperty(fieldName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetInt32();
            if (int.TryParse(prop.ToString(), out var val)) return val;
        }
        return 0;
    }

    private static string GetAssignedToName(JsonElement fields)
    {
        if (!fields.TryGetProperty("System.AssignedTo", out var prop)) return "Unassigned";

        if (prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? "Unassigned";

        if (prop.ValueKind == JsonValueKind.Object && prop.TryGetProperty("displayName", out var name))
            return name.GetString() ?? "Unassigned";

        return "Unassigned";
    }
}
