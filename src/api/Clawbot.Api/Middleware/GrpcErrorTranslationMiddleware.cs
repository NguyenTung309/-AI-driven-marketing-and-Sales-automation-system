using System.Net;
using Grpc.Core;

namespace Clawbot.Api.Middleware;

// Translates typed gRPC failures from the AgentService into clean HTTP responses at the API edge.
// Today: FailedPrecondition (e.g. `llm_config_not_configured`, D1) → 422 with the status detail as the
// error code, so an unbound/inactive agent surfaces a meaningful 4xx instead of a generic 500.
// Unmapped statuses rethrow unchanged (existing behavior preserved).
public sealed class GrpcErrorTranslationMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _next(ctx).ConfigureAwait(false);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.FailedPrecondition && !ctx.Response.HasStarted)
        {
            ctx.Response.Clear();
            ctx.Response.StatusCode = (int)HttpStatusCode.UnprocessableEntity; // 422
            await ctx.Response.WriteAsJsonAsync(new { error = ErrorCode(ex) }).ConfigureAwait(false);
        }
    }

    private static string ErrorCode(RpcException ex) =>
        string.IsNullOrWhiteSpace(ex.Status.Detail) ? "precondition_failed" : ex.Status.Detail;
}
