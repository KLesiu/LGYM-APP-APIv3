# ADR-006: LGYM evolves as a modular monolith

## Status
Accepted

## Context

LGYM already has a layered runtime and a single production deployable backed by one PostgreSQL database and one production `AppDbContext`.

Issue #375 captured the historical inventory. Issue #387 then extracted Platform, Identity, Training Planning, and Notifications into stable assemblies without changing the deployment shape.

## Source precedence

- `#311` is the constraint authority.
- `#375` is the historical baseline and inventory source. Its graph capture remains unchanged.
- `#387` is the completed extraction that established the current 18-project, 90-edge topology.
- `docs/ARCHITECTURE.md` is the integration target and reader guide.
- This ADR records the decision layer only and must stay aligned with those sources.

## Decision

LGYM is a modular monolith.

The current system stays as one deployable, one PostgreSQL database, one production `AppDbContext`, and one migration stream.

The extraction establishes four stable module assemblies while retaining the existing runtime shape. Module-owned repositories, EF configurations, and providers use internal persistence bridges over the shared context.

## Rationale

1. The #375 baseline already shows stable feature clusters that can be governed as modules.
2. #311 requires the modular-monolith direction to preserve the current runtime constraints while boundaries are defined.
3. Keeping the deployable, database, `AppDbContext`, and migration stream intact avoids inventing topology work that does not belong in this issue.
4. A modular-monolith contract lets the later module docs define ownership, dependency direction, and communication rules without changing the layered runtime.
5. The ADR keeps `docs/ARCHITECTURE.md` and the module docs aligned so the current system description stays consistent across the repo.

## Consequences

1. Durable docs must stay consistent with the eight-module catalog, the ownership map, the dependency policy matrix, and the current graph.
2. Ownership rules remain one-owner-per-artifact, with no shared write ownership hidden behind the one production `AppDbContext`.
3. The current layered runtime remains the implementation baseline. The extraction does not authorize a second deployment, database, context, schema, or migration stream.
4. The single migration stream remains the only migration history for the current production system.
5. `docs/ARCHITECTURE.md` can point at this ADR and the companion module docs as the durable modular-monolith references.

## Follow-up

1. Keep the module context and ownership maps aligned with the extracted assemblies and their public contracts.
2. Keep the project-reference graph at its guarded 18-project, 90-edge manifest.
3. Keep `docs/ARCHITECTURE.md` and project documents aligned with the finalized modular-monolith docs.

## Links

- `docs/modular-monolith/issue-376-module-context-map.md`
- `docs/modular-monolith/issue-376-ownership-map.md`
