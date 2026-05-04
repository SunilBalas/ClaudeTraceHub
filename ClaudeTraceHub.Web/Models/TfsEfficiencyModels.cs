namespace ClaudeTraceHub.Web.Models;

/// <summary>
/// A single field change (CompletedWork or RemainingWork) extracted from the Work Item Updates API.
/// </summary>
public class WorkItemFieldDelta
{
    public int WorkItemId { get; set; }
    public string WorkItemTitle { get; set; } = "";
    public string AssignedTo { get; set; } = "Unassigned";
    public string ChangedBy { get; set; } = "";
    public DateTime RevisedDate { get; set; }

    public double CompletedWorkOld { get; set; }
    public double CompletedWorkNew { get; set; }
    public double CompletedWorkDelta => Math.Round(CompletedWorkNew - CompletedWorkOld, 2);

    public double RemainingWorkOld { get; set; }
    public double RemainingWorkNew { get; set; }
    public double RemainingWorkDelta => Math.Round(RemainingWorkNew - RemainingWorkOld, 2);
}

/// <summary>
/// A single comment on a work item.
/// </summary>
public class WorkItemComment
{
    public int WorkItemId { get; set; }
    public string WorkItemTitle { get; set; } = "";
    public string Text { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedDate { get; set; }
}

/// <summary>
/// Per-task snapshot shown when a member row is expanded.
/// </summary>
public class TaskSummary
{
    public int WorkItemId { get; set; }
    public string Title { get; set; } = "";
    public double OriginalEstimate { get; set; }
    public double CompletedWork { get; set; }
    public double RemainingWork { get; set; }
}

/// <summary>
/// Day-wise breakdown row shown in the expandable detail section.
/// </summary>
public class DayWiseBreakdown
{
    public DateTime Date { get; set; }
    public double CompletedDelta { get; set; }
    public double RemainingDelta { get; set; }
    public int WorkItemsUpdated { get; set; }
    public List<WorkItemFieldDelta> Details { get; set; } = new();
    /// <summary>Comments on this date, keyed by work item ID.</summary>
    public Dictionary<int, List<WorkItemComment>> CommentsByWorkItem { get; set; } = new();
}

/// <summary>
/// Per-member aggregated efficiency data for the main grid row.
/// </summary>
public class MemberDailyEfficiency
{
    public string MemberName { get; set; } = "";
    public double CompletedDelta { get; set; }
    public double RemainingDelta { get; set; }
    public double TotalCompleted { get; set; }
    public double TotalRemaining { get; set; }
    public bool ManagedTfs { get; set; }
    public List<TaskSummary> Tasks { get; set; } = new();
    public List<DayWiseBreakdown> DayHistory { get; set; } = new();
}

/// <summary>
/// Top-level bundle returned by TfsEfficiencyService.
/// </summary>
public class EfficiencyTrackerBundle
{
    public List<MemberDailyEfficiency> MemberStats { get; set; } = new();
    public DateTime SelectedDate { get; set; }
    public string TeamName { get; set; } = "";
    public string IterationPath { get; set; } = "";
    public int TotalTasks { get; set; }
    public int ManagedCount { get; set; }
    public int NotManagedCount { get; set; }
    public string? ErrorMessage { get; set; }
}
