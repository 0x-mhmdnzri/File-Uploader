namespace WebApi.Auth;

public class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// When false (default), API is open (local/dev). When true, X-Api-Key is required.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Shared secret. Prefer env var Auth__ApiKey in production.
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// Header name for the API key.
    /// </summary>
    public string HeaderName { get; set; } = "X-Api-Key";

    /// <summary>
    /// Paths that skip auth (health always open).
    /// </summary>
    public string[] AnonymousPathPrefixes { get; set; } = ["/health", "/swagger"];
}
