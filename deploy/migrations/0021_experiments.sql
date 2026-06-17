IF OBJECT_ID(N'dbo.experiment_events', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.experiments (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_experiments PRIMARY KEY,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        code NVARCHAR(64) NOT NULL,
        target_type NVARCHAR(32) NOT NULL,
        target_id UNIQUEIDENTIFIER NOT NULL,
        name NVARCHAR(256) NOT NULL,
        status NVARCHAR(32) NOT NULL CONSTRAINT df_experiments_status DEFAULT N'active',
        created_at DATETIMEOFFSET NOT NULL CONSTRAINT df_experiments_created_at DEFAULT SYSDATETIMEOFFSET(),
        updated_at DATETIMEOFFSET NULL,
        deleted_at DATETIMEOFFSET NULL,
        CONSTRAINT fk_experiments_tenants FOREIGN KEY (tenant_id) REFERENCES dbo.tenants(id)
    );

    CREATE UNIQUE INDEX ux_experiments_tenant_code ON dbo.experiments(tenant_id, code);
    CREATE INDEX ix_experiments_tenant_target_status ON dbo.experiments(tenant_id, target_type, target_id, status);

    CREATE TABLE dbo.experiment_variants (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_experiment_variants PRIMARY KEY,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        experiment_id UNIQUEIDENTIFIER NOT NULL,
        code NVARCHAR(32) NOT NULL,
        name NVARCHAR(256) NOT NULL,
        weight INT NOT NULL,
        chat_scenario_id UNIQUEIDENTIFIER NULL,
        kb_version_id UNIQUEIDENTIFIER NULL,
        created_at DATETIMEOFFSET NOT NULL CONSTRAINT df_experiment_variants_created_at DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT fk_experiment_variants_experiments FOREIGN KEY (experiment_id) REFERENCES dbo.experiments(id) ON DELETE CASCADE,
        CONSTRAINT fk_experiment_variants_chat_scenarios FOREIGN KEY (chat_scenario_id) REFERENCES dbo.chat_scenarios(id),
        CONSTRAINT fk_experiment_variants_kb_versions FOREIGN KEY (kb_version_id) REFERENCES dbo.kb_versions(id),
        CONSTRAINT ck_experiment_variants_weight CHECK (weight > 0)
    );

    CREATE UNIQUE INDEX ux_experiment_variants_experiment_code ON dbo.experiment_variants(experiment_id, code);

    CREATE TABLE dbo.experiment_assignments (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_experiment_assignments PRIMARY KEY,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        experiment_id UNIQUEIDENTIFIER NOT NULL,
        variant_id UNIQUEIDENTIFIER NOT NULL,
        subject_key NVARCHAR(256) NOT NULL,
        assigned_at DATETIMEOFFSET NOT NULL CONSTRAINT df_experiment_assignments_assigned_at DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT fk_experiment_assignments_experiments FOREIGN KEY (experiment_id) REFERENCES dbo.experiments(id) ON DELETE CASCADE,
        CONSTRAINT fk_experiment_assignments_variants FOREIGN KEY (variant_id) REFERENCES dbo.experiment_variants(id)
    );

    CREATE UNIQUE INDEX ux_experiment_assignments_subject ON dbo.experiment_assignments(tenant_id, experiment_id, subject_key);
    CREATE INDEX ix_experiment_assignments_variant ON dbo.experiment_assignments(experiment_id, variant_id);

    CREATE TABLE dbo.experiment_events (
        id UNIQUEIDENTIFIER NOT NULL CONSTRAINT pk_experiment_events PRIMARY KEY,
        tenant_id UNIQUEIDENTIFIER NOT NULL,
        experiment_id UNIQUEIDENTIFIER NOT NULL,
        variant_id UNIQUEIDENTIFIER NOT NULL,
        subject_key NVARCHAR(256) NOT NULL,
        event_type NVARCHAR(32) NOT NULL,
        value DECIMAL(18, 4) NULL,
        occurred_at DATETIMEOFFSET NOT NULL CONSTRAINT df_experiment_events_occurred_at DEFAULT SYSDATETIMEOFFSET(),
        CONSTRAINT fk_experiment_events_experiments FOREIGN KEY (experiment_id) REFERENCES dbo.experiments(id) ON DELETE CASCADE,
        CONSTRAINT fk_experiment_events_variants FOREIGN KEY (variant_id) REFERENCES dbo.experiment_variants(id)
    );

    CREATE INDEX ix_experiment_events_tenant_type ON dbo.experiment_events(tenant_id, experiment_id, event_type, occurred_at);
    CREATE INDEX ix_experiment_events_variant_subject ON dbo.experiment_events(experiment_id, variant_id, subject_key);
END
