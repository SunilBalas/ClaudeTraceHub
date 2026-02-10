namespace ClaudeTraceHub.Web.Services;

/// <summary>
/// Monitors the Claude projects directory for file changes and notifies
/// subscribers so pages can refresh their data automatically.
/// </summary>
public class DataRefreshService : IHostedService, IDisposable
{
    private readonly string _projectsRoot;
    private readonly ILogger<DataRefreshService> _logger;
    private FileSystemWatcher? _watcher;
    private Timer? _debounceTimer;
    private DateTime _lastNotification = DateTime.MinValue;

    public event Action? OnDataChanged;

    public DataRefreshService(IConfiguration config, ILogger<DataRefreshService> logger)
    {
        _logger = logger;
        var configPath = config["ClaudeDataPath"];
        _projectsRoot = !string.IsNullOrEmpty(configPath)
            ? configPath
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_projectsRoot))
        {
            _logger.LogWarning("Projects root directory not found, file watching disabled: {Path}", _projectsRoot);
            return Task.CompletedTask;
        }

        _watcher = new FileSystemWatcher(_projectsRoot)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileRenamed;

        _logger.LogInformation("Watching for data changes in: {Path}", _projectsRoot);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _watcher = null;
        _debounceTimer?.Dispose();
        _debounceTimer = null;
        return Task.CompletedTask;
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        if (IsRelevantFile(e.FullPath))
            ScheduleNotification();
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        if (IsRelevantFile(e.FullPath))
            ScheduleNotification();
    }

    private static bool IsRelevantFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jsonl", StringComparison.OrdinalIgnoreCase)
               || Path.GetFileName(path).Equals("sessions-index.json", StringComparison.OrdinalIgnoreCase);
    }

    private void ScheduleNotification()
    {
        // Debounce: wait 2 seconds after the last change before notifying.
        // This prevents rapid-fire updates during active Claude conversations.
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(_ =>
        {
            var now = DateTime.UtcNow;
            if ((now - _lastNotification).TotalSeconds < 1)
                return;

            _lastNotification = now;
            _logger.LogDebug("Data change detected, notifying {Count} subscriber(s)", OnDataChanged?.GetInvocationList().Length ?? 0);

            try
            {
                OnDataChanged?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in data change notification handler");
            }
        }, null, TimeSpan.FromSeconds(2), Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer?.Dispose();
    }
}
