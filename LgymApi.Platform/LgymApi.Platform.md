# LgymApi.Platform.csproj

- Purpose: stable code boundary for shared platform services and contracts.
- Contains: the `ActorReference` contract-only typed-ID marker, neutral BuildingBlocks, mapping core and registration, serialization, UoW/transaction, background outbox/dispatcher, query pagination, Reference Data, and Platform-owned stage-only repositories/configurations. Mapping registration requires the exact API, Application, Platform, Identity, Training Planning, and Notifications assembly markers and rejects missing or duplicate assemblies. The established `LgymApi.Application.*` namespaces remain unchanged for compatibility.
- Rules: keep direct references limited to Domain and Resources. Its internal persistence context is friend-visible only to Infrastructure, UnitTests, and IntegrationTests.
- Boundary: do not add feature workflows, a DbContext, migrations, or Worker dependencies. `IPlatformPersistenceContext` exposes only the four Platform-owned sets and `Entry(CommandEnvelope)`; Infrastructure remains the sole context implementation. `PlatformModule.AddPlatformModule` is the only public Platform registration entry; its implementations are internal.
- EF coordination: `PlatformModelConfigurationRegistrar` provides the fixed Reference Data and reliability phase entry points. Infrastructure supplies the configurations without assembly scanning.
- `IActorRowSecurityScopeFactory` is the single Platform contract for owner services to begin an actor-bound `IUnitOfWorkTransaction` using `Id<ActorReference>` without provider dependencies.
- The staging-only tutorial RLS operating procedure is [`tutorial-rls-pilot.md`](../docs/security/tutorial-rls-pilot.md). The Platform contract remains the only approved path for transaction-local actor context.
