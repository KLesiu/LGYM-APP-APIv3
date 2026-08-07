# LgymApi.DataSeeder.csproj

- Purpose: deterministic bootstrap and data seeding executable.
- Contains: deterministic seeders, Infrastructure EF tooling, and bootstrap orchestration.
- Composition: uses the public `IdentityModule.AddIdentityModule()` facade for legacy password hashing while retaining the shared `AppDbContext` seeding path and the one migration stream.
- Stable role and claim IDs come from the entity-free public `IdentitySeedIds` contract; this executable does not use Identity internals.
- Rules: do not make API startup depend on this executable.
- Boundary: keep this as a console entrypoint, not a web host.
- Offline modes `--migrate-only` and `--prepare-hangfire` require `LGYM_MIGRATION_POSTGRES`; ordinary seed mode uses the same maintenance credential. The API runtime never consumes that environment variable.
- The tutorial RLS pilot requires this offline migration and Hangfire bootstrap before runtime startup. Its staging-only activation, diagnostics, and rollback procedure is [`tutorial-rls-pilot.md`](../docs/security/tutorial-rls-pilot.md).
