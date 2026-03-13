using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ClaudeTraceHub.Web.Models;
using Microsoft.Extensions.Options;

namespace ClaudeTraceHub.Web.Services;

public class AiAdoptionService
{
    private readonly AzureDevOpsSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<AiAdoptionService> _logger;
    private string ApiVersion => _settings.ApiVersion;

    // Cache the discovered field reference names
    private static string? _taskExecutionTypeField;
    private static string? _revisedEstimateField;
    private static readonly SemaphoreSlim _fieldDiscoveryLock = new(1, 1);

    public AiAdoptionService(
        IOptionsSnapshot<AzureDevOpsSettings> settings,
        HttpClient httpClient,
        ILogger<AiAdoptionService> logger)
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
    /// Auto-discover custom field reference names (TaskExecutionType, Revised Estimate).
    /// Queries the Azure DevOps fields API once and caches both results.
    /// </summary>
    public async Task<string?> DiscoverTaskExecutionTypeFieldAsync(string project)
    {
        if (_taskExecutionTypeField != null)
            return _taskExecutionTypeField;

        await _fieldDiscoveryLock.WaitAsync();
        try
        {
            if (_taskExecutionTypeField != null)
                return _taskExecutionTypeField;

            var encodedProject = Uri.EscapeDataString(project);
            var url = $"{encodedProject}/_apis/wit/fields?api-version={ApiVersion}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch fields from Azure DevOps: HTTP {StatusCode}",
                    (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("value", out var fields))
            {
                foreach (var field in fields.EnumerateArray())
                {
                    var name = field.TryGetProperty("name", out var nameProp)
                        ? nameProp.GetString() ?? "" : "";
                    var refName = field.TryGetProperty("referenceName", out var refProp)
                        ? refProp.GetString() ?? "" : "";

                    // Discover TaskExecutionType
                    if (_taskExecutionTypeField == null &&
                        (name.Equals("Task Execution Type", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("TaskExecutionType", StringComparison.OrdinalIgnoreCase)
                        || refName.EndsWith(".TaskExecutionType", StringComparison.OrdinalIgnoreCase)))
                    {
                        _taskExecutionTypeField = refName;
                        _logger.LogInformation("Discovered TaskExecutionType field: {RefName}", refName);
                    }

                    // Discover Revised Estimate
                    if (_revisedEstimateField == null &&
                        (name.Equals("Revised Estimate", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("RevisedEstimate", StringComparison.OrdinalIgnoreCase)
                        || refName.EndsWith(".RevisedEstimate", StringComparison.OrdinalIgnoreCase)))
                    {
                        _revisedEstimateField = refName;
                        _logger.LogInformation("Discovered RevisedEstimate field: {RefName}", refName);
                    }
                }
            }

            if (_taskExecutionTypeField == null)
                _logger.LogWarning("TaskExecutionType field not found in Azure DevOps fields");
            if (_revisedEstimateField == null)
                _logger.LogWarning("RevisedEstimate field not found in Azure DevOps fields — 'Change in Scope' fallback to OriginalEstimate");

            return _taskExecutionTypeField;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering custom fields");
            return null;
        }
        finally
        {
            _fieldDiscoveryLock.Release();
        }
    }

    /// <summary>
    /// Fetch available iteration paths for a project.
    /// </summary>
    public async Task<List<string>> GetIterationPathsAsync(string project)
    {
        var iterations = new List<string>();
        try
        {
            var encodedProject = Uri.EscapeDataString(project);
            var url = $"{encodedProject}/_apis/wit/classificationnodes/iterations?$depth=10&api-version={ApiVersion}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode) return iterations;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            CollectIterationPaths(doc.RootElement, "", iterations);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching iterations for project {Project}", project);
        }
        return iterations;
    }

    private void CollectIterationPaths(JsonElement node, string parentPath, List<string> paths)
    {
        var name = node.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
        var currentPath = string.IsNullOrEmpty(parentPath) ? name : $"{parentPath}\\{name}";

        if (!string.IsNullOrEmpty(currentPath))
            paths.Add(currentPath);

        if (node.TryGetProperty("children", out var children))
        {
            foreach (var child in children.EnumerateArray())
                CollectIterationPaths(child, currentPath, paths);
        }
    }

    /// <summary>
    /// Fetch available area paths for a project.
    /// </summary>
    public async Task<List<string>> GetAreaPathsAsync(string project)
    {
        var areas = new List<string>();
        try
        {
            var encodedProject = Uri.EscapeDataString(project);
            var url = $"{encodedProject}/_apis/wit/classificationnodes/areas?$depth=10&api-version={ApiVersion}";
            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode) return areas;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            CollectIterationPaths(doc.RootElement, "", areas);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching area paths for project {Project}", project);
        }
        return areas;
    }

