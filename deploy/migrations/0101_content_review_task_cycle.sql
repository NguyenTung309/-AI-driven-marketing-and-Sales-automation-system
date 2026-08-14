-- Give every reopened durable review task a distinct audit cycle.
-- One SqlCommand; do not add GO.
IF COL_LENGTH(N'dbo.content_review_tasks', N'review_cycle') IS NULL
BEGIN
    ALTER TABLE dbo.content_review_tasks
        ADD review_cycle INT NOT NULL
            CONSTRAINT DF_content_review_tasks_review_cycle DEFAULT (1),
            CONSTRAINT CK_content_review_tasks_review_cycle CHECK (review_cycle > 0);
END
ELSE IF NOT EXISTS (
    SELECT 1
    FROM sys.check_constraints
    WHERE name = N'CK_content_review_tasks_review_cycle'
      AND parent_object_id = OBJECT_ID(N'dbo.content_review_tasks'))
BEGIN
    DECLARE @reviewCycleCheckSql nvarchar(max);
    SET @reviewCycleCheckSql = N'ALTER TABLE dbo.content_review_tasks WITH CHECK ADD CONSTRAINT CK_content_review_tasks_review_cycle CHECK (review_cycle > 0);';
    EXEC(@reviewCycleCheckSql);
END
