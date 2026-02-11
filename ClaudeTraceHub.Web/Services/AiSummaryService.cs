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

    public async Task<string?> GenerateAiDaySummaryAsync(
        DateTime date,
        List<SessionSummary> sessions,
        DailyStats stats,
        bool forceRefresh = false)
    {
        if (!_settings.IsConfigured || sessions.Count == 0)
            return null;

        var cacheKey = $"ai_summary_{date:yyyy-MM-dd}_{stats.ConversationCount}_{stats.MessageCount}";

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

            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Headers.Add("x-api-key", _settings.ApiKey);
            request.Headers.Add("anthropic-version", ApiVersion);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

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
            _logger.LogWarning(ex, "Failed to generate AI summary for {Date}", date);
            return null;
        }
    }

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

            var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", ApiVersion);
            request.Content = new StringContent(
                JsonSerializer.Serialize(requestBody),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
                return (true, "API key is valid.");

            // Read the actual error details from the API response
            var errorDetail = await ExtractErrorMessage(response);

            return response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized =>
                    (false, "Invalid API key. Check the key and try again."),
                System.Net.HttpStatusCode.Forbidden =>
                    (false, "Access denied. Your API key may lack required permissions."),
                _ => (false, errorDetail ?? $"API returned {(int)response.StatusCode}. Please try again.")
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to test Claude API key");
            return (false, "Unable to reach the Anthropic API. Check your network.");
        }
    }

    private static async Task<string?> ExtractErrorMessage(HttpResponseMessage response)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString();
            }
        }
        catch
        {
            // Ignore parse failures
        }
        return null;
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

    private static string BuildUserMessage(
        DateTime date,
        List<SessionSummary> sessions,
        DailyStats stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Date: {date:MMMM dd, yyyy}");
        sb.AppendLine($"Total conversations: {stats.ConversationCount}");
        sb.AppendLine($"Total messages exchanged: {stats.MessageCount}");
        sb.AppendLine($"Projects active: {string.Join(", ", stats.ProjectNames)}");

        // Time window
        var timestamps = sessions
            .Where(s => s.Created.HasValue)
            .Select(s => s.Created!.Value)
            .OrderBy(t => t)
            .ToList();

        if (timestamps.Count >= 2)
        {
            sb.AppendLine($"Activity window: {timestamps.First():HH:mm} to {timestamps.Last():HH:mm}");
        }

        // Conversation topics (first 10)
        sb.AppendLine();
        sb.AppendLine("Conversations:");
        foreach (var session in sessions.Take(10))
        {
            var time = session.Created?.ToString("HH:mm") ?? "??:??";
            var branch = !string.IsNullOrEmpty(session.GitBranch) ? $" [{session.GitBranch}]" : "";
            sb.AppendLine($"- [{time}] {session.ProjectName}: {session.FirstPrompt}{branch}");
        }

        if (sessions.Count > 10)
        {
            sb.AppendLine($"... and {sessions.Count - 10} more conversations");
        }

        // Git branches
        var branches = sessions
            .Where(s => !string.IsNullOrEmpty(s.GitBranch))
            .Select(s => s.GitBranch!)
            .Distinct()
            .ToList();

        if (branches.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Git branches: {string.Join(", ", branches)}");
        }

        return sb.ToString();
    }

    private static string? ExtractTextFromResponse(string responseJson)
    {
        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;

        if (root.TryGetProperty("content", out var content) && content.GetArrayLength() > 0)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var type) &&
                    type.GetString() == "text" &&
                    block.TryGetProperty("text", out var text))
                {
                    return text.GetString()?.Trim();
                }
            }
        }

        return null;
    }
}