    /// <summary>
    /// Query work items using WIQL and return aggregated adoption data.
    /// </summary>
    public async Task<AdoptionDataBundle> GetAdoptionDataAsync(
        string project,
        List<string>? iterationPaths = null,
        string? disciplineFilter = null,
        string? areaPath = null)
    {
        var bundle = new AdoptionDataBundle();

        if (!_settings.IsConfigured)
        {
            bundle.ErrorMessage = "Azure DevOps is not configured. Go to Settings to configure.";
            return bundle;
        }

        try
        {
            // Step 1: Discover the TaskExecutionType field reference name
            var taskExecField = await DiscoverTaskExecutionTypeFieldAsync(project);
            if (taskExecField == null)
            {
                bundle.ErrorMessage = "Could not find 'TaskExecutionType' field in Azure DevOps. " +
                    "Ensure this custom field exists in your work item type.";
                return bundle;
            }

            // Step 2: Build and execute WIQL query
            var workItemIds = await ExecuteWiqlQueryAsync(project, taskExecField, iterationPaths, areaPath);
            if (workItemIds.Count == 0)
            {
                bundle.ErrorMessage = "No work items found matching the criteria.";
                return bundle;
            }

            // Step 3: Fetch work item details in batches
            var workItems = await FetchWorkItemDetailsAsync(workItemIds, taskExecField);
            bundle.RawWorkItems = workItems;

            // Step 4: Apply discipline filter if specified
            var filtered = workItems;
            if (!string.IsNullOrEmpty(disciplineFilter))
                filtered = workItems.Where(w => w.Discipline.Equals(disciplineFilter, StringComparison.OrdinalIgnoreCase)).ToList();

            // Step 5: Aggregate per member
            bundle.MemberStats = AggregateMemberStats(filtered);

            // Step 6: Build summary
            bundle.Summary = BuildSummary(bundle.MemberStats);

            // Step 7: Collect available filters
            bundle.AvailableDisciplines = workItems
                .Select(w => w.Discipline)
                .Where(d => !string.IsNullOrEmpty(d))
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            bundle.AvailableIterations = await GetIterationPathsAsync(project);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching AI adoption data");
            bundle.ErrorMessage = $"Error: {ex.Message}";
        }

        return bundle;
    }

    private async Task<List<int>> ExecuteWiqlQueryAsync(
        string project, string taskExecField, List<string>? iterationPaths, string? areaPath)
    {
        var encodedProject = Uri.EscapeDataString(project);
        var url = $"{encodedProject}/_apis/wit/wiql?api-version={ApiVersion}";

        // Build WIQL with configurable filters — fetch ALL tasks (AI + Manual)
        var conditions = new List<string>
        {
            "[System.WorkItemType] = 'Task'",
            "[Microsoft.VSTS.Scheduling.OriginalEstimate] > 0",
            "[System.Reason] <> 'Rejected'",
            "[System.State] IN ('Closed', 'Resolved')"
        };

        if (iterationPaths is { Count: > 0 })
        {
            var iterClauses = iterationPaths
                .Select(ip => $"[System.IterationPath] UNDER '{ip}'");
            conditions.Add($"({string.Join(" OR ", iterClauses)})");
        }

        if (!string.IsNullOrEmpty(areaPath))
            conditions.Add($"[System.AreaPath] UNDER '{areaPath}'");

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

    private async Task<List<AdoptionWorkItem>> FetchWorkItemDetailsAsync(
        List<int> ids, string taskExecField)
    {
        var allItems = new List<AdoptionWorkItem>();
        var fieldList = new List<string>
        {
            "System.Id",
            "System.Title",
            "System.AssignedTo",
            "System.State",
            "System.Reason",
            "System.IterationPath",
            "System.Tags",
            "Microsoft.VSTS.Common.Discipline",
            "Microsoft.VSTS.Scheduling.OriginalEstimate",
            "Microsoft.VSTS.Scheduling.CompletedWork",
            "Microsoft.VSTS.Scheduling.RemainingWork",
            taskExecField
        };
        if (_revisedEstimateField != null)
            fieldList.Add(_revisedEstimateField);
        var fields = string.Join(",", fieldList);

        // Fetch in batches of 200 (Azure DevOps limit)
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
                        allItems.Add(ParseAdoptionWorkItem(wi, taskExecField));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error fetching work items batch starting at index {Index}", i);
            }
        }

