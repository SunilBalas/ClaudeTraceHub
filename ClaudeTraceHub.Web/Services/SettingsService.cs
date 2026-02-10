using System.Text.Json;
using System.Text.Json.Serialization;
using ClaudeTraceHub.Web.Models;

namespace ClaudeTraceHub.Web.Services;

public class SettingsService
{
    private readonly string _settingsFilePath;
    private readonly ILogger<SettingsService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SettingsService(IWebHostEnvironment env, ILogger<SettingsService> logger)
    {
        _logger = logger;
        _settingsFilePath = Path.Combine(env.ContentRootPath, "usersettings.json");
    }

    public async Task SaveAzureDevOpsSettingsAsync(AzureDevOpsSettings settings)
    {
        var root = new Dictionary<string, object>
        {
            ["AzureDevOps"] = new
            {
                settings.OrganizationUrl,
                settings.PersonalAccessToken,
                settings.Projects
            }
        };

        var json = JsonSerializer.Serialize(root, _jsonOptions);
        await File.WriteAllTextAsync(_settingsFilePath, json);
        _logger.LogInformation("Azure DevOps settings saved to {Path}", _settingsFilePath);
    }
}
