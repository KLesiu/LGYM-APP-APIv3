# LgymApi.UnitTests.csproj

- Purpose: focused unit tests.
- Contains: unit coverage for service, domain, application, mapping, API, and infrastructure units.
- Rules: use NUnit, FluentAssertions, and NSubstitute; prefer mocks for touched collaborators instead of new hand-written fake interfaces.
- Rules: use shared helpers from `LgymApi.TestUtils` when they already fit the test.
- Rules: prefer `LgymApi.Resources` accessors over hardcoded translated/user-facing strings when the resource value is the source of truth.
- Boundary: keep tests isolated and fast.
- EF bootstrap coverage constructs `AppDbContextFactory` without host DI, verifies the canonical 48-entity Npgsql model and Infrastructure migration snapshot, and preserves Testing startup migration suppression versus non-Testing migration attempts.
- Composition guards assert one canonical registry/dispatcher/password adapter, one scoped AppConfig and enum service, one singleton converter per supported unit family, Notifications FCM ownership, environment-selected Worker push scheduling, and exact 15-row/16-handler startup validation.
- Coaching coverage exercises focused use cases and ports, persistence staging without repository commits, the Worker invitation-email adapter mapping, and exact-one port registration. Run the suite with `dotnet test LgymApi.UnitTests/LgymApi.UnitTests.csproj --configuration Release --no-build` after a Release build.
