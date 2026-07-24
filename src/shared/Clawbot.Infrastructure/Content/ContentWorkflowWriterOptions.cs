namespace Clawbot.Infrastructure.Content;

/// <summary>
/// Bridge/cutover writer identity stamped into SQL SESSION_CONTEXT on every AppDbContext connection.
/// Raise Content:WorkflowWriter:Version when shipping a compatibility/backstop build; keep
/// dbo.content_workflow_runtime_gate.minimum_writer_version in sync during cutover.
/// </summary>
public sealed class ContentWorkflowWriterOptions
{
    public const string SectionName = "Content:WorkflowWriter";

    /// <summary>Current binary writer version (must be &gt;= SQL minimum when minimum &gt; 0).</summary>
    public int Version { get; set; } = 1;

    /// <summary>SESSION_CONTEXT key — keep in sync with 0080 trigger SQL.</summary>
    public string SessionContextKey { get; set; } = "clawbot_content_writer_version";
}
