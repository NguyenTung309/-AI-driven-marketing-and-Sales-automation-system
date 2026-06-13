-- 0015: MassTransit EF transactional outbox (WS1).
-- Canonical MassTransit v8 SQL Server schema (PascalCase — excluded from snake_case conventions
-- so EF model mapping matches these tables exactly). Domain events enlist into OutboxMessage in
-- the same SaveChanges transaction (exactly-once); the delivery service relays to RabbitMQ.

CREATE TABLE InboxState (
    Id                  BIGINT IDENTITY(1,1) NOT NULL,
    MessageId           UNIQUEIDENTIFIER NOT NULL,
    ConsumerId          UNIQUEIDENTIFIER NOT NULL,
    LockId              UNIQUEIDENTIFIER NOT NULL,
    RowVersion          ROWVERSION NOT NULL,
    Received            DATETIME2 NOT NULL,
    ReceiveCount        INT NOT NULL,
    ExpirationTime      DATETIME2 NULL,
    Consumed            DATETIME2 NULL,
    Delivered           DATETIME2 NULL,
    LastSequenceNumber  BIGINT NULL,
    CONSTRAINT PK_InboxState PRIMARY KEY (Id)
);
CREATE UNIQUE INDEX IX_InboxState_MessageId_ConsumerId ON InboxState (MessageId, ConsumerId);
CREATE INDEX IX_InboxState_Delivered ON InboxState (Delivered);

CREATE TABLE OutboxState (
    OutboxId            UNIQUEIDENTIFIER NOT NULL,
    LockId              UNIQUEIDENTIFIER NOT NULL,
    RowVersion          ROWVERSION NOT NULL,
    Created             DATETIME2 NOT NULL,
    Delivered           DATETIME2 NULL,
    LastSequenceNumber  BIGINT NULL,
    CONSTRAINT PK_OutboxState PRIMARY KEY (OutboxId)
);
CREATE INDEX IX_OutboxState_Created ON OutboxState (Created);

CREATE TABLE OutboxMessage (
    SequenceNumber      BIGINT IDENTITY(1,1) NOT NULL,
    EnqueueTime         DATETIME2 NULL,
    SentTime            DATETIME2 NOT NULL,
    Headers             NVARCHAR(MAX) NULL,
    Properties          NVARCHAR(MAX) NULL,
    InboxMessageId      UNIQUEIDENTIFIER NULL,
    InboxConsumerId     UNIQUEIDENTIFIER NULL,
    OutboxId            UNIQUEIDENTIFIER NULL,
    MessageId           UNIQUEIDENTIFIER NOT NULL,
    ContentType         NVARCHAR(256) NOT NULL,
    MessageType         NVARCHAR(MAX) NOT NULL,
    Body                NVARCHAR(MAX) NOT NULL,
    ConversationId      UNIQUEIDENTIFIER NULL,
    CorrelationId       UNIQUEIDENTIFIER NULL,
    InitiatorId         UNIQUEIDENTIFIER NULL,
    RequestId           UNIQUEIDENTIFIER NULL,
    SourceAddress       NVARCHAR(256) NULL,
    DestinationAddress  NVARCHAR(256) NULL,
    ResponseAddress     NVARCHAR(256) NULL,
    FaultAddress        NVARCHAR(256) NULL,
    ExpirationTime      DATETIME2 NULL,
    CONSTRAINT PK_OutboxMessage PRIMARY KEY (SequenceNumber)
);
CREATE INDEX IX_OutboxMessage_EnqueueTime ON OutboxMessage (EnqueueTime);
CREATE INDEX IX_OutboxMessage_ExpirationTime ON OutboxMessage (ExpirationTime);
CREATE UNIQUE INDEX IX_OutboxMessage_OutboxId_SequenceNumber ON OutboxMessage (OutboxId, SequenceNumber) WHERE OutboxId IS NOT NULL;
CREATE UNIQUE INDEX IX_OutboxMessage_InboxMessageId_InboxConsumerId_SequenceNumber ON OutboxMessage (InboxMessageId, InboxConsumerId, SequenceNumber) WHERE InboxMessageId IS NOT NULL AND InboxConsumerId IS NOT NULL;
