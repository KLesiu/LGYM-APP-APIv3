# Tutorial RLS Pilot

## Status and boundary

This is a staging-only pilot. It protects only `public."UserTutorialProgresses"` and `public."UserTutorialStepProgresses"`. The ordinary migration creates eight policies, four per table for `SELECT`, `INSERT`, `UPDATE`, and `DELETE`, but leaves row-level security disabled.

RLS supplements Application authorization. It does not replace authenticated actor checks, use-case authorization, or ownership predicates. It never approves itself for Production.

Production configuration must keep both protected-table expectations at `RowSecurityEnabled: false` and `RowSecurityForced: false` until a separately recorded go/no-go approves a coordinated database and configuration change before traffic is admitted.

## Roles and actor scope

Use two distinct PostgreSQL roles. The maintenance role owns the database and the two protected tables, has `BYPASSRLS`, and is used only outside the API for provisioning, EF migration, Hangfire preparation, activation, deactivation, and recovery. The runtime role has `NOBYPASSRLS`, `NOINHERIT`, required DML and sequence grants, and no membership path to the maintenance role. The API uses only the runtime database setting. Do not put `LGYM_MIGRATION_POSTGRES` in the API process.

Every tutorial operation obtains an actor scope before its first tutorial repository access. The scope borrows an active caller-owned transaction or opens and owns a new unit-of-work transaction. Infrastructure then runs the parameterized command `SELECT set_config('lgym.account_id', @actorId, true)` on that transaction's active Npgsql connection. The `true` argument makes the setting transaction-local, so it expires when the transaction ends and cannot leak through connection pooling. Reads and no-op paths dispose without commit or rollback. Mutations save once; a borrowed scope leaves completion to its caller, while an owned scope commits once. Setup failures dispose only transactions created by the actor scope and do not clear existing EF tracking.

Do not use session-global actor state or multiplexing. Do not issue actor-setting SQL outside the Platform row-security scope.

## Provision and bootstrap

Run every command below from an operator workstation or deployment job with a disposable isolated PostgreSQL lease. Use secret-managed operator access. Do not save sensitive access details in shell history, tracked files, evidence, or this runbook.

1. Connect as an operator-admin role and provision the separate roles. The script is idempotent and checks that the runtime role cannot assume the maintenance role.

```powershell
psql -X -v ON_ERROR_STOP=1 `
  -v database_name=<database_name> `
  -v database_environment=Staging `
  -v maintenance_role=<maintenance_role> `
  -v runtime_role=<runtime_role> `
  -f deploy/postgres/provision-rls-pilot-roles.sql
```

`database_environment` must be `Development`, `Staging`, or `Production` and must describe the database being provisioned. Provisioning persists the normalized value as the database-level `lgym.deployment_environment` setting. Activation and deactivation read that independent marker from `pg_db_role_setting` for the current database and reject a mismatched `target_environment`; changing only the CLI argument cannot turn a Production database into a Staging target.

2. In the offline deployment environment only, provide `LGYM_MIGRATION_POSTGRES` through secret injection and run the EF and Hangfire bootstrap. This is the only supported schema-preparation path. The API never migrates or prepares Hangfire in Staging or Production.

```powershell
pwsh -NoProfile -File scripts/migrate-db.ps1
```

3. Confirm the startup configuration uses the runtime role and disables multiplexing. Confirm `PostgreSqlRuntime` names only the two tutorial tables, retains all eight policy contracts including command, exact roles, permissiveness, `USING`, and `WITH CHECK`, and still expects both RLS flags to be `false` before activation. Start the API only after the offline work completes. Startup rejects pending migrations, the wrong database or runtime role, elevated membership, superuser or `BYPASSRLS`, missing Hangfire schema usage or any required table/sequence grant, table ownership by the runtime role, multiplexing, and a policy semantic or RLS state that differs from configuration.

## Activate in Staging

Activation is manual and staging-only. The script starts one transaction, takes an advisory transaction lock, verifies the stored database environment, target database, maintenance connection, role properties, table ownership, and the exact eight-policy semantic contract, then enables and forces RLS on both tables together. It rejects `Production`, a missing or mismatched database marker, altered policy roles or permissiveness, and altered `USING` or `WITH CHECK` predicates.

Before activation, deploy configuration that expects `RowSecurityEnabled: true` and `RowSecurityForced: true` for both protected tables, but do not restart traffic yet. Then run:

```powershell
psql -X -v ON_ERROR_STOP=1 `
  -v database_name=<database_name> `
  -v target_environment=Staging `
  -v maintenance_role=<maintenance_role> `
  -v runtime_role=<runtime_role> `
  -f deploy/postgres/activate-tutorial-row-security.sql
```

