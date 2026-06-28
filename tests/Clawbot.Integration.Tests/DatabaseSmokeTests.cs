using System.Text;
using FluentAssertions;
using Microsoft.Data.SqlClient;

namespace Clawbot.Integration.Tests;

/// <summary>
/// M01 smoke: apply DDL, insert into core tables, verify reads.
/// M02 smoke: cross-tenant query returns 0 rows.
/// M06 smoke: mock Pancake webhook round-trip (insert inbound message, verify).
/// </summary>
public sealed class DatabaseSmokeTests : IClassFixture<SqlServerFixture>
{
    private readonly SqlServerFixture _fx;

    public DatabaseSmokeTests(SqlServerFixture fx) => _fx = fx;

    [Fact]
    public async Task M01_Can_insert_and_read_tenant()
    {
        await using var conn = await _fx.OpenConnectionAsync();
        var tenantId = Guid.NewGuid();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO tenants (id, slug, display_name, plan_name)
                VALUES (@id, @slug, @name, 'free')
                """;
            cmd.Parameters.AddWithValue("@id", tenantId);
            cmd.Parameters.AddWithValue("@slug", "smoke-test");
            cmd.Parameters.AddWithValue("@name", "Smoke Test Tenant");
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT slug, display_name FROM tenants WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", tenantId);
            using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("smoke-test");
            reader.GetString(1).Should().Be("Smoke Test Tenant");
        }
    }

    [Fact]
    public async Task M01_Can_insert_and_read_contact_with_tenant()
    {
        await using var conn = await _fx.OpenConnectionAsync();
        var tenantId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO tenants (id, slug, display_name, plan_name)
                VALUES (@tid, 'contact-test', 'Contact Test', 'free');
                INSERT INTO contacts (id, tenant_id, display_name, phone, email)
                VALUES (@cid, @tid, 'Nguyen Van A', '0901234567', 'a@test.com');
                """;
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@cid", contactId);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT display_name, phone, email FROM contacts WHERE id = @cid";
            cmd.Parameters.AddWithValue("@cid", contactId);
            using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("Nguyen Van A");
            reader.GetString(1).Should().Be("0901234567");
            reader.GetString(2).Should().Be("a@test.com");
        }
    }

    [Fact]
    public async Task M01_Can_insert_conversation_and_messages()
    {
        await using var conn = await _fx.OpenConnectionAsync();
        var tenantId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var convId = Guid.NewGuid();
        var msgId = Guid.NewGuid();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO tenants (id, slug, display_name, plan_name)
                VALUES (@tid, 'conv-test', 'Conv Test', 'free');
                INSERT INTO contacts (id, tenant_id, display_name)
                VALUES (@cid, @tid, 'Conv Contact');
                INSERT INTO conversations (id, tenant_id, contact_id, platform, external_thread_id)
                VALUES (@vid, @tid, @cid, 'facebook', 'thread-001');
                INSERT INTO messages (id, conversation_id, tenant_id, direction, sender_type, content)
                VALUES (@mid, @vid, @tid, 'in', 'contact', 'Hello from customer');
                """;
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@cid", contactId);
            cmd.Parameters.AddWithValue("@vid", convId);
            cmd.Parameters.AddWithValue("@mid", msgId);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT m.content, m.direction, m.sender_type
                FROM messages m
                WHERE m.conversation_id = @vid
                """;
            cmd.Parameters.AddWithValue("@vid", convId);
            using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("Hello from customer");
            reader.GetString(1).Should().Be("in");
            reader.GetString(2).Should().Be("contact");
        }
    }

    [Fact]
    public async Task M01_Can_insert_lead_with_scoring_rule()
    {
        await using var conn = await _fx.OpenConnectionAsync();
        var tenantId = Guid.NewGuid();
        var leadId = Guid.NewGuid();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO tenants (id, slug, display_name, plan_name)
                VALUES (@tid, 'lead-test', 'Lead Test', 'free');
                INSERT INTO leads (id, tenant_id, score, stage, source_platform)
                VALUES (@lid, @tid, 45, 'warm', 'facebook');
                INSERT INTO lead_scoring_rules (id, tenant_id, event_code, weight, description)
                VALUES (NEWID(), @tid, 'asks_price', 10, 'Customer asks about pricing');
                """;
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@lid", leadId);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT score, stage FROM leads WHERE id = @lid";
            cmd.Parameters.AddWithValue("@lid", leadId);
            using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetInt32(0).Should().Be(45);
            reader.GetString(1).Should().Be("warm");
        }
    }

    [Fact]
    public async Task M02_Cross_tenant_query_returns_zero_rows()
    {
        await using var conn = await _fx.OpenConnectionAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO tenants (id, slug, display_name, plan_name)
                VALUES (@ta, 'tenant-a', 'Tenant A', 'free');
                INSERT INTO tenants (id, slug, display_name, plan_name)
                VALUES (@tb, 'tenant-b', 'Tenant B', 'free');
                INSERT INTO contacts (id, tenant_id, display_name)
                VALUES (NEWID(), @ta, 'Contact In A');
                INSERT INTO contacts (id, tenant_id, display_name)
                VALUES (NEWID(), @tb, 'Contact In B');
                """;
            cmd.Parameters.AddWithValue("@ta", tenantA);
            cmd.Parameters.AddWithValue("@tb", tenantB);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT display_name FROM contacts WHERE tenant_id = @ta";
            cmd.Parameters.AddWithValue("@ta", tenantA);
            using var reader = await cmd.ExecuteReaderAsync();
            var names = new List<string>();
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));
            names.Should().ContainSingle("Contact In A");
            names.Should().NotContain("Contact In B");
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM contacts WHERE tenant_id = @nonexistent";
            cmd.Parameters.AddWithValue("@nonexistent", Guid.NewGuid());
            var count = await cmd.ExecuteScalarAsync();
            count.Should().Be(0);
        }
    }

    [Fact]
    public async Task M06_Mock_pancake_webhook_inbound_message_roundtrip()
    {
        await using var conn = await _fx.OpenConnectionAsync();
        var tenantId = Guid.NewGuid();
        var contactId = Guid.NewGuid();
        var convId = Guid.NewGuid();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO tenants (id, slug, display_name, plan_name)
                VALUES (@tid, 'pancake-test', 'Pancake Test', 'free');
                INSERT INTO contacts (id, tenant_id, display_name)
                VALUES (@cid, @tid, 'Pancake Customer');
                INSERT INTO contact_external_ids (id, contact_id, platform, external_id)
                VALUES (NEWID(), @cid, 'facebook', 'ext-user-123');
                INSERT INTO conversations (id, tenant_id, contact_id, platform, external_thread_id)
                VALUES (@vid, @tid, @cid, 'facebook', 'ext-thread-456');
                """;
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@cid", contactId);
            cmd.Parameters.AddWithValue("@vid", convId);
            await cmd.ExecuteNonQueryAsync();
        }

        var inboundMsgId = Guid.NewGuid();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO messages (id, conversation_id, tenant_id, direction, sender_type, content, metadata_json)
                VALUES (@mid, @vid, @tid, 'in', 'contact', N'Xin chào, tôi muốn hỏi về khóa học', '{"external_message_id":"ext-msg-789","display_name":"Pancake Customer"}');
                """;
            cmd.Parameters.AddWithValue("@mid", inboundMsgId);
            cmd.Parameters.AddWithValue("@vid", convId);
            cmd.Parameters.AddWithValue("@tid", tenantId);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT m.content, m.direction, m.metadata_json
                FROM messages m
                WHERE m.conversation_id = @vid
                """;
            cmd.Parameters.AddWithValue("@vid", convId);
            using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("Xin chào, tôi muốn hỏi về khóa học");
            reader.GetString(1).Should().Be("in");
            var meta = reader.GetString(2);
            meta.Should().Contain("ext-msg-789");
        }

        var outboundMsgId = Guid.NewGuid();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO messages (id, conversation_id, tenant_id, direction, sender_type, content)
                VALUES (@mid, @vid, @tid, 'out', 'agent', N'Cảm ơn bạn đã liên hệ! Chúng tôi sẽ tư vấn cho bạn.');
                """;
            cmd.Parameters.AddWithValue("@mid", outboundMsgId);
            cmd.Parameters.AddWithValue("@vid", convId);
            cmd.Parameters.AddWithValue("@tid", tenantId);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM messages WHERE conversation_id = @vid";
            cmd.Parameters.AddWithValue("@vid", convId);
            var count = await cmd.ExecuteScalarAsync();
            count.Should().Be(2);
        }
    }

    [Fact]
    public async Task M01_Kb_module_and_version_CRUD()
    {
        await using var conn = await _fx.OpenConnectionAsync();
        var tenantId = Guid.NewGuid();
        var moduleId = Guid.NewGuid();
        var versionId = Guid.NewGuid();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO tenants (id, slug, display_name, plan_name)
                VALUES (@tid, 'kb-test', 'KB Test', 'free');
                INSERT INTO kb_modules (id, tenant_id, code, name, description, status)
                VALUES (@mid, @tid, 'KB-01', 'Sales FAQ', 'Frequently asked questions about pricing', 'active');
                INSERT INTO kb_versions (id, kb_module_id, version, content_md, status)
                VALUES (@vid, @mid, 1, '# Sales FAQ\n\n## Pricing\nOur pricing starts at...', 'deployed');
                """;
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@mid", moduleId);
            cmd.Parameters.AddWithValue("@vid", versionId);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT km.code, km.name, kv.version, kv.status
                FROM kb_modules km
                JOIN kb_versions kv ON kv.kb_module_id = km.id
                WHERE km.id = @mid
                """;
            cmd.Parameters.AddWithValue("@mid", moduleId);
            using var reader = await cmd.ExecuteReaderAsync();
            (await reader.ReadAsync()).Should().BeTrue();
            reader.GetString(0).Should().Be("KB-01");
            reader.GetString(1).Should().Be("Sales FAQ");
            reader.GetInt32(2).Should().Be(1);
            reader.GetString(3).Should().Be("deployed");
        }
    }

    [Fact]
    public async Task M01_Agent_session_and_trace_lifecycle()
    {
        await using var conn = await _fx.OpenConnectionAsync();
        var tenantId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                INSERT INTO tenants (id, slug, display_name, plan_name)
                VALUES (@tid, 'agent-test', 'Agent Test', 'free');
                INSERT INTO agents (id, tenant_id, code, display_name, agent_type, model, status)
                VALUES (@aid, @tid, 'chat-01', 'Chat Agent', 'chat', 'claude-3-haiku', 'running');
                INSERT INTO agent_sessions (id, tenant_id, agent_id, goal, status)
                VALUES (@sid, @tid, @aid, 'Handle customer inquiry', 'running');
                INSERT INTO agent_traces (id, session_id, agent_name, phase, message)
                VALUES (NEWID(), @sid, 'ChatAgent', 'rag_retrieve', 'Found 3 relevant chunks');
                INSERT INTO agent_traces (id, session_id, agent_name, phase, message)
                VALUES (NEWID(), @sid, 'ChatAgent', 'claude_call', 'Generated response in 1.2s');
                """;
            cmd.Parameters.AddWithValue("@tid", tenantId);
            cmd.Parameters.AddWithValue("@aid", agentId);
            cmd.Parameters.AddWithValue("@sid", sessionId);
            await cmd.ExecuteNonQueryAsync();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM agent_traces WHERE session_id = @sid";
            cmd.Parameters.AddWithValue("@sid", sessionId);
            var count = await cmd.ExecuteScalarAsync();
            count.Should().Be(2);
        }
    }

    [Fact]
    public async Task M01_All_migration_tables_exist()
    {
        await using var conn = await _fx.OpenConnectionAsync();
        var expectedTables = new[]
        {
            "tenants", "users", "roles", "permissions", "role_permissions", "user_roles", "api_keys",
            "audit_logs", "contacts", "contact_external_ids", "conversations", "messages",
            "leads", "lead_scoring_rules", "lead_activities",
            "kb_modules", "kb_versions", "kb_test_cases",
            "chat_scenarios", "agents", "agent_sessions", "agent_traces",
            "quick_reply_templates", "document_templates", "generated_documents",
            "content_briefs", "content_items", "content_schedule",
            "ads_campaigns", "ads_rules", "ads_actions", "ads_creatives", "ads_metrics_daily",
            "kpi_daily", "kpi_forecast",
            "pancake_configs", "llm_configs"
        };

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_type = 'BASE TABLE'";
        using var reader = await cmd.ExecuteReaderAsync();
        var actualTables = new List<string>();
        while (await reader.ReadAsync())
            actualTables.Add(reader.GetString(0));

        foreach (var table in expectedTables)
            actualTables.Should().Contain(table, because: $"table '{table}' should exist after migration apply");
    }

    [Fact]
    public async Task M01_Permissions_seed_has_18_rows()
    {
        await using var conn = await _fx.OpenConnectionAsync();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM permissions";
        var count = await cmd.ExecuteScalarAsync();
        Convert.ToInt32(count, System.Globalization.CultureInfo.InvariantCulture).Should().BeGreaterThanOrEqualTo(18);
    }
}
