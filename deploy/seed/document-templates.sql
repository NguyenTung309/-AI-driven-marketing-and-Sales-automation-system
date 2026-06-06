-- =============================================================
-- ClawBot — M17 document_templates seed (QUOTE-V1, ONBOARDING-KIT)
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

DECLARE @tenant_slug NVARCHAR(64) = N'demo';   -- <-- CHANGE to the target tenant slug
DECLARE @tenant_id UNIQUEIDENTIFIER = (SELECT id FROM tenants WHERE slug = @tenant_slug);

IF @tenant_id IS NULL
BEGIN
    RAISERROR(N'Tenant slug "%s" not found. Seed aborted.', 16, 1, @tenant_slug);
    RETURN;
END

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
     N'{"student_name":"Tên học viên","class_name":"Tên lớp","level":"Trình độ","schedule":"Lịch học","teacher_name":"Tên giáo viên","start_date":"Ngày khai giảng","support_channel":"Kênh hỗ trợ"}')
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

PRINT N'document_templates seed applied for tenant: ' + @tenant_slug;
