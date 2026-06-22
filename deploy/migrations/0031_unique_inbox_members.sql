-- 0031_unique_inbox_members.sql
-- Enforce 1 sale per channel per business model (Section 8.1)
CREATE UNIQUE INDEX uq_inbox_members_inbox ON InboxMembers (InboxId);

