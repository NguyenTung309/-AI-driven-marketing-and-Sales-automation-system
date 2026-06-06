-- =============================================================
-- ClawBot — M05 chat_scenarios seed (KB-001..KB-050)
-- 50 sales conversation templates for a Chinese-language course (HSK) tenant.
-- Idempotent: MERGE on (tenant_id, code). Re-running updates existing rows in place.
--
-- USAGE: set the target tenant slug below, then apply:
--   sqlcmd -S <server> -d clawbot -i deploy/seed/chat-scenarios.sql
--
-- Groups: First | Lộ trình | Objection | Action | Platform | Follow-up
-- trigger_text is a regex (case-insensitive) consumed by ChatScenarioMatcher;
-- it falls back to substring containment when not a valid regex.
-- platforms is csv: zalo,facebook,tiktok,instagram,youtube  (or 'all').
-- =============================================================

SET NOCOUNT ON;

DECLARE @tenant_slug NVARCHAR(64) = N'demo';   -- <-- CHANGE to the target tenant slug
DECLARE @tenant_id UNIQUEIDENTIFIER = (SELECT id FROM tenants WHERE slug = @tenant_slug);

IF @tenant_id IS NULL
BEGIN
    RAISERROR(N'Tenant slug "%s" not found. Seed aborted.', 16, 1, @tenant_slug);
    RETURN;
END

