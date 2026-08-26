namespace Clawbot.Agents.Core.Content;

// Lint tất định chạy TRƯỚC reviewer LLM (§4.7). HÀM THUẦN — không LLM, không throw, coi body là dữ liệu bẩn.
// Chỉ bắt vi phạm CHẮC CHẮN với tỉ lệ báo nhầm thấp: cam kết tuyệt đối (đỗ/đậu/đầu ra), link ngoài, ký tự rác.
// KHÔNG bắt "số điện thoại lạ" ở P3: phân biệt "lạ" cần tập số đã biết của tenant (chưa có) — bắt bừa sẽ báo
// nhầm hàng loạt số CTA hợp lệ; để phase sau. Vi phạm => reviewer trả needs_human (fail-safe, không tự duyệt).
public sealed record ContentLintResult(bool Succeeded, string ErrorCode)
{
    public static ContentLintResult Ok() => new(true, string.Empty);

    public static ContentLintResult Fail(string errorCode) => new(false, errorCode);
}

public static class ContentLint
{
    // Ký tự thay thế Unicode U+FFFD — dấu hiệu hỏng mã hóa/mojibake. Dùng mã điểm để nguồn thuần ASCII.
    private const char ReplacementChar = (char)0xFFFD;

    // Cam kết tuyệt đối đầu ra — vi phạm quảng cáo giáo dục. Giữ HẸP để KHÔNG đánh nhầm khuyến mãi hợp lệ
    // (vd "giảm 100% học phí", "hoàn 100%" là ưu đãi, không phải cam kết đỗ). So khớp không phân biệt hoa thường.
    private static readonly string[] AbsoluteGuaranteePhrases =
    [
        "100% đỗ", "100% đậu", "đỗ 100%", "đậu 100%",
        "cam kết đỗ", "cam kết đậu", "đảm bảo đỗ", "đảm bảo đậu",
        "chắc chắn đỗ", "chắc chắn đậu", "cam kết đầu ra", "đảm bảo đầu ra",
    ];

    public static ContentLintResult Check(string? body)
    {
        // Rỗng đã do reviewer chặn bằng nhánh riêng; lint không phán về body rỗng.
        if (string.IsNullOrWhiteSpace(body))
            return ContentLintResult.Ok();

        if (ContainsAbsoluteGuarantee(body))
            return ContentLintResult.Fail(ContentLintCodes.AbsoluteGuarantee);
        if (ContainsExternalLink(body))
            return ContentLintResult.Fail(ContentLintCodes.ExternalLink);
        if (ContainsJunkChars(body))
            return ContentLintResult.Fail(ContentLintCodes.JunkChars);

        return ContentLintResult.Ok();
    }

    private static bool ContainsAbsoluteGuarantee(string body)
    {
        foreach (var phrase in AbsoluteGuaranteePhrases)
            if (body.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static readonly string[] AllowedBrandDomains =
    [
        "hoc-ba.edu.vn",
        "hocba.edu.vn",
    ];

    private static bool ContainsExternalLink(string body)
    {
        if (!body.Contains("http://", StringComparison.OrdinalIgnoreCase)
            && !body.Contains("https://", StringComparison.OrdinalIgnoreCase)
            && !body.Contains("www.", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sanitized = body;
        foreach (var domain in AllowedBrandDomains)
        {
            sanitized = sanitized
                .Replace($"https://{domain}", "", StringComparison.OrdinalIgnoreCase)
                .Replace($"http://{domain}", "", StringComparison.OrdinalIgnoreCase)
                .Replace($"https://www.{domain}", "", StringComparison.OrdinalIgnoreCase)
                .Replace($"http://www.{domain}", "", StringComparison.OrdinalIgnoreCase)
                .Replace($"www.{domain}", "", StringComparison.OrdinalIgnoreCase);
        }

        return sanitized.Contains("http://", StringComparison.OrdinalIgnoreCase)
            || sanitized.Contains("https://", StringComparison.OrdinalIgnoreCase)
            || sanitized.Contains("www.", StringComparison.OrdinalIgnoreCase);
    }

    // Ký tự rác: replacement char U+FFFD (hỏng mã hóa/mojibake) hoặc control char C0 ngoài tab/xuống dòng.
    private static bool ContainsJunkChars(string body)
    {
        foreach (var ch in body)
        {
            if (ch == ReplacementChar)
                return true;
            if (char.IsControl(ch) && ch != '\t' && ch != '\n' && ch != '\r')
                return true;
        }

        return false;
    }
}

public static class ContentLintCodes
{
    public const string AbsoluteGuarantee = "lint_absolute_guarantee";
    public const string ExternalLink = "lint_external_link";
    public const string JunkChars = "lint_junk_chars";
}
