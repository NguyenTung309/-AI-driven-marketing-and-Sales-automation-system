using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Clawbot.Agents.Core.Content.Chain;

// Cổng kiểm tất định giữa các mắt xích — HÀM THUẦN, không phụ thuộc LLM (dễ unit test, §9).
// G1: parse + kiểm JSON plan. G2: parse + đối chiếu citation dàn ý. G3: kiểm thân bài L3.
// Nguyên tắc: mọi lỗi ra mã có tên, không throw; đầu ra của LLM là dữ liệu, không tin cậy.
public static class ContentChainGates
{
    private const int MaxFieldLength = 400;
    private const int MaxListItems = 10;
    private const int MaxListItemLength = 200;

    // Hashtag dài hơn ngưỡng này gần như chắc là rác (câu bị dính liền) => loại (§4.4).
    private const int MaxHashtagLength = 100;

    // Ngưỡng dưới của độ dài brief để bật kiểm "sao chép brief" — brief quá ngắn thì trùng lặp là ngẫu nhiên.
    private const int MinBriefLengthForCopyCheck = 40;

    // Câu ngắn có thể không dấu; chỉ soi ngôn ngữ khi bài đủ dài để tin cậy tín hiệu.
    private const int MinBodyLengthForLanguageCheck = 60;

    // Chấm điểm chọn hook (§4.5) — trọng số tất định, không LLM.
    private const int MinHookLength = 16;
    private const int MaxHookLength = 120;
    private const int HookLengthBonus = 2;
    private const int HookMustIncludeBonus = 1;   // mỗi từ mustInclude khớp trong hook
    private const int HookDataBackedBonus = 1;     // hook có số liệu VÀ chuỗi có proofPoint đã qua G2
    private const int HookDuplicatePenalty = 2;    // hook trùng ý với hook trước đó

    private static readonly string[] Objectives = ["awareness", "lead_gen", "nurture", "promo"];
    private static readonly string[] CtaTypes = ["inbox", "comment", "link", "call"];
    private static readonly string[] Languages = ["vi", "en"];

    // Hashtag spam tương tác — nền tảng thường bóp/ẩn bài dính các tag này (§4.4). So khớp KHÔNG kể dấu '#',
    // không phân biệt hoa thường. Tập tối thiểu, an toàn; khách mở rộng qua config ở phase sau.
    private static readonly HashSet<string> BannedHashtags = new(StringComparer.OrdinalIgnoreCase)
    {
        "follow4follow", "followforfollow", "f4f",
        "like4like", "l4l", "sub4sub", "spam",
    };

