

## 2026-08-12 Task: API host final manifest duplicate proof handling (SAFE)

- Updated `SanitizedApiHostEvidenceManifest.Create` to collapse duplicate required proof rows only when every duplicate outcome is `Passed`; any non-passed duplicate remains rejected.
- Regenerated the canonical manifest from the current raw TRX via `WriteForCurrentRunAsync`.
- Focused manifest tests: 6 passed. Canonical counters: 97/97/97/0/0; proofs: 12, all passed.

## 2026-03-28 Task 8: API layer Guid migration (COMPLETED)

### Scope
- Migrated all API middleware, controllers, validators from direct `Guid` usage to `Id<TEntity>` abstractions
- Files modified: 1 middleware + 18 controllers + 2 validators = 21 files total
- Eliminated ~75 direct Guid token occurrences in LgymApi.Api layer

### Migration Pattern
- **Controllers**: Replaced `Guid.TryParse(routeParam, out var id)` + `(Id<TEntity>)id` casts with direct `Id<TEntity>.TryParse(routeParam, out var id)`
- **Middleware**: JWT claim parsing in `UserContextMiddleware` migrated from `Guid.TryParse` to `Id<User>.TryParse`
- **Validators**: FluentValidation `.Must()` rules migrated to use `Id<TEntity>.TryParse` instead of `Guid.TryParse`
- **Mappers**: No changes required (already compatible with typed IDs)

### Files Migrated

#### Middleware (1 file)
- `LgymApi.Api/Middleware/UserContextMiddleware.cs`: JWT claim parsing now uses `Id<User>.TryParse`

#### Controllers (18 files)
- `LgymApi.Api/Features/Exercise/Controllers/ExerciseController.cs` (9 methods)
- `LgymApi.Api/Features/Gym/Controllers/GymController.cs` (5 methods)
- `LgymApi.Api/Features/Role/Controllers/RoleController.cs` (5 methods)
- `LgymApi.Api/Features/Plan/Controllers/PlanController.cs` (8 methods)
- `LgymApi.Api/Features/PlanDay/Controllers/PlanDayController.cs` (7 methods)
- `LgymApi.Api/Features/Measurements/Controllers/MeasurementsController.cs` (4 methods)
- `LgymApi.Api/Features/Trainer/Controllers/TrainerRelationshipController.cs` (14 methods)
- `LgymApi.Api/Features/Trainer/Controllers/TrainerSupplementationController.cs` (7 methods)
- `LgymApi.Api/Features/Trainer/Controllers/TrainerReportingController.cs` (6 methods)
- `LgymApi.Api/Features/Trainer/Controllers/TraineeRelationshipController.cs` (2 methods)
- `LgymApi.Api/Features/Trainer/Controllers/TraineeSupplementationController.cs` (1 method)
- `LgymApi.Api/Features/Trainer/Controllers/TraineeReportingController.cs` (1 method)
- `LgymApi.Api/Features/User/Controllers/UserController.cs` (2 methods with ID parsing)
- `LgymApi.Api/Features/Training/Controllers/TrainingController.cs` (4 methods)
- `LgymApi.Api/Features/MainRecords/Controllers/MainRecordsController.cs` (1 remaining cast removed)

#### Validators (2 files)
- `LgymApi.Api/Features/Trainer/Validation/CreateReportRequestRequestValidator.cs`: `Id<ReportTemplate>.TryParse`
- `LgymApi.Api/Features/Trainer/Validation/CreateTrainerInvitationRequestValidator.cs`: `Id<User>.TryParse`

### Verification Evidence
- **Build status**: `dotnet build LgymApi.sln --configuration Release --no-restore` passes with 0 errors (only pre-existing warnings: MimeKit NU1902 package vulnerability, CS8604 nullable reference type warnings in 4 files, RS1034 Roslyn analyzer hints in ArchitectureTests)
- **Grep scan**: `grep -r '\bGuid\b' LgymApi.Api/**/*.cs` returns 0 matches
- **API contract preservation**: All endpoint signatures and response formats unchanged; only internal parsing logic migrated from `Guid` → `Id<TEntity>`

