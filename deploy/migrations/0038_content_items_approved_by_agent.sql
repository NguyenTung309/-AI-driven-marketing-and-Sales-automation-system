-- 0038: content_items.approved_by_agent_id — attribute autonomous reviewer-agent approval (SPEC-16 P2-6).
-- A reviewer (lead-type) agent approving a draft records its agent_definition id here (not a human users.id),
-- so audit distinguishes autonomous approval from human approval. Nullable; human approvals keep approved_by.

IF COL_LENGTH(N'dbo.content_items', N'approved_by_agent_id') IS NULL
    ALTER TABLE dbo.content_items ADD approved_by_agent_id UNIQUEIDENTIFIER NULL;
