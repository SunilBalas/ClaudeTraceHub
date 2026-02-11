using MudBlazor.Services;
using ClaudeTraceHub.Web.Components;
using ClaudeTraceHub.Web.Models;
using ClaudeTraceHub.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor
builder.Services.AddMudServices();

// Application services
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ClaudeDataDiscoveryService>();
builder.Services.AddSingleton<JsonlParserService>();
builder.Services.AddSingleton<ConversationCacheService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<DailySummaryService>();
builder.Services.AddScoped<ExcelExportService>();
builder.Services.AddScoped<ThemeService>();
builder.Services.AddScoped<TfsWorkItemFilterService>();

// Data refresh (filesystem watcher for live updates)
builder.Services.AddSingleton<DataRefreshService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<DataRefreshService>());

// User settings (layered on top of appsettings.json, reloads on save)
builder.Configuration.AddJsonFile("usersettings.json", optional: true, reloadOnChange: true);
builder.Services.AddSingleton<SettingsService>();

// Azure DevOps integration
builder.Services.Configure<AzureDevOpsSettings>(
    builder.Configuration.GetSection("AzureDevOps"));
builder.Services.AddHttpClient<AzureDevOpsService>();


// Ensure WebRootPath resolves correctly regardless of working directory
var contentRoot = builder.Environment.ContentRootPath;
var webRoot = Path.Combine(contentRoot, "wwwroot");
if (Directory.Exists(webRoot))
{
    builder.Environment.WebRootPath = webRoot;
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
