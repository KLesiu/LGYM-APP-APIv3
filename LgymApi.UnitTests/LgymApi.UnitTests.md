# LgymApi.UnitTests.csproj

- Purpose: focused unit tests.
- Contains: unit coverage for service, domain, application, mapping, API, and infrastructure units.
- Rules: use NUnit, FluentAssertions, and NSubstitute; prefer mocks for touched collaborators instead of new hand-written fake interfaces.
- Rules: use shared helpers from `LgymApi.TestUtils` when they already fit the test.
- Rules: prefer `LgymApi.Resources` accessors over hardcoded translated/user-facing strings when the resource value is the source of truth.
- Boundary: keep tests isolated and fast.
- EF bootstrap coverage constructs `AppDbContextFactory` without host DI, verifies the canonical 48-entity Npgsql model and Infrastructure migration snapshot, and preserves Testing startup migration suppression versus non-Testing migration attempts.
- Composition guards assert one canonical registry/dispatcher/password adapter, one scoped AppConfig and enum service, one singleton converter per supported unit family, Notifications FCM ownership, environment-selected Worker push scheduling, and exact 15-row/16-handler startup validation.
- API adapter registration coverage requires the exact 25 scoped Application and 3 scoped Notifications API-adapter contracts to be unique, resolve through host composition, retain internal implementations, and remain distinct from the three Notifications integration adapters.
- Notification hub and publisher unit coverage exercises active session registration and direct validated-connection delivery; publisher tests do not rely on user-group delivery alone.
- `RuntimeCompositionValidationTests` builds both Testing and non-Testing ordered host graphs with `ValidateOnBuild` and `ValidateScopes`, resolves every closed module contract and all 36 controllers without invoking providers, and rejects duplicate descriptors or scoped dependencies captured by singletons.
- Coaching coverage exercises focused use cases and ports, persistence staging without repository commits, the Worker invitation-email adapter mapping, and exact-one port registration. Run the suite with `dotnet test LgymApi.UnitTests/LgymApi.UnitTests.csproj --configuration Release --no-build` after a Release build.
- `ActorRowSecurityScopeTests` cover empty actors, InMemory no-op behavior, fail-closed relational setup, cancellation, and preservation of pre-existing tracked entries.
