namespace ClaudeTraceHub.Web.Models;

public class TaskPlanningBundle
{
    public List<ParentPlanningRow> Parents { get; set; } = new();
    public string TeamName { get; set; } = "";
    public string IterationPath { get; set; } = "";
    public int TotalParents { get; set; }
    public int TotalSuggested { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ParentPlanningRow
{
    public int WorkItemId { get; set; }
    public string Title { get; set; } = "";
    public string State { get; set; } = "";
    public string AssignedTo { get; set; } = "Unassigned";
    public string WorkItemType { get; set; } = "";
    public string Tags { get; set; } = "";
    public List<SuggestedTask> SuggestedTasks { get; set; } = new();
}

public class SuggestedTask
{
    public string TaskType { get; set; } = "";
    public string Title { get; set; } = "";
    public string Discipline { get; set; } = "";
    public string TaskExecutionType { get; set; } = "";
    public string Tag { get; set; } = "";
}
