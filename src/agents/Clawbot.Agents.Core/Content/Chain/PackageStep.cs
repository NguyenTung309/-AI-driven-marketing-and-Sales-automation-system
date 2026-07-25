using System.Text;
using Microsoft.Extensions.Options;
using static System.FormattableString;

namespace Clawbot.Agents.Core.Content.Chain;

// L4 — package: đóng gói thân bài (L3) theo nền tảng. Nhận THÂN BÀI + CTA + nền tảng (§4.6) — KHÔNG nhận
// brief/KB/dàn ý (đã chưng cất ở các bước trước). Ra JSON {caption, hashtags[], firstComment, altText}.
// Cổng G4 chuẩn hóa hashtag + kiểm trần ký tự; Body cuối = caption + hashtags (MergePackageBody, §4.4).
public sealed class PackageStep(IOptions<ContentChainOptions> options) : IContentChainStep
{
    public const string Id = "package";

    private const string DefaultPersona =
        "Bạn là người tối ưu bài đăng mạng xã hội. Nhiệm vụ: đóng gói THÂN BÀI bên dưới cho đúng nền tảng — " +
        "giữ nguyên ý và số liệu, KHÔNG thêm thông tin mới, KHÔNG chèn link. Viết caption hoàn chỉnh (có thể tinh " +
        "chỉnh nhẹ cho hợp nền tảng), chọn bộ hashtag ngắn gọn liên quan (không spam, không follow4follow), và " +
        "diễn đạt tự nhiên lời kêu gọi đã cho trong caption.";

    private const string OutputContract =
        "Trả về DUY NHẤT một JSON object (không kèm chữ nào khác, không markdown fence) theo đúng schema:\n"
        + "{\"caption\":\"string\",\"hashtags\":[\"string\"],\"firstComment\":\"string|null\",\"altText\":\"string|null\"}\n"
        + "Quy tắc: caption bắt buộc, không rỗng, không chèn URL; hashtag không kèm dấu cách bên trong; "
        + "firstComment/altText để null nếu không cần; tuyệt đối không bịa thêm số liệu ngoài thân bài.";

    private readonly ContentChainOptions _options = options.Value;

    public int Order => 4;

    public string StepId => Id;

    public ChainStepPrompt BuildPrompt(ContentChainContext context)
    {
        var persona = _options.PromptOverride(Id, context.Platform) ?? DefaultPersona;
        var system = AgentPromptDefaults.Compose(persona + "\n\n" + OutputContract);
        return new ChainStepPrompt(system, BuildUser(context));
    }

    public ChainStepPrompt BuildRepairPrompt(ContentChainContext context, string errorCode)
    {
        var basePrompt = BuildPrompt(context);
        var hint = errorCode switch
        {
            ContentChainErrorCodes.PackageCaptionEmpty => "Lần trước thiếu caption. Viết caption hoàn chỉnh.",
            ContentChainErrorCodes.PackageCaptionTooLong =>
                Invariant($"Lần trước bài (caption + hashtag) quá dài (tối đa {context.Limits.Max} ký tự). Rút gọn lại."),
            ContentChainErrorCodes.PackageFieldTooLong => "Lần trước firstComment/altText quá dài. Rút ngắn hoặc để null.",
            _ => "Lần trước không qua cổng kiểm. Trả lại DUY NHẤT một JSON đúng schema.",
        };
        var user = basePrompt.User + Invariant($"\n\n# LỖI CẦN SỬA (mã: {errorCode})\n") + hint;
        return basePrompt with { User = user };
    }

    public ChainStepGateResult ApplyGate(ContentChainContext context, string rawText)
    {
        var captionMax = context.Limits.Max;
        var hashtagMax = _options.HashtagMaxFor(context.Platform);
        var parsed = ContentChainGates.ParsePackage(rawText, captionMax, hashtagMax);
        if (!parsed.Succeeded || parsed.Package is null)
            return ChainStepGateResult.Fail(parsed.ErrorCode, context, null);

        var package = parsed.Package;
        var body = ContentChainGates.MergePackageBody(package);
        var payload = BuildPayload(package, body);
        return ChainStepGateResult.Advance(context with { Body = body }, payload);
    }

    private static string BuildUser(ContentChainContext context)
    {
        var builder = new StringBuilder();
        builder.Append("Nền tảng: ").Append(context.Platform).Append('\n');
        builder.Append(Invariant($"Trần độ dài bài cuối (caption + hashtag): tối đa {context.Limits.Max} ký tự.\n\n"));

        var cta = context.Plan?.Cta;
        if (cta is not null && !string.IsNullOrWhiteSpace(cta.Text))
            builder.Append("# LỜI KÊU GỌI (diễn đạt tự nhiên trong caption)\n")
                .Append(cta.Type).Append(" — ").Append(cta.Text).Append("\n\n");

        builder.Append("# THÂN BÀI (đóng gói lại, không thêm thông tin mới)\n");
        builder.Append(context.Body ?? string.Empty).Append('\n');

        return builder.ToString();
    }

    // Ảnh chụp cấu trúc PII-free: chỉ đếm + presence (không nhúng chữ caption/hashtag/firstComment của khách).
    private static string BuildPayload(ContentPackage package, string body) =>
        Invariant($"{{\"captionLen\":{package.Caption.Length},\"bodyLen\":{body.Length},")
        + Invariant($"\"hashtags\":{package.Hashtags.Count},\"droppedHashtags\":{package.DroppedHashtags},")
        + Invariant($"\"hasFirstComment\":{Bool(package.FirstComment)},\"hasAltText\":{Bool(package.AltText)}}}");

    private static string Bool(string? value) => string.IsNullOrWhiteSpace(value) ? "false" : "true";
}
