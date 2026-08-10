Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Read-RequiredFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required production deployment file is missing: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw
}

function Assert-Match {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($Content -notmatch $Pattern) {
        throw "Production deployment contract failed: $Description"
    }
}

function Assert-NotMatch {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($Content -match $Pattern) {
        throw "Production deployment contract failed: $Description"
    }
}

function Assert-Precedes {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$Earlier,
        [Parameter(Mandatory = $true)][string]$Later,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $earlierIndex = $Content.IndexOf($Earlier, [StringComparison]::Ordinal)
    $laterIndex = $Content.IndexOf($Later, [StringComparison]::Ordinal)
    if ($earlierIndex -lt 0 -or $laterIndex -lt 0 -or $earlierIndex -ge $laterIndex) {
        throw "Production deployment contract failed: $Description"
    }
}

function Assert-RegexPrecedes {
    param(
        [Parameter(Mandatory = $true)][string]$Content,
        [Parameter(Mandatory = $true)][string]$EarlierPattern,
        [Parameter(Mandatory = $true)][string]$LaterPattern,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $earlierMatch = [regex]::Match($Content, $EarlierPattern)
    $laterMatch = [regex]::Match($Content, $LaterPattern)
    if (-not $earlierMatch.Success -or -not $laterMatch.Success -or $earlierMatch.Index -ge $laterMatch.Index) {
        throw "Production deployment contract failed: $Description"
    }
}

$workflow = Read-RequiredFile ".github/workflows/production.yml"
$deployScript = Read-RequiredFile "deploy/production/deploy.sh"
$backupScript = Read-RequiredFile "deploy/production/backup.sh"
$migrateScript = Read-RequiredFile "deploy/production/migrate.sh"
$rollbackScript = Read-RequiredFile "deploy/production/rollback.sh"
$restoreScript = Read-RequiredFile "deploy/production/restore-verified-backup.sh"
$installerScript = Read-RequiredFile "deploy/production/install-release.sh"
$environmentExample = Read-RequiredFile "deploy/.env.production.example"
$processedMessageConfiguration = Read-RequiredFile "src/shared/Clawbot.Infrastructure/Persistence/Configurations/ProcessedMessageConfiguration.cs"
$processedMessagesInitialMigration = Read-RequiredFile "deploy/migrations/0002_processed_messages.sql"
$processedMessagesTenantMigration = Read-RequiredFile "deploy/migrations/0099_processed_messages_tenant_column.sql"
$processedMessagesDeduplicationMigration = Read-RequiredFile "deploy/migrations/0100_processed_messages_tenant_deduplication.sql"
$avatarFixMigration = Read-RequiredFile "deploy/migrations/0047_fix_wrong_avatars.sql"
$orchestrationWorker = Read-RequiredFile "src/agents/Clawbot.Agents.Core/Orchestrator/GenericLlmAgentWorker.cs"
$orchestratorGrpcService = Read-RequiredFile "src/agents/Clawbot.AgentService/Services/OrchestratorGrpcService.cs"
$inboxNotesEndpoint = Read-RequiredFile "src/api/Clawbot.Api/Endpoints/InboxNotesEndpoints.cs"
$inboxCollaborationRepair = Read-RequiredFile "deploy/repair_inbox_collaboration_tables.sql"

Assert-Match $workflow '(?m)^permissions:\s*\r?\n\s+contents: read$' "workflow defaults must restrict token permissions"
Assert-Match $workflow 'id: release_stage' "workflow must create an isolated release stage"
Assert-Match $workflow '\.clawbot-releases' "workflow must not stage deployment assets in shared /tmp"
Assert-Match $workflow 'scp_args=.*-P "\$SSH_PORT"' "scp must use uppercase -P for the SSH port"
Assert-NotMatch $workflow 'scp "\$\{ssh_args\[@\]\}"' "scp must not reuse SSH lowercase -p arguments"
Assert-NotMatch $workflow '(?m):/tmp/' "workflow must not upload release files to shared /tmp"
Assert-NotMatch $workflow '/tmp/\*\.sh|/tmp/repair_\*\.sql|/tmp/migrations|/tmp/images\.env' "workflow must not install wildcard files from shared /tmp"
Assert-Match $workflow 'verify-production-deployment\.ps1' "production validation must execute its deployment contract test"
Assert-Match $workflow 'COMPOSE_ENV is required' "workflow must reject an empty Compose environment"
Assert-Match $workflow 'RUNTIME_ENV is required' "workflow must reject an empty runtime environment"
Assert-Match $workflow 'git grep -I -qE' "credential scan must suppress matching source lines and ignore binary blobs"
Assert-Match $workflow "'\:\(exclude\)docs/\*\*'" "credential scan must scan tracked source and deployment inputs rather than a narrow file allow-list"
Assert-NotMatch $workflow 'printf.*\$matches' "credential scan must not print credential-bearing source lines"
Assert-Match $installerScript 'install -d -m 0700 "\$release_root"' "release installer must create protected versioned storage"
Assert-Match $installerScript 'candidate-production\.env' "release installer must synthesize candidate-only runtime and settings paths"
Assert-Match $installerScript 'mv -Tf "\$release_root/current\.new" "\$current_link"' "release installer must atomically promote the release pointer"

$requiredSqlContracts = @(
    "repair_tenant_runtime_columns.sql",
    "repair_inbox_runtime_columns.sql",
    "repair_agent_runtime_columns.sql",
    "repair_inbox_collaboration_tables.sql",
    "repair_agent_allowed_tools.sql",
    "verify_content_render_tasks.sql",
    "verify_database_table_consolidation.sql"
)

foreach ($sqlContract in $requiredSqlContracts) {
    Assert-Match $workflow ([regex]::Escape($sqlContract)) "workflow must transfer the explicit SQL contract '$sqlContract'"
    Assert-Match $migrateScript ([regex]::Escape($sqlContract)) "migration runner must explicitly execute '$sqlContract'"
}

Assert-NotMatch $workflow 'repair_\*\.sql|verify_\*\.sql' "workflow must not transfer wildcard repair or verification scripts"
Assert-NotMatch $environmentExample '(?m)^CLAWBOT_(API|GATEWAY|AGENT|WEB)_IMAGE=' "Compose environment must not duplicate release image variables"
Assert-Match $processedMessageConfiguration 'HasIndex\(x => new \{ x\.TenantId, x\.Platform, x\.ExternalMessageId \}\)\.IsUnique\(\)' "processed-message deduplication must be tenant scoped in the EF model"
Assert-Match $processedMessagesInitialMigration 'tenant_id, Platform, ExternalMessageId' "new databases must create tenant-scoped processed-message deduplication"
Assert-Match $processedMessagesTenantMigration 'ADD tenant_id UNIQUEIDENTIFIER NOT NULL' "existing databases must restore the required processed-message tenant column"
Assert-Match $processedMessagesDeduplicationMigration 'DROP CONSTRAINT' "existing databases must remove the legacy global processed-message uniqueness constraint"
Assert-Match $processedMessagesDeduplicationMigration 'CREATE UNIQUE INDEX IX_processed_messages_tenant_platform_external' "existing databases must add tenant-scoped processed-message uniqueness"
Assert-Match $avatarFixMigration 'i\.tenant_id = c\.tenant_id' "avatar cleanup must scope contact-to-inbox joins to a tenant"
Assert-Match $avatarFixMigration 'i\.tenant_id = m\.tenant_id' "avatar cleanup must scope message-to-inbox joins to a tenant"
Assert-Match $orchestrationWorker 'executionPermissions is null \|\| !executionPermissions\.Contains\(tool\.RequiredPermission\)' "orchestration tools must enforce the execution principal's required permission"
Assert-Match $orchestratorGrpcService 'ResolveExecutionPermissionsAsync' "orchestration service must resolve execution-principal permissions"
Assert-Match $inboxNotesEndpoint 'c\.Id == conversationId && c\.TenantId == tenant\.TenantId' "conversation notes must verify the conversation's tenant before insert"
Assert-Match $inboxCollaborationRepair 'FK_conversation_notes_tenant_conversations' "collaboration repair must enforce tenant-scoped note-to-conversation references"
Assert-Match $inboxCollaborationRepair 'FOREIGN KEY \(tenant_id, conversation_id\) REFERENCES dbo\.conversations\(tenant_id, id\)' "collaboration repair must use a composite tenant-conversation foreign key"

Assert-Match $deployScript 'validate_environment_file' "deployment must reject duplicate environment keys"
Assert-Match $deployScript 'preflight_http_port' "deployment must validate the public port before changing state"
Assert-Match $deployScript 'backup_existing_database' "deployment must back up an existing database before infrastructure recreation"
Assert-RegexPrecedes $deployScript '(?m)^validate_environment_file "\$compose_env"$' '(?m)^docker compose --env-file "\$compose_env" -f "\$compose_file" pull$' "environment validation must happen before pulling images"
Assert-RegexPrecedes $deployScript '(?m)^preflight_http_port$' '(?m)^docker compose --env-file "\$compose_env" -f "\$compose_file" pull$' "port validation must happen before pulling images"
Assert-RegexPrecedes $deployScript '(?m)^backup_existing_database$' '(?m)^docker compose --env-file "\$compose_env" -f "\$compose_file" up -d sqlserver redis rabbitmq qdrant searxng minio --wait$' "database backup must precede SQL Server reconciliation"
Assert-RegexPrecedes $deployScript '(?m)^\s*docker compose --env-file "\$compose_env" -f "\$compose_file" stop \$previously_running_services$' '(?m)^backup_existing_database$' "database backup must run after application quiescence"
Assert-Precedes $migrateScript 'repair_tenant_runtime_columns.sql' 'verify_content_render_tasks.sql' "repairs must run before verification"

Assert-Match $deployScript 'case "\$mssql_pid" in[\s\S]*Standard\|Enterprise' "deployment must restrict SQL Server editions to Standard or Enterprise"
Assert-NotMatch $deployScript 'stop web gateway api agentservice \|\| true' "application quiescing must not ignore stop failures"
Assert-Match $deployScript 'CURRENT_RELEASE_ENV_FILE' "deployment must retain the prior release for recovery"
Assert-Match $deployScript "trap 'recover_previous_application'" "deployment must recover the prior application after a post-quiesce failure"
Assert-Precedes $deployScript "trap 'recover_previous_application' EXIT" 'docker compose --env-file "$compose_env" -f "$compose_file" stop $previously_running_services' "deployment must arm application recovery before quiescing services"
Assert-Match $workflow 'deploy/production/install-release\.sh' "workflow must use the checked-in versioned release installer"
Assert-Match $workflow 'deploy/production/restore-verified-backup\.sh' "workflow must transfer the verified database recovery runner"
Assert-Match $installerScript 'restore-verified-backup\.sh' "release installer must retain the verified database recovery runner"
Assert-Match $deployScript 'restore-verified-backup\.sh' "deployment failure guidance must reference the verified database recovery runner"
Assert-Match $restoreScript 'RESTORE VERIFYONLY' "recovery runner must verify a database backup before restoring it"
Assert-Match $restoreScript 'CONFIRM_DATABASE_RESTORE' "recovery runner must require explicit restore confirmation"
Assert-Match $restoreScript 'acquire_release_lifecycle_lock' "recovery runner must serialize database restoration with deployment and rollback"
Assert-Match $restoreScript 'schema_recovery_marker' "verified recovery must resolve a schema-recovery requirement only after a matching backup restore"
Assert-Precedes $restoreScript 'RESTORE VERIFYONLY' 'stop agentservice api gateway web' "recovery runner must validate a backup before quiescing the application"
Assert-Match $installerScript 'current\.new' "release installer must promote the current release only after candidate deployment succeeds"
Assert-Match $installerScript 'A prior release-pointer promotion is incomplete' "installer must fail closed on incomplete pointer promotion"
Assert-Match $installerScript '\.schema-recovery-required' "installer must reject a deployment while a failed schema migration awaits recovery"
Assert-NotMatch $installerScript 'rm -f "\$release_root/previous\.new" "\$release_root/current\.new"' "installer must not erase evidence of a partial pointer promotion"
Assert-Precedes $installerScript '"$release_dir/deploy.sh"' 'ln -s "$release_dir" "$release_root/current.new"' "candidate smoke checks must precede current release promotion"
Assert-NotMatch $installerScript 'stop_failed_candidate' "installer must not stop a healthy migrated candidate when pointer promotion is incomplete"
Assert-Precedes $installerScript 'mv -Tf "$release_root/current.new" "$current_link"' 'mv -Tf "$release_root/previous.new" "$previous_link"' "installer must record the live candidate as current before updating previous"
Assert-NotMatch $installerScript 'mv -f .*effective\.env' "release installer must not promote the candidate environment before deployment succeeds"
Assert-Match $migrateScript 'Existing database has no migration history' "migration preflight must reject an unbaselined existing database"
Assert-Match $migrateScript 'migration_history_bootstrap_pending' "migration runner must resume an interrupted empty-ledger bootstrap"
Assert-Match $migrateScript 'highest_baseline_number' "migration runner must honor reviewed baseline markers"
Assert-Match $migrateScript 'IF NOT EXISTS \(SELECT 1 FROM dbo\.schema_migrations WHERE filename = N' "migration runner must recheck migration history inside the lock"
Assert-Match $migrateScript 'BEGIN TRANSACTION;' "production repairs must execute inside runner-owned transactions"
Assert-NotMatch $deployScript 'docker exec -e SQLCMDPASSWORD=' "deployment must not expose SQL passwords in Docker command-line arguments"
Assert-NotMatch $migrateScript 'docker exec -e SQLCMDPASSWORD=' "migration runner must not expose SQL passwords in Docker command-line arguments"
Assert-NotMatch $backupScript 'docker exec -e SQLCMDPASSWORD=' "backup runner must not expose SQL passwords in Docker command-line arguments"
Assert-Match $migrateScript 'database=\$1\s+user=\$2\s+shift 2' "migration runner must not forward database and username as sqlcmd positional arguments"
Assert-Match $deployScript 'schema_mutation_started' "deployment recovery must distinguish failures after schema mutation"
Assert-Match $deployScript 'The migration transaction did not commit' "deployment must clear recovery quarantine after a verified zero-commit migration"
Assert-Match $migrateScript ':r \$container_repair[\s\S]*schema_mutation_runs' "repair transactions must record a potential schema mutation before commit"
Assert-Match $deployScript 'running Clawbot %s container exists without a current release pointer' "deployment must refuse to migrate an unmanaged running Clawbot stack"
Assert-Match $deployScript 'candidate_application_started' "deployment must track whether a failed candidate created application containers"
Assert-Match $deployScript 'Stopping failed candidate application services.' "deployment must stop a failed candidate before preserving release pointers"
Assert-Precedes $deployScript 'candidate_application_started=true' 'up -d --wait --no-deps agentservice' "deployment must arm failed-candidate cleanup before starting application services"
Assert-Match $deployScript 'automatic application recovery is blocked' "deployment must not restart the prior application after schema mutation"
Assert-Match $installerScript "trap 'exit 1' HUP INT TERM" "installer signals must terminate the lock owner"
Assert-NotMatch $installerScript 'trap ''rmdir "\$lock_dir"'' EXIT HUP INT TERM' "installer must not release its lock while continuing after a signal"

Assert-Match $rollbackScript 'CURRENT_RELEASE_ENV_FILE' "rollback must merge previous application images with current infrastructure"
Assert-Match $rollbackScript 'PREVIOUS_RUNTIME_ENV_FILE' "rollback must restore the previous runtime configuration"
Assert-Match $rollbackScript 'current\.new' "rollback must atomically repoint the active release"
Assert-Match $rollbackScript "trap 'recover_current_application' EXIT" "rollback must recover the known-good current application when recreation fails"
Assert-Precedes $rollbackScript "trap 'recover_current_application' EXIT" 'up -d --wait --no-deps agentservice' "rollback must arm recovery before replacing application services"
Assert-Match $rollbackScript 'up -d --wait --no-deps agentservice' "rollback must recreate only the application services"
Assert-Match $rollbackScript 'smoke\.sh' "rollback must smoke-test the restored application before pointer promotion"
Assert-RegexPrecedes $rollbackScript 'CLAWBOT_PUBLIC_BASE_URL=.*smoke\.sh' 'ln -s "\$rollback_release_dir" "\$release_root/current\.new"' "rollback must not promote an un-smoked application release"
Assert-Match $rollbackScript 'A release-pointer promotion is incomplete' "rollback must reject partial release-pointer promotion"
Assert-Match $rollbackScript '\.schema-recovery-required' "rollback must reject an application rollback while schema recovery is required"
Assert-NotMatch $rollbackScript 'docker compose --env-file "\$PREVIOUS_RELEASE_ENV_FILE" -f "\$COMPOSE_FILE" (config|pull|up)' "rollback must not use the full previous environment directly"

Write-Host "Production deployment static contract passed."
