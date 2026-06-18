-- 0020: Inbox full-text search backing indexes for GET /api/inbox/search?q=
--
-- Local SQL Server containers used by run-all.bat may report Full-Text Search metadata
-- but still fail CREATE FULLTEXT with "component cannot be loaded". The API has an
-- application-level fallback search, so keep local/bootstrap migrations non-blocking.
-- Production environments that require SQL Server full-text search should provision it
-- with an environment-specific DBA migration.

PRINT 'Skipping SQL Server Full-Text Search bootstrap; /api/inbox/search will use application fallback search.';
