namespace Clawbot.Application.Common;

public readonly record struct AppError(string Code, string Message)
{
    public static readonly AppError None = new(string.Empty, string.Empty);

    public static AppError Validation(string message) => new("validation", message);

    public static AppError NotFound(string message) => new("not_found", message);

    public static AppError Conflict(string message) => new("conflict", message);
}

public sealed class Result<T>
{
    internal Result(bool isSuccess, T value, AppError error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public T Value { get; }

    public AppError Error { get; }
}

public static class Result
{
    public static Result<T> Success<T>(T value) => new(true, value, AppError.None);

    public static Result<T> Failure<T>(AppError error) => new(false, default!, error);
}
