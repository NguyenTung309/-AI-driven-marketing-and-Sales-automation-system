using Clawbot.Agents.Core.Skills.Nlp;
using Clawbot.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace Clawbot.Api.Tests;

public sealed class OutboundMessageSafetyServiceTests
{
    [Fact]
    public async Task EnsureAllowedAsync_rejects_toxic_outbound_sale_message()
    {
        var sut = new OutboundMessageSafetyService(
            new FixedToxicityFilter(blocked: true),
            Options.Create(new ToxicityOptions { OutboundBlockThreshold = 0.8f }));

        Func<Task> act = () => sut.EnsureAllowedAsync("bad outbound", CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*tone policy*");
    }

    [Fact]
    public async Task EnsureAllowedAsync_allows_clean_outbound_sale_message()
    {
        var sut = new OutboundMessageSafetyService(
            new FixedToxicityFilter(blocked: false),
            Options.Create(new ToxicityOptions { OutboundBlockThreshold = 0.8f }));

        await sut.EnsureAllowedAsync("Xin chao, em gui thong tin khoa HSK4.", CancellationToken.None);
    }

    private sealed class FixedToxicityFilter(bool blocked) : IToxicityFilter
    {
        public string Name => "fixed-toxicity";

        public Task<ToxicityScores> ScoreAsync(string text, CancellationToken ct) =>
            Task.FromResult(blocked
                ? new ToxicityScores(0.9f, 0.8f, 0f, 0f, 0.8f)
                : new ToxicityScores(0.1f, 0f, 0f, 0f, 0f));

        public Task<bool> IsBlockedAsync(string text, float threshold, CancellationToken ct) =>
            Task.FromResult(blocked);
    }
}
