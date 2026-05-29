namespace ClaudeTraceHub.Web.Models;

public class PlanningVerificationBundle
{
    public List<RequirementVerificationRow> Requirements { get; set; } = new();
    public string TeamName { get; set; } = "";
    public string IterationPath { get; set; } = "";
    public int TotalRequirements { get; set; }
    public int TotalTasks { get; set; }
    public int RequirementsWithIssues { get; set; }
    public int TasksWithIssues { get; set; }
    public string? ErrorMessage { get; set; }
}

public class RequirementVerificationRow
{
    public int WorkItemId { get; set; }
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
    public string AssignedTo { get; set; } = "Unassigned";
    public string WorkItemType { get; set; } = "";
    public string Tags { get; set; } = "";
    public List<ValidationIssue> Issues { get; set; } = new();
    public List<TaskVerificationRow> Tasks { get; set; } = new();

    public bool HasIssues => Issues.Count > 0 || Tasks.Any(t => t.Issues.Count > 0);
    public int IssueCount => Issues.Count + Tasks.Sum(t => t.Issues.Count);
    public int TasksWithIssuesCount => Tasks.Count(t => t.Issues.Count > 0);
}

public class TaskVerificationRow
{
    public int WorkItemId { get; set; }
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
    public string AssignedTo { get; set; } = "Unassigned";
    public string Tags { get; set; } = "";
    public string Discipline { get; set; } = "";
    public string TaskExecutionType { get; set; } = "";
    public double OriginalEstimate { get; set; }
    public double RemainingWork { get; set; }
    public string DetectedTaskType { get; set; } = "";
    public List<ValidationIssue> Issues { get; set; } = new();
}

public class ValidationIssue
{
    public string Rule { get; set; } = "";
    public string Severity { get; set; } = "Error";
    public string Message { get; set; } = "";
}
