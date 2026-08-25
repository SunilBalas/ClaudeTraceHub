using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaudeTraceHub.Web.Models;
using Microsoft.Extensions.Options;

namespace ClaudeTraceHub.Web.Services;

public class TaskPlanningService
{
    private readonly AzureDevOpsSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<TaskPlanningService> _logger;
    private string ApiVersion => _settings.ApiVersion;

    public TaskPlanningService(
        IOptionsSnapshot<AzureDevOpsSettings> settings,
        HttpClient httpClient,
        ILogger<TaskPlanningService> logger)
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

    public async Task<TaskPlanningBundle> GetTaskPlanningDataAsync(
        string project, string team, string iterationPath)
    {
        var bundle = new TaskPlanningBundle
        {
            TeamName = team,
            IterationPath = iterationPath
        };

        if (!_settings.IsConfigured)
        {
            bundle.ErrorMessage = "Azure DevOps is not configured. Go to Settings to configure.";
            return bundle;
        }

        try
        {
            var teamAreaPaths = await GetTeamAreaPathsAsync(project, team);

            var parentIds = await QueryWorkItemIdsAsync(project, iterationPath, teamAreaPaths,
                "[System.WorkItemType] IN ('Requirement', 'Change Request', 'Bug')");

            if (parentIds.Count == 0)
            {
                bundle.ErrorMessage = "No Requirements / Change Requests / Bugs found in this iteration for the selected team.";
                return bundle;
            }

            var parents = await FetchWorkItemsAsync(project, parentIds);

            foreach (var p in parents)
            {
                var row = new ParentPlanningRow
                {
                    WorkItemId = p.Id,
                    Title = p.Title,
                    State = p.State,
                    AssignedTo = p.AssignedTo,
                    WorkItemType = p.WorkItemType,
                    Tags = p.Tags,
                    SuggestedTasks = BuildSuggestions(p.WorkItemType, p.Title)
                };
                bundle.Parents.Add(row);
            }

            bundle.Parents = bundle.Parents.OrderBy(r => r.WorkItemId).ToList();
            bundle.TotalParents = bundle.Parents.Count;
            bundle.TotalSuggested = bundle.Parents.Sum(p => p.SuggestedTasks.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching task planning data");
            bundle.ErrorMessage = $"Error: {ex.Message}";
        }

        return bundle;
    }

    private static List<SuggestedTask> BuildSuggestions(string parentType, string parentTitle)
    {
        if (parentType.Equals("Bug", StringComparison.OrdinalIgnoreCase))
        {
            return new List<SuggestedTask>
            {
                new()
                {
                    TaskType = "Bug Resolution",
                    Title = $"Bug Resolution: {parentTitle}",
                    Discipline = "Development",
                    TaskExecutionType = "",
                    Tag = "Bug/Maintenance"
                }
            };
        }

        return new List<SuggestedTask>
        {
            new()
            {
                TaskType = "Code Development",
                Title = $"Code Development: {parentTitle}",
                Discipline = "Development",
                TaskExecutionType = "AI-Dev-Development",
                Tag = "Product"
            },
            new()
            {
                TaskType = "Code Review",
                Title = $"Code Review: {parentTitle}",
                Discipline = "Development",
                TaskExecutionType = "Manual",
                Tag = "Product"
            },
            new()
            {
                TaskType = "L1 Testing",
                Title = $"L1 Testing: {parentTitle}",
                Discipline = "Development",
                TaskExecutionType = "Manual",
                Tag = "Product"
            },
            new()
            {
                TaskType = "Code Support",
                Title = $"Code Support: {parentTitle}",
                Discipline = "Development",
                TaskExecutionType = "Manual",
                Tag = "Product"
            }
        };
    }

    private async Task<List<(string Path, bool IncludeChildren)>> GetTeamAreaPathsAsync(string project, string team)
    {
        var areaPaths = new List<(string Path, bool IncludeChildren)>();
        try
        {
            var encodedProject = Uri.EscapeDataString(project);
            var encodedTeam = Uri.EscapeDataString(team);
            var url = $"{encodedProject}/{encodedTeam}/_apis/work/teamsettings/teamfieldvalues?api-version={ApiVersion}";
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return areaPaths;

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
            _logger.LogWarning(ex, "Error fetching team area paths");
        }
        return areaPaths;
    }

    private async Task<List<int>> QueryWorkItemIdsAsync(
        string project, string iterationPath,
        List<(string Path, bool IncludeChildren)> teamAreaPaths,
        string typeClause)
    {
        var encodedProject = Uri.EscapeDataString(project);
        var url = $"{encodedProject}/_apis/wit/wiql?api-version={ApiVersion}";

        var conditions = new List<string>
        {
            typeClause,
            $"[System.IterationPath] UNDER '{iterationPath}'"
        };

        if (teamAreaPaths.Count > 0)
        {
            var clauses = teamAreaPaths.Select(ap =>
                ap.IncludeChildren
                    ? $"[System.AreaPath] UNDER '{ap.Path}'"
                    : $"[System.AreaPath] = '{ap.Path}'");
            conditions.Add($"({string.Join(" OR ", clauses)})");
        }

        var wiql = $"SELECT [System.Id] FROM WorkItems WHERE {string.Join(" AND ", conditions)} ORDER BY [System.Id] ASC";

        var requestBody = JsonSerializer.Serialize(new { query = wiql });
        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(url, content);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"WIQL query failed: HTTP {(int)response.StatusCode} - {body}");
        }

        var json = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json);

        var ids = new List<int>();
        if (doc.RootElement.TryGetProperty("workItems", out var arr))
        {
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("id", out var idProp))
                    ids.Add(idProp.GetInt32());
            }
        }
        return ids;
    }

    private record WorkItemDetail(
        int Id, string Title, string State, string AssignedTo, string WorkItemType, string Tags);

    private async Task<List<WorkItemDetail>> FetchWorkItemsAsync(string project, List<int> ids)
    {
        var results = new List<WorkItemDetail>();
        if (ids.Count == 0) return results;

        const int batchSize = 200;
        for (var i = 0; i < ids.Count; i += batchSize)
        {
            var batch = ids.Skip(i).Take(batchSize).ToList();
            var idsParam = string.Join(",", batch);
            var url = $"_apis/wit/workitems?ids={idsParam}&api-version={ApiVersion}";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed batch: HTTP {StatusCode}", (int)response.StatusCode);
                    continue;
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("value", out var arr))
                {
                    foreach (var wi in arr.EnumerateArray())
                    {
                        var f = wi.GetProperty("fields");
                        var id = wi.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number
                            ? idProp.GetInt32()
                            : 0;
                        results.Add(new WorkItemDetail(
                            id,
                            GetStringField(f, "System.Title"),
                            GetStringField(f, "System.State"),
                            GetAssignedToName(f),
                            GetStringField(f, "System.WorkItemType"),
                            GetStringField(f, "System.Tags")
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching work items batch at {Index}", i);
            }
        }

        return results;
    }

    private static string GetStringField(JsonElement fields, string fieldName)
    {
        return fields.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? ""
            : "";
    }

    private static string GetAssignedToName(JsonElement fields)
    {
        if (!fields.TryGetProperty("System.AssignedTo", out var assignedTo))
            return "Unassigned";

        if (assignedTo.ValueKind == JsonValueKind.String)
            return assignedTo.GetString() ?? "Unassigned";

        if (assignedTo.ValueKind == JsonValueKind.Object &&
            assignedTo.TryGetProperty("displayName", out var name))
            return name.GetString() ?? "Unassigned";

        return "Unassigned";
    }
}
