# LgymApi.TestUtils.csproj

- Purpose: shared test builders, fakes, fixtures, and setup helpers.
- Contains: reusable test utilities referenced by test projects.
- Rules: centralize reusable fakes/builders here when a shared stateful double is useful, but prefer mocks in individual tests.
- Boundary: keep it as shared test support, not a test project itself.
- `TestServiceProviderFactory` executes host-supplied public composition callbacks in the production order: canonical mappings, Platform, Identity, Training Planning, Notifications, remaining Application, Infrastructure, Application API adapters, Notifications API adapters, then optional Worker. Test replacements run only after that sequence; an explicit pre-module callback exists solely for ordering-negative tests.
- `FakeUserSessionStore` implements the Identity-owned session-store contract through the direct Identity reference. `TestDataFactory` and `TestEmailSender` justify the Infrastructure and Common edges. `AddApplicationAndWorkerServicesForTesting` provides an explicit public test-composition step for the remaining Application facade and Worker no-op scheduling; UnitTests owns its Platform UoW fakes.
