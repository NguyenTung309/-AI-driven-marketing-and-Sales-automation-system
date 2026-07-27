-- Operator helpers for content workflow runtime gate.
-- NOT part of automatic migration/repair. Run manually during cutover / rollback.
-- Examples:
--   pause + raise minimum writer:
--     UPDATE dbo.content_workflow_runtime_gate
--     SET publication_paused = 1, minimum_writer_version = 1,
--         updated_at = SYSDATETIMEOFFSET(), updated_by = N'ops', notes = N'cutover pause'
--     WHERE id = 1;
--   resume after smoke:
--     UPDATE dbo.content_workflow_runtime_gate
--     SET publication_paused = 0, minimum_writer_version = 1,
--         updated_at = SYSDATETIMEOFFSET(), updated_by = N'ops', notes = N'resume publication'
--     WHERE id = 1;

SET NOCOUNT ON;
SELECT
    id,
    publication_paused,
    minimum_writer_version,
    updated_at,
    updated_by,
    notes
FROM dbo.content_workflow_runtime_gate
WHERE id = 1;
