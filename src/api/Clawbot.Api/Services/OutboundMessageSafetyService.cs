using Clawbot.Agents.Core.Skills.Nlp;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Services;

public sealed class OutboundMessageSafetyService(
    IToxicityFilter toxicity,
    IOptions<ToxicityOptions> options)
{
    private readonly IToxicityFilter _toxicity = toxicity;
    private readonly ToxicityOptions _options = options.Value;

    public async Task EnsureAllowedAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Outbound message is empty.");

        var blocked = await _toxicity.IsBlockedAsync(text, _options.OutboundBlockThreshold, ct)
            .ConfigureAwait(false);
        if (blocked)
            throw new InvalidOperationException("Outbound message blocked by sale tone policy.");
    }
}
