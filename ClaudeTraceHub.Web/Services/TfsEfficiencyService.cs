using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaudeTraceHub.Web.Models;
using Microsoft.Extensions.Options;

namespace ClaudeTraceHub.Web.Services;

public class TfsEfficiencyService
{
    private readonly AzureDevOpsSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TfsEfficiencyService> _logger;
    private string ApiVersion => _settings.ApiVersion;

    private static readonly SemaphoreSlim _throttle = new(5, 5);

    public TfsEfficiencyService(
        IOptionsSnapshot<AzureDevOpsSettings> settings,
        HttpClient httpClient,
        ILogger<TfsEfficiencyService> logger)
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
    /// Main entry point: fetch efficiency data for a team's iteration on a given date.
    /// </summary>
    public async Task<EfficiencyTrackerBundle> GetEfficiencyDataAsync(
        string project, string team, string iterationPath, DateTime targetDate)
    {
        var bundle = new EfficiencyTrackerBundle
        {
            SelectedDate = targetDate,
            IterationPath = iterationPath
        };

        if (!_settings.IsConfigured)
        {
            bundle.ErrorMessage = "Azure DevOps is not configured. Go to Settings to configure.";
            return bundle;
        }

        try
        {
            // Step 1: Get team's area paths to scope query to this team only
            var teamAreaPaths = await GetTeamAreaPathsAsync(project, team);

            // Step 2: WIQL query for all Tasks under the iteration + team area paths
            var workItemIds = await ExecuteWiqlQueryAsync(project, iterationPath, teamAreaPaths);
            if (workItemIds.Count == 0)
            {
                bundle.ErrorMessage = "No tasks found in this iteration.";
                return bundle;
            }

            bundle.TotalTasks = workItemIds.Count;

            // Step 3: Fetch current work item details (AssignedTo, CompletedWork, RemainingWork)
            var workItems = await FetchWorkItemDetailsAsync(project, workItemIds);

            // Step 4: Fetch updates for each work item (parallel, throttled)
            var allDeltas = await FetchAllWorkItemUpdatesAsync(project, workItems);

            // Step 5: Aggregate by member
            bundle.MemberStats = AggregateByMemberAndDate(allDeltas, workItems, targetDate);
            bundle.ManagedCount = bundle.MemberStats.Count(m => m.ManagedTfs);
            bundle.NotManagedCount = bundle.MemberStats.Count(m => !m.ManagedTfs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching TFS efficiency data");
            bundle.ErrorMessage = $"Error: {ex.Message}";
        }

        return bundle;
    }

    /// <summary>
    /// Fetches the area paths configured for a team via the Team Field Values API.
    /// </summary>
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

    private async Task<List<int>> ExecuteWiqlQueryAsync(
        string project, string iterationPath, List<(string Path, bool IncludeChildren)> teamAreaPaths)
    {
        var encodedProject = Uri.EscapeDataString(project);
        var url = $"{encodedProject}/_apis/wit/wiql?api-version={ApiVersion}";

        var conditions = new List<string>
        {
            "[System.WorkItemType] = 'Task'",
            $"[System.IterationPath] UNDER '{iterationPath}'"
        };

        // Add team area path filter if available
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
            _logger.LogWarning("WIQL query failed: HTTP {StatusCode} - {Body}",
                (int)response.StatusCode, body);
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

    private async Task<List<(int Id, string Title, string AssignedTo, double CompletedWork, double RemainingWork)>>
        FetchWorkItemDetailsAsync(string project, List<int> ids)
    {
        var allItems = new List<(int, string, string, double, double)>();
        var fields = "System.Id,System.Title,System.AssignedTo," +
                     "Microsoft.VSTS.Scheduling.CompletedWork,Microsoft.VSTS.Scheduling.RemainingWork";

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
                    _logger.LogWarning("Failed to fetch work items batch: HTTP {StatusCode}",
                        (int)response.StatusCode);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("value", out var valueArray))
                {
                    foreach (var wi in valueArray.EnumerateArray())
                    {
                        var f = wi.GetProperty("fields");
                        allItems.Add((
                            GetIntField(f, "System.Id"),
                            GetStringField(f, "System.Title"),
                            GetAssignedToName(f),
                            GetDoubleField(f, "Microsoft.VSTS.Scheduling.CompletedWork"),
                            GetDoubleField(f, "Microsoft.VSTS.Scheduling.RemainingWork")
                        ));
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

    private async Task<List<WorkItemFieldDelta>> FetchAllWorkItemUpdatesAsync(
        string project,
        List<(int Id, string Title, string AssignedTo, double CompletedWork, double RemainingWork)> workItems)
    {
        var allDeltas = new List<WorkItemFieldDelta>();
        var lockObj = new object();

        var tasks = workItems.Select(async wi =>
        {
            await _throttle.WaitAsync();
            try
            {
                var deltas = await GetWorkItemUpdatesAsync(project, wi.Id, wi.Title, wi.AssignedTo);
                if (deltas.Count > 0)
                {
                    lock (lockObj)
                    {
                        allDeltas.AddRange(deltas);
                    }
                }
            }
            finally
            {
                _throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
        return allDeltas;
    }

    private async Task<List<WorkItemFieldDelta>> GetWorkItemUpdatesAsync(
        string project, int workItemId, string workItemTitle, string assignedTo)
    {
        var deltas = new List<WorkItemFieldDelta>();

        try
        {
            var encodedProject = Uri.EscapeDataString(project);
            var url = $"{encodedProject}/_apis/wit/workitems/{workItemId}/updates?api-version={ApiVersion}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch updates for work item {Id}: HTTP {StatusCode}",
                    workItemId, (int)response.StatusCode);
                return deltas;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("value", out var updates))
                return deltas;

            foreach (var update in updates.EnumerateArray())
            {
                if (!update.TryGetProperty("fields", out var fields))
                    continue;

                var hasCompleted = fields.TryGetProperty(
                    "Microsoft.VSTS.Scheduling.CompletedWork", out var completedChange);
                var hasRemaining = fields.TryGetProperty(
                    "Microsoft.VSTS.Scheduling.RemainingWork", out var remainingChange);

                if (!hasCompleted && !hasRemaining)
                    continue;

                // Use System.ChangedDate from fields (when change was MADE),
                // NOT revisedDate (when revision was SUPERSEDED)
                var changeDate = DateTime.MinValue;
                if (fields.TryGetProperty("System.ChangedDate", out var changedDateField))
                {
                    var dateStr = changedDateField.TryGetProperty("newValue", out var newVal)
                        ? newVal.GetString()
                        : changedDateField.TryGetProperty("oldValue", out var oldVal)
                            ? oldVal.GetString() : null;
                    if (dateStr != null)
                        DateTime.TryParse(dateStr, out changeDate);
                }

                // Fallback: use revisedDate if ChangedDate not available
                if (changeDate == DateTime.MinValue)
                {
                    if (update.TryGetProperty("revisedDate", out var dateProp)
                        && DateTime.TryParse(dateProp.GetString(), out var dt)
                        && dt.Year < 9999)
                    {
                        changeDate = dt;
                    }
                    else
                    {
                        changeDate = DateTime.UtcNow;
                    }
                }

                var changedBy = "";
                if (update.TryGetProperty("revisedBy", out var revisedBy))
                {
                    changedBy = revisedBy.TryGetProperty("displayName", out var dispName)
                        ? dispName.GetString() ?? ""
                        : revisedBy.TryGetProperty("name", out var nameP)
                            ? nameP.GetString() ?? "" : "";
                }

                var delta = new WorkItemFieldDelta
                {
                    WorkItemId = workItemId,
                    WorkItemTitle = workItemTitle,
                    AssignedTo = assignedTo,
                    ChangedBy = changedBy,
                    RevisedDate = changeDate.ToLocalTime()
                };

                if (hasCompleted)
                {
                    delta.CompletedWorkOld = GetUpdateFieldValue(completedChange, "oldValue");
                    delta.CompletedWorkNew = GetUpdateFieldValue(completedChange, "newValue");
                }

                if (hasRemaining)
                {
                    delta.RemainingWorkOld = GetUpdateFieldValue(remainingChange, "oldValue");
                    delta.RemainingWorkNew = GetUpdateFieldValue(remainingChange, "newValue");
                }

                deltas.Add(delta);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching updates for work item {Id}", workItemId);
        }

        return deltas;
    }

    private List<MemberDailyEfficiency> AggregateByMemberAndDate(
        List<WorkItemFieldDelta> allDeltas,
        List<(int Id, string Title, string AssignedTo, double CompletedWork, double RemainingWork)> workItems,
        DateTime targetDate)
    {
        // Get all unique members from work items (even those with no updates)
        var memberCurrentTotals = workItems
            .GroupBy(w => w.AssignedTo)
            .ToDictionary(
                g => g.Key,
                g => (
                    TotalCompleted: g.Sum(w => w.CompletedWork),
                    TotalRemaining: g.Sum(w => w.RemainingWork)
                ));

        // Group deltas by AssignedTo (the person the work item belongs to)
        var deltasByMember = allDeltas
            .GroupBy(d => d.AssignedTo)
            .ToDictionary(g => g.Key, g => g.ToList());

        var results = new List<MemberDailyEfficiency>();

        foreach (var member in memberCurrentTotals.Keys.OrderBy(k => k))
        {
            var efficiency = new MemberDailyEfficiency
            {
                MemberName = member,
                TotalCompleted = Math.Round(memberCurrentTotals[member].TotalCompleted, 2),
                TotalRemaining = Math.Round(memberCurrentTotals[member].TotalRemaining, 2)
            };

            if (deltasByMember.TryGetValue(member, out var memberDeltas))
            {
                // Today's deltas
                var todayDeltas = memberDeltas
                    .Where(d => d.RevisedDate.Date == targetDate.Date)
                    .ToList();

                efficiency.CompletedDelta = Math.Round(todayDeltas.Sum(d => d.CompletedWorkDelta), 2);
                efficiency.RemainingDelta = Math.Round(todayDeltas.Sum(d => d.RemainingWorkDelta), 2);
                efficiency.ManagedTfs = todayDeltas.Count > 0;

                // Build day-wise history from all deltas
                efficiency.DayHistory = memberDeltas
                    .GroupBy(d => d.RevisedDate.Date)
                    .OrderByDescending(g => g.Key)
                    .Select(g => new DayWiseBreakdown
                    {
                        Date = g.Key,
                        CompletedDelta = Math.Round(g.Sum(d => d.CompletedWorkDelta), 2),
                        RemainingDelta = Math.Round(g.Sum(d => d.RemainingWorkDelta), 2),
                        WorkItemsUpdated = g.Select(d => d.WorkItemId).Distinct().Count(),
                        Details = g.ToList()
                    })
                    .ToList();
            }

            results.Add(efficiency);
        }

        return results;
    }

    // --- Helper methods ---

    private static double GetUpdateFieldValue(JsonElement fieldChange, string key)
    {
        if (fieldChange.TryGetProperty(key, out var val))
        {
            if (val.ValueKind == JsonValueKind.Number) return val.GetDouble();
            if (double.TryParse(val.ToString(), out var parsed)) return parsed;
        }
        return 0;
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

    private static double GetDoubleField(JsonElement fields, string fieldName)
    {
        if (fields.TryGetProperty(fieldName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number) return prop.GetDouble();
            if (double.TryParse(prop.ToString(), out var val)) return val;
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
