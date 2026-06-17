-- =============================================================
-- ClawBot — M17 document_templates seed (QUOTE-V1, ONBOARDING-KIT, BROCHURE-HSK, SLIDE-DEMO-5)
-- Branded document templates for a Chinese-language course (HSK) tenant.
-- Idempotent: MERGE on (tenant_id, code). Re-running updates existing rows in place.
--
-- USAGE: set the target tenant slug below, then apply:
--   sqlcmd -S <server> -d clawbot -i deploy/seed/document-templates.sql
--
-- template_html is a Scriban template (placeholders use {{ key }}); unknown keys
-- render empty. fields_json documents the variables the template expects.
-- doc_type one of: quote | brochure | slide | onboarding.
-- =============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @tenant_slug NVARCHAR(64) = N'demo';   -- <-- CHANGE to the target tenant slug
DECLARE @tenant_id UNIQUEIDENTIFIER = (SELECT id FROM tenants WHERE slug = @tenant_slug);

IF @tenant_id IS NULL
BEGIN
    RAISERROR(N'Tenant slug "%s" not found. Seed aborted.', 16, 1, @tenant_slug);
    RETURN;
END

BEGIN TRANSACTION;

DECLARE @expected_rows INT = 4;

MERGE INTO document_templates AS target
USING (VALUES
    (N'QUOTE-V1', N'quote',
     N'BÁO GIÁ KHÓA HỌC TIẾNG TRUNG

Kính gửi: {{ customer_name }}
Ngày báo giá: {{ quote_date }}

Khóa học: {{ course_name }}
Trình độ mục tiêu: {{ level }}
Thời lượng: {{ duration }}
Hình thức: {{ format }}

Học phí gốc: {{ price }} VNĐ
Ưu đãi áp dụng: {{ discount }}
Thành tiền: {{ total }} VNĐ

Tư vấn viên: {{ sale_name }} — {{ sale_phone }}
Báo giá có hiệu lực trong 7 ngày kể từ ngày phát hành.
Cảm ơn bạn đã quan tâm tới trung tâm.',
     N'{"customer_name":"Tên khách hàng","quote_date":"dd/MM/yyyy","course_name":"Tên khóa học","level":"HSK1-HSK6","duration":"Số buổi/tháng","format":"Online/Offline","price":"Học phí gốc","discount":"Mô tả ưu đãi","total":"Thành tiền","sale_name":"Tên tư vấn viên","sale_phone":"SĐT tư vấn"}'),

    (N'ONBOARDING-KIT', N'onboarding',
     N'CHÀO MỪNG HỌC VIÊN MỚI

Xin chào {{ student_name }},

Chúc mừng bạn đã chính thức trở thành học viên của trung tâm. Dưới đây là thông tin lớp học của bạn:

Lớp: {{ class_name }}
Trình độ: {{ level }}
Lịch học: {{ schedule }}
Giáo viên phụ trách: {{ teacher_name }}
Ngày khai giảng: {{ start_date }}

Kênh hỗ trợ: {{ support_channel }}
Tài liệu học tập sẽ được gửi qua email trước buổi đầu tiên.

Chúc bạn học tập hiệu quả và sớm chinh phục mục tiêu tiếng Trung của mình!',
     N'{"student_name":"Tên học viên","class_name":"Tên lớp","level":"Trình độ","schedule":"Lịch học","teacher_name":"Tên giáo viên","start_date":"Ngày khai giảng","support_channel":"Kênh hỗ trợ"}'),

    (N'BROCHURE-HSK', N'brochure',
     N'BROCHURE CHƯƠNG TRÌNH HSK

Trung tâm: {{ center_name }}
Chương trình: {{ program_name }}
Trình độ phù hợp: {{ levels }}
Đối tượng học viên: {{ audience }}

Điểm nổi bật:
- Lộ trình cá nhân hóa theo mục tiêu HSK.
- Giáo viên theo sát phát âm, ngữ pháp và phản xạ hội thoại.
- Bài kiểm tra định kỳ giúp đo tiến bộ rõ ràng.
- Tài liệu luyện đề và từ vựng theo từng cấp độ.

Khai giảng: {{ intake_date }}
Hình thức học: {{ format }}
Học phí tham khảo: {{ price_range }}

Đăng ký tư vấn: {{ sale_phone }} hoặc {{ contact_url }}',
     N'{"center_name":"Tên trung tâm","program_name":"Tên chương trình","levels":"HSK1-HSK6","audience":"Đối tượng học viên","intake_date":"Ngày khai giảng","format":"Online/Offline","price_range":"Khoảng học phí","sale_phone":"SĐT tư vấn","contact_url":"Link đăng ký"}'),

    (N'SLIDE-DEMO-5', N'slide',
     N'SLIDE 1 — VẤN ĐỀ
{{ pain_point }}

SLIDE 2 — MỤC TIÊU HỌC TẬP
{{ learning_goal }}

SLIDE 3 — LỘ TRÌNH 5 BUỔI DEMO
Buổi 1: {{ session_1 }}
Buổi 2: {{ session_2 }}
Buổi 3: {{ session_3 }}
Buổi 4: {{ session_4 }}
Buổi 5: {{ session_5 }}

SLIDE 4 — KẾT QUẢ KỲ VỌNG
{{ expected_outcome }}

SLIDE 5 — BƯỚC TIẾP THEO
{{ next_step }}',
     N'{"pain_point":"Vấn đề của học viên","learning_goal":"Mục tiêu học tập","session_1":"Nội dung buổi 1","session_2":"Nội dung buổi 2","session_3":"Nội dung buổi 3","session_4":"Nội dung buổi 4","session_5":"Nội dung buổi 5","expected_outcome":"Kết quả kỳ vọng","next_step":"CTA / bước tiếp theo"}')
) AS source (code, doc_type, template_html, fields_json)
ON target.tenant_id = @tenant_id AND target.code = source.code
WHEN MATCHED THEN
    UPDATE SET
        doc_type      = source.doc_type,
        template_html = source.template_html,
        fields_json   = source.fields_json,
        updated_at    = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, code, doc_type, template_html, fields_json, created_at, updated_at)
    VALUES (NEWID(), @tenant_id, source.code, source.doc_type, source.template_html, source.fields_json,
            SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

DECLARE @actual_rows INT;

SELECT @actual_rows = COUNT(*)
FROM document_templates
WHERE tenant_id = @tenant_id
  AND code IN (N'QUOTE-V1', N'ONBOARDING-KIT', N'BROCHURE-HSK', N'SLIDE-DEMO-5');

IF @actual_rows <> @expected_rows
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'Expected %d document_templates rows for tenant "%s"; found %d. Seed aborted.', 16, 1, @expected_rows, @tenant_slug, @actual_rows);
    RETURN;
END;

COMMIT TRANSACTION;

PRINT N'document_templates seed applied for tenant: ' + @tenant_slug;
