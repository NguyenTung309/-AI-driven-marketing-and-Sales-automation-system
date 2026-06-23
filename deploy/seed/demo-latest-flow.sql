/*
  ClawBot latest-flow demo seed for staging.
  Idempotent: reruns refresh demo rows by stable business keys and fixed timestamps.

  Prerequisites:
    - migrations applied through latest schema
    - tenant with slug below exists
    - optional: document-templates.sql and lead-scoring-rules.sql already applied

  Usage:
    sqlcmd -S <server> -d <db> -i deploy/seed/demo-latest-flow.sql -C
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @tenant_slug NVARCHAR(64) = N'$(TenantSlug)'; -- <-- CHANGE to the staging demo tenant slug
DECLARE @tenant_id UNIQUEIDENTIFIER = (SELECT TOP 1 id FROM tenants WHERE slug = @tenant_slug AND deleted_at IS NULL);
DECLARE @now DATETIMEOFFSET = SYSDATETIMEOFFSET();
DECLARE @base_time DATETIMEOFFSET = DATETIMEOFFSETFROMPARTS(2026, 6, 23, 9, 0, 0, 0, 7, 0, 7);

IF @tenant_id IS NULL
BEGIN
    RAISERROR(N'Tenant slug "%s" not found. Seed aborted.', 16, 1, @tenant_slug);
    RETURN;
END;

BEGIN TRANSACTION;

UPDATE tenants
SET display_name = N'Học Bá Education Demo',
    brand_name = N'Học Bá Education',
    primary_color = N'#d32f2f',
    accent_color = N'#1e293b',
    support_name = N'Tư vấn Học Bá',
    widget_greeting = N'Chào bạn, Học Bá có thể tư vấn lộ trình tiếng Trung nào cho bạn hôm nay?',
    updated_at = @now
WHERE id = @tenant_id
  AND COL_LENGTH(N'dbo.tenants', N'brand_name') IS NOT NULL;

DECLARE @owner_user_id UNIQUEIDENTIFIER = (
    SELECT TOP 1 id
    FROM users
    WHERE tenant_id = @tenant_id
      AND is_active = 1
      AND deleted_at IS NULL
    ORDER BY created_at
);

DECLARE @contacts TABLE (
    code NVARCHAR(32) PRIMARY KEY,
    display_name NVARCHAR(256) NOT NULL,
    phone NVARCHAR(32),
    email NVARCHAR(256),
    lifecycle_stage NVARCHAR(32) NOT NULL,
    lifetime_score INT NOT NULL,
    platform NVARCHAR(32) NOT NULL,
    external_id NVARCHAR(256) NOT NULL
);

INSERT INTO @contacts (code, display_name, phone, email, lifecycle_stage, lifetime_score, platform, external_id)
VALUES
    (N'price',  N'Nguyễn Minh Anh', N'0900000001', N'minh.anh.demo@example.com', N'lead', 45, N'zalo', N'demo-zalo-minh-anh'),
    (N'parent', N'Trần Thu Hà',     N'0900000002', N'thu.ha.demo@example.com',   N'lead', 35, N'facebook', N'demo-fb-thu-ha'),
    (N'hot',    N'Lê Quang Huy',    N'0900000003', N'quang.huy.demo@example.com',N'opportunity', 82, N'web', N'demo-web-quang-huy');

MERGE contacts AS target
USING @contacts AS source
ON target.tenant_id = @tenant_id AND target.email = source.email
WHEN MATCHED THEN
    UPDATE SET
        display_name = source.display_name,
        phone = source.phone,
        lifecycle_stage = source.lifecycle_stage,
        lifetime_score = source.lifetime_score,
        locale = N'vi-VN',
        meta_json = N'{"demo":"latest-flow"}',
        updated_at = @now,
        deleted_at = NULL
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, display_name, phone, email, locale, lifetime_score, lifecycle_stage, meta_json, created_by, updated_by, created_at, updated_at)
    VALUES (NEWID(), @tenant_id, source.display_name, source.phone, source.email, N'vi-VN', source.lifetime_score, source.lifecycle_stage, N'{"demo":"latest-flow"}', @owner_user_id, @owner_user_id, @now, @now);

MERGE contact_external_ids AS target
USING (
    SELECT c.id AS contact_id, s.platform, CONCAT(@tenant_slug, N':', s.external_id) AS external_id
    FROM @contacts AS s
    JOIN contacts AS c ON c.tenant_id = @tenant_id AND c.email = s.email
) AS source
ON target.platform = source.platform AND target.external_id = source.external_id
WHEN MATCHED THEN
    UPDATE SET contact_id = source.contact_id
WHEN NOT MATCHED THEN
    INSERT (id, contact_id, platform, external_id, first_seen_at)
    VALUES (NEWID(), source.contact_id, source.platform, source.external_id, @base_time);

DECLARE @conversations TABLE (
    code NVARCHAR(32) PRIMARY KEY,
    contact_code NVARCHAR(32) NOT NULL,
    platform NVARCHAR(32) NOT NULL,
    external_thread_id NVARCHAR(256) NOT NULL,
    status NVARCHAR(32) NOT NULL,
    last_msg_offset_min INT NOT NULL
);

INSERT INTO @conversations (code, contact_code, platform, external_thread_id, status, last_msg_offset_min)
VALUES
    (N'price',  N'price',  N'zalo',     N'demo-thread-price',  N'open',    12),
    (N'parent', N'parent', N'facebook', N'demo-thread-parent', N'pending', 35),
    (N'hot',    N'hot',    N'web',      N'demo-thread-hot',    N'open',    3);

MERGE conversations AS target
USING (
    SELECT v.code, c.id AS contact_id, v.platform, v.external_thread_id, v.status,
           DATEADD(MINUTE, v.last_msg_offset_min, @base_time) AS last_msg_at
    FROM @conversations AS v
    JOIN @contacts AS seed_contact ON seed_contact.code = v.contact_code
    JOIN contacts AS c ON c.tenant_id = @tenant_id AND c.email = seed_contact.email
) AS source
ON target.tenant_id = @tenant_id AND target.platform = source.platform AND target.external_thread_id = source.external_thread_id
WHEN MATCHED THEN
    UPDATE SET
        contact_id = source.contact_id,
        status = source.status,
        assigned_to = @owner_user_id,
        last_msg_at = source.last_msg_at,
        last_message_at = source.last_msg_at,
        updated_at = @now,
        deleted_at = NULL
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, contact_id, platform, external_thread_id, status, assigned_to, last_msg_at, last_message_at, created_at, updated_at)
    VALUES (NEWID(), @tenant_id, source.contact_id, source.platform, source.external_thread_id, source.status, @owner_user_id, source.last_msg_at, source.last_msg_at, @base_time, @now);

DECLARE @messages TABLE (
    conversation_code NVARCHAR(32) NOT NULL,
    sort_order INT NOT NULL,
    direction NVARCHAR(8) NOT NULL,
    sender_type NVARCHAR(16) NOT NULL,
    content NVARCHAR(MAX) NOT NULL,
    sent_at DATETIMEOFFSET NOT NULL
);

INSERT INTO @messages (conversation_code, sort_order, direction, sender_type, content, sent_at)
VALUES
    (N'price', 1, N'in',  N'contact', N'Em muốn học HSK3, cho em hỏi học phí khoảng bao nhiêu ạ?', DATEADD(MINUTE, 1, @base_time)),
    (N'price', 2, N'out', N'agent',   N'Dạ học phí phụ thuộc mục tiêu và lịch học. Em cho trung tâm biết em muốn học để thi HSK3 hay giao tiếp trước nhé?', DATEADD(MINUTE, 2, @base_time)),
    (N'parent', 1, N'in',  N'contact', N'Bé nhà chị 10 tuổi, muốn học tiếng Trung từ đầu thì bắt đầu thế nào?', DATEADD(MINUTE, 10, @base_time)),
    (N'parent', 2, N'out', N'user',    N'Dạ với bé 10 tuổi, trung tâm thường bắt đầu bằng lớp YCT/thiếu nhi để bé làm quen phát âm và từ vựng qua hoạt động.', DATEADD(MINUTE, 12, @base_time)),
    (N'hot', 1, N'in',  N'contact', N'Anh muốn đặt lịch học thử tuần này, anh để số 0900000003 nhé.', DATEADD(MINUTE, 20, @base_time));

INSERT INTO messages (id, conversation_id, tenant_id, direction, sender_type, sender_user_id, content, content_type, metadata_json, sent_at, original_content, redacted_content)
SELECT NEWID(), conv.id, @tenant_id, m.direction, m.sender_type,
       CASE WHEN m.sender_type = N'user' THEN @owner_user_id ELSE NULL END,
       m.content,
       N'text',
       N'{"demo":"latest-flow"}',
       m.sent_at,
       m.content,
       REPLACE(m.content, N'0900000003', N'[redacted-phone]')
FROM @messages AS m
JOIN @conversations AS cv ON cv.code = m.conversation_code
JOIN conversations AS conv ON conv.tenant_id = @tenant_id AND conv.external_thread_id = cv.external_thread_id
WHERE NOT EXISTS (
    SELECT 1
    FROM messages AS existing
    WHERE existing.conversation_id = conv.id
      AND existing.sent_at = m.sent_at
      AND existing.direction = m.direction
      AND existing.content = m.content
);

DECLARE @lead_rows TABLE (
    contact_code NVARCHAR(32) PRIMARY KEY,
    source_platform NVARCHAR(32),
    score INT NOT NULL,
    stage NVARCHAR(32) NOT NULL,
    activity_type NVARCHAR(64) NOT NULL,
    notes NVARCHAR(MAX) NOT NULL
);

INSERT INTO @lead_rows (contact_code, source_platform, score, stage, activity_type, notes)
VALUES
    (N'price', N'zalo', 45, N'warm', N'asks_price', N'Hỏi học phí HSK3, cần tư vấn gói phù hợp.'),
    (N'parent', N'facebook', 35, N'warm', N'asks_curriculum', N'Phụ huynh hỏi chương trình cho trẻ em.'),
    (N'hot', N'web', 82, N'hot', N'books_trial', N'Để lại số điện thoại và muốn đặt lịch học thử tuần này.');

MERGE leads AS target
USING (
    SELECT c.id AS contact_id, l.source_platform, l.score, l.stage, l.notes
    FROM @lead_rows AS l
    JOIN @contacts AS seed_contact ON seed_contact.code = l.contact_code
    JOIN contacts AS c ON c.tenant_id = @tenant_id AND c.email = seed_contact.email
) AS source
ON target.tenant_id = @tenant_id AND target.contact_id = source.contact_id AND target.deleted_at IS NULL
WHEN MATCHED THEN
    UPDATE SET
        owner_user_id = @owner_user_id,
        score = source.score,
        stage = source.stage,
        source_platform = source.source_platform,
        last_activity_at = @base_time,
        updated_at = @now
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, contact_id, owner_user_id, score, stage, source_platform, last_activity_at, created_at, updated_at)
    VALUES (NEWID(), @tenant_id, source.contact_id, @owner_user_id, source.score, source.stage, source.source_platform, @base_time, @base_time, @now);

INSERT INTO lead_activities (id, tenant_id, lead_id, activity_type, notes, meta_json, occurred_at)
SELECT NEWID(), @tenant_id, lead.id, row.activity_type, row.notes,
       N'{"demo":"latest-flow","source":"demo-latest-flow.sql"}', DATEADD(MINUTE, 30, @base_time)
FROM @lead_rows AS row
JOIN @contacts AS seed_contact ON seed_contact.code = row.contact_code
JOIN contacts AS c ON c.tenant_id = @tenant_id AND c.email = seed_contact.email
JOIN leads AS lead ON lead.tenant_id = @tenant_id AND lead.contact_id = c.id
WHERE NOT EXISTS (
    SELECT 1
    FROM lead_activities AS existing
    WHERE existing.lead_id = lead.id
      AND existing.activity_type = row.activity_type
      AND existing.notes = row.notes
);

MERGE quick_reply_templates AS target
USING (VALUES
    (N'DEMO-ASK-GOAL', N'sales', N'Dạ bạn đang muốn học tiếng Trung để giao tiếp, đi làm hay thi HSK ạ? Em hỏi để tư vấn đúng lộ trình hơn.', N'zalo,facebook,web'),
    (N'DEMO-BOOK-TRIAL', N'sales', N'Trung tâm có thể sắp xếp một buổi học thử và test trình độ miễn phí. Bạn muốn học thử buổi tối hay cuối tuần ạ?', N'zalo,facebook,web'),
    (N'DEMO-QUOTE', N'sales', N'Em gửi bạn lộ trình và báo giá phù hợp sau khi xác nhận mục tiêu học. Nếu tiện, bạn cho em khung giờ học mong muốn nhé.', N'zalo,facebook,web')
) AS source (code, category, body, platforms)
ON target.tenant_id = @tenant_id AND target.code = source.code
WHEN MATCHED THEN
    UPDATE SET category = source.category, body = source.body, platforms = source.platforms, updated_at = @now
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, code, category, body, platforms, created_at, updated_at)
    VALUES (NEWID(), @tenant_id, source.code, source.category, source.body, source.platforms, @now, @now);

DECLARE @quote_template_id UNIQUEIDENTIFIER = (
    SELECT TOP 1 id FROM document_templates WHERE tenant_id = @tenant_id AND code = N'QUOTE-V1' AND deleted_at IS NULL
);
DECLARE @hot_contact_id UNIQUEIDENTIFIER = (
    SELECT TOP 1 c.id FROM contacts AS c JOIN @contacts AS s ON s.email = c.email WHERE c.tenant_id = @tenant_id AND s.code = N'hot'
);

IF @quote_template_id IS NOT NULL AND @hot_contact_id IS NOT NULL
BEGIN
    INSERT INTO generated_documents (id, tenant_id, contact_id, template_id, generated_by, file_url, file_hash, sent_via, sent_at, opened_at, created_at, expires_at)
    SELECT NEWID(), @tenant_id, @hot_contact_id, @quote_template_id, @owner_user_id,
           N'https://demo.local/documents/quote-quang-huy.pdf', N'demo-quote-hash', N'zalo', DATEADD(MINUTE, 45, @base_time), NULL, DATEADD(MINUTE, 44, @base_time), DATEADD(DAY, 7, @base_time)
    WHERE NOT EXISTS (
        SELECT 1 FROM generated_documents WHERE tenant_id = @tenant_id AND file_url = N'https://demo.local/documents/quote-quang-huy.pdf'
    );
END;

DECLARE @brief_id UNIQUEIDENTIFIER;
MERGE content_briefs AS target
USING (VALUES (N'tiktok', N'Demo: tạo video ngắn giải thích 3 lỗi phát âm tiếng Trung phổ biến cho người mới học.')) AS source (platform, brief)
ON target.tenant_id = @tenant_id AND target.platform = source.platform AND target.brief = source.brief
WHEN MATCHED THEN
    UPDATE SET status = N'pending', updated_at = @now
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, platform, brief, status, created_by, created_at, updated_at)
    VALUES (NEWID(), @tenant_id, source.platform, source.brief, N'pending', @owner_user_id, @now, @now);

SELECT @brief_id = id FROM content_briefs WHERE tenant_id = @tenant_id AND platform = N'tiktok' AND brief = N'Demo: tạo video ngắn giải thích 3 lỗi phát âm tiếng Trung phổ biến cho người mới học.';

MERGE content_items AS target
USING (VALUES (
    N'tiktok', N'approved', N'3 lỗi phát âm tiếng Trung người mới hay gặp: nhầm thanh điệu, bật hơi chưa rõ, đọc pinyin theo tiếng Việt. Lưu lại để luyện mỗi ngày nhé!', N'[]'
)) AS source (platform, status, body, assets_json)
ON target.tenant_id = @tenant_id AND target.platform = source.platform AND target.body = source.body AND target.deleted_at IS NULL
WHEN MATCHED THEN
    UPDATE SET status = source.status, assets_json = source.assets_json, approved_by = @owner_user_id, approved_at = DATEADD(MINUTE, 50, @base_time), updated_at = @now
WHEN NOT MATCHED THEN
    INSERT (id, tenant_id, brief_id, platform, status, body, assets_json, created_by, approved_by, approved_at, created_at, updated_at)
    VALUES (NEWID(), @tenant_id, @brief_id, source.platform, source.status, source.body, source.assets_json, @owner_user_id, @owner_user_id, DATEADD(MINUTE, 50, @base_time), @now, @now);

DECLARE @content_item_id UNIQUEIDENTIFIER = (
    SELECT TOP 1 id FROM content_items WHERE tenant_id = @tenant_id AND platform = N'tiktok' AND body LIKE N'3 lỗi phát âm tiếng Trung%' AND deleted_at IS NULL
);

IF @content_item_id IS NOT NULL
BEGIN
    MERGE content_schedule AS target
    USING (SELECT @content_item_id AS content_item_id, N'tiktok' AS platform, DATEADD(DAY, 1, @base_time) AS scheduled_at) AS source
    ON target.tenant_id = @tenant_id AND target.content_item_id = source.content_item_id AND target.status = N'pending'
    WHEN MATCHED THEN
        UPDATE SET scheduled_at = source.scheduled_at, platform = source.platform, updated_at = @now
    WHEN NOT MATCHED THEN
        INSERT (id, tenant_id, content_item_id, platform, scheduled_at, status, created_at, updated_at)
        VALUES (NEWID(), @tenant_id, source.content_item_id, source.platform, source.scheduled_at, N'pending', @now, @now);
END;

DECLARE @hot_lead_id UNIQUEIDENTIFIER = (
    SELECT TOP 1 lead.id
    FROM leads AS lead
    JOIN contacts AS c ON c.id = lead.contact_id
    JOIN @contacts AS s ON s.email = c.email
    WHERE lead.tenant_id = @tenant_id AND s.code = N'hot'
);

INSERT INTO notifications (id, tenant_id, user_id, type, severity, title, body, link, is_read, created_at)
SELECT NEWID(), @tenant_id, @owner_user_id, N'hot_lead', N'warning', N'Demo: lead nóng cần gọi ngay', N'Lê Quang Huy đã để lại số điện thoại và muốn đặt lịch học thử trong tuần này.', N'/leads', 0, DATEADD(MINUTE, 55, @base_time)
WHERE NOT EXISTS (SELECT 1 FROM notifications WHERE tenant_id = @tenant_id AND title = N'Demo: lead nóng cần gọi ngay');

INSERT INTO notifications (id, tenant_id, user_id, type, severity, title, body, link, is_read, created_at)
SELECT NEWID(), @tenant_id, NULL, N'system', N'info', N'Demo: staging đã có dữ liệu mẫu', N'Bộ dữ liệu demo latest-flow đã sẵn sàng cho rehearsal.', N'/logs', 0, DATEADD(MINUTE, 56, @base_time)
WHERE NOT EXISTS (SELECT 1 FROM notifications WHERE tenant_id = @tenant_id AND title = N'Demo: staging đã có dữ liệu mẫu');

DECLARE @session_id UNIQUEIDENTIFIER;
SELECT @session_id = id FROM agent_sessions WHERE tenant_id = @tenant_id AND goal = N'Demo latest-flow: chuẩn bị chiến dịch HSK3 tháng 7';

IF @session_id IS NULL
BEGIN
    SET @session_id = NEWID();
    INSERT INTO agent_sessions (id, tenant_id, agent_id, conversation_id, goal, status, plan_json, started_at, finished_at)
    VALUES (
        @session_id,
        @tenant_id,
        NULL,
        NULL,
        N'Demo latest-flow: chuẩn bị chiến dịch HSK3 tháng 7',
        N'completed',
        N'{"version":3,"tasks":[{"id":"t1","agent":"research-agent","description":"Tìm insight học HSK3","status":"completed"},{"id":"t2","agent":"content-agent","description":"Viết nội dung TikTok","status":"completed"}]}',
        DATEADD(MINUTE, 60, @base_time),
        DATEADD(MINUTE, 66, @base_time)
    );
END
ELSE
BEGIN
    UPDATE agent_sessions
    SET status = N'completed',
        plan_json = N'{"version":3,"tasks":[{"id":"t1","agent":"research-agent","description":"Tìm insight học HSK3","status":"completed"},{"id":"t2","agent":"content-agent","description":"Viết nội dung TikTok","status":"completed"}]}',
        finished_at = DATEADD(MINUTE, 66, @base_time)
    WHERE id = @session_id;
END;

INSERT INTO agent_traces (id, session_id, task_id, agent_name, phase, message, occurred_at)
SELECT NEWID(), @session_id, v.task_id, v.agent_name, v.phase, v.message, v.occurred_at
FROM (VALUES
    (N't1', N'research-agent', N'started', N'Demo trace: bắt đầu tìm insight HSK3.', DATEADD(MINUTE, 61, @base_time)),
    (N't1', N'research-agent', N'completed', N'Demo trace: hoàn tất insight HSK3.', DATEADD(MINUTE, 63, @base_time)),
    (N't2', N'content-agent', N'completed', N'Demo trace: sinh nội dung TikTok từ insight.', DATEADD(MINUTE, 65, @base_time))
) AS v(task_id, agent_name, phase, message, occurred_at)
WHERE NOT EXISTS (
    SELECT 1 FROM agent_traces AS existing
    WHERE existing.session_id = @session_id
      AND existing.task_id = v.task_id
      AND existing.phase = v.phase
      AND existing.message = v.message
);

INSERT INTO claude_cost_ledger (id, tenant_id, agent_code, model, input_tokens, output_tokens, usd, created_at)
SELECT NEWID(), @tenant_id, v.agent_code, v.model, v.input_tokens, v.output_tokens, v.usd, v.created_at
FROM (VALUES
    (N'sale-assist-agent', N'claude-sonnet-4-6', 820, 210, CAST(0.005610 AS DECIMAL(12,6)), DATEADD(MINUTE, 70, @base_time)),
    (N'content-agent', N'claude-sonnet-4-6', 1200, 450, CAST(0.010350 AS DECIMAL(12,6)), DATEADD(MINUTE, 72, @base_time)),
    (N'orchestrator', N'claude-sonnet-4-6', 650, 180, CAST(0.004650 AS DECIMAL(12,6)), DATEADD(MINUTE, 74, @base_time))
) AS v(agent_code, model, input_tokens, output_tokens, usd, created_at)
WHERE NOT EXISTS (
    SELECT 1 FROM claude_cost_ledger AS existing
    WHERE existing.tenant_id = @tenant_id
      AND existing.agent_code = v.agent_code
      AND existing.created_at = v.created_at
);

IF COL_LENGTH(N'dbo.llm_configs', N'display_name') IS NOT NULL
BEGIN
    MERGE llm_configs AS target
    USING (SELECT N'anthropic' AS provider, N'claude-sonnet-4-6' AS model_id, N'Demo fallback — no real secret' AS display_name) AS source
    ON target.tenant_id = @tenant_id AND target.display_name = source.display_name
    WHEN MATCHED THEN
        UPDATE SET provider = source.provider,
                   model_id = source.model_id,
                   api_key_encrypted = N'DEMO_PLACEHOLDER_NOT_A_SECRET',
                   base_url = NULL,
                   is_active = 0,
                   input_usd_per_1m = 3.0000,
                   output_usd_per_1m = 15.0000,
                   updated_at = @now
    WHEN NOT MATCHED THEN
        INSERT (id, tenant_id, provider, model_id, api_key_encrypted, base_url, is_active, created_at, updated_at, display_name, input_usd_per_1m, output_usd_per_1m)
        VALUES (NEWID(), @tenant_id, source.provider, source.model_id, N'DEMO_PLACEHOLDER_NOT_A_SECRET', NULL, 0, @now, @now, source.display_name, 3.0000, 15.0000);
END;

DECLARE @expected_contacts INT = 3;
DECLARE @actual_contacts INT = (
    SELECT COUNT(*)
    FROM contacts AS c
    JOIN @contacts AS s ON s.email = c.email
    WHERE c.tenant_id = @tenant_id AND c.deleted_at IS NULL
);

IF @actual_contacts <> @expected_contacts
BEGIN
    ROLLBACK TRANSACTION;
    RAISERROR(N'Expected %d demo contacts for tenant "%s"; found %d. Seed aborted.', 16, 1, @expected_contacts, @tenant_slug, @actual_contacts);
    RETURN;
END;

COMMIT TRANSACTION;

PRINT N'demo-latest-flow seed applied for tenant: ' + @tenant_slug;
