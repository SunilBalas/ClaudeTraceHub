namespace ClaudeTraceHub.Web.Models;

public class ClaudeAccountInfo
{
    public bool IsLoggedIn { get; set; }
    public string AuthMethod { get; set; } = "";
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Plan { get; set; } = "";
    public string OrganizationName { get; set; } = "";
    public string RateLimitTier { get; set; } = "";
    public DateTime? TokenExpiry { get; set; }
    public bool IsTokenExpired => TokenExpiry.HasValue && TokenExpiry.Value < DateTime.UtcNow;
}
