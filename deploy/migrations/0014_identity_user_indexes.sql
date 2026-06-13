-- 0014: Indexes on the Identity columns added to `users` in 0013.
-- Separate file (= separate batch) because referencing a column added via ALTER in the same
-- batch as the ALTER raises "Invalid column name" (deferred name resolution). The runner runs
-- each file as its own batch, so by here the 0013 ALTER has committed.
CREATE INDEX ix_users_normalized_email     ON users (normalized_email);
CREATE INDEX ix_users_normalized_user_name ON users (normalized_user_name);
