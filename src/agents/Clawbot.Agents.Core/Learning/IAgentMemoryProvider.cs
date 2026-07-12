namespace Clawbot.Agents.Core.Learning;

// ai-self-learning-memory Lớp 3: nguồn "bài học nghiệp vụ" theo agent, nạp vào persona lúc chạy.
// Impl EF nằm ở Infrastructure; host nào không đăng ký thì agent chạy không memory (optional).
public interface IAgentMemoryProvider
{
    Task<IReadOnlyList<string>> GetTopFactsAsync(Guid tenantId, string agentCode, int topK, CancellationToken ct = default);
}
