namespace ClaudeTraceHub.Web.Models;

/// <summary>
/// Represents a single work item fetched from Azure DevOps for AI adoption analysis.
/// </summary>
public class AdoptionWorkItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string AssignedTo { get; set; } = "Unassigned";
    public string Discipline { get; set; } = "";
    public string TaskExecutionType { get; set; } = "";
    public string State { get; set; } = "";
    public string Reason { get; set; } = "";
    public string IterationPath { get; set; } = "";
    public double OriginalEstimate { get; set; }
    public double RevisedEstimate { get; set; }
    public double CompletedWork { get; set; }
    public double RemainingWork { get; set; }
    public string Tags { get; set; } = "";

    /// <summary>
    /// A task is AI if it has the "Claude AI" tag.
    /// </summary>
    public bool IsAiTask => Tags.Contains("Claude AI", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// If the task has "Change in Scope" tag and a Revised Estimate, use that instead of Original Estimate.
    /// </summary>
    public bool HasChangeInScope => Tags.Contains("Change in Scope", StringComparison.OrdinalIgnoreCase);

    public double EffectiveEstimate => HasChangeInScope && RevisedEstimate > 0
        ? RevisedEstimate
        : OriginalEstimate;
}

/// <summary>
/// Per-member aggregated AI adoption statistics.
/// </summary>
public class MemberAdoptionStats
{
    public string MemberName { get; set; } = "";
    public string Discipline { get; set; } = "";
    public int TotalTasks { get; set; }
    public int ClaudeAiTasks { get; set; }
    public int ManualTasks { get; set; }

    // AI hours
    public double AiOriginalEstimate { get; set; }
    public double AiCompletedWork { get; set; }

    // Manual hours
    public double ManualOriginalEstimate { get; set; }
    public double ManualCompletedWork { get; set; }

    // Calculated fields
    public double AiAdoptionPercent => TotalTasks > 0
        ? Math.Round((double)ClaudeAiTasks / TotalTasks * 100, 0) : 0;

    public double ManualEfficiencyPercent => ManualOriginalEstimate > 0
        ? Math.Round((ManualOriginalEstimate - ManualCompletedWork) / ManualOriginalEstimate * 100, 0) : 0;

    public double AiEfficiencyPercent => AiOriginalEstimate > 0
        ? Math.Round((AiOriginalEstimate - AiCompletedWork) / AiOriginalEstimate * 100, 0) : 0;

    public double HoursSaved => Math.Round(Math.Max(0, AiOriginalEstimate - AiCompletedWork), 2);
}

/// <summary>
/// Overall summary totals for the AI adoption page.
/// </summary>
public class AdoptionSummary
{
    public int DevMembers { get; set; }
    public int TotalTasks { get; set; }
    public double TotalOriginalEstimate { get; set; }
    public double TotalCompletedWork { get; set; }
    public double ManualEfficiencyPercent { get; set; }
    public double AiEfficiencyPercent { get; set; }
}

/// <summary>
/// Bundle containing all data for the AI Adoption page.
/// </summary>
public class AdoptionDataBundle
{
    public AdoptionSummary Summary { get; set; } = new();
    public List<MemberAdoptionStats> MemberStats { get; set; } = new();
    public List<AdoptionWorkItem> RawWorkItems { get; set; } = new();
    public List<string> AvailableIterations { get; set; } = new();
    public List<string> AvailableDisciplines { get; set; } = new();
    public string? ErrorMessage { get; set; }
}
