using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ClaudeTraceHub.Web.Models;
using Microsoft.Extensions.Options;

namespace ClaudeTraceHub.Web.Services;

public class AzureDevOpsService
{
    private readonly AzureDevOpsSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AzureDevOpsService> _logger;
    private string ApiVersion => _settings.ApiVersion;

    public AzureDevOpsService(
        IOptionsSnapshot<AzureDevOpsSettings> settings,
        HttpClient httpClient,
        ILogger<AzureDevOpsService> logger)
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

    public async Task<TfsQueryResult> GetWorkItemsForBranchAsync(string branchName)
    {
        var result = new TfsQueryResult { BranchName = branchName };

        if (!_settings.IsConfigured)
        {
            result.ErrorMessage = "Azure DevOps is not configured. Add OrganizationUrl, PersonalAccessToken, and Projects to appsettings.json under 'AzureDevOps'.";
            return result;
        }

        try
        {
            // Step 1: Check if branch is already linked to work items via Pull Requests
            result.DiscoveryNotes.Add("Step 1: Searching pull requests for linked work items...");
            var prWorkItems = await GetWorkItemsFromPullRequestsAsync(branchName, result);
            result.WorkItemsFromPullRequests.AddRange(prWorkItems);

            if (prWorkItems.Count > 0)
            {
                result.Path = DiscoveryPath.LinkedViaPullRequest;
                result.DiscoveryNotes.Add($"Found {prWorkItems.Count} work item(s) linked via pull requests.");
                result.Success = true;
                return result;
            }

            result.DiscoveryNotes.Add("No work items found via pull requests.");

            // Step 2: No PR-linked items — try extracting work item ID from branch name
            result.DiscoveryNotes.Add("Step 2: Extracting work item ID from branch name...");
            var ids = ExtractWorkItemIds(branchName);
            result.ExtractedWorkItemIds = ids;

            if (ids.Count > 0)
            {
                result.DiscoveryNotes.Add($"Extracted ID(s) from branch name: {string.Join(", ", ids)}");
                var items = await GetWorkItemsByIdsAsync(ids, result);
                result.WorkItemsFromBranch.AddRange(items);

                if (items.Count > 0)
                {
                    result.Path = DiscoveryPath.ExtractedFromBranchName;
                    result.DiscoveryNotes.Add($"Found {items.Count} work item(s) by ID from branch name.");
                    result.Success = true;
                    return result;
                }

                result.DiscoveryNotes.Add("Extracted IDs did not match any existing work items in TFS.");
            }
            else
            {
                result.DiscoveryNotes.Add("No numeric work item ID could be extracted from the branch name.");
            }

            // Step 3: Nothing found
            result.Path = DiscoveryPath.NotFound;
            result.DiscoveryNotes.Add("No work items are associated with this branch.");
            result.Success = true;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP error querying Azure DevOps for branch {Branch}", branchName);
            result.ErrorMessage = $"Error connecting to Azure DevOps: {ex.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying Azure DevOps for branch {Branch}", branchName);
            result.ErrorMessage = $"Unexpected error: {ex.Message}";
        }

