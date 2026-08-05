namespace Clawbot.Infrastructure.Channels.Pancake;

public static class PancakeEndpointPolicy
{
    public const string DefaultPublicApiBaseUrl =
        "https://pages.fm/api/public_api/v1";
    public const string DefaultUserApiBaseUrl =
        "https://pages.fm/api/v1";

    private static readonly HashSet<string> AllowedPaths = new(StringComparer.Ordinal)
    {
        "/api/v1",
        "/api/public_api/v1",
        "/api/public_api/v2",
    };

    public static string NormalizeBaseUrl(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.Equals(uri.DnsSafeHost, "pages.fm", StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "pancake_base_url_not_allowed",
                nameof(baseUrl));
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        if (!AllowedPaths.Contains(path))
        {
            throw new ArgumentException(
                "pancake_base_url_not_allowed",
                nameof(baseUrl));
        }

        return $"https://pages.fm{path}";
    }

    public static bool TryNormalizeBaseUrl(string? baseUrl, out string normalized)
    {
        try
        {
            normalized = NormalizeBaseUrl(baseUrl ?? string.Empty);
            return true;
        }
        catch (ArgumentException)
        {
            normalized = string.Empty;
            return false;
        }
    }

    public static HttpMessageHandler CreateNoRedirectHandler() =>
        new HttpClientHandler
        {
            AllowAutoRedirect = false,
        };
}
