-- Give every reopened durable review task a distinct audit cycle.
-- One SqlCommand; do not add GO.
IF COL_LENGTH(N'dbo.content_review_tasks', N'review_cycle') IS NULL
BEGIN
    ALTER TABLE dbo.content_review_tasks
        ADD review_cycle INT NOT NULL
            CONSTRAINT DF_content_review_tasks_review_cycle DEFAULT (1);
END

-- Boc EXEC de hoan bien dich: ca file la mot batch, tham chieu thang review_cycle o day se loi
-- "Invalid column name" vi cot vua them o tren chua ton tai luc SQL Server compile batch.
IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_content_review_tasks_review_cycle'
      AND parent_object_id = OBJECT_ID(N'dbo.content_review_tasks'))
BEGIN
    EXEC(N'ALTER TABLE dbo.content_review_tasks WITH CHECK
        ADD CONSTRAINT CK_content_review_tasks_review_cycle
        CHECK (review_cycle > 0);');
END