        return allItems;
    }

    private AdoptionWorkItem ParseAdoptionWorkItem(JsonElement wi, string taskExecField)
    {
        var fields = wi.GetProperty("fields");

        return new AdoptionWorkItem
        {
            Id = GetIntField(fields, "System.Id"),
            Title = GetStringField(fields, "System.Title"),
            AssignedTo = GetAssignedToName(fields),
            State = GetStringField(fields, "System.State"),
            Reason = GetStringField(fields, "System.Reason"),
            IterationPath = GetStringField(fields, "System.IterationPath"),
            Discipline = GetStringField(fields, "Microsoft.VSTS.Common.Discipline"),
            TaskExecutionType = GetStringField(fields, taskExecField),
            OriginalEstimate = GetDoubleField(fields, "Microsoft.VSTS.Scheduling.OriginalEstimate"),
            RevisedEstimate = _revisedEstimateField != null
                ? GetDoubleField(fields, _revisedEstimateField) : 0,
            CompletedWork = GetDoubleField(fields, "Microsoft.VSTS.Scheduling.CompletedWork"),
            RemainingWork = GetDoubleField(fields, "Microsoft.VSTS.Scheduling.RemainingWork"),
            Tags = GetStringField(fields, "System.Tags")
        };
    }

    private List<MemberAdoptionStats> AggregateMemberStats(List<AdoptionWorkItem> workItems)
    {
        return workItems
            .GroupBy(w => w.AssignedTo)
            .Select(g =>
            {
                var aiTasks = g.Where(w => w.IsAiTask).ToList();
                var manualTasks = g.Where(w => !w.IsAiTask).ToList();

                return new MemberAdoptionStats
                {
                    MemberName = g.Key,
                    Discipline = g.Select(w => w.Discipline).FirstOrDefault(d => !string.IsNullOrEmpty(d)) ?? "",
                    TotalTasks = g.Count(),
                    ClaudeAiTasks = aiTasks.Count,
                    ManualTasks = manualTasks.Count,
                    AiOriginalEstimate = aiTasks.Sum(w => w.EffectiveEstimate),
                    AiCompletedWork = aiTasks.Sum(w => w.CompletedWork),
                    ManualOriginalEstimate = manualTasks.Sum(w => w.EffectiveEstimate),
                    ManualCompletedWork = manualTasks.Sum(w => w.CompletedWork)
                };
            })
            .OrderByDescending(m => m.TotalTasks)
            .ToList();
    }

    private AdoptionSummary BuildSummary(List<MemberAdoptionStats> members)
    {
        var totalAiOrig = members.Sum(m => m.AiOriginalEstimate);
        var totalAiComp = members.Sum(m => m.AiCompletedWork);
        var totalManualOrig = members.Sum(m => m.ManualOriginalEstimate);
        var totalManualComp = members.Sum(m => m.ManualCompletedWork);

        return new AdoptionSummary
        {
            DevMembers = members.Count,
            TotalTasks = members.Sum(m => m.TotalTasks),
            TotalOriginalEstimate = totalAiOrig + totalManualOrig,
            TotalCompletedWork = totalAiComp + totalManualComp,
            ManualEfficiencyPercent = totalManualOrig > 0
                ? Math.Round((totalManualOrig - totalManualComp) / totalManualOrig * 100, 0) : 0,
            AiEfficiencyPercent = totalAiOrig > 0
                ? Math.Round((totalAiOrig - totalAiComp) / totalAiOrig * 100, 0) : 0
        };
    }

    // --- Helper methods ---

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
