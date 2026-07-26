-- =============================================================
-- ClawBot — M17 document_templates seed (QUOTE-V1, ONBOARDING-KIT, BROCHURE-HSK, SLIDE-DEMO-5)
-- Branded document templates for a Chinese-language course (HSK) tenant.
-- Idempotent: MERGE on (tenant_id, code). Re-running updates existing rows in place.
--
-- USAGE: set the target tenant slug below, then apply:
--   sqlcmd -S <server> -d clawbot -i deploy/seed/document-templates.sql
--
-- template_html is a plain-text template (placeholders use {{ key }}); a key with no
-- value renders as an empty string in the PDF (SimpleTemplateEngine.Render). The first
-- non-empty line renders as the document title.
-- fields_json is the form schema the UI builds its input form from — a JSON array of
--   {"key","label","type","required","placeholder","sample"} where type is one of
--   text | multiline | number | currency | date. "sample" is real data used by the
--   "fill sample data" button, so never put format hints there; use "placeholder".
-- Keys the system auto-fills (contact_name, customer_name, contact_phone, contact_email,
-- knowledge, kb_content, kb_module_codes — see DocsAgentGrpcService) must stay
-- required:false so the form never blocks on values the user cannot type.
-- doc_type one of: quote | brochure | slide | onboarding.
-- =============================================================

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @tenant_slug NVARCHAR(64) = N'$(TenantSlug)';   -- <-- CHANGE to the target tenant slug
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
     N'[
{"key":"customer_name","label":"Tên khách hàng","type":"text","required":false,"sample":"Nguyễn Minh Anh"},
{"key":"quote_date","label":"Ngày báo giá","type":"date","required":false,"sample":null},
{"key":"course_name","label":"Tên khóa học","type":"text","required":true,"sample":"HSK 4 cấp tốc"},
{"key":"level","label":"Trình độ mục tiêu","type":"text","required":false,"sample":"HSK 4"},
{"key":"duration","label":"Thời lượng","type":"text","required":false,"sample":"3 tháng, 36 buổi"},
{"key":"format","label":"Hình thức học","type":"text","required":false,"sample":"Offline tại trung tâm"},
{"key":"price","label":"Học phí gốc","type":"currency","required":true,"sample":"5.000.000"},
{"key":"discount","label":"Ưu đãi áp dụng","type":"text","required":false,"sample":"Giảm 10% khi đóng đủ"},
{"key":"total","label":"Thành tiền","type":"currency","required":true,"sample":"4.500.000"},
{"key":"sale_name","label":"Tên tư vấn viên","type":"text","required":false,"sample":"Trần Thu Hà"},
{"key":"sale_phone","label":"SĐT tư vấn","type":"text","required":false,"sample":"0900 000 000"}
]'),

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
     N'[
{"key":"student_name","label":"Tên học viên","type":"text","required":true,"sample":"Nguyễn Minh Anh"},
{"key":"class_name","label":"Tên lớp","type":"text","required":true,"sample":"HSK4-T7"},
{"key":"level","label":"Trình độ","type":"text","required":false,"sample":"HSK 4"},
{"key":"schedule","label":"Lịch học","type":"text","required":false,"sample":"Thứ 3 - 5 - 7, 19h00"},
{"key":"teacher_name","label":"Tên giáo viên","type":"text","required":false,"sample":"Cô Lâm"},
{"key":"start_date","label":"Ngày khai giảng","type":"date","required":true,"sample":null},
{"key":"support_channel","label":"Kênh hỗ trợ","type":"text","required":false,"sample":"Zalo 0900 000 000"}
]'),

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
     N'[
{"key":"center_name","label":"Tên trung tâm","type":"text","required":true,"sample":"Trung tâm Học Bá"},
{"key":"program_name","label":"Tên chương trình","type":"text","required":true,"sample":"Luyện thi HSK toàn diện"},
{"key":"levels","label":"Trình độ phù hợp","type":"text","required":false,"sample":"HSK1 - HSK6"},
{"key":"audience","label":"Đối tượng học viên","type":"text","required":false,"sample":"Người mới bắt đầu và học viên ôn thi"},
{"key":"intake_date","label":"Ngày khai giảng","type":"date","required":false,"sample":null},
{"key":"format","label":"Hình thức học","type":"text","required":false,"sample":"Online và Offline"},
{"key":"price_range","label":"Khoảng học phí","type":"text","required":false,"sample":"3.500.000đ - 6.000.000đ"},
{"key":"sale_phone","label":"SĐT tư vấn","type":"text","required":false,"sample":"0900 000 000"},
{"key":"contact_url","label":"Link đăng ký","type":"text","required":false,"sample":"hocba.vn/dang-ky"}
]'),

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
     N'[
{"key":"pain_point","label":"Vấn đề của học viên","type":"multiline","required":true,"sample":"Học 2 năm vẫn không dám nói tiếng Trung"},
{"key":"learning_goal","label":"Mục tiêu học tập","type":"multiline","required":true,"sample":"Đạt HSK 4 trong 4 tháng"},
{"key":"session_1","label":"Nội dung buổi 1","type":"text","required":false,"sample":"Phát âm và thanh điệu"},
{"key":"session_2","label":"Nội dung buổi 2","type":"text","required":false,"sample":"Từ vựng chủ đề công việc"},
{"key":"session_3","label":"Nội dung buổi 3","type":"text","required":false,"sample":"Mẫu câu hội thoại thường dùng"},
{"key":"session_4","label":"Nội dung buổi 4","type":"text","required":false,"sample":"Luyện nghe theo đề HSK"},
{"key":"session_5","label":"Nội dung buổi 5","type":"text","required":false,"sample":"Thực hành hội thoại tổng hợp"},
{"key":"expected_outcome","label":"Kết quả kỳ vọng","type":"multiline","required":false,"sample":"Tự tin giới thiệu bản thân và trao đổi cơ bản"},
{"key":"next_step","label":"Bước tiếp theo","type":"text","required":false,"sample":"Đăng ký lớp chính thức trước ngày khai giảng"}
]')
) AS source (code, doc_type, template_html, fields_json)
ON target.tenant_id = @tenant_id AND target.code = source.code
WHEN MATCHED THEN
    UPDATE SET
        doc_type      = source.doc_type,
        template_html = source.template_html,
        fields_json   = source.fields_json,
        -- Clear the soft-delete flag: a row matched on (tenant_id, code) but left deleted stays
        -- invisible to the API, so the seed would report success while the template is unusable.
        deleted_at    = NULL,
        updated_at    = SYSDATETIMEOFFSET()
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, code, doc_type, template_html, fields_json, created_at, updated_at)
    VALUES (NEWID(), @tenant_id, source.code, source.doc_type, source.template_html, source.fields_json,
            SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());

DECLARE @actual_rows INT;

-- Count live rows only, so the guard fails loudly if any template is still soft-deleted.
SELECT @actual_rows = COUNT(*)
FROM document_templates
WHERE tenant_id = @tenant_id
  AND deleted_at IS NULL
  AND code IN (N'QUOTE-V1', N'ONBOARDING-KIT', N'BROCHURE-HSK', N'SLIDE-DEMO-5');

IF @actual_rows <> @expected_rows
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'Expected %d document_templates rows for tenant "%s"; found %d. Seed aborted.', 16, 1, @expected_rows, @tenant_slug, @actual_rows);
    RETURN;
END;

COMMIT TRANSACTION;

PRINT N'document_templates seed applied for tenant: ' + @tenant_slug;
