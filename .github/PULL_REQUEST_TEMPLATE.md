# Pull Request

## Summary

Describe the change, its user or operational impact, and any compatibility considerations.

## Testing

List the tests, checks, or manual verification performed. Note anything not run and why.

## Conditional Module Boundary Checklist

For changes that affect a module boundary, complete this checklist with the [module contribution guide](../docs/MODULE_CONTRIBUTION_GUIDE.md). Mark items that do not apply as N/A and give a rationale, but retain every checklist item and its marker.

<!-- module-contribution-checklist:start -->
- [ ] Confirm the canonical owner for the changed capability. <!-- module-contribution:owner -->
- [ ] Confirm dependencies are allowed by the project reference graph. <!-- module-contribution:dependencies -->
- [ ] Keep the public surface focused and expose only needed contracts. <!-- module-contribution:public-surface -->
- [ ] Place applicable code in the appropriate vertical slice. <!-- module-contribution:vertical-slice -->
- [ ] Preserve the single physical persistence topology. <!-- module-contribution:persistence-topology -->
- [ ] Keep repositories stage-only and justify UoW saves or transactions. <!-- module-contribution:uow-transactions -->
- [ ] Confirm outbox, Worker, and consumer ownership for applicable messaging. <!-- module-contribution:messaging -->
- [ ] Preserve endpoint-specific API compatibility and localized user-facing text. <!-- module-contribution:api-compatibility-localization -->
- [ ] Use registered mapping profiles for cross-layer model transformations. <!-- module-contribution:mapping -->
- [ ] Add or update applicable architecture tests. <!-- module-contribution:architecture-tests -->
- [ ] Update applicable contributor and project documentation. <!-- module-contribution:documentation -->
- [ ] Review project graph impact and include topology evidence when applicable. <!-- module-contribution:project-topology-evidence -->
<!-- module-contribution-checklist:end -->
