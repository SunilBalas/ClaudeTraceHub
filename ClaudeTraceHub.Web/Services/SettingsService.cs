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
        var root = await LoadRootAsync();

        root["AzureDevOps"] = JsonSerializer.SerializeToElement(new
        {
            settings.OrganizationUrl,
            settings.PersonalAccessToken,
            settings.Projects
        }, _jsonOptions);

        await SaveRootAsync(root);
        _logger.LogInformation("Azure DevOps settings saved to {Path}", _settingsFilePath);
    }

    public async Task SaveClaudeAiSettingsAsync(ClaudeAiSettings settings)
    {
        var root = await LoadRootAsync();

        root["ClaudeAi"] = JsonSerializer.SerializeToElement(new
        {
            settings.ApiKey,
            settings.Model
        }, _jsonOptions);

        await SaveRootAsync(root);
        _logger.LogInformation("Claude AI settings saved to {Path}", _settingsFilePath);
    }

    private async Task<Dictionary<string, JsonElement>> LoadRootAsync()
    {
        if (!File.Exists(_settingsFilePath))
            return new Dictionary<string, JsonElement>();

        try
        {
            var existing = await File.ReadAllTextAsync(_settingsFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(existing) ?? new();
        }
        catch
        {
            return new Dictionary<string, JsonElement>();
        }
    }

    private async Task SaveRootAsync(Dictionary<string, JsonElement> root)
    {
        var json = JsonSerializer.Serialize(root, _jsonOptions);
        await File.WriteAllTextAsync(_settingsFilePath, json);
    }
}
