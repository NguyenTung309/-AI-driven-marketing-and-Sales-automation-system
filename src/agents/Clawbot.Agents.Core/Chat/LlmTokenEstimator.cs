using System.Collections.Concurrent;
using Microsoft.ML.Tokenizers;

namespace Clawbot.Agents.Core.Chat;

// Đếm token cục bộ để ước lượng chi phí KHI provider không trả usage.
// Quan sát aigatewayport 2026-07: SSE có `response.completed` nhưng object `response` không có field
// `usage`, non-stream trả `"usage":{}` -> token = 0 -> cost = 0 -> ledger không ghi gì, cap vô hiệu.
//
// GIỚI HẠN CỐ HỮU: con số này luôn THẤP HƠN hóa đơn thật vì không thấy được reasoning token
// (probe cho 403 event reasoning_summary_text.delta so với 4 event output_text.delta). Mọi chỗ dùng
// số này phải gắn nhãn "ước lượng" cho người dùng.
//
// Static + cache: chat client được khởi tạo bằng `new` trong LlmChatClientFactory (không qua DI),
// nên tránh thêm dependency phải luồn qua factory và 25 caller của IClaudeChatClient.
internal static class LlmTokenEstimator
{
    // Overhead định dạng hội thoại: mỗi message tốn thêm ~4 token khung (role + delimiter),
    // cộng ~3 token priming cho lượt trả lời. Số của OpenAI cookbook cho chat format.
    private const int TokensPerMessage = 4;
    private const int PrimingTokens = 3;

    // Ký tự/token khi tokenizer không dùng được (package vocab lỗi). Thô nhưng còn hơn 0.
    private const int FallbackCharsPerToken = 4;

    private const string O200kBase = "o200k_base";
    private const string Cl100kBase = "cl100k_base";

    private static readonly ConcurrentDictionary<string, Tokenizer?> Cache = new(StringComparer.Ordinal);

    /// <summary>Số token của một đoạn text (không tính overhead message).</summary>
    public static int CountText(string model, string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        var tokenizer = Resolve(model);
        return tokenizer is null
            ? Math.Max(1, text.Length / FallbackCharsPerToken)
            : tokenizer.CountTokens(text);
    }

    /// <summary>Số token của toàn bộ prompt gửi đi: system + history + tin hiện tại + overhead khung.</summary>
    public static int CountPrompt(
        string model,
        string? systemPrompt,
        IReadOnlyList<ChatTurn>? history,
        string? userMessage)
    {
        var total = PrimingTokens;

        if (!string.IsNullOrEmpty(systemPrompt))
            total += TokensPerMessage + CountText(model, systemPrompt);

        foreach (var turn in history ?? [])
            total += TokensPerMessage + CountText(model, turn.Content);

        if (!string.IsNullOrEmpty(userMessage))
            total += TokensPerMessage + CountText(model, userMessage);

        return total;
    }

    private static Tokenizer? Resolve(string? model)
    {
        var encoding = EncodingFor(model);
        return Cache.GetOrAdd(encoding, static name =>
        {
            try
            {
                return TiktokenTokenizer.CreateForEncoding(name);
            }
            catch (Exception ex) when (ex is ArgumentException
                or InvalidOperationException
                or NotSupportedException
                or IOException
                or TypeInitializationException)
            {
                // Không được để việc ước lượng làm chết call LLM thật -> rơi về heuristic ký tự.
                return null;
            }
        });
    }

    // Không dùng TiktokenTokenizer.CreateForModel: bảng model của thư viện không có `gpt-5.5`
    // (và các tên gateway tự đặt) -> throw. Tự map sang encoding rồi CreateForEncoding.
    private static string EncodingFor(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return O200kBase;

        var name = model.Trim().ToLowerInvariant();

        // Gateway hay thêm tiền tố kiểu `cx/gpt-5.5-review` -> so khớp cả phần sau dấu '/'.
        var slash = name.LastIndexOf('/');
        var bare = slash >= 0 && slash < name.Length - 1 ? name[(slash + 1)..] : name;

        return MatchEncoding(bare) ?? MatchEncoding(name) ?? O200kBase;
    }

    private static string? MatchEncoding(string name)
    {
        if (name.StartsWith("gpt-5", StringComparison.Ordinal)
            || name.StartsWith("gpt-4o", StringComparison.Ordinal)
            || name.StartsWith("gpt-4.1", StringComparison.Ordinal)
            || name.StartsWith("gpt-4.5", StringComparison.Ordinal)
            || name.StartsWith("o1", StringComparison.Ordinal)
            || name.StartsWith("o3", StringComparison.Ordinal)
            || name.StartsWith("o4", StringComparison.Ordinal))
        {
            return O200kBase;
        }

        if (name.StartsWith("gpt-4", StringComparison.Ordinal)
            || name.StartsWith("gpt-3.5", StringComparison.Ordinal)
            || name.StartsWith("text-embedding", StringComparison.Ordinal))
        {
            return Cl100kBase;
        }

        // claude*, model lạ: Anthropic không public tokenizer, dùng o200k làm proxy.
        // Đường Anthropic có usage thật nên nhánh này gần như không chạy.
        return null;
    }
}