Run the API startup validation before admitting traffic. The database state and `PostgreSqlRuntime` expectation must agree. Re-running activation is safe after a successful or interrupted attempt because the database transaction and advisory lock prevent a partial two-table state.

## Validate and observe

Use the isolated PostgreSQL runner to validate the tracked activation behavior and runtime containment. It creates and removes its own lease, generated roles, and test data.

```powershell
pwsh -NoProfile -File scripts/run-postgresql-integration-tests.ps1 `
  -TestFilter "FullyQualifiedName~PostgreSqlTutorialRowSecurityTests"
```

Expected evidence includes actor A and B parent/child isolation, zero visible rows and zero writes for missing or malformed context, rejected foreign writes, no actor value after a pooled connection returns, denied runtime `SET ROLE` and protected-table DDL, denied runtime Hangfire schema preparation, and a working runtime-role tutorial API flow.

During staging, monitor the API and PostgreSQL diagnostics for unexpected RLS denials or missing actor-context events. Treat either as an incident: stop further rollout, preserve redacted timestamps and request correlation data, verify the scope begins before tutorial access, and use the rollback below if service is affected. Do not log actor values, sensitive access details, or tutorial data.

## Deactivate and break-glass rollback

Use deactivation for a controlled staging rollback or break-glass recovery. It validates the same database, environment, maintenance role, role properties, and table ownership, takes the same advisory lock, and clears `FORCE` and `ENABLE` for both tables without deleting policies or data.

1. Stop or drain affected API traffic.
2. Change the deployed runtime configuration to expect `RowSecurityEnabled: false` and `RowSecurityForced: false` for both tables.
3. Run the paired script through the maintenance role.

```powershell
psql -X -v ON_ERROR_STOP=1 `
  -v database_name=<database_name> `
  -v target_environment=Staging `
  -v maintenance_role=<maintenance_role> `
  -v runtime_role=<runtime_role> `
  -f deploy/postgres/deactivate-tutorial-row-security.sql
```

4. Run startup validation with the disabled expectation before restoring traffic. Verify both tables report RLS disabled and not forced. Native Application authorization remains active throughout this rollback.

For a disaster recovery restore, restore into an isolated lease first, rerun offline migration and Hangfire preparation with the maintenance role, leave RLS disabled, and validate the runtime role before any staging traffic. Rehearse this path before exit approval.

## Staging exit and Production gate

The staging checklist starts incomplete. Automated tests alone do not satisfy it. Record redacted evidence for every item before a separate Production go/no-go:

- Full PostgreSQL suite green.
- Zero unexpected RLS denials or context-missing events.
- Verified connection-pool isolation.
- Migration and rollback rehearsal.
- DataSeeder rehearsal.
- Backup and restore rehearsal.
- Seven-day soak.

Production remains disabled until a separate go-no-go approves the evidence. That decision must change the database state and the `PostgreSqlRuntime` configuration together before traffic. The production rollback is the same paired deactivation: drain traffic, return both configuration flags to `false`, run `deactivate-tutorial-row-security.sql` as the maintenance role with `target_environment=Production`, validate startup, then restore traffic. The activation script intentionally refuses Production, so any future approval needs a separately reviewed production procedure. It is not authorized by this pilot.
