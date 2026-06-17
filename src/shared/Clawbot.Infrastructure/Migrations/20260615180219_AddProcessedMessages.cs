#pragma warning disable CA1861, IDE0161
#pragma warning disable CA1861
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clawbot.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessedMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ads_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    campaign_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    rule_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action_taken = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    executed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ads_actions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ads_campaigns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    external_campaign_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    objective = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    daily_budget = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    synced_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ads_campaigns", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ads_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    metric = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    comparator = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    threshold = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ads_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    agent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    goal = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    plan_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_sessions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    agent_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    model = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    skill_files_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    kb_modules_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    config_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    key_hash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    scopes_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    security_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phone_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "bit", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "bit", nullable: false),
                    access_failed_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    resource_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    resource_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    diff_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ip_address = table.Column<string>(type: "nvarchar(45)", nullable: true),
                    user_agent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chat_scenarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    group_name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    trigger_text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    response_template = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    tone_voice = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    platforms = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    success_rate = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_scenarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    locale = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    lifetime_score = table.Column<int>(type: "int", nullable: false),
                    lifecycle_stage = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "content_briefs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    brief = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_briefs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "content_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    brief_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    assets_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "content_schedule",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    content_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    posted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    post_url = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_content_schedule", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "conversations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    external_thread_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    assigned_to = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    last_message_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    doc_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    template_html = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    fields_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "generated_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    template_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    generated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    file_url = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    file_hash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    sent_via = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    opened_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generated_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kb_modules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    owner_role = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kb_modules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kpi_daily",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    leads = table.Column<int>(type: "int", nullable: false),
                    dms = table.Column<int>(type: "int", nullable: false),
                    replies = table.Column<int>(type: "int", nullable: false),
                    conversions = table.Column<int>(type: "int", nullable: false),
                    avg_response_time_sec = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ad_spend = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kpi_daily", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lead_scoring_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    event_code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    weight = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lead_scoring_rules", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "leads",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    owner_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    score = table.Column<int>(type: "int", nullable: false),
                    stage = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    source_platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    last_activity_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leads", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "llm_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    provider = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    model_id = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    api_key_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    base_url = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    max_tokens = table.Column<int>(type: "int", nullable: true),
                    temperature = table.Column<decimal>(type: "decimal(3,2)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_llm_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pancake_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    base_url = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    access_token_encrypted = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    webhook_secret_encrypted = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    signature_header = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    signature_algo = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    signature_encoding = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    send_path_template = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    auth_mode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pancake_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    external_message_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    conversation_external_id = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    processed_at = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processed_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "quick_reply_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    category = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    platforms = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_quick_reply_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    family_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    replaced_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_ip = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    slug = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    plan_name = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agent_traces",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    task_id = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    agent_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    phase = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_traces", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_traces_agent_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "agent_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    claim_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    claim_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_role_claims_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "AspNetRoles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    claim_type = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    claim_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_asp_net_user_claims_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    provider_display_name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_asp_net_user_logins_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "AspNetRoles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asp_net_user_roles_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    login_provider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asp_net_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_asp_net_user_tokens_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "AspNetUsers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_external_ids",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    contact_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    platform = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    external_id = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_external_ids", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_external_ids_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    direction = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    sender_type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    sender_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "kb_test_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kb_module_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    question = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    expected_answer = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kb_test_cases", x => x.id);
                    table.ForeignKey(
                        name: "fk_kb_test_cases_kb_modules_kb_module_id",
                        column: x => x.kb_module_id,
                        principalTable: "kb_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "kb_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kb_module_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    content_md = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    embedding = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    accuracy_score = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    deployed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kb_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_kb_versions_kb_modules_kb_module_id",
                        column: x => x.kb_module_id,
                        principalTable: "kb_modules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lead_activities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    lead_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    activity_type = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    meta_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lead_activities", x => x.id);
                    table.ForeignKey(
                        name: "fk_lead_activities_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    permission_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_role_permissions_permissions_permission_id",
                        column: x => x.permission_id,
                        principalTable: "permissions",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_role_permissions_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ads_actions_campaign_id_executed_at",
                table: "ads_actions",
                columns: new[] { "campaign_id", "executed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ads_campaigns_tenant_id_platform_external_campaign_id",
                table: "ads_campaigns",
                columns: new[] { "tenant_id", "platform", "external_campaign_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ads_rules_tenant_id_is_active",
                table: "ads_rules",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_sessions_tenant_id_started_at",
                table: "agent_sessions",
                columns: new[] { "tenant_id", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agent_traces_session_id_occurred_at",
                table: "agent_traces",
                columns: new[] { "session_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_agents_tenant_id_agent_type",
                table: "agents",
                columns: new[] { "tenant_id", "agent_type" });

            migrationBuilder.CreateIndex(
                name: "ix_agents_tenant_id_code",
                table: "agents",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_role_claims_role_id",
                table: "AspNetRoleClaims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "role_name_index",
                table: "AspNetRoles",
                column: "normalized_name",
                unique: true,
                filter: "[normalized_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_claims_user_id",
                table: "AspNetUserClaims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_logins_user_id",
                table: "AspNetUserLogins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_asp_net_user_roles_role_id",
                table: "AspNetUserRoles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "email_index",
                table: "AspNetUsers",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "user_name_index",
                table: "AspNetUsers",
                column: "normalized_user_name",
                unique: true,
                filter: "[normalized_user_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_resource_type_resource_id",
                table: "audit_logs",
                columns: new[] { "resource_type", "resource_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_tenant_id_occurred_at",
                table: "audit_logs",
                columns: new[] { "tenant_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_scenarios_tenant_id_code",
                table: "chat_scenarios",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chat_scenarios_tenant_id_group_name",
                table: "chat_scenarios",
                columns: new[] { "tenant_id", "group_name" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_external_ids_contact_id",
                table: "contact_external_ids",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_external_ids_platform_external_id",
                table: "contact_external_ids",
                columns: new[] { "platform", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_tenant_id_created_at",
                table: "contacts",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_content_briefs_tenant_id_status",
                table: "content_briefs",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_content_items_tenant_id_status_created_at",
                table: "content_items",
                columns: new[] { "tenant_id", "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_content_schedule_tenant_id_scheduled_at",
                table: "content_schedule",
                columns: new[] { "tenant_id", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_platform_external_thread_id",
                table: "conversations",
                columns: new[] { "tenant_id", "platform", "external_thread_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_conversations_tenant_id_status_last_message_at",
                table: "conversations",
                columns: new[] { "tenant_id", "status", "last_message_at" });

            migrationBuilder.CreateIndex(
                name: "ix_document_templates_tenant_id_code",
                table: "document_templates",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_generated_documents_tenant_id_created_at",
                table: "generated_documents",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_kb_modules_tenant_id_code",
                table: "kb_modules",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kb_test_cases_kb_module_id",
                table: "kb_test_cases",
                column: "kb_module_id");

            migrationBuilder.CreateIndex(
                name: "ix_kb_versions_kb_module_id_version",
                table: "kb_versions",
                columns: new[] { "kb_module_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kpi_daily_tenant_id_date_platform",
                table: "kpi_daily",
                columns: new[] { "tenant_id", "date", "platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lead_activities_lead_id_occurred_at",
                table: "lead_activities",
                columns: new[] { "lead_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_lead_scoring_rules_tenant_id_event_code",
                table: "lead_scoring_rules",
                columns: new[] { "tenant_id", "event_code" });

            migrationBuilder.CreateIndex(
                name: "ix_leads_tenant_id_stage_score",
                table: "leads",
                columns: new[] { "tenant_id", "stage", "score" });

            migrationBuilder.CreateIndex(
                name: "ix_llm_configs_tenant_id_is_active",
                table: "llm_configs",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_sent_at",
                table: "messages",
                columns: new[] { "conversation_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_tenant_id_sent_at",
                table: "messages",
                columns: new[] { "tenant_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pancake_configs_tenant_id",
                table: "pancake_configs",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_permissions_code",
                table: "permissions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_processed_messages_platform_external_message_id",
                table: "processed_messages",
                columns: new[] { "platform", "external_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_quick_reply_templates_tenant_id_code",
                table: "quick_reply_templates",
                columns: new[] { "tenant_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family_id",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id_expires_at",
                table: "refresh_tokens",
                columns: new[] { "user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_name",
                table: "roles",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                table: "tenants",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ads_actions");

            migrationBuilder.DropTable(
                name: "ads_campaigns");

            migrationBuilder.DropTable(
                name: "ads_rules");

            migrationBuilder.DropTable(
                name: "agent_traces");

            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "chat_scenarios");

            migrationBuilder.DropTable(
                name: "contact_external_ids");

            migrationBuilder.DropTable(
                name: "content_briefs");

            migrationBuilder.DropTable(
                name: "content_items");

            migrationBuilder.DropTable(
                name: "content_schedule");

            migrationBuilder.DropTable(
                name: "document_templates");

            migrationBuilder.DropTable(
                name: "generated_documents");

            migrationBuilder.DropTable(
                name: "kb_test_cases");

            migrationBuilder.DropTable(
                name: "kb_versions");

            migrationBuilder.DropTable(
                name: "kpi_daily");

            migrationBuilder.DropTable(
                name: "lead_activities");

            migrationBuilder.DropTable(
                name: "lead_scoring_rules");

            migrationBuilder.DropTable(
                name: "llm_configs");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "pancake_configs");

            migrationBuilder.DropTable(
                name: "processed_messages");

            migrationBuilder.DropTable(
                name: "quick_reply_templates");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "tenants");

            migrationBuilder.DropTable(
                name: "agent_sessions");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "kb_modules");

            migrationBuilder.DropTable(
                name: "leads");

            migrationBuilder.DropTable(
                name: "conversations");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "roles");
        }
    }
}


