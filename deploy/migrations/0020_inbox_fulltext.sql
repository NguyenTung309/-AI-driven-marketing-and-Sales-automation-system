-- 0020: Inbox full-text search backing indexes for GET /api/inbox/search?q=
-- SQL Server Full-Text Search must be installed on the target instance.

IF FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_catalogs WHERE name = N'clawbot_inbox_fts')
        CREATE FULLTEXT CATALOG clawbot_inbox_fts AS DEFAULT;

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.messages') AND name = N'ux_messages_fts_key')
        CREATE UNIQUE INDEX ux_messages_fts_key ON dbo.messages(id);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.contacts') AND name = N'ux_contacts_fts_key')
        CREATE UNIQUE INDEX ux_contacts_fts_key ON dbo.contacts(id);

    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.conversations') AND name = N'ux_conversations_fts_key')
        CREATE UNIQUE INDEX ux_conversations_fts_key ON dbo.conversations(id);

    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.messages'))
        CREATE FULLTEXT INDEX ON dbo.messages
        (
            content LANGUAGE 0,
            original_content LANGUAGE 0,
            redacted_content LANGUAGE 0,
            external_message_id LANGUAGE 0,
            parent_post_id LANGUAGE 0
        )
        KEY INDEX ux_messages_fts_key
        ON clawbot_inbox_fts
        WITH CHANGE_TRACKING AUTO;

    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.contacts'))
        CREATE FULLTEXT INDEX ON dbo.contacts
        (
            display_name LANGUAGE 0,
            phone LANGUAGE 0,
            email LANGUAGE 0
        )
        KEY INDEX ux_contacts_fts_key
        ON clawbot_inbox_fts
        WITH CHANGE_TRACKING AUTO;

    IF NOT EXISTS (SELECT 1 FROM sys.fulltext_indexes WHERE object_id = OBJECT_ID(N'dbo.conversations'))
        CREATE FULLTEXT INDEX ON dbo.conversations
        (
            external_thread_id LANGUAGE 0
        )
        KEY INDEX ux_conversations_fts_key
        ON clawbot_inbox_fts
        WITH CHANGE_TRACKING AUTO;
END
ELSE
BEGIN
    PRINT 'SQL Server Full-Text Search is not installed; /api/inbox/search will use application fallback search.';
END
