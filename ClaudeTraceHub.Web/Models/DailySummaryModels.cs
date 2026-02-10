namespace ClaudeTraceHub.Web.Models;

/// <summary>
/// Top-level daily aggregate stats, computed from lightweight SessionSummary data.
/// </summary>
public class DailyStats
{
    public DateTime Date { get; set; }
    public int ConversationCount { get; set; }
    public int MessageCount { get; set; }
    public int ProjectCount { get; set; }
    public List<string> ProjectNames { get; set; } = new();
}

/// <summary>
/// Extended per-conversation detail, populated on-demand when user expands a conversation card.
/// </summary>
public class DailyConversationDetail
{
    public string SessionId { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string ProjectDirName { get; set; } = "";
    public string FirstPrompt { get; set; } = "";
    public string? GitBranch { get; set; }
    public DateTime? Started { get; set; }
    public DateTime? Ended { get; set; }
    public TimeSpan? Duration { get; set; }
    public int MessageCount { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens => InputTokens + OutputTokens;
    public string? Model { get; set; }
    public List<FileTouchedInfo> FilesTouched { get; set; } = new();
    public Dictionary<string, int> ToolUsageSummary { get; set; } = new();
}

/// <summary>
/// Aggregated file activity across all conversations for a given day.
/// </summary>
public class DailyFileActivity
{
    public string FilePath { get; set; } = "";
    public FileActionType Action { get; set; }
    public int TotalOperations { get; set; }
    public List<string> SessionIds { get; set; } = new();
}

/// <summary>
/// Hourly token usage data point for the bar chart.
/// </summary>
public class HourlyTokenUsage
{
    public int Hour { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
}

/// <summary>
/// Per-day activity point for the 30-day mini chart.
/// </summary>
public class DailyActivityPoint
{
    public DateTime Date { get; set; }
    public int ConversationCount { get; set; }
    public int MessageCount { get; set; }
}
