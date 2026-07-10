-- 0051: Review-gate P2 — messages.status: 'sent' | 'pending_approval' (AI reply hold chờ người duyệt)
-- | 'blocked' (safety/review chặn, không bao giờ gửi). Default 'sent' cho mọi row cũ.
-- One SqlCommand, no GO.
IF COL_LENGTH('dbo.messages', 'status') IS NULL
    ALTER TABLE dbo.messages ADD status NVARCHAR(32) NOT NULL CONSTRAINT DF_messages_status DEFAULT N'sent';
