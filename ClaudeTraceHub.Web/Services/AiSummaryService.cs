using System.Text;
using System.Text.Json;
using ClaudeTraceHub.Web.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace ClaudeTraceHub.Web.Services;

public class AiSummaryService
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    private readonly ClaudeAiSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AiSummaryService> _logger;

    public AiSummaryService(
        IOptionsSnapshot<ClaudeAiSettings> settings,
        HttpClient httpClient,
        IMemoryCache cache,
        ILogger<AiSummaryService> logger)
    {
        _settings = settings.Value;
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public bool IsConfigured => _settings.IsConfigured;

    /// <summary>
    /// Generate an AI-powered day summary using the Claude API.
    /// Returns null on failure (caller should fall back to local summary).
    /// </summary>
    public async Task<string?> GenerateAiDaySummaryAsync(
        DateTime date,
        List<SessionSummary> sessions,
        DailyStats stats,
        bool forceRefresh = false)
    {
        if (!_settings.IsConfigured || sessions.Count == 0)
            return null;

        var cacheKey = $"ai_summary_{date:yyyy-MM-dd}_{stats.ConversationCount}";

        if (!forceRefresh && _cache.TryGetValue(cacheKey, out string? cached))
            return cached;

        try
        {
            var systemPrompt = BuildSystemPrompt();
            var userMessage = BuildUserMessage(date, sessions, stats);

            var requestBody = new
            {
                model = _settings.Model,
                max_tokens = 250,
                system = systemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userMessage }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-api-key", _settings.ApiKey);
            request.Headers.Add("anthropic-version", ApiVersion);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var summary = ExtractTextFromResponse(responseJson);

            if (!string.IsNullOrEmpty(summary))
            {
                _cache.Set(cacheKey, summary, new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromMinutes(30)
                });
            }

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to generate AI summary for {Date}", date.ToString("yyyy-MM-dd"));
            return null;
        }
    }

    /// <summary>
    /// Test the API key by making a minimal request.
    /// </summary>
    public async Task<(bool Success, string Message)> TestApiKeyAsync(string apiKey, string model)
    {
        try
        {
            var requestBody = new
            {
                model,
                max_tokens = 10,
                messages = new[]
                {
                    new { role = "user", content = "Say OK" }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", ApiVersion);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
                return (true, "API key is valid.");

            var errorBody = await response.Content.ReadAsStringAsync();
            if ((int)response.StatusCode == 401)
                return (false, "Invalid API key. Check that your key is correct.");
            if ((int)response.StatusCode == 403)
                return (false, "Access denied. Your API key may lack required permissions.");

            return (false, $"API returned {(int)response.StatusCode}: {errorBody}");
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Network error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Unexpected error: {ex.Message}");
        }
    }

    private static string BuildSystemPrompt()
    {
        return """
            You are a developer productivity assistant integrated into a coding activity dashboard.
            Write a brief, natural-language paragraph (2-4 sentences) summarizing the developer's day
            based on the data provided. Write in second person ("You worked on..."). Be specific about
            what was accomplished, referencing project names, topics, and key activities. Keep the tone
            professional and concise, like a developer journal entry. Do not use bullet points or lists.
            Do not add any greeting or sign-off.
            """;
    }

    private static string BuildUserMessage(DateTime date, List<SessionSummary> sessions, DailyStats stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Date: {date:MMMM dd, yyyy}");
        sb.AppendLine($"Total conversations: {stats.ConversationCount}");
        sb.AppendLine($"Total messages: {stats.MessageCount}");
        sb.AppendLine($"Projects active: {string.Join(", ", stats.ProjectNames)}");

        // Time range
        var times = sessions.Where(s => s.Created.HasValue).Select(s => s.Created!.Value).ToList();
        if (times.Count > 0)
        {
            sb.AppendLine($"Activity window: {times.Min():HH:mm} to {times.Max():HH:mm}");
        }

        // Topics (first prompts)
        sb.AppendLine("\nConversation topics:");
        foreach (var session in sessions.Take(10))
        {
            var prompt = session.FirstPrompt.Length > 100
                ? session.FirstPrompt[..100] + "..."
                : session.FirstPrompt;
            var time = session.Created?.ToString("HH:mm") ?? "?";
            sb.AppendLine($"- [{time}] [{session.ProjectName}] {prompt}");
        }

        if (sessions.Count > 10)
            sb.AppendLine($"  ... and {sessions.Count - 10} more conversations");

        // Git branches
        var branches = sessions
            .Where(s => !string.IsNullOrEmpty(s.GitBranch))
            .Select(s => s.GitBranch!)
            .Distinct()
            .ToList();

        if (branches.Count > 0)
        {
            sb.AppendLine($"\nGit branches: {string.Join(", ", branches)}");
        }

        return sb.ToString();
    }

    private static string? ExtractTextFromResponse(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            var content = doc.RootElement.GetProperty("content");
            foreach (var block in content.EnumerateArray())
            {
                if (block.GetProperty("type").GetString() == "text")
                {
                    return block.GetProperty("text").GetString();
                }
            }
        }
        catch (Exception)
        {
            // Fall through
        }

        return null;
    }
}