        return result;
    }

    public List<int> ExtractWorkItemIds(string branchName)
    {
        var ids = new HashSet<int>();
        var cleanBranch = branchName
            .Replace("refs/heads/", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        foreach (var pattern in _settings.BranchWorkItemPatterns)
        {
            try
            {
                var matches = Regex.Matches(cleanBranch, pattern, RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1 && int.TryParse(match.Groups[1].Value, out var id))
                        ids.Add(id);
                }
                if (ids.Count > 0) break;
            }
            catch (RegexParseException ex)
            {
                _logger.LogWarning(ex, "Invalid regex pattern: {Pattern}", pattern);
            }
        }

        return ids.ToList();
    }

    private async Task<List<TfsWorkItem>> GetWorkItemsByIdsAsync(List<int> ids, TfsQueryResult result)
    {
        if (ids.Count == 0) return new();

        var items = new List<TfsWorkItem>();
        var idsParam = string.Join(",", ids);
        var fields = "System.Id,System.Title,System.WorkItemType,System.State,System.AssignedTo,System.TeamProject";
        var url = $"_apis/wit/workitems?ids={idsParam}&fields={fields}&api-version={ApiVersion}";

        try
        {
            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                result.DiscoveryNotes.Add($"Work item(s) {idsParam} not found in TFS (404).");
                // Try fetching individually — some may exist
                if (ids.Count > 1)
                {
                    foreach (var id in ids)
                    {
                        var single = await GetSingleWorkItemAsync(id);
                        if (single != null) items.Add(single);
                        else result.DiscoveryNotes.Add($"Work item #{id} does not exist.");
                    }
                }
                return items;
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                result.DiscoveryNotes.Add($"TFS API returned {(int)response.StatusCode} for work item lookup. {TruncateForNote(body)}");
                return items;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("value", out var valueArray))
            {
                foreach (var wi in valueArray.EnumerateArray())
                    items.Add(ParseWorkItem(wi));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching work items by IDs: {Ids}", idsParam);
            result.DiscoveryNotes.Add($"Error fetching work items: {ex.Message}");
        }

        return items;
    }

    private async Task<TfsWorkItem?> GetSingleWorkItemAsync(int id)
    {
        try
        {
            var fields = "System.Id,System.Title,System.WorkItemType,System.State,System.AssignedTo,System.TeamProject";
            var url = $"_apis/wit/workitems/{id}?fields={fields}&api-version={ApiVersion}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            return ParseWorkItem(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<TfsWorkItem>> GetWorkItemsFromPullRequestsAsync(string branchName, TfsQueryResult result)
    {
        var allWorkItems = new List<TfsWorkItem>();
        var cleanBranch = branchName.Replace("refs/heads/", "", StringComparison.OrdinalIgnoreCase);
        var refName = $"refs/heads/{cleanBranch}";
        var encodedRef = Uri.EscapeDataString(refName);

        foreach (var project in _settings.Projects)
        {
            try
            {
                var encodedProject = Uri.EscapeDataString(project);

                // List repos in this project
                var reposUrl = $"{encodedProject}/_apis/git/repositories?api-version={ApiVersion}";
                var reposResponse = await _httpClient.GetAsync(reposUrl);
                if (!reposResponse.IsSuccessStatusCode)
                {
                    var statusCode = (int)reposResponse.StatusCode;
                    result.DiscoveryNotes.Add($"Could not list repos in project '{project}' (HTTP {statusCode}).");
                    continue;
                }

                var reposJson = await reposResponse.Content.ReadAsStringAsync();
                var reposDoc = JsonDocument.Parse(reposJson);
                if (!reposDoc.RootElement.TryGetProperty("value", out var repos)) continue;

                var repoCount = 0;
                foreach (var repo in repos.EnumerateArray()) repoCount++;
                result.DiscoveryNotes.Add($"Searching {repoCount} repo(s) in project '{project}'...");

                foreach (var repo in repos.EnumerateArray())
                {
                    var repoId = repo.GetProperty("id").GetString();

                    // Search PRs by source branch
                    var prUrl = $"{encodedProject}/_apis/git/repositories/{repoId}/pullrequests" +
                                $"?searchCriteria.sourceRefName={encodedRef}&searchCriteria.status=all&api-version={ApiVersion}";
                    var prResponse = await _httpClient.GetAsync(prUrl);
                    if (!prResponse.IsSuccessStatusCode) continue;

                    var prJson = await prResponse.Content.ReadAsStringAsync();
                    var prDoc = JsonDocument.Parse(prJson);
                    if (!prDoc.RootElement.TryGetProperty("value", out var prs)) continue;

                    foreach (var pr in prs.EnumerateArray())
                    {
                        var prId = pr.GetProperty("pullRequestId").GetInt32();

                        // Get work items linked to this PR
                        var wiUrl = $"{encodedProject}/_apis/git/repositories/{repoId}/pullRequests/{prId}/workitems?api-version={ApiVersion}";
                        var wiResponse = await _httpClient.GetAsync(wiUrl);
                        if (!wiResponse.IsSuccessStatusCode) continue;

                        var wiJson = await wiResponse.Content.ReadAsStringAsync();
                        var wiDoc = JsonDocument.Parse(wiJson);
                        if (!wiDoc.RootElement.TryGetProperty("value", out var workItemRefs)) continue;

                        var prLinkedIds = new List<int>();
                        foreach (var wiRef in workItemRefs.EnumerateArray())
                        {
                            if (wiRef.TryGetProperty("id", out var idProp))
                            {
                                if (int.TryParse(idProp.ToString(), out var wiId))
                                    prLinkedIds.Add(wiId);
                            }
                            else if (wiRef.TryGetProperty("url", out var urlProp))
                            {
                                var urlStr = urlProp.GetString() ?? "";
                                var lastSlash = urlStr.LastIndexOf('/');
                                if (lastSlash >= 0 && int.TryParse(urlStr[(lastSlash + 1)..], out var urlId))
                                    prLinkedIds.Add(urlId);
                            }
                        }

                        if (prLinkedIds.Count > 0)
                        {
                            result.DiscoveryNotes.Add($"PR #{prId} has {prLinkedIds.Count} linked work item(s).");
                            var items = await GetWorkItemsByIdsAsync(prLinkedIds, result);
                            allWorkItems.AddRange(items);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error searching PRs in project {Project} for branch {Branch}",
                    project, branchName);
                result.DiscoveryNotes.Add($"Error searching project '{project}': {ex.Message}");
            }
        }

        return allWorkItems;
    }

    private TfsWorkItem ParseWorkItem(JsonElement wi)
    {
        var fields = wi.GetProperty("fields");
        var id = fields.GetProperty("System.Id").GetInt32();
        var projectName = GetStringField(fields, "System.TeamProject");

        return new TfsWorkItem
        {
            Id = id,
            Title = GetStringField(fields, "System.Title"),
            WorkItemType = GetStringField(fields, "System.WorkItemType"),
            State = GetStringField(fields, "System.State"),
            AssignedTo = GetAssignedTo(fields),
            ProjectName = projectName,
            Url = $"{_settings.OrganizationUrl.TrimEnd('/')}/{Uri.EscapeDataString(projectName)}/_workitems/edit/{id}"
        };
    }

    private static string GetStringField(JsonElement fields, string fieldName)
    {
        if (fields.TryGetProperty(fieldName, out var prop))
            return prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? "" : prop.ToString();
        return "";
    }

    private static string GetAssignedTo(JsonElement fields)
    {
        if (!fields.TryGetProperty("System.AssignedTo", out var prop)) return "Unassigned";

        if (prop.ValueKind == JsonValueKind.String)
            return prop.GetString() ?? "Unassigned";

        if (prop.ValueKind == JsonValueKind.Object && prop.TryGetProperty("displayName", out var name))
            return name.GetString() ?? "Unassigned";

        return "Unassigned";
    }

    public async Task<(bool Success, string Message, List<string> Projects)> VerifyAndFetchProjectsAsync(
        string orgUrl, string pat, string apiVersion = "5.0")
    {
        using var client = new HttpClient();
        client.BaseAddress = new Uri(orgUrl.TrimEnd('/') + "/");
        var creds = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{pat}"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", creds);

        try
        {
            var response = await client.GetAsync($"_apis/projects?api-version={apiVersion}");

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);
                var projects = new List<string>();
                if (doc.RootElement.TryGetProperty("value", out var arr))
                {
                    foreach (var p in arr.EnumerateArray())
                    {
                        if (p.TryGetProperty("name", out var name))
                        {
                            var n = name.GetString();
                            if (!string.IsNullOrEmpty(n))
                                projects.Add(n);
                        }
                    }
                }
                return (true, $"Connected! Found {projects.Count} project(s).", projects.OrderBy(p => p).ToList());
            }

            var body = await response.Content.ReadAsStringAsync();
            return (false, $"HTTP {(int)response.StatusCode}: {TruncateForNote(body, 200)}", new());
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Connection failed: {ex.Message}", new());
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}", new());
        }
    }

    public async Task<(bool Success, string Message)> TestConnectionAsync(AzureDevOpsSettings testSettings)
    {
        if (!testSettings.IsConfigured)
            return (false, "Settings are incomplete. Organization URL, PAT, and at least one project are required.");

        using var client = new HttpClient();
        client.BaseAddress = new Uri(testSettings.OrganizationUrl.TrimEnd('/') + "/");
        var creds = Convert.ToBase64String(
            Encoding.ASCII.GetBytes($":{testSettings.PersonalAccessToken}"));
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", creds);

        try
        {
            var project = Uri.EscapeDataString(testSettings.Projects.First());
            var url = $"{project}/_apis/git/repositories?api-version={testSettings.ApiVersion}&$top=1";
            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
                return (true, $"Connected successfully to {testSettings.OrganizationUrl}");

            var body = await response.Content.ReadAsStringAsync();
            return (false, $"HTTP {(int)response.StatusCode}: {TruncateForNote(body, 200)}");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Connection failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}");
        }
    }

    private static string TruncateForNote(string text, int max = 120)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..max] + "...";
    }
}
