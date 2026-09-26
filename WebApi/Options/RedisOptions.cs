namespace WebApi.Options;

public class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// When set (e.g. "localhost:6379"), SessionCache and ReceivedChunkCache use Redis
    /// so multi-instance nodes share hot-path hints (perf 4.4). Empty = in-process only.
    /// </summary>
    public string? ConnectionString { get; set; }

    public string KeyPrefix { get; set; } = "fu:";

    /// <summary>TTL for session entries in Redis (seconds).</summary>
    public int SessionTtlSeconds { get; set; } = 60;

    /// <summary>TTL for received-chunk sets in Redis (seconds).</summary>
    public int ReceivedChunksTtlSeconds { get; set; } = 600;
}
