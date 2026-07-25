using Clawbot.Agents.Core.Content;
using Clawbot.Agents.Core.Content.Chain;
using FluentAssertions;

namespace Clawbot.Agents.Tests.Content.Chain;

// Unit test cổng kiểm tất định (plan §9): G1 = ParsePlan, G3 = CheckBody. Cổng là HÀM THUẦN nên
// test không cần LLM/DI — chỉ dựng chuỗi đầu vào rồi khẳng định mã lỗi. Coi output LLM là dữ liệu bẩn.
public sealed class ContentChainGatesTests
{
    // ===== G1 — ParsePlan =====

    [Fact]
    public void ParsePlan_ReturnsPlan_WhenJsonValid()
    {
        var result = ContentChainGates.ParsePlan(PlanJson());

        result.Succeeded.Should().BeTrue();
        result.ErrorCode.Should().BeEmpty();
        result.Plan.Should().NotBeNull();
        result.Plan!.Objective.Should().Be("awareness");
        result.Plan.KeyMessage.Should().Be("Khoa tieng Trung cho nguoi moi bat dau");
        result.Plan.Cta.Type.Should().Be("inbox");
        result.Plan.Language.Should().Be("vi");
        result.Plan.Offer.Should().BeNull();
    }

    [Fact]
    public void ParsePlan_NormalizesEnumsToLowercase_WhenModelUppercases()
    {
        var result = ContentChainGates.ParsePlan(PlanJson(objective: "AWARENESS", ctaType: "Inbox", language: "VI"));

        result.Succeeded.Should().BeTrue();
        result.Plan!.Objective.Should().Be("awareness");
        result.Plan.Cta.Type.Should().Be("inbox");
        result.Plan.Language.Should().Be("vi");
    }

    [Fact]
    public void ParsePlan_KeepsOffer_WhenPresent()
    {
        var result = ContentChainGates.ParsePlan(PlanJson(offerJson: "\"Giam 20% hoc phi\""));

        result.Succeeded.Should().BeTrue();
        result.Plan!.Offer.Should().Be("Giam 20% hoc phi");
    }

    [Fact]
    public void ParsePlan_UnwrapsMarkdownFence_WhenModelWrapsJson()
    {
        var fenced = "```json\n" + PlanJson() + "\n```";

        var result = ContentChainGates.ParsePlan(fenced);

        result.Succeeded.Should().BeTrue();
        result.Plan.Should().NotBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParsePlan_Fails_WhenEmpty(string? raw)
    {
        var result = ContentChainGates.ParsePlan(raw);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanEmptyOutput);
        result.Plan.Should().BeNull();
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ broken json ")]
    public void ParsePlan_Fails_WhenNotParseable(string raw)
    {
        var result = ContentChainGates.ParsePlan(raw);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanParseFailed);
    }

