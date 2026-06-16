-- 0017: index for comment lookups by post (separate batch — CREATE INDEX on the ALTER-added
-- parent_post_id column from 0016 cannot run in the same batch without GO).
CREATE INDEX ix_messages_parent_post ON messages (tenant_id, parent_post_id) WHERE parent_post_id IS NOT NULL;
