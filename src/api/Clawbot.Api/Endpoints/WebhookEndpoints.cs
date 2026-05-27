using Clawbot.SharedKernel.Channels;

namespace Clawbot.Api.Endpoints;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhooks(this IEndpointRouteBuilder app)
    {
        app.MapPost("/webhooks/pancake", async (HttpRequest req, IChannelAdapter adapter, CancellationToken ct) =>
        {
            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync(ct);
            var headers = req.Headers.ToDictionary(h => h.Key, h => h.Value.ToString());
            var ok = await adapter.VerifyWebhookSignatureAsync(body, headers, ct);
            if (!ok) return Results.Unauthorized();
            // TODO(student): enqueue ChannelMessage via MassTransit; respond 202.
            return Results.Accepted();
        }).AllowAnonymous();

        return app;
    }
}
