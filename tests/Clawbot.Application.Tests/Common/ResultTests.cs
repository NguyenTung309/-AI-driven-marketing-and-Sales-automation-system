using Clawbot.Application.Common;
using FluentAssertions;

namespace Clawbot.Application.Tests.Common;

public sealed class AppErrorTests
{
    [Fact]
    public void None_IsEmptyCodeAndMessage()
    {
        AppError.None.Code.Should().BeEmpty();
        AppError.None.Message.Should().BeEmpty();
    }

    [Fact]
    public void Validation_UsesValidationCode()
    {
        var error = AppError.Validation("email không hợp lệ");

        error.Code.Should().Be("validation");
        error.Message.Should().Be("email không hợp lệ");
    }

    [Fact]
    public void NotFound_UsesNotFoundCode()
    {
        var error = AppError.NotFound("không tìm thấy hội thoại");

        error.Code.Should().Be("not_found");
        error.Message.Should().Be("không tìm thấy hội thoại");
    }

    [Fact]
    public void Conflict_UsesConflictCode()
    {
        var error = AppError.Conflict("phiên đang chạy");

        error.Code.Should().Be("conflict");
        error.Message.Should().Be("phiên đang chạy");
    }

    [Fact]
    public void Equality_SameCodeAndMessage_AreEqual()
    {
        AppError.NotFound("x").Should().Be(AppError.NotFound("x"));
        AppError.NotFound("x").Should().NotBe(AppError.Conflict("x"));
    }
}

public sealed class ResultTests
{
    [Fact]
    public void Success_CarriesValueAndNoError()
    {
        var id = Guid.NewGuid();

        var result = Result.Success(id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(id);
        result.Error.Should().Be(AppError.None);
    }

    [Fact]
    public void Failure_CarriesErrorAndDefaultValue()
    {
        var error = AppError.NotFound("không có");

        var result = Result.Failure<Guid>(error);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be(error);
        result.Value.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Failure_ReferenceType_ValueIsNull()
    {
        var result = Result.Failure<string>(AppError.Validation("sai"));

        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void Success_NullValue_IsStillSuccess()
    {
        var result = Result.Success<string?>(null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }
}