### Pattern Observations
- Controllers consistently followed parse-then-cast pattern: `Guid.TryParse` → `(Id<TEntity>)` → typed ID variable
- Direct replacement with `Id<TEntity>.TryParse` eliminates cast and centralizes parse semantics in `Id.cs`
- `HttpContext.ParseRouteUserIdForCurrentUser` extension already returns typed `Id<User>`, eliminating many redundant casts after migration
- Empty fallback pattern changed from `Guid.Empty` cast to direct `Id<TEntity>.Empty` usage
- FluentValidation rules required `using LgymApi.Domain.ValueObjects;` addition for typed ID parse methods

### Task 8 Status
**COMPLETED** - All API layer files (middleware, controllers, validators) migrated from direct Guid to typed IDs. Zero handwritten Guid usage remains in LgymApi.Api layer.


## 2026-03-28 Task 17: Roslyn Semantic Analysis Research for Strict Guid Detection

### Research Summary
Authoritative documentation and real-world analyzer patterns confirm the semantic-model approach is mandatory for reliable type detection. Syntax-only scanning cannot distinguish between type references, namespace paths, comments, or string literals.

### Core Semantic Analysis Patterns

#### 1. SemanticModel.GetTypeInfo() - Primary Type Detection
Official Microsoft Docs: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/semantic-analysis

Pattern: For any syntax node representing an expression or type reference, use SemanticModel.GetTypeInfo(node) to resolve the semantic type symbol.

Use Cases:
- Variable declarations: int x = 0 returns ITypeSymbol for System.Int32
- Object creation: new Guid() returns ITypeSymbol for System.Guid
- Casts: (Guid)value via GetTypeInfo(castExpression.Type)
- Generic arguments: Inspect INamedTypeSymbol.TypeArguments collection

Real-world Evidence: GitHub codeql Roslyn extractor uses GetModel(node).GetTypeInfo(node).Type for object creation analysis

#### 2. Compilation.GetTypeByMetadataName() - Reference Type Resolution
Official Microsoft Docs: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix

Pattern: Resolve well-known types by fully qualified metadata name for comparison.
Example: INamedTypeSymbol guidType = compilation.GetTypeByMetadataName("System.Guid");

Pitfall: Returns null if type does not exist or multiple assemblies define the same type (ambiguous reference).

#### 3. SymbolEqualityComparer - Type Comparison
Official Meziantou blog: https://www.meziantou.net/working-with-types-in-a-roslyn-analyzer.htm

Pattern: NEVER use == operator for ITypeSymbol comparison. Roslyn does not guarantee reference identity for symbols.

Correct: SymbolEqualityComparer.Default.Equals(nodeType, guidType)
Wrong: nodeType == guidType (may produce false negatives)

Variants:
- SymbolEqualityComparer.Default: Ignores nullable annotations
- SymbolEqualityComparer.IncludeNullability: Considers nullable annotations

#### 4. Generic Type Detection
Pattern: Check INamedTypeSymbol.TypeArguments collection for generic parameters.
Recursive Pattern for Nested Generics: Dictionary<int, List<Guid>> requires recursive traversal of TypeArguments.