    // ===== G1 — plan =====
    public static ContentPlanGateResult ParsePlan(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanEmptyOutput);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(UnwrapJson(rawText));
        }
        catch (JsonException)
        {
            return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanParseFailed);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanParseFailed);

            var objective = GetString(root, "objective");
            var audience = GetString(root, "audience");
            var keyMessage = GetString(root, "keyMessage");
            var offer = GetNullableString(root, "offer");
            var tone = GetString(root, "tone");
            var language = GetString(root, "language");

            if (string.IsNullOrWhiteSpace(keyMessage))
                return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanFieldMissing);
            if (!InAllowList(objective, Objectives) || !InAllowList(language, Languages))
                return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanEnumInvalid);

            if (!root.TryGetProperty("cta", out var ctaElement) || ctaElement.ValueKind != JsonValueKind.Object)
                return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanFieldMissing);
            var ctaType = GetString(ctaElement, "type");
            var ctaText = GetString(ctaElement, "text");
            if (!InAllowList(ctaType, CtaTypes))
                return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanEnumInvalid);
            if (string.IsNullOrWhiteSpace(ctaText))
                return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanFieldMissing);

            var mustInclude = GetStringArray(root, "mustInclude");
            var mustAvoid = GetStringArray(root, "mustAvoid");
            if (mustInclude is null || mustAvoid is null)
                return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanParseFailed);
            if (mustInclude.Count > MaxListItems || mustAvoid.Count > MaxListItems)
                return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanTooManyItems);

            if (TooLong(objective) || TooLong(audience) || TooLong(keyMessage) || TooLong(offer)
                || TooLong(tone) || TooLong(language) || TooLong(ctaType) || TooLong(ctaText))
            {
                return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanFieldTooLong);
            }

            foreach (var item in mustInclude)
                if (item.Length > MaxListItemLength)
                    return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanFieldTooLong);
            foreach (var item in mustAvoid)
                if (item.Length > MaxListItemLength)
                    return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanFieldTooLong);

            if (ContainsUrl(objective) || ContainsUrl(audience) || ContainsUrl(keyMessage) || ContainsUrl(offer)
                || ContainsUrl(tone) || ContainsUrl(ctaText)
                || ContainsAnyUrl(mustInclude) || ContainsAnyUrl(mustAvoid))
            {
                return ContentPlanGateResult.Fail(ContentChainErrorCodes.PlanContainsUrl);
            }

            var plan = new ContentPlan(
                objective.Trim().ToLowerInvariant(),
                audience.Trim(),
                keyMessage.Trim(),
                string.IsNullOrWhiteSpace(offer) ? null : offer.Trim(),
                tone.Trim(),
                new ContentPlanCta(ctaType.Trim().ToLowerInvariant(), ctaText.Trim()),
                mustInclude,
                mustAvoid,
                language.Trim().ToLowerInvariant());
            return ContentPlanGateResult.Ok(plan);
        }
    }

    // ===== G3 — write =====
    public static ContentBodyGateResult CheckBody(string? body, string brief, string language, ContentChainLimits limits)
    {
        if (string.IsNullOrWhiteSpace(body))
            return ContentBodyGateResult.Fail(ContentChainErrorCodes.WriteEmptyOutput);

        var trimmed = body.Trim();
        if (trimmed.Length < limits.Min)
            return ContentBodyGateResult.Fail(ContentChainErrorCodes.WriteTooShort);
        if (trimmed.Length > limits.Max)
            return ContentBodyGateResult.Fail(ContentChainErrorCodes.WriteTooLong);
        if (ContainsUrl(trimmed))
            return ContentBodyGateResult.Fail(ContentChainErrorCodes.WriteContainsUrl);
        if (trimmed.Contains("{{", StringComparison.Ordinal))
            return ContentBodyGateResult.Fail(ContentChainErrorCodes.WritePlaceholderLeft);
        if (CopiesBrief(trimmed, brief))
            return ContentBodyGateResult.Fail(ContentChainErrorCodes.WriteCopiesBrief);
        if (LanguageMismatch(trimmed, language))
            return ContentBodyGateResult.Fail(ContentChainErrorCodes.WriteLanguageMismatch);

        return ContentBodyGateResult.Ok();
    }

    // ===== G2 — outline =====
    // Cổng quan trọng nhất (§4.2): mọi citationId phải nằm trong tập chunk thật [1..citationCount].
    // proofPoint trỏ citation lạ => LOẠI điểm đó (đếm vào DroppedProofPoints), KHÔNG sửa, KHÔNG bịa.
    // Chỉ fail khi hooks rỗng / parse hỏng / vượt giới hạn — biến "đừng bịa số liệu" thành ràng buộc thực thi.
    public static ContentOutlineGateResult ParseOutline(string? rawText, int citationCount)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return ContentOutlineGateResult.Fail(ContentChainErrorCodes.OutlineEmptyOutput);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(UnwrapJson(rawText));
        }
        catch (JsonException)
        {
            return ContentOutlineGateResult.Fail(ContentChainErrorCodes.OutlineParseFailed);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ContentOutlineGateResult.Fail(ContentChainErrorCodes.OutlineParseFailed);

            // hooks — bắt buộc, không rỗng (§4.2).
            var hooks = GetStringArray(root, "hooks");
            if (hooks is null)
                return ContentOutlineGateResult.Fail(ContentChainErrorCodes.OutlineParseFailed);
            if (hooks.Count == 0)
                return ContentOutlineGateResult.Fail(ContentChainErrorCodes.OutlineNoHooks);
            if (hooks.Count > MaxListItems)
                return ContentOutlineGateResult.Fail(ContentChainErrorCodes.OutlineTooManyItems);
            foreach (var hook in hooks)
                if (hook.Length > MaxFieldLength)
                    return ContentOutlineGateResult.Fail(ContentChainErrorCodes.OutlineFieldTooLong);

            // outline (sections) — tùy chọn.
            var (sectionsError, sections) = ParseSections(root);
            if (sectionsError is not null)
                return ContentOutlineGateResult.Fail(sectionsError);

            // proofPoints — tùy chọn; đối chiếu citationId, loại điểm trỏ citation lạ.
            var (proofError, proofPoints, dropped) = ParseProofPoints(root, citationCount);
            if (proofError is not null)
                return ContentOutlineGateResult.Fail(proofError);

            // riskFlags — tùy chọn.
            var riskFlags = GetStringArray(root, "riskFlags");
            if (riskFlags is null)
                return ContentOutlineGateResult.Fail(ContentChainErrorCodes.OutlineParseFailed);
            if (riskFlags.Count > MaxListItems)
                return ContentOutlineGateResult.Fail(ContentChainErrorCodes.OutlineTooManyItems);
            foreach (var flag in riskFlags)
                if (flag.Length > MaxFieldLength)
                    return ContentOutlineGateResult.Fail(ContentChainErrorCodes.OutlineFieldTooLong);

            var outline = new ContentOutline(
                hooks,
                SelectedHookIndex: -1,   // OutlineStep chọn qua SelectHook sau khi cổng qua
                sections,
                proofPoints,
                riskFlags,
                dropped);
            return ContentOutlineGateResult.Ok(outline);
        }
    }

    // ===== chọn hook (bước xử lý không LLM, §4.5) =====
    // Chấm điểm tất định rồi lấy điểm cao nhất; hòa điểm thì lấy index nhỏ nhất. Trả cả bảng điểm để ghi trace.
    public static HookSelection SelectHook(
        IReadOnlyList<string> hooks, IReadOnlyList<string> mustInclude, bool hasProof)
    {
        if (hooks is null || hooks.Count == 0)
            return new HookSelection(-1, Array.Empty<int>());

        var scores = new int[hooks.Count];
        var seen = new List<string>(hooks.Count);
        for (var i = 0; i < hooks.Count; i++)
        {
            var trimmed = (hooks[i] ?? string.Empty).Trim();
            var score = 0;

            if (trimmed.Length >= MinHookLength && trimmed.Length <= MaxHookLength)
                score += HookLengthBonus;

            if (mustInclude is not null)
            {
                foreach (var term in mustInclude)
                    if (!string.IsNullOrWhiteSpace(term)
                        && trimmed.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase))
                        score += HookMustIncludeBonus;
            }

            if (hasProof && HasDigit(trimmed))
                score += HookDataBackedBonus;

            // Trùng ý với hook trước đó (§4.5: không trùng ý nhau) => phạt một lần.
            var normalized = CollapseWhitespace(trimmed).ToLowerInvariant();
            if (!string.IsNullOrEmpty(normalized) && IsDuplicate(seen, normalized))
                score -= HookDuplicatePenalty;
            seen.Add(normalized);

            scores[i] = score;
        }

        var best = 0;
        for (var i = 1; i < scores.Length; i++)
            if (scores[i] > scores[best])
                best = i;

        return new HookSelection(best, scores);
    }

    // ===== G4 — package =====
    // Đóng gói bài theo nền tảng (§4.4): caption trong trần ký tự + hashtag CHUẨN HÓA. Như G2, hashtag rác thì
    // LOẠI (đếm DroppedHashtags), KHÔNG fail — hashtag là phần trang trí. Chỉ fail khi rỗng / parse hỏng /
    // caption rỗng-hoặc-vượt-trần (repair được) / field phụ quá dài — giữ triết lý "output LLM là dữ liệu bẩn".
    public static ContentPackageGateResult ParsePackage(string? rawText, int captionMax, int hashtagMax)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return ContentPackageGateResult.Fail(ContentChainErrorCodes.PackageEmptyOutput);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(UnwrapJson(rawText));
        }
        catch (JsonException)
        {
            return ContentPackageGateResult.Fail(ContentChainErrorCodes.PackageParseFailed);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ContentPackageGateResult.Fail(ContentChainErrorCodes.PackageParseFailed);

            var caption = GetString(root, "caption").Trim();
            if (string.IsNullOrWhiteSpace(caption))
                return ContentPackageGateResult.Fail(ContentChainErrorCodes.PackageCaptionEmpty);

            var rawHashtags = GetStringArray(root, "hashtags");
            if (rawHashtags is null)
                return ContentPackageGateResult.Fail(ContentChainErrorCodes.PackageParseFailed);
            var (hashtags, dropped) = NormalizeHashtags(rawHashtags, Math.Max(0, hashtagMax));

            // firstComment/altText: P3 chỉ ghi PRESENCE vào trace; vẫn chặn quá dài để không nuốt rác.
            var firstComment = GetNullableString(root, "firstComment");
            var altText = GetNullableString(root, "altText");
            if (TooLong(firstComment?.Trim()) || TooLong(altText?.Trim()))
                return ContentPackageGateResult.Fail(ContentChainErrorCodes.PackageFieldTooLong);

            var package = new ContentPackage(
                caption,
                hashtags,
                string.IsNullOrWhiteSpace(firstComment) ? null : firstComment.Trim(),
                string.IsNullOrWhiteSpace(altText) ? null : altText.Trim(),
                dropped);

            // Trần ký tự áp cho BÀI CUỐI (caption + hashtags) — đúng cách nền tảng đếm; vượt => repair rút gọn.
            if (MergePackageBody(package).Length > Math.Max(1, captionMax))
                return ContentPackageGateResult.Fail(ContentChainErrorCodes.PackageCaptionTooLong);

            return ContentPackageGateResult.Ok(package);
        }
    }

    // Ghép Body cuối = caption + (dòng trống) + hashtags nối bằng dấu cách (§4.4).
    // firstComment/altText KHÔNG vào Body (trace-only ở P3, mở rộng phase sau).
    public static string MergePackageBody(ContentPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.Hashtags.Count == 0)
            return package.Caption;
        return package.Caption + "\n\n" + string.Join(' ', package.Hashtags);
    }

    // ===== helpers =====

    // Gỡ markdown fence ```json ... ``` nếu model bọc quanh JSON — chuỗi có repair + fallback nên khoan dung ở đây.
    private static string UnwrapJson(string raw)
    {
        var text = raw.Trim();
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;

        var firstNewline = text.IndexOf('\n');
        if (firstNewline >= 0)
            text = text[(firstNewline + 1)..];
        if (text.EndsWith("```", StringComparison.Ordinal))
            text = text[..^3];
        return text.Trim();
    }

    private static string GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : string.Empty;

    private static string? GetNullableString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var element))
            return null;
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    // citationId là số nguyên; khoan dung cả khi model trả chuỗi số ("1"). Không phải số => null.
    private static int? GetInt(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var element))
            return null;
        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(
                element.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    // Dàn ý: mảng {section, points[]}. Thiếu/null => rỗng. Sai kiểu => parse fail. Bỏ section rỗng hoàn toàn.
    private static (string? Error, IReadOnlyList<ContentOutlineSection> Sections) ParseSections(JsonElement root)
    {
        if (!root.TryGetProperty("outline", out var element) || element.ValueKind == JsonValueKind.Null)
            return (null, Array.Empty<ContentOutlineSection>());
        if (element.ValueKind != JsonValueKind.Array)
            return (ContentChainErrorCodes.OutlineParseFailed, Array.Empty<ContentOutlineSection>());
        if (element.GetArrayLength() > MaxListItems)
            return (ContentChainErrorCodes.OutlineTooManyItems, Array.Empty<ContentOutlineSection>());

        var sections = new List<ContentOutlineSection>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return (ContentChainErrorCodes.OutlineParseFailed, Array.Empty<ContentOutlineSection>());

            var section = GetString(item, "section");
            if (section.Length > MaxFieldLength)
                return (ContentChainErrorCodes.OutlineFieldTooLong, Array.Empty<ContentOutlineSection>());

            var points = GetStringArray(item, "points");
            if (points is null)
                return (ContentChainErrorCodes.OutlineParseFailed, Array.Empty<ContentOutlineSection>());
            if (points.Count > MaxListItems)
                return (ContentChainErrorCodes.OutlineTooManyItems, Array.Empty<ContentOutlineSection>());
            foreach (var point in points)
                if (point.Length > MaxListItemLength)
                    return (ContentChainErrorCodes.OutlineFieldTooLong, Array.Empty<ContentOutlineSection>());

            if (string.IsNullOrWhiteSpace(section) && points.Count == 0)
                continue;
            sections.Add(new ContentOutlineSection(section.Trim(), points));
        }

        return (null, sections);
    }

    // proofPoints: mảng {claim, citationId}. Van chặn bịa số liệu — citationId ngoài [1..citationCount] => loại điểm.
    private static (string? Error, IReadOnlyList<ContentProofPoint> Points, int Dropped) ParseProofPoints(
        JsonElement root, int citationCount)
    {
        if (!root.TryGetProperty("proofPoints", out var element) || element.ValueKind == JsonValueKind.Null)
            return (null, Array.Empty<ContentProofPoint>(), 0);
        if (element.ValueKind != JsonValueKind.Array)
            return (ContentChainErrorCodes.OutlineParseFailed, Array.Empty<ContentProofPoint>(), 0);
        if (element.GetArrayLength() > MaxListItems)
            return (ContentChainErrorCodes.OutlineTooManyItems, Array.Empty<ContentProofPoint>(), 0);

        var points = new List<ContentProofPoint>();
        var dropped = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return (ContentChainErrorCodes.OutlineParseFailed, Array.Empty<ContentProofPoint>(), 0);

            var claim = GetString(item, "claim");
            if (claim.Length > MaxFieldLength)
                return (ContentChainErrorCodes.OutlineFieldTooLong, Array.Empty<ContentProofPoint>(), 0);

            var citationId = GetInt(item, "citationId");
            if (string.IsNullOrWhiteSpace(claim) || citationId is null
                || citationId < 1 || citationId > citationCount)
            {
                dropped++;
                continue;
            }

            points.Add(new ContentProofPoint(claim.Trim(), citationId.Value));
        }

        return (null, points, dropped);
    }

    // Thiếu field / null => list rỗng (model có thể bỏ trống). Sai kiểu => null (parse fail).
    private static IReadOnlyList<string>? GetStringArray(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return Array.Empty<string>();
        if (element.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return null;
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                list.Add(value.Trim());
        }

        return list;
    }

    private static bool InAllowList(string value, string[] allow) =>
        !string.IsNullOrWhiteSpace(value)
        && Array.Exists(allow, a => string.Equals(a, value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool TooLong(string? value) => value is not null && value.Length > MaxFieldLength;

    private static bool ContainsUrl(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        var lower = text.ToLowerInvariant();
        return lower.Contains("http://", StringComparison.Ordinal)
            || lower.Contains("https://", StringComparison.Ordinal)
            || lower.Contains("www.", StringComparison.Ordinal);
    }

    private static bool ContainsAnyUrl(IReadOnlyList<string> values)
    {
        foreach (var value in values)
            if (ContainsUrl(value))
                return true;
        return false;
    }

    // Chặn bài chỉ chép lại brief: so khớp sau khi gom khoảng trắng (Ordinal, không phân biệt hoa thường).
    private static bool CopiesBrief(string body, string brief)
    {
        if (string.IsNullOrWhiteSpace(brief))
            return false;
        var normalizedBrief = CollapseWhitespace(brief);
        if (normalizedBrief.Length < MinBriefLengthForCopyCheck)
            return false;
        return CollapseWhitespace(body).Contains(normalizedBrief, StringComparison.OrdinalIgnoreCase);
    }

    // vi mà bài đủ dài nhưng KHÔNG có ký tự chữ cái ngoài ASCII (chữ có dấu) => nhiều khả năng sai ngôn ngữ.
    private static bool LanguageMismatch(string body, string language)
    {
        if (!string.Equals(language?.Trim(), "vi", StringComparison.OrdinalIgnoreCase))
            return false;
        if (body.Length <= MinBodyLengthForLanguageCheck)
            return false;
        return !HasNonAsciiLetter(body);
    }

    private static bool HasNonAsciiLetter(string text)
    {
        foreach (var ch in text)
            if (ch > 127 && char.IsLetter(ch))
                return true;
        return false;
    }

    private static bool HasDigit(string text)
    {
        foreach (var ch in text)
            if (char.IsDigit(ch))
                return true;
        return false;
    }

    // Chuẩn hóa danh sách hashtag (§4.4): làm sạch từng tag, bỏ rỗng/trùng (không kể hoa thường)/nằm danh sách
    // cấm/vượt số lượng max. Trả list sạch (giữ thứ tự) + số bị loại. KHÔNG fail — hashtag chỉ là trang trí.
    private static (IReadOnlyList<string> Tags, int Dropped) NormalizeHashtags(IReadOnlyList<string> raw, int max)
    {
        var tags = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dropped = 0;
        foreach (var item in raw)
        {
            var tag = CleanHashtag(item);
            if (tag is null || BannedHashtags.Contains(tag[1..]) || !seen.Add(tag))
            {
                dropped++;
                continue;
            }

            if (tags.Count >= max)
            {
                dropped++;
                continue;
            }

            tags.Add(tag);
        }

        return (tags, dropped);
    }

    // Gom một token hashtag: bỏ MỌI khoảng trắng + dấu '#' bên trong rồi ép đúng một '#' đầu.
    // Rỗng sau khi làm sạch, hoặc dài bất thường (câu dính liền) => null (loại).
    private static string? CleanHashtag(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (char.IsWhiteSpace(ch) || ch == '#')
                continue;
            builder.Append(ch);
        }

        if (builder.Length == 0 || builder.Length > MaxHashtagLength)
            return null;
        return "#" + builder.ToString();
    }

    // Trùng ý = một hook chứa/bằng hook đã thấy (đều đã chuẩn hóa lower + gom khoảng trắng).
    private static bool IsDuplicate(IReadOnlyList<string> seen, string normalized)
    {
        foreach (var prior in seen)
        {
            if (prior.Length == 0)
                continue;
            if (prior.Equals(normalized, StringComparison.Ordinal)
                || prior.Contains(normalized, StringComparison.Ordinal)
                || normalized.Contains(prior, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousWhitespace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWhitespace)
                    builder.Append(' ');
                previousWhitespace = true;
            }
            else
            {
                builder.Append(ch);
                previousWhitespace = false;
            }
        }

        return builder.ToString();
    }
}
