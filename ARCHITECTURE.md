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
└── build/ (implicit in NuGet)
    └── NkChinh.SqlPartial.Generator.targets   MSBuild targets packaged in NuGet
```

---

## Generator Pipeline

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

### Why use `Collect()` twice?

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

### `SqlFile`

Represents a parsed SQL file. It implements `IEquatable<SqlFile>` — mandatory for Roslyn incremental caching to work correctly. It stores the resolved `ProviderName` (e.g., `"PostgreSql"` or `"Fallback"`).

### `SqlQueryGroup`

Groups all `SqlFile` objects with the same `(Namespace, ClassName, QueryName)`. It contains an `ImmutableDictionary<string, string>` mapping **ProviderName** → SQL content.

`GetContent(providerName)` method:
```
providerName exists in dict → returns content for that provider
providerName not found      → falls back to "Fallback"
"Fallback" missing           → returns string.Empty
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

### `ISqlString` Interface

Generated once per project. Provides a common contract for both static and dynamic SQL:
- `Fallback { get; }`
- `Get(string providerName)`

### `SqlStrings` struct

Generated once per project (unless `ExternalSqlStringsType` is set). The struct is `readonly` to ensure immutability and implements `ISqlString`.

**Key Features**:
- **Consistent Fallback**: DBMS-specific properties (e.g., `PostgreSql`) use private backing fields and expression-bodied getters that automatically return `Fallback` if the specific content is missing.
- **Deduplication**: If multiple extensions (e.g., `.pg.sql` and `.pgsql`) map to the same `PostgreSql` provider, the struct will only contain one `PostgreSql` property.
- **Backward Compatibility**: The generator automatically detects the project's C# version. If C# < 8.0, it won't emit `#nullable` directives.
- **Implicit Conversion**: Supports implicit cast to `string` (returns `Fallback`) and from `string` (creates a fallback-only `SqlStrings`).
- **Manual Construction**: Users can manually create `SqlStrings` for ad-hoc static SQL.

### `SqlDynamic` struct

Generated once per project. Implements `ISqlString` and allows for lazy-evaluated dynamic SQL using factories (`Func<string>`).

- **Fully Factory-Based**: All properties, including `Fallback`, are evaluated via factories. This ensures consistency and allows the entire query to be dynamic.
- **Zero Caching**: Evaluates the factory every time a property is accessed. This keeps the struct extremely lightweight (no heap-allocated `Lazy<T>` objects) and ensures truly dynamic behavior (e.g., embedding timestamps).
- **Generic Support**: Designed to be used as a generic constraint: `where TSql : struct, ISqlString`.

### Partial Class

Each `(Namespace, ClassName)` generates a hint file `ClassName.{hash}.g.cs`. The hash is calculated from the fully-qualified class name to avoid collisions when two classes with the same name exist in different namespaces.

Generated properties are `private static readonly` and prefixed with `Sql` (e.g., `SqlGetUser`).

---

## File Path Parsing Logic — `FilePathParser`

The parser uses a **longest-match-first** strategy against configured extensions to resolve the provider.

1.  **Custom Extensions**: It checks if the filename ends with any extension configured in `SqlPartialProviders` (sorted by length descending to prevent partial matching).
2.  **Fallback Mechanism**: If no match, it checks for hardcoded defaults: `.an.sql` and `.sql`, mapping them to `Fallback`.
3.  **Decomposition**: The matched extension is stripped, and the remaining filename is split by `.` to extract `ClassName` and `QueryName`.

**Example**: `UserRepo.GetUsers.pg.sql`
- Configured: `.pg.sql:PostgreSql`
- Matched Extension: `.pg.sql` → `ProviderName = "PostgreSql"`
- Class: `UserRepo`, Query: `GetUsers`

---

## SQL Content Processing — `SqlContentCleaner`

Performed in order:
1.  **Strip Exclusion Blocks**:
    - `--#exclude … --/exclude` (official)
    - `--#testpart … --/testpart` (legacy support)
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
