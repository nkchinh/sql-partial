# Architecture — SqlPartial.Generator

This document describes the internal architecture of the generator for maintainers and contributors.

---

## Overview

The generator is a **Roslyn Incremental Source Generator** (`IIncrementalGenerator`). The entire code generation process occurs within the Roslyn compiler pipeline — no `.cs` files are written to disk during a standard build (they exist as virtual source in the Language Server's memory, or in `obj/` when `EmitCompilerGeneratedFiles=true`).

---

## Directory Structure

```
SqlPartial.Generator/
├── SqlPartialGenerator.cs          Entry point — pipeline declaration
├── Core/
│   ├── ConfigParser.cs             Reads MSBuild properties → GeneratorConfig
│   ├── FilePathParser.cs           Parses file paths → (ns, className, queryName, providerName)
│   ├── SqlContentCleaner.cs        Strips comments, exclude blocks, escapes verbatim strings
│   └── SourceBuilder.cs            Generates C# source from models
├── Models/
│   ├── GeneratorConfig.cs          Project-level configuration (providers, namespaces...)
│   ├── SqlProvider.cs              A DBMS provider (extension + display name)
│   ├── SqlFile.cs                  A parsed file with its associated provider
│   └── SqlQueryGroup.cs            Group of SqlFiles with same (ns, className, queryName)
├── Analyzers/
│   ├── ManualInstantiationAnalyzer.cs  Validates 'new SqlStrings' coverage
│   └── SqlMethodAnalyzer.cs            Validates [Sql] parameter usage (SQLPG030)
└── build/ (implicit in NuGet)
    └── NkChinh.SqlPartial.Generator.targets   MSBuild targets packaged in NuGet
```

---

## Generator Pipeline

The generator uses two distinct incremental pipelines:

### 1. File-based Pipeline (Properties & SQL Files)

```
AdditionalTextsProvider
    │ filter: SourceItemType=SqlPartial (Any extension!)
    │ select: read content, clean
    ▼
(FilePath, Content)
    │ combine with GeneratorConfig + ProjectDir
    │ select: FilePathParser.TryParse → SqlFile
    ▼
ImmutableArray<SqlFile>  ← Collect()
    │ SelectMany: group by (Namespace, ClassName, QueryName)
    ▼
ImmutableArray<SqlQueryGroup>  ← Collect()
    │ combine with GeneratorConfig
    │ SelectMany: group by (Namespace, ClassName)
    ▼
classBatches → RegisterSourceOutput → SourceBuilder.BuildPartialClass()

AnalyzerConfigOptionsProvider
    │ select: ConfigParser.Parse → GeneratorConfig
    ▼
GeneratorConfig → RegisterSourceOutput → SourceBuilder.BuildSqlStringsStruct()
```

### 2. Semantic Pipeline (Attributes & Overloads)

```
SyntaxProvider.ForAttributeWithMetadataName("SqlPartial.SqlAttribute")
    │ transform: ctx.TargetSymbol.ContainingSymbol as IMethodSymbol
    ▼
ImmutableArray<IMethodSymbol> ← Collect()
    │ combine with GeneratorConfig
    │ Group by ContainingType
    ▼
RegisterSourceOutput → SourceBuilder.BuildOverloads()

SyntaxProvider.ForAttributeWithMetadataName("SqlPartial.SqlPartialAttribute")
    │ transform: ctx.TargetSymbol as INamedTypeSymbol
    │ select: extract AccessModifier value
    ▼
ImmutableArray<(Namespace, ClassName, Modifier)> ← Collect()
    │ used to customize BuildPartialClass()
```

### Why use `Collect()` multiple times?

The first `Collect()` gathers all `SqlFile` objects so they can be grouped by query. Without this, each `SqlFile` would be processed independently, and it would be impossible to know which files belong to the same query.

The second `Collect()` gathers all `SqlQueryGroup` objects to group them by class, allowing for a single `.g.cs` file to be generated per class instead of one file per query.

**Trade-off**: Whenever any tracked file changes, the entire pipeline after `Collect()` re-executes. This is a necessary trade-off because grouping is a global operation. For the typical number of files in a project, this cost is negligible.

---

## Models

### `GeneratorConfig`

Parsed from MSBuild global properties once. It remains stable between file edits — Roslyn caches it and does not re-parse unless the project file changes.

| Property | MSBuild source | Description |
|---|---|---|
| `RootNamespace` | `build_property.RootNamespace` | The project's root namespace |
| `Providers` | `build_property.SqlPartialProviders` | List of extensions mapped to provider names |
| `SqlStringsNamespace` | `build_property.SqlPartialStringsNamespace` | Namespace for the `SqlStrings` struct |
| `ExternalSqlStringsType` | `build_property.SqlPartialStringsType` | Use a struct from another assembly |
| `NullableEnabled` | `build_property.Nullable` | Whether to emit `#nullable enable` |
| `EmitSharedNamespace` | `build_property.SqlPartialEmitSharedNamespace` | Emit public shared types in this namespace |
| `UseSharedNamespace` | `build_property.SqlPartialUseSharedNamespace` | Import shared types from this namespace |

### `SqlFile`

Represents a parsed SQL file. It implements `IEquatable<SqlFile>` — mandatory for Roslyn incremental caching to work correctly. It stores the resolved `ProviderName` (e.g., `"PostgreSql"` or `"Default"`).

### `SqlQueryGroup`

Groups all `SqlFile` objects with the same `(Namespace, ClassName, QueryName)`. It contains an `ImmutableDictionary<string, string>` mapping **ProviderName** → SQL content.

`GetContent(providerName)` method:
```
providerName exists in dict → returns content for that provider
providerName not found      → falls back to "Default"
"Default" missing           → returns string.Empty
```

---

## Targets and Trigger Mechanism

### Direct `AdditionalFiles` Declaration

Other generators often use a custom item type (like `<SqlPartial>`) and then transform it to `<AdditionalFiles>`. This approach breaks the file-watching mechanism of the Roslyn Language Server in VSCode/C# DevKit: the Language Server only watches `AdditionalFiles` declared directly, not those transformed via an intermediate item type.

Solution: Users declare `<AdditionalFiles>` trực tiếp với metadata `<SourceItemType>SqlPartial</SourceItemType>`. The Language Server sees the file path directly and sets up the watcher correctly — the generator triggers automatically when the file is saved. Since we use `extension-based` matching, the generator does not hardcode a `.sql` check in its initial filter, allowing any extension to be used.

### `CompilerVisibleProperty` and `CompilerVisibleItemMetadata`

These declarations in `.targets` allow the generator to read MSBuild properties/metadata via `AnalyzerConfigOptionsProvider`. Without them, `TryGetValue("build_property.XYZ")` would always return `false`.

These are implementation details of the package — users do not and should not declare them manually.

### `UpToDateCheckInput`

Ensures that MSBuild Fast Up-To-Date Check (FUTDC) detects changes in tracked files and triggers a full build. Without this, FUTDC might skip the build even if a SQL file has changed.

---

## Code Generation — `SourceBuilder`

### `SqlAttribute`

Injected via `RegisterPostInitializationOutput` into the `SqlPartial` namespace. This ensures the attribute is always available for semantic analysis even before other code is generated. It is used to mark parameters for overload generation.

### `ISqlString` Interface

Generated once per project. Provides a common contract for both static and dynamic SQL:
- `Default { get; }`
- `Get(string providerName)`

### `SqlStrings` struct

Generated once per project (unless `ExternalSqlStringsType` or `UseSharedNamespace` is set). The struct is `readonly` to ensure immutability and implements `ISqlString`.

**Key Features**:
- **Visibility**: `internal` by default, `public` if `EmitSharedNamespace` is set.
- **Consistent Defaulting**: DBMS-specific properties (e.g., `PostgreSql`) use private backing fields and expression-bodied getters that automatically return `Default` if the specific content is missing.
- **Deduplication**: If multiple extensions (e.g., `.pg.sql` and `.pgsql`) map to the same `PostgreSql` provider, the struct will only contain one `PostgreSql` property.
- **Backward Compatibility**: The generator automatically detects the project's C# version. If C# < 8.0, it won't emit `#nullable` directives.
- **No Implicit Conversion**: Since Phase 1, implicit conversion to `string` is removed to avoid ambiguity with generated `[Sql]` overloads. Use `.Default` or `.Get()` explicitly.
- **Manual Construction**: Users can manually create `SqlStrings` for ad-hoc static SQL.

### `SqlDynamic` struct

Generated once per project. Implements `ISqlString` and allows for lazy-evaluated dynamic SQL using factories (`Func<string>`).

- **Fully Factory-Based**: All properties, including `Default`, are evaluated via factories. This ensures consistency and allows the entire query to be dynamic.
- **Zero Caching**: Evaluates the factory every time a property is accessed. This keeps the struct extremely lightweight (no heap-allocated `Lazy<T>` objects) and ensures truly dynamic behavior (e.g., embedding timestamps).
- **Generic Support**: Designed to be used as a generic constraint: `where TSql : struct, ISqlString`.

### Partial Class

Each `(Namespace, ClassName)` generates a hint file `ClassName.{hash}.g.cs`. The hash is calculated from the fully-qualified class name to avoid collisions when two classes with the same name exist in different namespaces.

Generated properties are `private static readonly` and prefixed with `Sql` (e.g., `SqlGetUser`).

### Method Overloads

When a method parameter is marked with `[Sql]`, the generator produces a generic overload in the same class (or an extension class for interfaces). This overload resolves the provider-specific string at runtime using the type's `SqlProviderName` property.

---

## Analyzers

### `ManualInstantiationAnalyzer` (`SQLPG012`)

Detects when developers manually create `SqlStrings` or `SqlDynamic` with missing DBMS coverage and no default value.

### `SqlMethodAnalyzer` (`SQLPG030`)

Ensures that any type using the `[Sql]` attribute on method parameters also defines a `string SqlProviderName` property (static or instance) to allow runtime resolution.

### Technical Note: RS2008
Release tracking for analyzers is currently disabled to simplify development. See `SqlPartial.Generator/Analyzers/ReleaseTracking/README.md` for more details and future implementation steps.

---

## File Path Parsing Logic — `FilePathParser`

The parser uses a **longest-match-first** strategy against configured extensions to resolve the provider.

1.  **Custom Extensions**: It checks if the filename ends with any extension configured in `SqlPartialProviders` (sorted by length descending to prevent partial matching).
2.  **Default Mechanism**: If no match, it checks for the hardcoded default: `.sql`, mapping it to `Default`.
3.  **Decomposition**: The matched extension is stripped, and the remaining filename is split by `.` to extract `ClassName` and `QueryName`.

**Example**: `UserRepo.GetUsers.pg.sql`
- Configured: `.pg.sql:PostgreSql`
- Matched Extension: `.pg.sql` → `ProviderName = "PostgreSql"`
- Class: `UserRepo`, Query: `GetUsers`

---

## SQL Content Processing — `SqlContentCleaner`

Performed in order:
1.  **Strip Exclusion Blocks**:
    - `-- #exclude … -- /exclude` (official)
    - `-- #testpart … -- /testpart` (legacy support)
    Both are case-insensitive and support spaces after `--`.
2.  **Strip Comments**: Removes lines starting with `--` and blank lines.
3.  **Escape Double Quotes**: Converts `"` to `""` for C# verbatim string literals (`@"..."`).

---

## NuGet Packaging

The `.targets` file must be located at `build/NkChinh.SqlPartial.Generator.targets` within the NuGet package to be automatically imported. The generator assembly must be marked as an analyzer and placed in `analyzers/dotnet/cs/`.

---

## Adding a New Provider

The generator is entirely DBMS-agnostic. To support a new system or custom extension, add an `extension:DisplayName` pair to `SqlPartialProviders` in their `.csproj`:

```xml
<PropertyGroup>
    <SqlPartialProviders>.pgsql:PostgreSql;.ms.sql:SqlServer;.lt.sql:Sqlite</SqlPartialProviders>
</PropertyGroup>
```

The generator automatically adds the corresponding property to the `SqlStrings` struct and updates the `Get()` method.

---

## Manual Testing

Enable `EmitCompilerGeneratedFiles` to see the generated files:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Files will appear at `obj/Debug/{tfm}/generated/SqlPartial.Generator/`.
