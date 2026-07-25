-- Telemetry chuỗi sinh nội dung (prompt chaining P1): 1 dòng / mắt xích / lượt chạy.
-- payload_json là ảnh chụp CẤU TRÚC đã PII-redact (enum/độ dài/đếm) — không chứa văn bản khách. Retention 30 ngày.
-- Một SqlCommand, không GO. An toàn chạy lại (IF OBJECT_ID guard).
IF OBJECT_ID(N'dbo.content_generation_traces', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.content_generation_traces (
        id BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT pk_content_generation_traces PRIMARY KEY,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        content_item_id UNIQUEIDENTIFIER NULL,
        brief_id UNIQUEIDENTIFIER NULL,
        chain_run_id UNIQUEIDENTIFIER NOT NULL,
        step_id NVARCHAR(32) NOT NULL,
        prompt_version NVARCHAR(64) NOT NULL,
        model NVARCHAR(128) NOT NULL,
        input_tokens INT NOT NULL CONSTRAINT df_content_generation_traces_input_tokens DEFAULT 0,
        output_tokens INT NOT NULL CONSTRAINT df_content_generation_traces_output_tokens DEFAULT 0,
        usd_cost DECIMAL(18,6) NOT NULL CONSTRAINT df_content_generation_traces_usd_cost DEFAULT 0,
        latency_ms BIGINT NOT NULL CONSTRAINT df_content_generation_traces_latency_ms DEFAULT 0,
        gate_result NVARCHAR(128) NOT NULL,
        payload_json NVARCHAR(2000) NULL,
        created_at DATETIMEOFFSET NOT NULL
    );

    CREATE INDEX ix_content_generation_traces_tenant_created
        ON dbo.content_generation_traces(tenant_id, created_at);

    CREATE INDEX ix_content_generation_traces_chain_run
        ON dbo.content_generation_traces(chain_run_id);
END
