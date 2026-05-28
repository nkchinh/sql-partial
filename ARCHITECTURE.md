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
│   ├── FilePathParser.cs           Parses file paths → (ns, className, queryName, slug)
│   ├── SqlContentCleaner.cs        Strips comments, exclude blocks, escapes verbatim strings
│   └── SourceBuilder.cs            Generates C# source from models
├── Models/
│   ├── GeneratorConfig.cs          Project-level configuration (providers, namespaces...)
│   ├── SqlProvider.cs              A DBMS provider (slug + display name)
│   ├── SqlFile.cs                  A parsed .sql file
│   └── SqlQueryGroup.cs            Group of SqlFiles with same (ns, className, queryName)
└── build/ (implicit in NuGet)
    └── NkChinh.SqlPartial.Generator.targets   MSBuild targets packaged in NuGet
```

---

## Generator Pipeline

```
AdditionalTextsProvider
    │ filter: .sql + SourceItemType=SqlPartial
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

**Trade-off**: Whenever any `.sql` file changes, the entire pipeline after `Collect()` re-executes. This is a necessary trade-off because grouping is a global operation. For the typical number of `.sql` files in a project, this cost is negligible.

---

## Models

### `GeneratorConfig`

Parsed from MSBuild global properties once. It remains stable between `.sql` file edits — Roslyn caches it and does not re-parse unless the project file changes.

| Property | MSBuild source | Description |
|---|---|---|
| `RootNamespace` | `build_property.RootNamespace` | The project's root namespace |
| `Providers` | `build_property.SqlPartialProviders` | List of DBMS providers |
| `SqlStringsNamespace` | `build_property.SqlPartialStringsNamespace` | Namespace for the `SqlStrings` struct |
| `ExternalSqlStringsType` | `build_property.SqlPartialStringsType` | Use a struct from another assembly |
| `NullableEnabled` | `build_property.Nullable` | Whether to emit `#nullable enable` |

### `SqlFile`

Represents a parsed `.sql` file. It implements `IEquatable<SqlFile>` — mandatory for Roslyn incremental caching to work correctly. Without `IEquatable`, the pipeline would always re-execute even if content remained unchanged.

### `SqlQueryGroup`

Groups all `SqlFile` objects with the same `(Namespace, ClassName, QueryName)`. It contains an `ImmutableDictionary<string, string>` mapping slug → SQL content.

`GetContent(slug)` method:
```
slug exists in dict → returns content for that slug
slug not found      → falls back to "an" (ANSI)
"an" missing        → returns string.Empty
```

---

## Targets and Trigger Mechanism

### Direct `AdditionalFiles` Declaration

Other generators often use a custom item type (like `<SqlPartial>`) and then transform it to `<AdditionalFiles>`. This approach breaks the file-watching mechanism of the Roslyn Language Server in VSCode/C# DevKit: the Language Server only watches `AdditionalFiles` declared directly, not those transformed via an intermediate item type.

Solution: Users declare `<AdditionalFiles>` directly with the metadata `<SourceItemType>SqlPartial</SourceItemType>`. The Language Server sees the file path directly and sets up the watcher correctly — the generator triggers automatically when the `.sql` file is saved.

### `CompilerVisibleProperty` and `CompilerVisibleItemMetadata`

These declarations in `.targets` allow the generator to read MSBuild properties/metadata via `AnalyzerConfigOptionsProvider`. Without them, `TryGetValue("build_property.XYZ")` would always return `false`.

These are implementation details of the package — users do not and should not declare them manually.

### `UpToDateCheckInput`

Ensures that MSBuild Fast Up-To-Date Check (FUTDC) detects `.sql` changes and triggers a full build. Without this, FUTDC might skip the build even if a `.sql` file has changed.

---

## Code Generation — `SourceBuilder`

### `SqlStrings` struct

Generated once per project. The struct is `readonly` to ensure immutability. The `AnsiSql` property is always present; provider properties are `string?` (or `string` depending on the C# version) to distinguish "no specific SQL" from "empty SQL."

**Special Features**:
- **Backward Compatibility**: The generator automatically detects the project's C# version. If C# < 8.0, it won't emit `#nullable` directives and will use `string` instead of `string?`.
- **Implicit Conversion**: The `SqlStrings` struct can be implicitly cast to `string`, returning `AnsiSql`. This keeps code concise for projects using a single DBMS.

All properties use `{ get; }` (getter-only) and are initialized via the constructor for compatibility with C# 7.3.

The `Get(string providerName)` method uses a `switch` on the display name (not the slug) because this is the value users configure in their application settings — for example, `"PostgreSql"` instead of `"pg"`.

### Partial Class

Each `(Namespace, ClassName)` generates a hint file `ClassName.{hash}.g.cs`. The hash is calculated from the fully-qualified class name to avoid collisions when two classes with the same name exist in different namespaces.

Generated properties are `private static readonly` and prefixed with `Sql` (e.g., `SqlGetUser`).

---

## File Naming Convention

```
ClassName.QueryName.sql          → providerSlug = "an"
ClassName.QueryName.an.sql       → providerSlug = "an"  (explicit)
ClassName.QueryName.pg.sql       → providerSlug = "pg"
```

`FilePathParser.TryParse` decomposes paths according to this convention. Files that do not match the pattern (fewer than 2 segments before `.sql`) are ignored — they do not cause build errors.

---

## SQL Content Processing — `SqlContentCleaner`

Performed in order:
1. Strip editor support/test blocks:
   - `--#exclude … --/exclude` (official)
   - `--#testpart … --/testpart` (legacy support)
   Both are case-insensitive and support spaces after `--`.
2. Strip blank lines and line comments (`--`).
3. Escape `"` to `""` for C# verbatim string literals (`@"..."`).

---

## NuGet Packaging

The `.targets` file must be located at `build/NkChinh.SqlPartial.Generator.targets` within the NuGet package to be automatically imported. Configured in the generator's `.csproj`:

```xml
<ItemGroup>
    <None Include="NkChinh.SqlPartial.Generator.targets" Pack="true" PackagePath="build\" />
</ItemGroup>
```

The generator assembly must be marked as an analyzer:

```xml
<ItemGroup>
    <None Include="$(OutputPath)\$(AssemblyName).dll" Pack="true"
          PackagePath="analyzers\dotnet\cs\" Visible="false" />
</ItemGroup>
```

---

## Adding a New Provider

The generator is entirely DBMS-agnostic. No code changes are required to support a new database system. Users simply add a new `slug:DisplayName` pair to `SqlPartialProviders` in their `.csproj`:

```xml
<PropertyGroup>
    <SqlPartialProviders>pg:PostgreSql;ms:SqlServer;ora:Oracle;lt:Sqlite</SqlPartialProviders>
</PropertyGroup>
```

The generator automatically:
1. Adds a corresponding property to the `SqlStrings` struct.
2. Updates the `SqlStrings` constructor to accept the new provider's SQL.
3. Adds a case to the `Get(string providerName)` method to return the SQL when queried by the display name (e.g., `"Oracle"`).

---

## Manual Testing

Enable `EmitCompilerGeneratedFiles` to see the generated files:

```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Files will appear at `obj/Debug/{tfm}/generated/SqlPartial.Generator/`.
