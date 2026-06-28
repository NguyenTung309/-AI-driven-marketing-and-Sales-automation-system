-- Them row_version cho concurrency
ALTER TABLE conversations ADD row_version TIMESTAMP NOT NULL;