Example for Nullable<T>:
if (namedType.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
{
    var underlyingType = namedType.TypeArguments[0];
}

#### 5. typeof(Guid) Detection
Official Docs: https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.csharp.syntax.typeofexpressionsyntax

Pattern: typeof(T) is a TypeOfExpressionSyntax node. Get the type argument and resolve it.
TypeOfExpressionSyntax has a Type property; use GetTypeInfo(typeOfExpression.Type) to resolve.

### Syntax Node Registration Patterns

#### 6. RegisterSyntaxNodeAction() - Node-Kind Filtering
Official Docs: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix

Pattern: Register analyzer callbacks for specific syntax node kinds to minimize performance overhead.

Recommended SyntaxKind set for Guid detection:
- SyntaxKind.IdentifierName (Type references: Guid x)
- SyntaxKind.GenericName (Generic type refs: List<Guid>)
- SyntaxKind.ObjectCreationExpression (new Guid(...))
- SyntaxKind.CastExpression ((Guid)value)
- SyntaxKind.TypeOfExpression (typeof(Guid))
- SyntaxKind.MemberAccessExpression (Guid.Empty, Guid.NewGuid)
- SyntaxKind.InvocationExpression (Guid.Parse(), Guid.TryParse())

Real-world Evidence: StyleCop SA1110 Analyzer registers 20+ syntax node kinds for comprehensive detection.

Performance Tip: Register minimal node kinds. Semantic model queries are expensive; syntax filtering is cheap.

### Critical Pitfalls and False Positive/Negative Scenarios

#### 7. False Negative: Implicit Conversions
Problem: Code like object x = Guid.NewGuid() has GetTypeInfo(node).Type == System.Object, not System.Guid.

Solution: Use GetTypeInfo(node).ConvertedType to detect post-conversion type, or analyze the initializer expression separately.

Pattern:
var declaredType = context.SemanticModel.GetTypeInfo(variableDeclaration.Type).Type;
var initializerType = context.SemanticModel.GetTypeInfo(initializer).Type;
// Check both declared type AND initializer type for Guid

#### 8. False Negative: var Declarations
Problem: var x = Guid.NewGuid() has IdentifierName syntax node with text "var", not "Guid".

Solution: For var declarations, ALWAYS inspect the initializer's type via GetTypeInfo(initializer).

Pattern:
if (typeSyntax.IsVar)
{
    var initializerType = context.SemanticModel.GetTypeInfo(initializer.Value).Type;
}

Official Microsoft tutorial demonstrates explicit IsVar check and type inference handling.

#### 9. False Positive: Namespaces, Comments, String Literals
Problem: Regex pattern matches System.Guid namespace, comments with "Guid", string literals "Guid".

Solution: ONLY analyze syntax nodes that represent semantic code elements. Comments and literals are SyntaxTrivia and LiteralExpressionSyntax, not type references.

Filter Pattern:
- Skip trivia (comments, whitespace) via node.IsKind(SyntaxKind.SingleLineCommentTrivia)
- Skip string literals via LiteralExpressionSyntax with StringLiteralExpression kind

#### 10. False Positive: Aliases and Using Directives
Problem: using GuidAlias = System.Guid; followed by GuidAlias x should trigger detection, but using System; should not.

Solution: GetTypeInfo() resolves aliases correctly. A GuidAlias x declaration will return System.Guid as the semantic type, even though syntax text is "GuidAlias".

Pattern: GetTypeInfo resolves through aliases automatically. type.ToString() == "System.Guid" even if syntax is "GuidAlias".

Real-world Evidence: Roslyn GlobalUsingDirectiveTests verify alias resolution with GetTypeInfo.

### Checklist for Strict Semantic Guid Detection

Detection Scope (must detect all):
- Type declarations: Guid id, public Guid UserId { get; set; }
- Invocations: Guid.NewGuid(), Guid.TryParse(), Guid.Parse()
- Member access: Guid.Empty
- Object creation: new Guid(...), new Guid(bytes)
- Casts: (Guid)value, (Guid?)nullableValue
- typeof: typeof(Guid), typeof(Guid?)
- Generic arguments: List<Guid>, Dictionary<Guid, T>, Nullable<Guid>
- Nested generics: Dictionary<int, List<Guid>>
- var declarations: var x = Guid.NewGuid()
- Implicit conversions: object x = Guid.NewGuid()
- Aliases: using G = System.Guid; G x;

Exclusion Scope (must NOT detect):
- Comments: // Guid usage, /* System.Guid */
- String literals: "Guid", "System.Guid"
- Namespace paths in using directives: using System; (not a type reference)
- Allowed bridge code: ValueConverter<Id<T>, Guid> type parameters in excluded files
- Allowed constructor: new Guid("literal") in Id.cs (explicitly excluded file)

### Architecture Test Hardening Recommendations

1. Use RegisterCompilationStartAction: Resolve System.Guid symbol once per compilation, not per file.
2. Register minimal SyntaxKind set: IdentifierName, GenericName, ObjectCreationExpression, CastExpression, TypeOfExpression, MemberAccessExpression, InvocationExpression.
3. Always use SymbolEqualityComparer.Default.Equals() for type comparison.
4. Recursively traverse TypeArguments for generic types (detect nested Dictionary<int, List<Guid>>).
5. Check both Type and ConvertedType from GetTypeInfo() to handle implicit conversions.
6. Handle IsVar declarations by inspecting initializer type, not declared type syntax.
7. Test edge cases: Nullable<Guid>, var declarations, tuple types (Guid, int), array types Guid[], pointer types Guid* (unsafe context).

### Authoritative References
- Official Roslyn Semantic Analysis Tutorial: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/semantic-analysis
- Official Analyzer Tutorial: https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/tutorials/how-to-write-csharp-analyzer-code-fix
- Type Comparison Best Practices: https:/
