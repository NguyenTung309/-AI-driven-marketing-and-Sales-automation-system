namespace Clawbot.Infrastructure.Content.Publishing;

public sealed class RoutingSocialPublisher(
    GraphSocialPublisher nativePublisher,
    HttpSocialPublisher fallbackPublisher) : ISocialPublisher
{
    public async Task<PublishResult> PublishAsync(PublishRequest request, CancellationToken ct = default)
    {
        var platform = (request.Platform ?? string.Empty).Trim().ToLowerInvariant();
        if (platform is not ("facebook" or "zalo"))
            return await fallbackPublisher.PublishAsync(request, ct).ConfigureAwait(false);

        var result = await nativePublisher.PublishAsync(request, ct).ConfigureAwait(false);
        if (result.Error is "facebook_not_configured" or "zalo_not_configured")
            return await fallbackPublisher.PublishAsync(request, ct).ConfigureAwait(false);
        return result;
    }
}
