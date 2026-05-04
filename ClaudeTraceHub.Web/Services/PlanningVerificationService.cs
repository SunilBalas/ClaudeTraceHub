using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaudeTraceHub.Web.Models;
using Microsoft.Extensions.Options;

namespace ClaudeTraceHub.Web.Services;

public class PlanningVerificationService
{
    private readonly AzureDevOpsSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<PlanningVerificationService> _logger;
    private string ApiVersion => _settings.ApiVersion;

    public PlanningVerificationService(
        IOptionsSnapshot<AzureDevOpsSettings> settings,
        HttpClient httpClient,
        ILogger<PlanningVerificationService> logger)
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

    public async Task<PlanningVerificationBundle> GetVerificationDataAsync(
        string project, string team, string iterationPath)
    {
        var bundle = new PlanningVerificationBundle
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

            // Step 1: Query all Requirements/Bugs in iteration + area (regardless of whether they have tasks)
            var parentIds = await QueryWorkItemIdsAsync(project, iterationPath, teamAreaPaths,
                "[System.WorkItemType] IN ('Requirement', 'Bug', 'Product Backlog Item', 'User Story')");

            if (parentIds.Count == 0)
            {
                bundle.ErrorMessage = "No requirements/bugs found in this iteration for the selected team.";
                return bundle;
            }

            // Step 2: Query all Tasks in iteration + area separately
            var taskIds = await QueryWorkItemIdsAsync(project, iterationPath, teamAreaPaths,
                "[System.WorkItemType] = 'Task'");

            // Step 3: Fetch parent (Requirement/Bug) details
            var parents = await FetchWorkItemsAsync(project, parentIds);

            // Step 4: Fetch task details with relations to map back to parent
            var (tasks, taskParentMap) = await FetchTasksWithParentMapAsync(project, taskIds);

            // Step 4: Build rows + run validation
            foreach (var parent in parents)
            {
                var row = new RequirementVerificationRow
                {
                    WorkItemId = parent.Id,
                    Title = parent.Title,
                    State = parent.State,
                    AssignedTo = parent.AssignedTo,
                    WorkItemType = parent.WorkItemType,
                    Tags = parent.Tags
                };
                ValidateRequirement(row);

                if (taskParentMap.TryGetValue(parent.Id, out var childIds))
                {
                    foreach (var taskId in childIds)
                    {
                        var task = tasks.FirstOrDefault(t => t.Id == taskId);
                        if (task == null) continue;
                        var taskRow = new TaskVerificationRow
                        {
                            WorkItemId = task.Id,
                            Title = task.Title,
                            State = task.State,
                            AssignedTo = task.AssignedTo,
                            Tags = task.Tags,
                            Discipline = task.Discipline,
                            TaskExecutionType = task.TaskExecutionType,
                            OriginalEstimate = task.OriginalEstimate,
                            DetectedTaskType = DetectTaskType(task.Title)
                        };
                        ValidateTask(taskRow, parent.WorkItemType);
                        row.Tasks.Add(taskRow);
                    }
                }

                bundle.Requirements.Add(row);
            }

            bundle.Requirements = bundle.Requirements
                .OrderBy(r => r.WorkItemId)
                .ToList();

            bundle.TotalRequirements = bundle.Requirements.Count;
            bundle.TotalTasks = bundle.Requirements.Sum(r => r.Tasks.Count);
            bundle.RequirementsWithIssues = bundle.Requirements.Count(r => r.HasIssues);
            bundle.TasksWithIssues = bundle.Requirements.Sum(r => r.TasksWithIssuesCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching planning verification data");
            bundle.ErrorMessage = $"Error: {ex.Message}";
        }

        return bundle;
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

    /// <summary>
    /// Flat WIQL query — returns IDs of work items matching the given type filter,
    /// scoped to iteration + team area paths.
    /// </summary>
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
        int Id, string Title, string State, string AssignedTo, string WorkItemType,
        string Tags, string Discipline, string TaskExecutionType, double OriginalEstimate);

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
                            : GetIntField(f, "System.Id");
                        results.Add(new WorkItemDetail(
                            id,
                            GetStringField(f, "System.Title"),
                            GetStringField(f, "System.State"),
                            GetAssignedToName(f),
                            GetStringField(f, "System.WorkItemType"),
                            GetStringField(f, "System.Tags"),
                            GetStringField(f, "Microsoft.VSTS.Common.Discipline"),
                            GetTaskExecutionType(f),
                            GetDoubleField(f, "Microsoft.VSTS.Scheduling.OriginalEstimate")
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

    /// <summary>
    /// Fetches tasks with their parent relation and returns a parent→children map.
    /// </summary>
    private async Task<(List<WorkItemDetail> Tasks, Dictionary<int, List<int>> ParentMap)> FetchTasksWithParentMapAsync(
        string project, List<int> ids)
    {
        var tasks = new List<WorkItemDetail>();
        var parentMap = new Dictionary<int, List<int>>();
        if (ids.Count == 0) return (tasks, parentMap);

        const int batchSize = 200;
        for (var i = 0; i < ids.Count; i += batchSize)
        {
            var batch = ids.Skip(i).Take(batchSize).ToList();
            var idsParam = string.Join(",", batch);
            var url = $"_apis/wit/workitems?ids={idsParam}&$expand=relations&api-version={ApiVersion}";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("value", out var arr))
                {
                    foreach (var wi in arr.EnumerateArray())
                    {
                        var f = wi.GetProperty("fields");
                        var taskId = wi.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number
                            ? idProp.GetInt32()
                            : GetIntField(f, "System.Id");
                        tasks.Add(new WorkItemDetail(
                            taskId,
                            GetStringField(f, "System.Title"),
                            GetStringField(f, "System.State"),
                            GetAssignedToName(f),
                            GetStringField(f, "System.WorkItemType"),
                            GetStringField(f, "System.Tags"),
                            GetStringField(f, "Microsoft.VSTS.Common.Discipline"),
                            GetTaskExecutionType(f),
                            GetDoubleField(f, "Microsoft.VSTS.Scheduling.OriginalEstimate")
                        ));

                        if (wi.TryGetProperty("relations", out var rels))
                        {
                            foreach (var rel in rels.EnumerateArray())
                            {
                                var relType = rel.TryGetProperty("rel", out var rp) ? rp.GetString() : "";
                                if (relType != "System.LinkTypes.Hierarchy-Reverse") continue;

                                var urlVal = rel.TryGetProperty("url", out var up) ? up.GetString() ?? "" : "";
                                var parentId = ParseIdFromUrl(urlVal);
                                if (parentId == 0) continue;

                                if (!parentMap.TryGetValue(parentId, out var list))
                                {
                                    list = new List<int>();
                                    parentMap[parentId] = list;
                                }
                                list.Add(taskId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching task batch at {Index}", i);
            }
        }

        return (tasks, parentMap);
    }

    private static int ParseIdFromUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return 0;
        var lastSlash = url.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash >= url.Length - 1) return 0;
        return int.TryParse(url[(lastSlash + 1)..], out var id) ? id : 0;
    }

    // ---------- Validation ----------

    private static void ValidateRequirement(RequirementVerificationRow row)
    {
        // Bugs don't need Deliverable/Non-deliverable tag — only Requirements do.
        if (row.WorkItemType.Equals("Bug", StringComparison.OrdinalIgnoreCase))
            return;

        var tags = SplitTags(row.Tags);
        var isAlm = row.Title.Contains("ALM", StringComparison.OrdinalIgnoreCase);

        if (isAlm)
        {
            if (!tags.Contains("ALM", StringComparer.OrdinalIgnoreCase))
                row.Issues.Add(new ValidationIssue
                {
                    Rule = "ALM Tag",
                    Message = "ALM-related work item must contain 'ALM' tag."
                });
        }
        else
        {
            var hasDeliverable = tags.Contains("Deliverable", StringComparer.OrdinalIgnoreCase);
            var hasNonDeliverable = tags.Any(t => t.Equals("Non-deliverable", StringComparison.OrdinalIgnoreCase)
                                                 || t.Equals("NonDeliverable", StringComparison.OrdinalIgnoreCase)
                                                 || t.Equals("Non Deliverable", StringComparison.OrdinalIgnoreCase));
            if (!hasDeliverable && !hasNonDeliverable)
                row.Issues.Add(new ValidationIssue
                {
                    Rule = "Deliverable Tag",
                    Message = "Requirement must have 'Deliverable' or 'Non-deliverable' tag."
                });
        }
    }

    private static void ValidateTask(TaskVerificationRow row, string parentWorkItemType)
    {
        var tags = SplitTags(row.Tags);
        var isAlm = row.Title.Contains("ALM", StringComparison.OrdinalIgnoreCase);
        var isUnderBug = parentWorkItemType.Equals("Bug", StringComparison.OrdinalIgnoreCase);

        // Tag rule
        if (isAlm)
        {
            if (!tags.Contains("ALM", StringComparer.OrdinalIgnoreCase))
                row.Issues.Add(new ValidationIssue
                {
                    Rule = "ALM Tag",
                    Message = "ALM task must contain 'ALM' tag."
                });
        }
        else if (isUnderBug)
        {
            var hasBugTag = tags.Any(t => t.Equals("Bug/Maintenance", StringComparison.OrdinalIgnoreCase)
                                       || t.Equals("Bug", StringComparison.OrdinalIgnoreCase)
                                       || t.Equals("Maintenance", StringComparison.OrdinalIgnoreCase));
            if (!hasBugTag)
                row.Issues.Add(new ValidationIssue
                {
                    Rule = "Bug/Maintenance Tag",
                    Message = "Task under a Bug must contain 'Bug/Maintenance' tag."
                });
        }
        else
        {
            if (!tags.Contains("Product", StringComparer.OrdinalIgnoreCase))
                row.Issues.Add(new ValidationIssue
                {
                    Rule = "Product Tag",
                    Message = "Task tags must include 'Product'."
                });
        }

        // Discipline must be set
        if (string.IsNullOrWhiteSpace(row.Discipline))
        {
            row.Issues.Add(new ValidationIssue
            {
                Rule = "Discipline",
                Message = "Discipline is missing."
            });
        }
        else if (!row.Discipline.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            row.Issues.Add(new ValidationIssue
            {
                Rule = "Discipline",
                Message = $"Discipline must be 'Development' (currently '{row.Discipline}')."
            });
        }

        // TaskExecutionType
        if (string.IsNullOrWhiteSpace(row.TaskExecutionType))
        {
            row.Issues.Add(new ValidationIssue
            {
                Rule = "Task Execution Type",
                Message = "Task Execution Type is missing."
            });
        }
        else
        {
            var expected = ExpectedExecutionType(row.DetectedTaskType);
            if (expected != null && !row.TaskExecutionType.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                row.Issues.Add(new ValidationIssue
                {
                    Rule = "Task Execution Type",
                    Message = $"For '{row.DetectedTaskType}' task, expected '{expected}' (currently '{row.TaskExecutionType}')."
                });
            }
            else if (expected == null && !string.IsNullOrEmpty(row.DetectedTaskType) && row.DetectedTaskType == "Unknown")
            {
                row.Issues.Add(new ValidationIssue
                {
                    Rule = "Task Type",
                    Message = "Unrecognised task type — discipline / execution type cannot be auto-verified."
                });
            }
        }

        // Original Estimate
        if (row.OriginalEstimate <= 0)
        {
            row.Issues.Add(new ValidationIssue
            {
                Rule = "Original Estimate",
                Message = "Original Estimate is missing or zero."
            });
        }
    }

    private static string DetectTaskType(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "Unknown";

        var t = title.Trim();

        if (StartsWithAny(t, "Code Development", "Code Dev")) return "Code Development";
        if (StartsWithAny(t, "Code Review")) return "Code Review";
        if (StartsWithAny(t, "Technical Analysis")) return "Technical Analysis";
        if (StartsWithAny(t, "Verification")) return "Verification";
        if (StartsWithAny(t, "Support", "Code Support")) return "Support";
        if (StartsWithAny(t, "Level 1", "L1 Testing", "L1:", "L1 ")) return "Level 1";
        if (StartsWithAny(t, "Bug Resolution")) return "Bug Resolution";
        if (t.Contains("ALM", StringComparison.OrdinalIgnoreCase)) return "ALM";

        return "Unknown";
    }

    private static bool StartsWithAny(string title, params string[] prefixes)
    {
        foreach (var p in prefixes)
        {
            if (title.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string? ExpectedExecutionType(string detectedType) => detectedType switch
    {
        "Code Development" => "AI-Dev-Development",
        "Technical Analysis" => "AI-Dev-Analysis",
        "Code Review" => "Manual",
        "Verification" => "Manual",
        "Support" => "Manual",
        "Level 1" => "Manual",
        "Bug Resolution" => null,
        "ALM" => null,
        _ => null
    };

    private static List<string> SplitTags(string tags)
    {
        if (string.IsNullOrWhiteSpace(tags)) return new List<string>();
        return tags.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    // ---------- Field helpers ----------

    private static string GetStringField(JsonElement fields, string fieldName)
    {
        return fields.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() ?? ""
            : "";
    }

    private static int GetIntField(JsonElement fields, string fieldName)
    {
        return fields.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : 0;
    }

    private static double GetDoubleField(JsonElement fields, string fieldName)
    {
        return fields.TryGetProperty(fieldName, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetDouble()
            : 0;
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

    /// <summary>
    /// Reads TaskExecutionType from the most likely custom field locations (varies by TFS install).
    /// </summary>
    private static string GetTaskExecutionType(JsonElement fields)
    {
        foreach (var fieldName in new[]
        {
            "Casepoint.TFS.CustomFields.TaskExecutionType",
            "Custom.TaskExecutionType",
            "Microsoft.VSTS.Common.TaskExecutionType",
            "TaskExecutionType"
        })
        {
            var v = GetStringField(fields, fieldName);
            if (!string.IsNullOrEmpty(v)) return v;
        }
        return "";
    }
}
