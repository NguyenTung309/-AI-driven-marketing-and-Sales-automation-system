namespace Clawbot.Domain.Common;

/// <summary>
/// Marker: entity changes are not written to audit_logs by AuditSaveChangesInterceptor.
/// Use for technical/ephemeral rows (OAuth state, processed-message dedup, refresh tokens, jobs, notifications).
/// </summary>
public interface IAuditExempt;
