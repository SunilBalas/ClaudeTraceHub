using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using ClaudeTraceHub.Web.Models;

namespace ClaudeTraceHub.Web.Services;

public class ClaudeAccountService
{
    private static readonly string CredentialsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    private const string ProfileEndpoint = "https://claude.ai/api/oauth/profile";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly HttpClient _http = new();
    private ClaudeAccountInfo? _cached;
    private DateTime _cacheExpiry = DateTime.MinValue;

    public async Task<ClaudeAccountInfo> GetAccountInfoAsync()
    {
        if (_cached != null && DateTime.UtcNow < _cacheExpiry)
            return _cached;

        var local = ReadLocalCredentials();
        if (!local.IsLoggedIn)
        {
            _cached = local;
            _cacheExpiry = DateTime.UtcNow + CacheDuration;
            return _cached;
        }

        var token = ReadAccessToken();
        if (token is null)
        {
            _cached = local;
            _cacheExpiry = DateTime.UtcNow + CacheDuration;
            return _cached;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ProfileEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.UserAgent.ParseAdd("claude-cli/2.1.123");

            using var response = await _http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var enriched = ParseProfile(json, local);
                _cached = enriched;
                _cacheExpiry = DateTime.UtcNow + CacheDuration;
                return _cached;
            }
        }
        catch { }

        // Network failed — return what we have locally
        _cached = local;
        _cacheExpiry = DateTime.UtcNow + TimeSpan.FromMinutes(1);
        return _cached;
    }

    private static ClaudeAccountInfo ReadLocalCredentials()
    {
        if (!File.Exists(CredentialsPath))
            return new ClaudeAccountInfo { IsLoggedIn = false };

        try
        {
            var json = File.ReadAllText(CredentialsPath);
            var root = JsonNode.Parse(json);

            if (root?["claudeAiOauth"] is JsonNode oauth)
            {
                var subscriptionType = oauth["subscriptionType"]?.GetValue<string>() ?? "";
                var rateLimitTier = oauth["rateLimitTier"]?.GetValue<string>() ?? "";
                var expiresAt = oauth["expiresAt"]?.GetValue<long>();

                return new ClaudeAccountInfo
                {
                    IsLoggedIn = true,
                    AuthMethod = "Claude AI",
                    Plan = MapPlan(subscriptionType),
                    RateLimitTier = MapRateLimitTier(rateLimitTier),
                    TokenExpiry = expiresAt.HasValue
                        ? DateTimeOffset.FromUnixTimeMilliseconds(expiresAt.Value).UtcDateTime
                        : null
                };
            }
        }
        catch { }

        return new ClaudeAccountInfo { IsLoggedIn = false };
    }

    private static string? ReadAccessToken()
    {
        try
        {
            var json = File.ReadAllText(CredentialsPath);
            var root = JsonNode.Parse(json);
            return root?["claudeAiOauth"]?["accessToken"]?.GetValue<string>();
        }
        catch { return null; }
    }

    private static ClaudeAccountInfo ParseProfile(string json, ClaudeAccountInfo local)
    {
        try
        {
            var root = JsonNode.Parse(json);
            var account = root?["account"];
            var org = root?["organization"];

            var subscriptionType = org?["organization_type"]?.GetValue<string>() ?? "";
            var rateLimitTier = org?["rate_limit_tier"]?.GetValue<string>() ?? local.RateLimitTier;

            return new ClaudeAccountInfo
            {
                IsLoggedIn = true,
                AuthMethod = local.AuthMethod,
                Email = account?["email"]?.GetValue<string>() ?? "",
                DisplayName = account?["display_name"]?.GetValue<string>() ?? "",
                OrganizationName = org?["name"]?.GetValue<string>() ?? "",
                Plan = MapPlan(subscriptionType),
                RateLimitTier = MapRateLimitTier(rateLimitTier),
                TokenExpiry = local.TokenExpiry
            };
        }
        catch { return local; }
    }

    private static string MapPlan(string raw) => raw.ToLowerInvariant() switch
    {
        "free" => "Free",
        "pro" => "Claude Pro",
        "claude_team" or "team" => "Claude Team",
        "enterprise" => "Claude Enterprise",
        "max" => "Claude Max",
        _ when !string.IsNullOrEmpty(raw) => raw.Replace("_", " "),
        _ => "Unknown"
    };

    private static string MapRateLimitTier(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "";
        return raw
            .Replace("default_claude_", "Claude ")
            .Replace("default", "Standard")
            .Replace("_", " ");
    }
}