    [Fact]
    public void ParsePlan_Fails_WhenRootNotObject()
    {
        var result = ContentChainGates.ParsePlan("[1,2,3]");

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanParseFailed);
    }

    [Fact]
    public void ParsePlan_Fails_WhenKeyMessageMissing()
    {
        var result = ContentChainGates.ParsePlan(PlanJson(keyMessage: ""));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanFieldMissing);
    }

    [Theory]
    [InlineData("selling")]
    [InlineData("")]
    public void ParsePlan_Fails_WhenObjectiveNotInAllowList(string objective)
    {
        var result = ContentChainGates.ParsePlan(PlanJson(objective: objective));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanEnumInvalid);
    }

    [Fact]
    public void ParsePlan_Fails_WhenLanguageNotInAllowList()
    {
        var result = ContentChainGates.ParsePlan(PlanJson(language: "fr"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanEnumInvalid);
    }

    [Fact]
    public void ParsePlan_Fails_WhenCtaObjectMissing()
    {
        // JSON hợp lệ nhưng thiếu hẳn object cta.
        const string json = """
        {"objective":"awareness","audience":"a","keyMessage":"km day du","offer":null,"tone":"t","mustInclude":[],"mustAvoid":[],"language":"vi"}
        """;

        var result = ContentChainGates.ParsePlan(json);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanFieldMissing);
    }

    [Fact]
    public void ParsePlan_Fails_WhenCtaTypeInvalid()
    {
        var result = ContentChainGates.ParsePlan(PlanJson(ctaType: "dm"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanEnumInvalid);
    }

    [Fact]
    public void ParsePlan_Fails_WhenCtaTextMissing()
    {
        var result = ContentChainGates.ParsePlan(PlanJson(ctaText: ""));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanFieldMissing);
    }

    [Fact]
    public void ParsePlan_Fails_WhenTooManyMustIncludeItems()
    {
        var eleven = Enumerable.Range(0, 11).Select(i => "item" + i);

        var result = ContentChainGates.ParsePlan(PlanJson(mustInclude: eleven));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanTooManyItems);
    }

    [Fact]
    public void ParsePlan_Fails_WhenFieldTooLong()
    {
        var result = ContentChainGates.ParsePlan(PlanJson(keyMessage: new string('a', 401)));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanFieldTooLong);
    }

    [Fact]
    public void ParsePlan_Fails_WhenListItemTooLong()
    {
        var result = ContentChainGates.ParsePlan(PlanJson(mustInclude: new[] { new string('b', 201) }));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanFieldTooLong);
    }

    [Fact]
    public void ParsePlan_Fails_WhenFieldContainsUrl()
    {
        var result = ContentChainGates.ParsePlan(PlanJson(keyMessage: "Xem tai https://example.com nhe"));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanContainsUrl);
    }

    [Fact]
    public void ParsePlan_Fails_WhenListTypeWrong()
    {
        // mustInclude là mảng số => sai kiểu phần tử => parse fail (không phải string).
        const string json = """
        {"objective":"awareness","audience":"a","keyMessage":"km day du","offer":null,"tone":"t","cta":{"type":"inbox","text":"nhan tin"},"mustInclude":[1,2],"mustAvoid":[],"language":"vi"}
        """;

        var result = ContentChainGates.ParsePlan(json);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PlanParseFailed);
    }

    // ===== G3 — CheckBody =====

    private static readonly ContentChainLimits Limits = new(Min: 10, Max: 5000);

    [Fact]
    public void CheckBody_Passes_WhenBodyValidVietnamese()
    {
        const string body = "Khoa tiếng Trung cho người mới bắt đầu, học phí ưu đãi, nhắn tin để được tư vấn nhé.";

        var result = ContentChainGates.CheckBody(body, brief: "brief ngắn", language: "vi", Limits);

        result.Succeeded.Should().BeTrue();
        result.ErrorCode.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CheckBody_Fails_WhenEmpty(string? body)
    {
        var result = ContentChainGates.CheckBody(body, brief: "b", language: "vi", Limits);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.WriteEmptyOutput);
    }

    [Fact]
    public void CheckBody_Fails_WhenTooShort()
    {
        var result = ContentChainGates.CheckBody("ngắn", brief: "b", language: "vi", new ContentChainLimits(Min: 20, Max: 100));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.WriteTooShort);
    }

    [Fact]
    public void CheckBody_Fails_WhenTooLong()
    {
        var body = new string('a', 51);

        var result = ContentChainGates.CheckBody(body, brief: "b", language: "en", new ContentChainLimits(Min: 10, Max: 50));

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.WriteTooLong);
    }

    [Theory]
    [InlineData("Bài viết có link http://spam.vn ở đây")]
    [InlineData("Truy cập www.spam.vn ngay hôm nay")]
    public void CheckBody_Fails_WhenContainsUrl(string body)
    {
        var result = ContentChainGates.CheckBody(body, brief: "b", language: "vi", Limits);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.WriteContainsUrl);
    }

    [Fact]
    public void CheckBody_Fails_WhenPlaceholderLeft()
    {
        const string body = "Xin chào {{tên khách hàng}}, mời bạn tham gia khóa học nhé.";

        var result = ContentChainGates.CheckBody(body, brief: "b", language: "vi", Limits);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.WritePlaceholderLeft);
    }

    [Fact]
    public void CheckBody_Fails_WhenBodyCopiesBrief()
    {
        // Brief đủ dài (>= 40 ký tự sau khi gom khoảng trắng) và bài chép nguyên văn brief.
        const string brief = "Viết bài quảng cáo khóa tiếng Trung giao tiếp cho người đi làm bận rộn";
        var body = brief + " Nhắn tin ngay để nhận tư vấn.";

        var result = ContentChainGates.CheckBody(body, brief, language: "vi", Limits);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.WriteCopiesBrief);
    }

    [Fact]
    public void CheckBody_IgnoresBriefCopy_WhenBriefTooShort()
    {
        // Brief < 40 ký tự: trùng lặp là ngẫu nhiên, không tính là chép.
        const string brief = "khóa tiếng Trung";
        var body = brief + " cho người mới, nhắn tin để được tư vấn thêm nhé bạn ơi.";

        var result = ContentChainGates.CheckBody(body, brief, language: "vi", Limits);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void CheckBody_Fails_WhenVietnameseBodyHasNoDiacritics()
    {
        // language=vi, bài đủ dài (>60) nhưng không có ký tự có dấu => nhiều khả năng sai ngôn ngữ.
        var body = new string('a', 61) + " bai viet khong dau";

        var result = ContentChainGates.CheckBody(body, brief: "brief khac han noi dung", language: "vi", Limits);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.WriteLanguageMismatch);
    }

    [Fact]
    public void CheckBody_SkipsLanguageCheck_WhenLanguageEnglish()
    {
        // language=en: bài toàn ASCII là bình thường, không báo sai ngôn ngữ.
        var body = "This is a perfectly valid English social post that is well over sixty characters long.";

        var result = ContentChainGates.CheckBody(body, brief: "some different brief text here", language: "en", Limits);

        result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public void CheckBody_SkipsLanguageCheck_WhenBodyShort()
    {
        // Bài ngắn (<= 60) có thể không dấu mà vẫn hợp lệ (vượt Min nhưng dưới ngưỡng soi ngôn ngữ).
        var result = ContentChainGates.CheckBody("Uu dai hoc phi", brief: "b", language: "vi", Limits);

        result.Succeeded.Should().BeTrue();
    }

    // ===== G2 — ParseOutline (đối chiếu citation) =====

    [Fact]
    public void ParseOutline_Succeeds_WhenJsonValid()
    {
        var result = ContentChainGates.ParseOutline(OutlineJson(), citationCount: 2);

        result.Succeeded.Should().BeTrue();
        result.ErrorCode.Should().BeEmpty();
        result.Outline.Should().NotBeNull();
        result.Outline!.Hooks.Should().HaveCount(3);
        result.Outline.Sections.Should().HaveCount(1);
        result.Outline.ProofPoints.Should().HaveCount(1);
        result.Outline.ProofPoints[0].CitationId.Should().Be(1);
        result.Outline.DroppedProofPoints.Should().Be(0);
        result.Outline.RiskFlags.Should().HaveCount(1);
        result.Outline.SelectedHookIndex.Should().Be(-1); // ParseOutline chưa chọn hook — OutlineStep mới chọn
    }

    [Fact]
    public void ParseOutline_UnwrapsMarkdownFence_WhenModelWrapsJson()
    {
        var fenced = "```json\n" + OutlineJson() + "\n```";

        var result = ContentChainGates.ParseOutline(fenced, citationCount: 2);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseOutline_Fails_WhenEmpty(string? raw)
    {
        var result = ContentChainGates.ParseOutline(raw, citationCount: 2);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.OutlineEmptyOutput);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ broken json ")]
    public void ParseOutline_Fails_WhenNotParseable(string raw)
    {
        var result = ContentChainGates.ParseOutline(raw, citationCount: 2);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.OutlineParseFailed);
    }

    [Fact]
    public void ParseOutline_Fails_WhenRootNotObject()
    {
        var result = ContentChainGates.ParseOutline("[1,2,3]", citationCount: 2);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.OutlineParseFailed);
    }

    [Fact]
    public void ParseOutline_Fails_WhenHooksEmpty()
    {
        var result = ContentChainGates.ParseOutline(OutlineJson(hooksJson: "[]"), citationCount: 2);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.OutlineNoHooks);
    }

    [Fact]
    public void ParseOutline_Fails_WhenHooksMissing()
    {
        const string json = """{"outline":[],"proofPoints":[],"riskFlags":[]}""";

        var result = ContentChainGates.ParseOutline(json, citationCount: 2);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.OutlineNoHooks);
    }

    [Fact]
    public void ParseOutline_Fails_WhenHooksWrongType()
    {
        var result = ContentChainGates.ParseOutline(OutlineJson(hooksJson: "\"khong phai mang\""), citationCount: 2);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.OutlineParseFailed);
    }

    [Fact]
    public void ParseOutline_Fails_WhenTooManyHooks()
    {
        var eleven = JsonArray(Enumerable.Range(0, 11).Select(i => "hook so " + i));

        var result = ContentChainGates.ParseOutline(OutlineJson(hooksJson: eleven), citationCount: 2);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.OutlineTooManyItems);
    }

    [Fact]
    public void ParseOutline_Fails_WhenHookTooLong()
    {
        var longHook = JsonArray(new[] { new string('a', 401) });

        var result = ContentChainGates.ParseOutline(OutlineJson(hooksJson: longHook), citationCount: 2);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.OutlineFieldTooLong);
    }

    [Fact]
    public void ParseOutline_Fails_WhenProofPointsWrongType()
    {
        var result = ContentChainGates.ParseOutline(
            OutlineJson(proofPointsJson: "\"khong phai mang\""), citationCount: 2);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.OutlineParseFailed);
    }

    [Fact]
    public void ParseOutline_Fails_WhenTooManyProofPoints()
    {
        var many = "[" + string.Join(",", Enumerable.Range(0, 11)
            .Select(i => "{\"claim\":\"c" + i + "\",\"citationId\":1}")) + "]";

        var result = ContentChainGates.ParseOutline(OutlineJson(proofPointsJson: many), citationCount: 2);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.OutlineTooManyItems);
    }

    [Fact]
    public void ParseOutline_DropsProofPoint_WhenCitationOutOfRange()
    {
        // citationId=5 nhưng chỉ có 2 chunk => loại điểm đó (evidence_missing), KHÔNG fail cả dàn ý.
        const string proof = """[{"claim":"con so bia dat","citationId":5}]""";

        var result = ContentChainGates.ParseOutline(OutlineJson(proofPointsJson: proof), citationCount: 2);

        result.Succeeded.Should().BeTrue();
        result.Outline!.ProofPoints.Should().BeEmpty();
        result.Outline.DroppedProofPoints.Should().Be(1);
    }

    [Fact]
    public void ParseOutline_DropsProofPoint_WhenCitationZeroOrNegative()
    {
        const string proof =
            """[{"claim":"a","citationId":0},{"claim":"b","citationId":-1},{"claim":"c","citationId":1}]""";

        var result = ContentChainGates.ParseOutline(OutlineJson(proofPointsJson: proof), citationCount: 2);

        result.Succeeded.Should().BeTrue();
        result.Outline!.ProofPoints.Should().HaveCount(1);
        result.Outline.ProofPoints[0].CitationId.Should().Be(1);
        result.Outline.DroppedProofPoints.Should().Be(2);
    }

    [Fact]
    public void ParseOutline_DropsProofPoint_WhenCitationMissing()
    {
        const string proof = """[{"claim":"khong co citation"}]""";

        var result = ContentChainGates.ParseOutline(OutlineJson(proofPointsJson: proof), citationCount: 2);

        result.Succeeded.Should().BeTrue();
        result.Outline!.ProofPoints.Should().BeEmpty();
        result.Outline.DroppedProofPoints.Should().Be(1);
    }

    [Fact]
    public void ParseOutline_DropsAllProofPoints_WhenNoChunks()
    {
        // citationCount=0 => không citation nào hợp lệ => mọi proofPoint bị loại, dàn ý vẫn qua.
        var result = ContentChainGates.ParseOutline(OutlineJson(), citationCount: 0);

        result.Succeeded.Should().BeTrue();
        result.Outline!.ProofPoints.Should().BeEmpty();
        result.Outline.DroppedProofPoints.Should().Be(1);
    }

    [Fact]
    public void ParseOutline_KeepsProofPoint_WhenCitationIsNumericString()
    {
        // Khoan dung: model trả citationId dạng chuỗi số "2".
        const string proof = """[{"claim":"hop le","citationId":"2"}]""";

        var result = ContentChainGates.ParseOutline(OutlineJson(proofPointsJson: proof), citationCount: 2);

        result.Succeeded.Should().BeTrue();
        result.Outline!.ProofPoints.Should().HaveCount(1);
        result.Outline.ProofPoints[0].CitationId.Should().Be(2);
    }

    [Fact]
    public void ParseOutline_Allows_WhenSectionsAndProofPointsEmpty()
    {
        var result = ContentChainGates.ParseOutline(
            OutlineJson(outlineJson: "[]", proofPointsJson: "[]", riskFlagsJson: "[]"), citationCount: 2);

        result.Succeeded.Should().BeTrue();
        result.Outline!.Sections.Should().BeEmpty();
        result.Outline.ProofPoints.Should().BeEmpty();
        result.Outline.RiskFlags.Should().BeEmpty();
    }

    // ===== chọn hook tất định (SelectHook, §4.5) =====

    [Fact]
    public void SelectHook_ReturnsMinusOne_WhenNoHooks()
    {
        var selection = ContentChainGates.SelectHook(Array.Empty<string>(), Array.Empty<string>(), hasProof: false);

        selection.SelectedIndex.Should().Be(-1);
        selection.Scores.Should().BeEmpty();
    }

    [Fact]
    public void SelectHook_PrefersHookMatchingMustInclude()
    {
        var hooks = new[]
        {
            "Mot cau mo bai chung chung du dai",       // đủ dài, không khớp mustInclude
            "Lich khai giang thang chin da san sang",   // đủ dài + khớp "khai giang"
        };

        var selection = ContentChainGates.SelectHook(hooks, new[] { "khai giang" }, hasProof: false);

        selection.SelectedIndex.Should().Be(1);
    }

    [Fact]
    public void SelectHook_PenalizesDuplicateHooks()
    {
        var hooks = new[]
        {
            "Hoc tieng Trung de mo tuong lai moi",
            "Hoc tieng Trung de mo tuong lai moi",     // trùng hệt hook đầu
            "Bat dau hanh trinh chinh phuc HSK ngay",   // độc nhất
        };

        var selection = ContentChainGates.SelectHook(hooks, Array.Empty<string>(), hasProof: false);

        selection.SelectedIndex.Should().NotBe(1);
        selection.Scores[1].Should().BeLessThan(selection.Scores[0]);
    }

    [Fact]
    public void SelectHook_RewardsDataBackedHook_WhenProofExists()
    {
        var hooks = new[]
        {
            "Mot cau mo bai khong co so lieu nao ca",
            "Toi 90 phan tram hoc vien dat HSK4 som",   // có số + có proofPoint
        };

        var selection = ContentChainGates.SelectHook(hooks, Array.Empty<string>(), hasProof: true);

        selection.SelectedIndex.Should().Be(1);
        selection.Scores[1].Should().BeGreaterThan(selection.Scores[0]);
    }

    [Fact]
    public void SelectHook_PicksLowestIndex_OnTie()
    {
        var hooks = new[]
        {
            "Cau mo bai thu nhat vua du do dai nhe",
            "Cau mo bai thu hai cung vua du do dai",
        };

        var selection = ContentChainGates.SelectHook(hooks, Array.Empty<string>(), hasProof: false);

        selection.SelectedIndex.Should().Be(0);
    }

    // ===== G4 — ParsePackage (đóng gói + chuẩn hóa hashtag) =====

    [Fact]
    public void ParsePackage_Succeeds_WhenJsonValid()
    {
        var result = ContentChainGates.ParsePackage(PackageJson(), captionMax: 5000, hashtagMax: 30);

        result.Succeeded.Should().BeTrue();
        result.ErrorCode.Should().BeEmpty();
        result.Package.Should().NotBeNull();
        result.Package!.Caption.Should().Be("Caption hoan chinh cho bai dang");
        result.Package.Hashtags.Should().HaveCount(2);
        result.Package.DroppedHashtags.Should().Be(0);
        result.Package.FirstComment.Should().Be("Binh luan dau tien");
        result.Package.AltText.Should().Be("Mo ta anh");
    }

    [Fact]
    public void ParsePackage_UnwrapsMarkdownFence_WhenModelWrapsJson()
    {
        var fenced = "```json\n" + PackageJson() + "\n```";

        var result = ContentChainGates.ParsePackage(fenced, captionMax: 5000, hashtagMax: 30);

        result.Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParsePackage_Fails_WhenEmpty(string? raw)
    {
        var result = ContentChainGates.ParsePackage(raw, captionMax: 5000, hashtagMax: 30);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PackageEmptyOutput);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ broken json ")]
    public void ParsePackage_Fails_WhenNotParseable(string raw)
    {
        var result = ContentChainGates.ParsePackage(raw, captionMax: 5000, hashtagMax: 30);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PackageParseFailed);
    }

    [Fact]
    public void ParsePackage_Fails_WhenRootNotObject()
    {
        var result = ContentChainGates.ParsePackage("[1,2,3]", captionMax: 5000, hashtagMax: 30);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PackageParseFailed);
    }

    [Fact]
    public void ParsePackage_Fails_WhenCaptionEmpty()
    {
        var result = ContentChainGates.ParsePackage(PackageJson(caption: ""), captionMax: 5000, hashtagMax: 30);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PackageCaptionEmpty);
    }

    [Fact]
    public void ParsePackage_Fails_WhenHashtagsWrongType()
    {
        var result = ContentChainGates.ParsePackage(
            PackageJson(hashtagsJson: "\"khong phai mang\""), captionMax: 5000, hashtagMax: 30);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PackageParseFailed);
    }

    [Fact]
    public void ParsePackage_Fails_WhenMergedBodyExceedsCaptionMax()
    {
        // Trần áp cho BÀI CUỐI (caption + hashtags). Caption vừa sát trần + hashtag => vượt.
        var caption = new string('a', 40);

        var result = ContentChainGates.ParsePackage(
            PackageJson(caption: caption, hashtagsJson: """["#hoctiengtrung"]"""),
            captionMax: 45,
            hashtagMax: 30);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PackageCaptionTooLong);
    }

    [Fact]
    public void ParsePackage_Fails_WhenFirstCommentTooLong()
    {
        var result = ContentChainGates.ParsePackage(
            PackageJson(firstCommentJson: "\"" + new string('x', 401) + "\""),
            captionMax: 5000,
            hashtagMax: 30);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentChainErrorCodes.PackageFieldTooLong);
    }

    [Fact]
    public void ParsePackage_NormalizesHashtag_StripsInternalSpaceAndHash()
    {
        // "# hoc tieng trung" => gom mọi khoảng trắng + '#' bên trong, ép đúng một '#' đầu.
        var result = ContentChainGates.ParsePackage(
            PackageJson(hashtagsJson: """["# hoc tieng trung"]"""), captionMax: 5000, hashtagMax: 30);

        result.Succeeded.Should().BeTrue();
        result.Package!.Hashtags.Should().ContainSingle().Which.Should().Be("#hoctiengtrung");
    }

    [Fact]
    public void ParsePackage_DropsDuplicateHashtags_CaseInsensitive()
    {
        var result = ContentChainGates.ParsePackage(
            PackageJson(hashtagsJson: """["#HocTiengTrung","#hoctiengtrung","#hsk"]"""),
            captionMax: 5000,
            hashtagMax: 30);

        result.Succeeded.Should().BeTrue();
        result.Package!.Hashtags.Should().HaveCount(2);
        result.Package.DroppedHashtags.Should().Be(1);
    }

    [Fact]
    public void ParsePackage_DropsBannedHashtags()
    {
        var result = ContentChainGates.ParsePackage(
            PackageJson(hashtagsJson: """["#f4f","#follow4follow","#hsk"]"""),
            captionMax: 5000,
            hashtagMax: 30);

        result.Succeeded.Should().BeTrue();
        result.Package!.Hashtags.Should().ContainSingle().Which.Should().Be("#hsk");
        result.Package.DroppedHashtags.Should().Be(2);
    }

    [Fact]
    public void ParsePackage_CapsHashtagsAtMax_AndCountsDropped()
    {
        var five = """["#a1","#a2","#a3","#a4","#a5"]""";

        var result = ContentChainGates.ParsePackage(
            PackageJson(hashtagsJson: five), captionMax: 5000, hashtagMax: 3);

        result.Succeeded.Should().BeTrue();
        result.Package!.Hashtags.Should().HaveCount(3);
        result.Package.DroppedHashtags.Should().Be(2);
    }

    [Fact]
    public void ParsePackage_DropsOverlongHashtag_AsJunk()
    {
        var longTag = "\"#" + new string('z', 101) + "\"";

        var result = ContentChainGates.ParsePackage(
            PackageJson(hashtagsJson: "[" + longTag + ",\"#hsk\"]"), captionMax: 5000, hashtagMax: 30);

        result.Succeeded.Should().BeTrue();
        result.Package!.Hashtags.Should().ContainSingle().Which.Should().Be("#hsk");
        result.Package.DroppedHashtags.Should().Be(1);
    }

    [Fact]
    public void ParsePackage_AllowsNullOptionalFields()
    {
        var result = ContentChainGates.ParsePackage(
            PackageJson(firstCommentJson: "null", altTextJson: "null"), captionMax: 5000, hashtagMax: 30);

        result.Succeeded.Should().BeTrue();
        result.Package!.FirstComment.Should().BeNull();
        result.Package.AltText.Should().BeNull();
    }

    [Fact]
    public void MergePackageBody_JoinsCaptionAndHashtags()
    {
        var package = new ContentPackage(
            "Caption day", new[] { "#hsk", "#hoctiengtrung" }, FirstComment: null, AltText: null, DroppedHashtags: 0);

        var body = ContentChainGates.MergePackageBody(package);

        body.Should().Be("Caption day\n\n#hsk #hoctiengtrung");
    }

    [Fact]
    public void MergePackageBody_ReturnsCaptionOnly_WhenNoHashtags()
    {
        var package = new ContentPackage(
            "Chi co caption", Array.Empty<string>(), FirstComment: null, AltText: null, DroppedHashtags: 0);

        ContentChainGates.MergePackageBody(package).Should().Be("Chi co caption");
    }

    // ===== ContentLint (§4.7) — lint tất định trước reviewer LLM =====

    [Fact]
    public void ContentLint_Passes_WhenBodyClean()
    {
        const string body = "Khoa tieng Trung giao tiep cho nguoi di lam, nhan tin de duoc tu van nhe.";

        var result = ContentLint.Check(body);

        result.Succeeded.Should().BeTrue();
        result.ErrorCode.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ContentLint_Passes_WhenEmpty(string? body)
    {
        // Body rỗng do reviewer chặn bằng nhánh riêng; lint không phán về rỗng.
        ContentLint.Check(body).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Cam kết 100% đỗ HSK5 sau khóa học")]
    [InlineData("Trung tâm đảm bảo đầu ra cho mọi học viên")]
    [InlineData("Học là chắc chắn đậu, không đậu hoàn tiền")]
    public void ContentLint_Fails_OnAbsoluteGuarantee(string body)
    {
        var result = ContentLint.Check(body);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentLintCodes.AbsoluteGuarantee);
    }

    [Fact]
    public void ContentLint_AllowsLegitPromo_NotFlaggedAsGuarantee()
    {
        // "giảm 100% học phí" là ưu đãi hợp lệ, KHÔNG phải cam kết đỗ/đậu.
        const string body = "Uu dai dac biet: giam 100% hoc phi thang dau cho hoc vien moi dang ky.";

        ContentLint.Check(body).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("Xem chi tiet tai http://spam.vn ngay")]
    [InlineData("Truy cap https://example.com de biet them")]
    [InlineData("Vao www.trungtam.vn dang ky nhe")]
    public void ContentLint_Fails_OnExternalLink(string body)
    {
        var result = ContentLint.Check(body);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentLintCodes.ExternalLink);
    }

    [Fact]
    public void ContentLint_Fails_OnReplacementChar()
    {
        var body = "Noi dung bi hong ma hoa " + (char)0xFFFD + " o day";

        var result = ContentLint.Check(body);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentLintCodes.JunkChars);
    }

    [Fact]
    public void ContentLint_Fails_OnControlChar()
    {
        var body = "Noi dung co ky tu dieu khien  chuong";

        var result = ContentLint.Check(body);

        result.Succeeded.Should().BeFalse();
        result.ErrorCode.Should().Be(ContentLintCodes.JunkChars);
    }

    [Fact]
    public void ContentLint_AllowsTabAndNewline()
    {
        const string body = "Dong mot\nDong hai\tco tab van hop le nhe ban oi mua thu.";

        ContentLint.Check(body).Succeeded.Should().BeTrue();
    }

    // ===== helper — dựng JSON plan hợp lệ, cho override từng trường để test negative =====
    private static string PlanJson(
        string objective = "awareness",
        string keyMessage = "Khoa tieng Trung cho nguoi moi bat dau",
        string language = "vi",
        string ctaType = "inbox",
        string ctaText = "Nhan tin de biet them",
        string offerJson = "null",
        IEnumerable<string>? mustInclude = null,
        IEnumerable<string>? mustAvoid = null)
    {
        var include = JsonArray(mustInclude ?? new[] { "lich khai giang" });
        var avoid = JsonArray(mustAvoid ?? new[] { "cam ket dau ra" });
        return $$"""
        {"objective":"{{objective}}","audience":"phu huynh","keyMessage":"{{keyMessage}}","offer":{{offerJson}},"tone":"than thien","cta":{"type":"{{ctaType}}","text":"{{ctaText}}"},"mustInclude":{{include}},"mustAvoid":{{avoid}},"language":"{{language}}"}
        """;
    }

    // ===== helper — dựng JSON dàn ý (L2) hợp lệ, cho override từng khối để test G2 =====
    private static string OutlineJson(
        string hooksJson = """["Hook mot hap dan nhe","Hook hai khac biet han","Hook ba moi la day"]""",
        string outlineJson = """[{"section":"Mo bai","points":["diem mot","diem hai"]}]""",
        string proofPointsJson = """[{"claim":"90 phan tram hoc vien tien bo","citationId":1}]""",
        string riskFlagsJson = """["Tranh cam ket tuyet doi"]""")
    {
        return $$"""
        {"hooks":{{hooksJson}},"outline":{{outlineJson}},"proofPoints":{{proofPointsJson}},"riskFlags":{{riskFlagsJson}}}
        """;
    }

    // ===== helper — dựng JSON package (L4) hợp lệ, cho override từng trường để test G4 =====
    private static string PackageJson(
        string caption = "Caption hoan chinh cho bai dang",
        string hashtagsJson = """["#hoctiengtrung","#hsk"]""",
        string firstCommentJson = "\"Binh luan dau tien\"",
        string altTextJson = "\"Mo ta anh\"")
    {
        return $$"""
        {"caption":"{{caption}}","hashtags":{{hashtagsJson}},"firstComment":{{firstCommentJson}},"altText":{{altTextJson}}}
        """;
    }

    private static string JsonArray(IEnumerable<string> items) =>
        "[" + string.Join(",", items.Select(i => "\"" + i.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"")) + "]";
}
