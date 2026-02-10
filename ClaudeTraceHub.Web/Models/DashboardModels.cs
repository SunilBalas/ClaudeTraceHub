namespace ClaudeTraceHub.Web.Models;

public class DashboardStats
{
    public int TotalConversations { get; set; }
    public int TotalMessages { get; set; }
    public long TotalOutputTokens { get; set; }
    public int ActiveProjects { get; set; }
}

public class ConversationsPerDay
{
    public DateTime Date { get; set; }
    public int Count { get; set; }
}

public class MessagesPerProject
{
    public string ProjectName { get; set; } = "";
    public int MessageCount { get; set; }
}

public class TokenUsagePoint
{
    public DateTime Date { get; set; }
    public long OutputTokens { get; set; }
}

public class ModelDistribution
{
    public string ModelName { get; set; } = "";
    public int Count { get; set; }
}