MERGE INTO chat_scenarios AS target
USING (VALUES
    -- ---------------- First contact (greeting / discovery) ----------------
    (N'KB-001', N'First', N'(?i)(xin chào|chào shop|alo|hello|hi)\b', N'Chào bạn 👋 Mình là trợ lý của trung tâm tiếng Trung. Bạn đang muốn học giao tiếp, luyện thi HSK hay học cho công việc ạ?', N'friendly', N'all'),
    (N'KB-002', N'First', N'(?i)(mình mới|người mới|chưa biết gì|bắt đầu từ đâu|mất gốc)', N'Bạn mới bắt đầu hoàn toàn cũng không sao nhé! Lộ trình mất gốc của trung tâm đi từ phát âm pinyin → 200 từ vựng nền → mẫu câu giao tiếp. Bạn cho mình hỏi mục tiêu của bạn là gì ạ?', N'encouraging', N'all'),
    (N'KB-003', N'First', N'(?i)(trung tâm.*(ở đâu|địa chỉ)|học online|học offline)', N'Trung tâm có cả lớp online qua Zoom và offline tại cơ sở. Bạn ở khu vực nào để mình tư vấn lớp phù hợp ạ?', N'consultative', N'all'),
    (N'KB-004', N'First', N'(?i)(tư vấn|cho mình hỏi|hỏi chút|cần hỗ trợ)', N'Dạ mình sẵn sàng tư vấn ạ! Bạn quan tâm tới khoá giao tiếp, HSK hay tiếng Trung thương mại để mình gửi thông tin chi tiết nhé?', N'friendly', N'all'),
    (N'KB-005', N'First', N'(?i)(học.*(bao lâu|mất bao lâu)|nhanh không)', N'Tuỳ mục tiêu bạn nhé: giao tiếp cơ bản ~3 tháng, HSK3 ~4-5 tháng nếu học đều 3 buổi/tuần. Bạn muốn đạt mục tiêu trong khoảng thời gian nào ạ?', N'consultative', N'all'),
    (N'KB-006', N'First', N'(?i)(con mình|cho bé|trẻ em|học sinh cấp)', N'Trung tâm có lớp riêng cho các bé theo độ tuổi, giáo trình trực quan nhiều hình ảnh. Bé nhà mình bao nhiêu tuổi để mình xếp lớp phù hợp ạ?', N'warm', N'all'),
    (N'KB-007', N'First', N'(?i)(tên.*gì|bạn là ai|đây là)', N'Mình là trợ lý tư vấn của trung tâm tiếng Trung ạ. Mình hỗ trợ bạn tìm khoá học và lộ trình phù hợp nhất nhé!', N'friendly', N'all'),
    (N'KB-008', N'First', N'(?i)(học thử|trải nghiệm|buổi demo|test trình)', N'Trung tâm có 1 buổi học thử miễn phí + test trình độ đầu vào. Bạn muốn đặt lịch học thử buổi tối hay cuối tuần ạ?', N'inviting', N'all'),

    -- ---------------- Lộ trình (learning roadmap) ----------------
    (N'KB-009', N'Lộ trình', N'(?i)(lộ trình|roadmap|học theo trình tự|trình tự học)', N'Lộ trình chuẩn của trung tâm: Pinyin & phát âm → Sơ cấp (HSK1-2) → Trung cấp (HSK3-4) → Giao tiếp nâng cao. Mỗi cấp có kiểm tra cuối kỳ. Bạn đang ở mức nào ạ?', N'structured', N'all'),
    (N'KB-010', N'Lộ trình', N'(?i)(hsk\s*1|sơ cấp|cơ bản nhất)', N'HSK1-2 phù hợp người mới: ~300 từ, giao tiếp tình huống cơ bản, ~2.5 tháng. Học xong bạn tự giới thiệu, mua sắm, hỏi đường tốt. Bạn muốn xem giáo trình mẫu không ạ?', N'structured', N'all'),
    (N'KB-011', N'Lộ trình', N'(?i)(hsk\s*3|trung cấp)', N'HSK3 là mốc nhiều bạn cần cho công việc/du học: ~600 từ, đọc hiểu đoạn văn. Lộ trình ~4-5 tháng từ HSK2. Mình gửi bạn đề cương chi tiết nhé?', N'structured', N'all'),
    (N'KB-012', N'Lộ trình', N'(?i)(hsk\s*[4-6]|nâng cao|du học|luyện thi cao)', N'HSK4-6 dành cho du học & làm việc chuyên sâu. Trung tâm có lớp luyện đề bám sát format thi thật + chữa bài cá nhân. Bạn cần đạt HSK mấy và hạn thi khi nào ạ?', N'expert', N'all'),
    (N'KB-013', N'Lộ trình', N'(?i)(giao tiếp|nói chuyện|khẩu ngữ|nghe nói)', N'Khoá giao tiếp tập trung 70% luyện nói theo chủ đề thực tế, lớp nhỏ để bạn được nói nhiều. Bạn muốn giao tiếp cho du lịch, công việc hay đời sống ạ?', N'energetic', N'all'),
    (N'KB-014', N'Lộ trình', N'(?i)(thương mại|business|công việc|đi làm|văn phòng)', N'Tiếng Trung thương mại dạy từ vựng đàm phán, email, họp, hợp đồng. Phù hợp bạn đã có nền HSK3+. Bạn làm trong lĩnh vực nào để mình cá nhân hoá nội dung ạ?', N'professional', N'all'),
    (N'KB-015', N'Lộ trình', N'(?i)(giáo trình|tài liệu|sách học|học bằng gì)', N'Trung tâm dùng giáo trình Hán ngữ Boya/Standard Course HSK + tài liệu tự biên có phiên âm & ví dụ đời thường. Tài liệu được số hoá trên app học viên. Bạn muốn mình gửi link xem thử không ạ?', N'informative', N'all'),
    (N'KB-016', N'Lộ trình', N'(?i)(lớp.*(mấy người|sĩ số|bao nhiêu học viên)|lớp nhỏ)', N'Lớp giao tiếp tối đa 8 học viên, lớp HSK tối đa 12, đảm bảo giáo viên kèm sát từng bạn. Bạn thích lớp nhóm hay học 1 kèm 1 ạ?', N'reassuring', N'all'),
    (N'KB-017', N'Lộ trình', N'(?i)(1 kèm 1|gia sư|học riêng|cá nhân)', N'Lớp 1 kèm 1 linh hoạt lịch + lộ trình may đo theo mục tiêu của bạn, tiến độ nhanh hơn ~30%. Bạn muốn mình báo lịch giáo viên trống không ạ?', N'consultative', N'all'),
    (N'KB-018', N'Lộ trình', N'(?i)(giáo viên|giảng viên|thầy cô|ai dạy)', N'Giáo viên của trung tâm tốt nghiệp chuyên ngành tiếng Trung, có chứng chỉ giảng dạy + kinh nghiệm 3-8 năm, nhiều cô từng du học Trung Quốc. Bạn muốn xem profile giáo viên không ạ?', N'reassuring', N'all'),

    -- ---------------- Objection (price / doubt / hesitation) ----------------
    (N'KB-019', N'Objection', N'(?i)(giá|học phí|bao nhiêu tiền|chi phí|phí.*khoá)', N'Học phí tuỳ khoá ạ: giao tiếp cơ bản từ 2.4tr/khoá, HSK3 từ 3.2tr/khoá (~đã gồm tài liệu). Hiện đang có ưu đãi đăng ký sớm. Bạn muốn mình gửi bảng giá chi tiết theo mục tiêu của bạn không ạ?', N'transparent', N'all'),
    (N'KB-020', N'Objection', N'(?i)(đắt|mắc quá|hơi cao|cao quá|không có tiền)', N'Mình hiểu ạ. Trung tâm cho trả góp 0% theo 2 đợt và học phí đã gồm trọn tài liệu + app + lớp bù miễn phí. Tính ra mỗi buổi chỉ ~80-100k mà sĩ số nhỏ. Bạn muốn mình tư vấn gói tiết kiệm hơn không ạ?', N'empathetic', N'all'),
    (N'KB-021', N'Objection', N'(?i)(suy nghĩ|để xem|cân nhắc|tính sau|từ từ)', N'Dạ bạn cứ cân nhắc thoải mái ạ! Để dễ quyết, bạn đặt 1 buổi học thử miễn phí trải nghiệm giáo viên & phương pháp trước rồi quyết cũng được nhé. Bạn rảnh buổi nào ạ?', N'low-pressure', N'all'),
    (N'KB-022', N'Objection', N'(?i)(sợ.*(khó|không theo được)|học không vào|tiếng trung khó)', N'Rất nhiều bạn mất gốc đã học tốt nhờ phương pháp chia nhỏ + luyện lặp lại của trung tâm ạ. Có giáo viên kèm và nhóm hỗ trợ ngoài giờ nên bạn không lo bị bỏ lại. Bạn muốn thử 1 buổi xem có hợp không nhé?', N'encouraging', N'all'),
    (N'KB-023', N'Objection', N'(?i)(bận|không có thời gian|sợ.*không sắp xếp)', N'Trung tâm có lớp tối (19h30) và cuối tuần, kèm bản ghi buổi học để bạn xem lại nếu bận. Học viên bận vẫn theo được nhờ lịch linh hoạt. Bạn thường rảnh khung giờ nào ạ?', N'flexible', N'all'),
    (N'KB-024', N'Objection', N'(?i)(tự học|app|youtube|miễn phí.*được không)', N'Tự học qua app rất tốt để bổ trợ ạ, nhưng phát âm & phản xạ nói cần người sửa trực tiếp để khỏi sai thành thói quen. Trung tâm tặng kèm app + lộ trình rõ ràng nên bạn vừa có thầy vừa tự luyện. Bạn muốn so sánh thử không ạ?', N'consultative', N'all'),
    (N'KB-025', N'Objection', N'(?i)(trung tâm khác|chỗ khác rẻ hơn|so sánh)', N'Mỗi nơi một thế mạnh ạ. Điểm khác biệt của trung tâm là sĩ số nhỏ, cam kết đầu ra HSK và học lại miễn phí nếu chưa đạt. Bạn quan tâm tiêu chí nào nhất để mình tư vấn đúng trọng tâm ạ?', N'confident', N'all'),
    (N'KB-026', N'Objection', N'(?i)(cam kết|đảm bảo.*đầu ra|không đạt thì sao)', N'Trung tâm cam kết đầu ra theo cấp độ: nếu học đủ buổi mà chưa đạt mục tiêu, bạn được học lại miễn phí khoá tiếp theo. Mình gửi bạn điều khoản cam kết để bạn yên tâm nhé?', N'reassuring', N'all'),
    (N'KB-027', N'Objection', N'(?i)(học online.*hiệu quả|online.*được không|sợ online)', N'Lớp online của trung tâm tương tác trực tiếp như offline: chia phòng luyện nói, bảng tương tác, điểm danh & bài tập trên app. Học viên online đạt HSK tỉ lệ tương đương offline ạ. Bạn muốn dự thính 1 buổi online thử không?', N'reassuring', N'all'),
    (N'KB-028', N'Objection', N'(?i)(hỏi vợ|hỏi chồng|bàn với gia đình|hỏi ba mẹ)', N'Dạ bạn trao đổi với gia đình là hợp lý ạ. Mình gửi bạn bảng giá + lộ trình + lịch khai giảng để bạn dễ trình bày nhé. Bạn cho mình xin thời điểm dự kiến bắt đầu để giữ ưu đãi giúp bạn ạ?', N'low-pressure', N'all'),
    (N'KB-029', N'Objection', N'(?i)(lo.*quên|học xong quên|không có ai luyện)', N'Trung tâm có nhóm luyện nói định kỳ + ôn tập spaced-repetition trên app để chống quên, kèm câu lạc bộ tiếng Trung hàng tuần. Bạn sẽ luôn có môi trường dùng tiếng. Bạn muốn tham gia thử buổi CLB không ạ?', N'encouraging', N'all'),
    (N'KB-030', N'Objection', N'(?i)(ưu đãi|khuyến mãi|giảm giá|coupon|mã giảm)', N'Hiện trung tâm có ưu đãi đăng ký sớm giảm tới 15% + tặng bộ tài liệu phát âm. Ưu đãi áp dụng cho lớp khai giảng tháng này thôi ạ. Bạn muốn mình giữ suất ưu đãi giúp bạn không ạ?', N'urgency', N'all'),

    -- ---------------- Action (booking / CTA / close) ----------------
    (N'KB-031', N'Action', N'(?i)(đăng ký|ghi danh|muốn học|tham gia khoá)', N'Tuyệt vời ạ! Để đăng ký, mình xin tên + số điện thoại + mục tiêu học của bạn nhé, mình sẽ giữ suất lớp và gửi hướng dẫn đóng học phí. Bạn cho mình xin thông tin nha 🎉', N'enthusiastic', N'all'),
    (N'KB-032', N'Action', N'(?i)(đặt lịch|book.*(thử|test)|hẹn.*học thử)', N'Mình đặt lịch học thử miễn phí cho bạn nhé. Bạn chọn giúp mình: tối T2/T4/T6 (19h30) hay sáng T7 (9h)? Mình cần thêm số điện thoại để gửi link/nhắc lịch ạ.', N'action', N'all'),
    (N'KB-033', N'Action', N'(?i)(test trình độ|kiểm tra trình|thi xếp lớp|đánh giá đầu vào)', N'Mình gửi bạn bài test xếp lớp ~15 phút online nhé, có kết quả ngay để xếp đúng trình độ. Bạn muốn làm test bây giờ hay để mình hẹn giờ gọi tư vấn kèm test ạ?', N'action', N'all'),
    (N'KB-034', N'Action', N'(?i)(số tài khoản|đóng học phí|thanh toán|chuyển khoản)', N'Dạ mình gửi thông tin thanh toán qua tin nhắn riêng để bảo mật nhé. Sau khi chuyển khoản bạn gửi mình ảnh xác nhận, trung tâm kích hoạt tài khoản học + xếp lớp trong 24h ạ.', N'professional', N'all'),
    (N'KB-035', N'Action', N'(?i)(khai giảng|lớp mới|khi nào học|lịch khai giảng)', N'Lớp mới khai giảng vào đầu mỗi tháng, đợt gần nhất còn vài suất ạ. Bạn muốn vào lớp tối hay cuối tuần để mình giữ chỗ trước khi đầy lớp nhé?', N'urgency', N'all'),
    (N'KB-036', N'Action', N'(?i)(cho.*số điện thoại|gọi cho mình|tư vấn qua điện thoại|hotline)', N'Dạ bạn để lại số điện thoại + khung giờ tiện nghe máy, tư vấn viên sẽ gọi tư vấn miễn phí và gửi lộ trình chi tiết cho bạn ạ.', N'helpful', N'all'),
    (N'KB-037', N'Action', N'(?i)(gửi.*(thông tin|bảng giá|brochure)|cho xin tài liệu)', N'Mình gửi ngay bảng giá + lộ trình + lịch khai giảng cho bạn nhé. Bạn muốn nhận qua Zalo hay email ạ?', N'helpful', N'all'),
    (N'KB-038', N'Action', N'(?i)(^ok$|chốt|đồng ý|được.*đăng ký|lấy lớp)', N'Cảm ơn bạn đã tin tưởng trung tâm 🎉 Mình xác nhận giữ suất cho bạn. Bạn cho mình xin tên + SĐT để hoàn tất đăng ký và gửi hướng dẫn buổi đầu tiên nhé!', N'enthusiastic', N'all'),
    (N'KB-039', N'Action', N'(?i)(địa điểm học|đến trung tâm|tới cơ sở|đường đi)', N'Dạ mình gửi địa chỉ + bản đồ cơ sở gần bạn nhất qua tin nhắn nhé. Bạn dự định đến buổi học thử ngày nào để lễ tân chuẩn bị đón bạn ạ?', N'helpful', N'all'),

    -- ---------------- Platform-specific ----------------
    (N'KB-040', N'Platform', N'(?i)(giá|inbox|tư vấn|đăng ký)', N'Cảm ơn bạn đã nhắn tin! 😍 Inbox bị giới hạn, bạn để lại SĐT/Zalo để được tư vấn lộ trình + bảng giá chi tiết nhanh nhất nhé. Trung tâm đang có ưu đãi học phí tháng này ạ!', N'energetic', N'facebook,instagram'),
    (N'KB-041', N'Platform', N'(?i)(comment|cmt|ib mình|inbox)', N'Đã ib bạn thông tin nha! 📩 Bạn check tin nhắn giúp mình, có lộ trình + ưu đãi khoá tiếng Trung mới nhất ạ.', N'short', N'facebook,instagram,tiktok'),
    (N'KB-042', N'Platform', N'(?i)(zalo|kết bạn zalo|add zalo)', N'Dạ bạn kết bạn Zalo trung tâm để nhận tài liệu phát âm miễn phí + lịch khai giảng nhé. Bên mình sẽ tư vấn 1-1 ngay trên Zalo cho tiện ạ!', N'friendly', N'zalo'),
    (N'KB-043', N'Platform', N'(?i)(video|clip này|học theo video|theo dõi kênh)', N'Cảm ơn bạn đã xem video! 🎬 Trung tâm có khoá học bài bản theo đúng phương pháp trong clip. Bạn comment "HỌC" hoặc nhắn tin để nhận lộ trình + ưu đãi nha!', N'energetic', N'tiktok,youtube'),
    (N'KB-044', N'Platform', N'(?i)(livestream|live|đang live|minigame)', N'Chào bạn đang xem live 👋 Thả tim + nhắn "ĐĂNG KÝ" để nhận ưu đãi riêng cho người xem live hôm nay nhé! Số lượng suất ưu đãi có hạn ạ.', N'energetic', N'tiktok,facebook,youtube'),
    (N'KB-045', N'Platform', N'(?i)(story|reel|bio|link trong)', N'Bạn bấm link ở bio/story để xem lộ trình & đăng ký học thử miễn phí nhé, hoặc nhắn mình "HSK" để được gửi thông tin ngay ạ!', N'short', N'instagram,tiktok'),

    -- ---------------- Follow-up (re-engage / nurture) ----------------
    (N'KB-046', N'Follow-up', N'(?i)(chưa.*(quyết|đăng ký)|vẫn đang.*suy nghĩ|để mai)', N'Chào bạn 👋 Mình theo dõi thấy bạn quan tâm khoá tiếng Trung. Ưu đãi đăng ký sớm sắp hết hạn, bạn còn thắc mắc gì để mình hỗ trợ giúp bạn quyết định không ạ?', N'gentle', N'all'),
    (N'KB-047', N'Follow-up', N'(?i)(quên|bận quá|lúc trước.*hỏi|hôm bữa)', N'Dạ không sao ạ! Mình gửi lại bạn lộ trình + lịch khai giảng mới nhất nhé. Lớp đợt này còn vài suất, bạn muốn mình giữ chỗ giúp không ạ?', N'gentle', N'all'),
    (N'KB-048', N'Follow-up', N'(?i)(lần sau|hẹn dịp|khi nào rảnh|để sau nhé)', N'Dạ bạn cứ thoải mái ạ. Mình lưu thông tin và sẽ báo bạn khi có ưu đãi/khai giảng phù hợp nhé. Bạn muốn nhận lộ trình tự học miễn phí trong lúc chờ không ạ?', N'low-pressure', N'all'),
    (N'KB-049', N'Follow-up', N'(?i)(học thử rồi|đi thử rồi|thấy.*(ổn|được))', N'Bạn thấy buổi học thử thế nào ạ? 😊 Nếu thấy hợp, mình giữ suất lớp chính thức + ưu đãi học viên trải nghiệm cho bạn luôn nhé. Bạn muốn bắt đầu từ khoá khai giảng nào ạ?', N'warm', N'all'),
    (N'KB-050', N'Follow-up', N'(?i)(học xong.*(khoá|rồi)|tái tục|học tiếp|lên cấp)', N'Chúc mừng bạn hoàn thành khoá học! 🎓 Để giữ phong độ và lên cấp tiếp, trung tâm ưu đãi học phí học viên cũ. Bạn muốn mình tư vấn lộ trình lên HSK tiếp theo không ạ?', N'warm', N'all')
) AS source (code, group_name, trigger_text, response_template, tone_voice, platforms)
ON target.tenant_id = @tenant_id AND target.code = source.code
WHEN MATCHED THEN
    UPDATE SET
        group_name        = source.group_name,
        trigger_text      = source.trigger_text,
        response_template = source.response_template,
        tone_voice        = source.tone_voice,
        platforms         = source.platforms,
        updated_at        = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (tenant_id, code, group_name, trigger_text, response_template, tone_voice, platforms)
    VALUES (@tenant_id, source.code, source.group_name, source.trigger_text, source.response_template, source.tone_voice, source.platforms);

PRINT N'Seeded chat_scenarios for tenant ' + CAST(@tenant_id AS NVARCHAR(64)) + N' (KB-001..KB-050).';
