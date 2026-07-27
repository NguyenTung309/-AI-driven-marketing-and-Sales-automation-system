SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET QUOTED_IDENTIFIER ON;
SET NUMERIC_ROUNDABORT OFF;

IF OBJECT_ID(N'dbo.content_review_tasks', N'U') IS NULL
    THROW 50001, 'dbo.content_review_tasks is missing.', 1;

IF COL_LENGTH(N'dbo.content_review_tasks', N'claimed_lease_token') IS NULL
    ALTER TABLE dbo.content_review_tasks
        ADD claimed_lease_token UNIQUEIDENTIFIER NULL;
