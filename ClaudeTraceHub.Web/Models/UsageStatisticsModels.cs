namespace ClaudeTraceHub.Web.Models;

public class UsageSummaryStats
{
    public long TotalInputTokens { get; set; }
    public long TotalOutputTokens { get; set; }
    public long TotalTokens => TotalInputTokens + TotalOutputTokens;
    public int TotalConversations { get; set; }
    public int TotalMessages { get; set; }
    public double AvgTokensPerConversation => TotalConversations > 0
        ? (double)TotalTokens / TotalConversations : 0;
    public double AvgTokensPerMessage => TotalMessages > 0
        ? (double)TotalTokens / TotalMessages : 0;
}

public class BranchUsageStats
{
    public string BranchName { get; set; } = "";
    public int ConversationCount { get; set; }
    public int MessageCount { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens => InputTokens + OutputTokens;
}

public class ModelUsageStats
{
    public string ModelName { get; set; } = "";
    public int ConversationCount { get; set; }
    public int MessageCount { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens => InputTokens + OutputTokens;
    public double Percentage { get; set; }
}

public class DailyUsagePoint
{
    public DateTime Date { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens => InputTokens + OutputTokens;
    public int ConversationCount { get; set; }
    public int MessageCount { get; set; }
}

public class DailyModelUsagePoint
{
    public DateTime Date { get; set; }
    public string ModelName { get; set; } = "";
    public long TotalTokens { get; set; }
}

public class ProjectUsageStats
{
    public string ProjectName { get; set; } = "";
    public string ProjectDirName { get; set; } = "";
    public int ConversationCount { get; set; }
    public int MessageCount { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public long TotalTokens => InputTokens + OutputTokens;
}

public class HourlyUsagePoint
{
    public int Hour { get; set; }
    public long TotalTokens { get; set; }
    public int ConversationCount { get; set; }
}

public class ToolUsageBreakdown
{
    public string ToolName { get; set; } = "";
    public int InvocationCount { get; set; }
    public int ConversationCount { get; set; }
}

public class UsageDataBundle
{
    public UsageSummaryStats Summary { get; set; } = new();
    public List<BranchUsageStats> BranchStats { get; set; } = new();
    public List<ModelUsageStats> ModelStats { get; set; } = new();
    public List<DailyUsagePoint> DailyUsage { get; set; } = new();
    public List<DailyModelUsagePoint> DailyModelUsage { get; set; } = new();
    public List<ProjectUsageStats> ProjectStats { get; set; } = new();
    public List<HourlyUsagePoint> HourlyUsage { get; set; } = new();
    public List<ToolUsageBreakdown> ToolUsage { get; set; } = new();
}
