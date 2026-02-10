using ClaudeTraceHub.Web.Models;
using Microsoft.Extensions.Caching.Memory;

namespace ClaudeTraceHub.Web.Services;

public class ConversationCacheService
{
    private readonly IMemoryCache _cache;
    private readonly JsonlParserService _parser;

    public ConversationCacheService(IMemoryCache cache, JsonlParserService parser)
    {
        _cache = cache;
        _parser = parser;
    }

    public Conversation GetOrParse(string filePath, string projectName, string projectDirName)
    {
        var cacheKey = $"conv_{filePath}";

        if (_cache.TryGetValue(cacheKey, out Conversation? cached) && cached != null)
        {
            // Check if file has changed
            if (File.Exists(filePath))
            {
                var lastWrite = File.GetLastWriteTimeUtc(filePath);
                var cachedTimeKey = $"conv_time_{filePath}";
                if (_cache.TryGetValue(cachedTimeKey, out DateTime cachedTime) && cachedTime >= lastWrite)
                {
                    return cached;
                }
            }
        }

        // Parse fresh
        var conversation = _parser.ParseFile(filePath, projectName, projectDirName);

        var options = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromMinutes(5));

        _cache.Set(cacheKey, conversation, options);

        if (File.Exists(filePath))
        {
            _cache.Set($"conv_time_{filePath}", File.GetLastWriteTimeUtc(filePath), options);
        }

        return conversation;
    }

    public void InvalidateAll()
    {
        if (_cache is MemoryCache mc)
            mc.Compact(1.0);
    }
}
