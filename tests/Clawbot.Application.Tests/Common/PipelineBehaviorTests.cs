using Clawbot.Application.Common.Behaviors;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Clawbot.Application.Tests.Common;

public sealed record SampleRequest(string Email) : IRequest<string>;

public sealed class ValidationBehaviorTests
{
    private static RequestHandlerDelegate<string> NextReturning(string value) =>
        () => Task.FromResult(value);

    [Fact]
    public async Task Handle_NoValidators_CallsNext()
    {
        var behavior = new ValidationBehavior<SampleRequest, string>([]);

        var result = await behavior.Handle(
            new SampleRequest("a@b.vn"),
            NextReturning("ok"),
            CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_AllValidatorsPass_CallsNext()
    {
        var validator = Substitute.For<IValidator<SampleRequest>>();
        validator
            .ValidateAsync(Arg.Any<ValidationContext<SampleRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult());
        var behavior = new ValidationBehavior<SampleRequest, string>([validator]);

        var result = await behavior.Handle(
            new SampleRequest("a@b.vn"),
            NextReturning("ok"),
            CancellationToken.None);

        result.Should().Be("ok");
    }

    [Fact]
    public async Task Handle_ValidatorFails_ThrowsAndSkipsNext()
    {
        var validator = Substitute.For<IValidator<SampleRequest>>();
        validator
            .ValidateAsync(Arg.Any<ValidationContext<SampleRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([new ValidationFailure("Email", "email bắt buộc")]));
        var behavior = new ValidationBehavior<SampleRequest, string>([validator]);
        var nextCalled = false;

        var act = async () => await behavior.Handle(
            new SampleRequest(""),
            () => { nextCalled = true; return Task.FromResult("ok"); },
            CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.ErrorMessage == "email bắt buộc");
        nextCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_MultipleValidators_AggregatesAllFailures()
    {
        var first = Substitute.For<IValidator<SampleRequest>>();
        first
            .ValidateAsync(Arg.Any<ValidationContext<SampleRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([new ValidationFailure("Email", "lỗi 1")]));
        var second = Substitute.For<IValidator<SampleRequest>>();
        second
            .ValidateAsync(Arg.Any<ValidationContext<SampleRequest>>(), Arg.Any<CancellationToken>())
            .Returns(new ValidationResult([new ValidationFailure("Email", "lỗi 2")]));
        var behavior = new ValidationBehavior<SampleRequest, string>([first, second]);

        var act = async () => await behavior.Handle(
            new SampleRequest(""),
            NextReturning("ok"),
            CancellationToken.None);

        (await act.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().HaveCount(2);
    }
}

public sealed class LoggingBehaviorTests
{
    [Fact]
    public async Task Handle_ReturnsNextResponse()
    {
        var behavior = new LoggingBehavior<SampleRequest, string>(
            NullLogger<LoggingBehavior<SampleRequest, string>>.Instance);

        var result = await behavior.Handle(
            new SampleRequest("a@b.vn"),
            () => Task.FromResult("handled"),
            CancellationToken.None);

        result.Should().Be("handled");
    }

    [Fact]
    public async Task Handle_PropagatesHandlerException()
    {
        var behavior = new LoggingBehavior<SampleRequest, string>(
            NullLogger<LoggingBehavior<SampleRequest, string>>.Instance);

        var act = async () => await behavior.Handle(
            new SampleRequest("a@b.vn"),
            () => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
    }
}

public sealed class AuditBehaviorTests
{
    [Fact]
    public async Task Handle_ReturnsNextResponse()
    {
        var behavior = new AuditBehavior<SampleRequest, string>(
            NullLogger<AuditBehavior<SampleRequest, string>>.Instance);

        var result = await behavior.Handle(
            new SampleRequest("a@b.vn"),
            () => Task.FromResult("audited"),
            CancellationToken.None);

        result.Should().Be("audited");
    }

    [Fact]
    public async Task Handle_NullNext_Throws()
    {
        var behavior = new AuditBehavior<SampleRequest, string>(
            NullLogger<AuditBehavior<SampleRequest, string>>.Instance);

        var act = async () => await behavior.Handle(
            new SampleRequest("a@b.vn"),
            null!,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Handle_HandlerThrows_LogsAndRethrows()
    {
        var behavior = new AuditBehavior<SampleRequest, string>(
            NullLogger<AuditBehavior<SampleRequest, string>>.Instance);

        var act = async () => await behavior.Handle(
            new SampleRequest("a@b.vn"),
            () => throw new InvalidOperationException("handler failed"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("handler failed");
    }
}
