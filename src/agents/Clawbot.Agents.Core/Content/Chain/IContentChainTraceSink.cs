namespace Clawbot.Agents.Core.Content.Chain;

// Ghi trace các mắt xích vào kho bền (bảng content_generation_traces). Cài đặt EF nằm ở Infrastructure;
// Core chỉ biết abstraction để không kéo phụ thuộc DB vào Agents.Core.
public interface IContentChainTraceSink
{
    Task WriteAsync(Guid tenantId, Guid? briefId, ContentChainOutcome outcome, CancellationToken ct);
}

// Mặc định không làm gì — dùng khi chưa cấu hình kho trace (ví dụ unit test Core).
public sealed class NullContentChainTraceSink : IContentChainTraceSink
{
    public Task WriteAsync(Guid tenantId, Guid? briefId, ContentChainOutcome outcome, CancellationToken ct) =>
        Task.CompletedTask;
}
