# Issue #375: Historical Project-Reference Graph

This historical capture records the solution-state graph at issue #375. It uses tracked `.csproj` `ProjectReference` items and does not infer dependencies from namespaces, folders, or runtime behavior. The current graph is [issue-380-project-reference-graph.md](issue-380-project-reference-graph.md).

## Solution projects

1. `LgymApi.Api`
2. `LgymApi.Domain`
3. `LgymApi.Application`
4. `LgymApi.Infrastructure`
5. `LgymApi.Resources`
6. `LgymApi.Resources.Generator`
7. `LgymApi.IntegrationTests`
8. `LgymApi.UnitTests`
9. `LgymApi.ArchitectureTests`
10. `LgymApi.DataSeeder`
11. `LgymApi.DataSeeder.Tests`
12. `LgymApi.BackgroundWorker`
13. `LgymApi.BackgroundWorker.Common`
14. `LgymApi.TestUtils`

## Mermaid graph

```mermaid
graph TD
  Api[LgymApi.Api]
  Application[LgymApi.Application]
  ArchitectureTests[LgymApi.ArchitectureTests]
  BackgroundWorker[LgymApi.BackgroundWorker]
  BackgroundWorkerCommon[LgymApi.BackgroundWorker.Common]
  DataSeeder[LgymApi.DataSeeder]
  DataSeederTests[LgymApi.DataSeeder.Tests]
  Domain[LgymApi.Domain]
  Infrastructure[LgymApi.Infrastructure]
  IntegrationTests[LgymApi.IntegrationTests]
  Resources[LgymApi.Resources]
  ResourcesGenerator[LgymApi.Resources.Generator]
  TestUtils[LgymApi.TestUtils]
  UnitTests[LgymApi.UnitTests]

  Api --> Application
  Api --> BackgroundWorker
  Api --> Domain
  Api --> Infrastructure
  Api --> Resources
  Application --> BackgroundWorkerCommon
  Application --> Domain
  Application --> Resources
  ArchitectureTests --> Api
  ArchitectureTests --> Application
  BackgroundWorker --> Application
  BackgroundWorker --> BackgroundWorkerCommon
  BackgroundWorker --> Infrastructure
  BackgroundWorkerCommon --> Domain
  DataSeeder --> Infrastructure
  DataSeederTests --> DataSeeder
  DataSeederTests --> Infrastructure
  Domain --> Resources
  Infrastructure --> Application
  Infrastructure --> BackgroundWorkerCommon
  Infrastructure --> Domain
  IntegrationTests --> Api
  IntegrationTests --> Infrastructure
  IntegrationTests --> TestUtils
  Resources --> ResourcesGenerator
  TestUtils --> Application
  TestUtils --> BackgroundWorker
  TestUtils --> BackgroundWorkerCommon
  TestUtils --> Domain
  TestUtils --> Infrastructure
  UnitTests --> Api
  UnitTests --> Application
  UnitTests --> Infrastructure
  UnitTests --> TestUtils
```

## Readable edge list

Sources are listed in solution order; targets are sorted alphabetically within each source.

- `LgymApi.Api` → `LgymApi.Application`, `LgymApi.BackgroundWorker`, `LgymApi.Domain`, `LgymApi.Infrastructure`, `LgymApi.Resources`
- `LgymApi.Application` → `LgymApi.BackgroundWorker.Common`, `LgymApi.Domain`, `LgymApi.Resources`
- `LgymApi.ArchitectureTests` → `LgymApi.Api`, `LgymApi.Application`
- `LgymApi.BackgroundWorker` → `LgymApi.Application`, `LgymApi.BackgroundWorker.Common`, `LgymApi.Infrastructure`
- `LgymApi.BackgroundWorker.Common` → `LgymApi.Domain`
- `LgymApi.DataSeeder` → `LgymApi.Infrastructure`
- `LgymApi.DataSeeder.Tests` → `LgymApi.DataSeeder`, `LgymApi.Infrastructure`
- `LgymApi.Domain` → `LgymApi.Resources`
- `LgymApi.Infrastructure` → `LgymApi.Application`, `LgymApi.BackgroundWorker.Common`, `LgymApi.Domain`
- `LgymApi.IntegrationTests` → `LgymApi.Api`, `LgymApi.Infrastructure`, `LgymApi.TestUtils`
- `LgymApi.Resources` → `LgymApi.Resources.Generator`
- `LgymApi.Resources.Generator` → _no outgoing project references_
- `LgymApi.TestUtils` → `LgymApi.Application`, `LgymApi.BackgroundWorker`, `LgymApi.BackgroundWorker.Common`, `LgymApi.Domain`, `LgymApi.Infrastructure`
- `LgymApi.UnitTests` → `LgymApi.Api`, `LgymApi.Application`, `LgymApi.Infrastructure`, `LgymApi.TestUtils`
