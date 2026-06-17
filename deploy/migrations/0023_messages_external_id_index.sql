-- Create the external_message_id index after the column has been added in 0007.
-- SQL Server compiles each migration file as one batch in CI/Testcontainers.

CREATE UNIQUE INDEX ix_messages_external_id
    ON messages (tenant_id, external_message_id)
    WHERE external_message_id IS NOT NULL;
