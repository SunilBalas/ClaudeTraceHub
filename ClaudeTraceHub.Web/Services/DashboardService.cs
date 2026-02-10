using ClaudeTraceHub.Web.Models;

namespace ClaudeTraceHub.Web.Services;

public class DashboardService
{
    private readonly ClaudeDataDiscoveryService _discovery;
    private readonly ConversationCacheService _cache;

    public DashboardService(ClaudeDataDiscoveryService discovery, ConversationCacheService cache)
    {
        _discovery = discovery;
        _cache = cache;
    }

    public DashboardStats GetStats()
    {
        var projects = _discovery.GetAllProjects();
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        return new DashboardStats
        {
            TotalConversations = projects.Sum(p => p.SessionCount),
            TotalMessages = projects.Sum(p => p.TotalMessages),
            ActiveProjects = projects.Count(p => p.LastActivity.HasValue && p.LastActivity.Value > thirtyDaysAgo),
            TotalOutputTokens = 0 // Computed lazily if needed
        };
    }

    public List<ConversationsPerDay> GetConversationsPerDay(int days = 30)
    {
        var sessions = _discovery.GetAllSessions();
        var cutoff = DateTime.UtcNow.Date.AddDays(-days);

        var grouped = sessions
            .Where(s => s.Created.HasValue && s.Created.Value.Date >= cutoff)
            .GroupBy(s => s.Created!.Value.Date)
            .Select(g => new ConversationsPerDay
            {
                Date = g.Key,
                Count = g.Count()
            })
            .OrderBy(c => c.Date)
            .ToList();

        // Fill in missing days with 0
        var result = new List<ConversationsPerDay>();
        for (var date = cutoff; date <= DateTime.UtcNow.Date; date = date.AddDays(1))
        {
            var existing = grouped.FirstOrDefault(g => g.Date == date);
            result.Add(new ConversationsPerDay
            {
                Date = date,
                Count = existing?.Count ?? 0
            });
        }

        return result;
    }

    public List<MessagesPerProject> GetMessagesPerProject()
    {
        var projects = _discovery.GetAllProjects();
        return projects
            .Where(p => p.TotalMessages > 0)
            .Select(p => new MessagesPerProject
            {
                ProjectName = p.ProjectName,
                MessageCount = p.TotalMessages
            })
            .OrderByDescending(m => m.MessageCount)
            .Take(10)
            .ToList();
    }

    public List<SessionSummary> GetRecentConversations(int count = 20)
    {
        return _discovery.GetAllSessions().Take(count).ToList();
    }

    public List<ModelDistribution> GetModelDistribution()
    {
        var sessions = _discovery.GetAllSessions();
        var modelCounts = new Dictionary<string, int>();

        // Sample up to 50 recent conversations for model info
        foreach (var session in sessions.Take(50))
        {
            if (!File.Exists(session.FilePath)) continue;

            var conv = _cache.GetOrParse(session.FilePath, session.ProjectName, session.ProjectDirName);
            var firstAssistant = conv.Messages.FirstOrDefault(m => m.Role == "assistant" && !string.IsNullOrEmpty(m.Model));
            if (firstAssistant?.Model != null)
            {
                var modelName = SimplifyModelName(firstAssistant.Model);
                modelCounts[modelName] = modelCounts.GetValueOrDefault(modelName) + 1;
            }
        }

        return modelCounts
            .Select(kv => new ModelDistribution { ModelName = kv.Key, Count = kv.Value })
            .OrderByDescending(m => m.Count)
            .ToList();
    }

    private static string SimplifyModelName(string model)
    {
        if (model.Contains("opus")) return "Claude Opus";
        if (model.Contains("sonnet")) return "Claude Sonnet";
        if (model.Contains("haiku")) return "Claude Haiku";
        return model;
    }
}
