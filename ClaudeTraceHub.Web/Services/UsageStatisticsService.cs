using ClaudeTraceHub.Web.Models;

namespace ClaudeTraceHub.Web.Services;

public class UsageStatisticsService
{
    private readonly ClaudeDataDiscoveryService _discovery;
    private readonly ConversationCacheService _cache;

    public UsageStatisticsService(
        ClaudeDataDiscoveryService discovery,
        ConversationCacheService cache)
    {
        _discovery = discovery;
        _cache = cache;
    }

    public UsageDataBundle GetUsageData(DateTime startDate, DateTime endDate)
    {
        var sessions = _discovery.GetAllSessions()
            .Where(s => s.Created.HasValue
                        && s.Created.Value.Date >= startDate.Date
                        && s.Created.Value.Date <= endDate.Date)
            .ToList();

        var branchMap = new Dictionary<string, BranchUsageStats>();
        var modelMap = new Dictionary<string, ModelUsageStats>();
        var dailyMap = new Dictionary<DateTime, DailyUsagePoint>();
        var dailyModelMap = new Dictionary<(DateTime, string), DailyModelUsagePoint>();
        var projectMap = new Dictionary<string, ProjectUsageStats>();
        var hourlyTokens = new long[24];
        var hourlyCounts = new int[24];
        var toolMap = new Dictionary<string, (int invocations, HashSet<string> sessions)>();

        long totalInput = 0, totalOutput = 0;
        int totalMessages = 0;

        foreach (var session in sessions)
        {
            if (!File.Exists(session.FilePath)) continue;

            var conv = _cache.GetOrParse(session.FilePath, session.ProjectName, session.ProjectDirName);

            var branch = session.GitBranch ?? "(no branch)";
            var project = session.ProjectName;
            var convInput = conv.TotalInputTokens;
            var convOutput = conv.TotalOutputTokens;
            var convMessages = conv.Messages.Count;
            var convDate = session.Created!.Value.Date;

            totalInput += convInput;
            totalOutput += convOutput;
            totalMessages += convMessages;

            // Branch aggregation
            if (!branchMap.TryGetValue(branch, out var branchStats))
            {
                branchStats = new BranchUsageStats { BranchName = branch };
                branchMap[branch] = branchStats;
            }
            branchStats.ConversationCount++;
            branchStats.MessageCount += convMessages;
            branchStats.InputTokens += convInput;
            branchStats.OutputTokens += convOutput;

            // Project aggregation
            if (!projectMap.TryGetValue(project, out var projStats))
            {
                projStats = new ProjectUsageStats
                {
                    ProjectName = project,
                    ProjectDirName = session.ProjectDirName
                };
                projectMap[project] = projStats;
            }
            projStats.ConversationCount++;
            projStats.MessageCount += convMessages;
            projStats.InputTokens += convInput;
            projStats.OutputTokens += convOutput;

            // Daily aggregation
            if (!dailyMap.TryGetValue(convDate, out var dayStats))
            {
                dayStats = new DailyUsagePoint { Date = convDate };
                dailyMap[convDate] = dayStats;
            }
            dayStats.ConversationCount++;
            dayStats.InputTokens += convInput;
            dayStats.OutputTokens += convOutput;
            dayStats.MessageCount += convMessages;

            // Hourly: count conversation start hour
            if (session.Created.HasValue)
            {
                var hour = session.Created.Value.Hour;
                hourlyCounts[hour]++;
            }

            // Per-message: model breakdown, hourly tokens, daily-model, tool usage
            var convModelsSeen = new HashSet<string>();
            foreach (var msg in conv.Messages)
            {
                if (msg.Role != "assistant") continue;

                var model = SimplifyModelName(msg.Model);
                var msgInput = msg.InputTokens ?? 0;
                var msgOutput = msg.OutputTokens ?? 0;
                var msgTokens = (long)msgInput + msgOutput;

                // Model aggregation (message-level)
                if (!modelMap.TryGetValue(model, out var modelStats))
                {
                    modelStats = new ModelUsageStats { ModelName = model };
                    modelMap[model] = modelStats;
                }
                modelStats.MessageCount++;
                modelStats.InputTokens += msgInput;
                modelStats.OutputTokens += msgOutput;

                // Track unique models per conversation
                convModelsSeen.Add(model);

                // Daily-model aggregation
                var dmKey = (convDate, model);
                if (!dailyModelMap.TryGetValue(dmKey, out var dmPoint))
                {
                    dmPoint = new DailyModelUsagePoint { Date = convDate, ModelName = model };
                    dailyModelMap[dmKey] = dmPoint;
                }
                dmPoint.TotalTokens += msgTokens;

                // Hourly token aggregation
                var msgHour = msg.Timestamp.Hour;
                hourlyTokens[msgHour] += msgTokens;

                // Tool usage
                foreach (var tool in msg.ToolUsages)
                {
                    if (!toolMap.TryGetValue(tool.ToolName, out var toolEntry))
                    {
                        toolEntry = (0, new HashSet<string>());
                    }
                    toolEntry.invocations++;
                    toolEntry.sessions.Add(session.SessionId);
                    toolMap[tool.ToolName] = toolEntry;
                }
            }

            // Model conversation count (per-conv, not per-message)
            foreach (var m in convModelsSeen)
            {
                if (modelMap.TryGetValue(m, out var ms))
                    ms.ConversationCount++;
            }
        }

        // Assemble summary
        var summary = new UsageSummaryStats
        {
            TotalInputTokens = totalInput,
            TotalOutputTokens = totalOutput,
            TotalConversations = sessions.Count,
            TotalMessages = totalMessages
        };

        // Branch stats sorted by total tokens
        var branchStats2 = branchMap.Values
            .OrderByDescending(b => b.TotalTokens)
            .ToList();

        // Model stats with percentages
        var totalModelTokens = modelMap.Values.Sum(m => m.TotalTokens);
        var modelStats2 = modelMap.Values
            .Select(m =>
            {
                m.Percentage = totalModelTokens > 0
                    ? (double)m.TotalTokens / totalModelTokens * 100 : 0;
                return m;
            })
            .OrderByDescending(m => m.TotalTokens)
            .ToList();

        // Daily usage (fill gaps with zeroes)
        var dailyList = new List<DailyUsagePoint>();
        for (var d = startDate.Date; d <= endDate.Date; d = d.AddDays(1))
        {
            dailyList.Add(dailyMap.GetValueOrDefault(d)
                          ?? new DailyUsagePoint { Date = d });
        }

        // Daily model usage
        var dailyModelList = dailyModelMap.Values
            .OrderBy(d => d.Date)
            .ThenBy(d => d.ModelName)
            .ToList();

        // Project stats
        var projectStats2 = projectMap.Values
            .OrderByDescending(p => p.TotalTokens)
            .ToList();

        // Hourly usage
        var hourlyList = Enumerable.Range(0, 24)
            .Select(h => new HourlyUsagePoint
            {
                Hour = h,
                TotalTokens = hourlyTokens[h],
                ConversationCount = hourlyCounts[h]
            })
            .ToList();

        // Tool usage (top 15)
        var toolList = toolMap
            .Select(kv => new ToolUsageBreakdown
            {
                ToolName = kv.Key,
                InvocationCount = kv.Value.invocations,
                ConversationCount = kv.Value.sessions.Count
            })
            .OrderByDescending(t => t.InvocationCount)
            .Take(15)
            .ToList();

        return new UsageDataBundle
        {
            Summary = summary,
            BranchStats = branchStats2,
            ModelStats = modelStats2,
            DailyUsage = dailyList,
            DailyModelUsage = dailyModelList,
            ProjectStats = projectStats2,
            HourlyUsage = hourlyList,
            ToolUsage = toolList
        };
    }

    private static string SimplifyModelName(string? model)
    {
        if (string.IsNullOrEmpty(model)) return "Unknown";
        if (model.Contains("opus")) return "Claude Opus";
        if (model.Contains("sonnet")) return "Claude Sonnet";
        if (model.Contains("haiku")) return "Claude Haiku";
        return model;
    }
}
