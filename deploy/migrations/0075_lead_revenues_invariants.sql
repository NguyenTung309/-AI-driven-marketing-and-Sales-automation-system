-- Revenue invariants: FK to leads, amount bounds, one active (pending|approved) row per lead.
-- One SqlCommand, no GO. Safe to re-run. Depends on 0073 (table) + leads.

IF OBJECT_ID(N'dbo.lead_revenues', N'U') IS NOT NULL
BEGIN
    IF OBJECT_ID(N'dbo.leads', N'U') IS NOT NULL
       AND NOT EXISTS (
        SELECT 1 FROM sys.foreign_keys
        WHERE name = N'FK_lead_revenues_leads' AND parent_object_id = OBJECT_ID(N'dbo.lead_revenues'))
    BEGIN
        ALTER TABLE dbo.lead_revenues WITH NOCHECK
            ADD CONSTRAINT FK_lead_revenues_leads
            FOREIGN KEY (lead_id) REFERENCES dbo.leads(id) ON DELETE CASCADE;
    END

    IF NOT EXISTS (
        SELECT 1 FROM sys.check_constraints
        WHERE name = N'CK_lead_revenues_amount' AND parent_object_id = OBJECT_ID(N'dbo.lead_revenues'))
    BEGIN
        ALTER TABLE dbo.lead_revenues WITH NOCHECK
            ADD CONSTRAINT CK_lead_revenues_amount
            CHECK (amount > 0 AND amount <= 10000000000 AND currency = N'VND');
    END

    -- Chỉ 1 dòng pending hoặc approved / lead. Rejected history không chặn thanh toán lại.
    IF NOT EXISTS (
        SELECT 1 FROM sys.indexes
        WHERE name = N'UX_lead_revenues_one_active' AND object_id = OBJECT_ID(N'dbo.lead_revenues'))
    BEGIN
        CREATE UNIQUE INDEX UX_lead_revenues_one_active
            ON dbo.lead_revenues (lead_id)
            WHERE status IN (N'pending', N'approved');
    END
END
