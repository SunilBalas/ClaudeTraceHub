namespace ClaudeTraceHub.Web.Models;

public class TfsWorkItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string WorkItemType { get; set; } = "";
    public string State { get; set; } = "";
    public string AssignedTo { get; set; } = "";
    public string Url { get; set; } = "";
    public string ProjectName { get; set; } = "";
}

public enum DiscoveryPath
{
    None,
    LinkedViaPullRequest,
    ExtractedFromBranchName,
    NotFound
}

public class TfsQueryResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string BranchName { get; set; } = "";
    public DiscoveryPath Path { get; set; } = DiscoveryPath.None;
    public List<string> DiscoveryNotes { get; set; } = new();
    public List<int> ExtractedWorkItemIds { get; set; } = new();
    public List<TfsWorkItem> WorkItemsFromBranch { get; set; } = new();
    public List<TfsWorkItem> WorkItemsFromPullRequests { get; set; } = new();

    public List<TfsWorkItem> AllWorkItems =>
        WorkItemsFromBranch
            .Concat(WorkItemsFromPullRequests)
            .GroupBy(w => w.Id)
            .Select(g => g.First())
            .OrderBy(w => w.WorkItemType)
            .ThenBy(w => w.Id)
            .ToList();

    public List<TfsWorkItem> Requirements =>
        AllWorkItems.Where(w =>
            w.WorkItemType.Contains("Requirement", StringComparison.OrdinalIgnoreCase)
            || w.WorkItemType.Contains("User Story", StringComparison.OrdinalIgnoreCase)
            || w.WorkItemType.Contains("Product Backlog Item", StringComparison.OrdinalIgnoreCase))
        .ToList();

    public List<TfsWorkItem> ChangeRequests =>
        AllWorkItems.Where(w =>
            w.WorkItemType.Contains("Change Request", StringComparison.OrdinalIgnoreCase)
            || w.WorkItemType.Contains("Task", StringComparison.OrdinalIgnoreCase))
        .ToList();

    public List<TfsWorkItem> Bugs =>
        AllWorkItems.Where(w =>
            w.WorkItemType.Contains("Bug", StringComparison.OrdinalIgnoreCase))
        .ToList();

    public List<TfsWorkItem> Other
    {
        get
        {
            var knownIds = new HashSet<int>(
                Requirements.Select(r => r.Id)
                    .Concat(ChangeRequests.Select(c => c.Id))
                    .Concat(Bugs.Select(b => b.Id)));
            return AllWorkItems.Where(w => !knownIds.Contains(w.Id)).ToList();
        }
    }
}

public class BranchSessionGroup
{
    public string BranchName { get; set; } = "";
    public List<SessionSummary> Sessions { get; set; } = new();
}

public class WorkItemConversationLink
{
    public TfsWorkItem WorkItem { get; set; } = new();
    public List<SessionSummary> LinkedSessions { get; set; } = new();
    public List<string> BranchNames { get; set; } = new();
    public DiscoveryPath DiscoveryPath { get; set; }
}

public class WorkItemScanResult
{
    public List<WorkItemConversationLink> WorkItemLinks { get; set; } = new();
    public List<SessionSummary> UnlinkedSessions { get; set; } = new();
    public int TotalBranchesScanned { get; set; }
}

public class ScanProgress
{
    public int ScannedBranches { get; set; }
    public int TotalBranches { get; set; }
    public int WorkItemsFound { get; set; }
    public string CurrentBranch { get; set; } = "";
    public double PercentComplete => TotalBranches > 0 ? (double)ScannedBranches / TotalBranches * 100 : 0;
}
