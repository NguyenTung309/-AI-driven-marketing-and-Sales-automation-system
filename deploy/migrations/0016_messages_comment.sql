-- 0016: Chat-2 — distinguish public comments from DMs on messages.
-- message_type: 'text' (DM/normal) | 'comment' (public comment under a post) | 'dm'.
-- parent_post_id: links a comment to the post it was made under (for comment auto-reply + DM invite).
-- Index on the ALTER-added parent_post_id column lives in 0017 (separate batch — cannot share with ALTER).
ALTER TABLE messages ADD message_type NVARCHAR(16) NOT NULL DEFAULT 'text';
ALTER TABLE messages ADD parent_post_id NVARCHAR(128) NULL;
