# SqlPartial Configuration Guide

This guide details all available MSBuild properties to configure the generator's behavior.

## MSBuild Properties

Properties should be defined in your `.csproj` file within a `<PropertyGroup>`.

### Core Configuration

| Property | Description | Default |
| :--- | :--- | :--- |
| `SqlPartialProviders` | Semicolon or comma separated `extension:DisplayName` pairs. Used to map file extensions (e.g., `.pg.sql`) to C# property names. | (none) |
| `SqlPartialEmitSharedNamespace` | If set, the generator emits the core types (`SqlStrings`, `SqlAttribute`, etc.) with `public` visibility in this namespace. Use this in your Abstractions/Core project. | (none) |
| `SqlPartialUseSharedNamespace` | If set, the generator skips local type emission and instead imports them from this namespace. Use this in your Implementation/Consumer projects. | (none) |

### Fine-Grained Control

| Property | Description | Default |
| :--- | :--- | :--- |
| `SqlPartialStringsNamespace` | The namespace where the `SqlStrings` struct will be generated (when not using shared types). | `$(RootNamespace)` |
| `SqlPartialStringsType` | Use an existing `SqlStrings` type from another assembly. This is an absolute override that prevents any struct generation. | (none) |
| `SqlPartialWarnOnUnrecognized` | When `true`, the generator emits a `SQLPG020` warning for `.sql` files with extensions not registered in `SqlPartialProviders`. | `false` |
| `Nullable` | The generator respects your project's `<Nullable>` setting (e.g., `enable`) to emit appropriate `#nullable` directives. | (from project) |

---

## Shared Namespace Model (Best Practice)

For solutions with multiple projects, use the **Shared Namespace** model to ensure type consistency and enable cross-project attribute sharing.

### 1. The "Abstractions" Project
This project holds the shared SQL contracts.

```xml
<PropertyGroup>
  <!-- Core types will be public and live here -->
  <SqlPartialEmitSharedNamespace>MyCompany.Data.Abstractions</SqlPartialEmitSharedNamespace>
</PropertyGroup>
```

### 2. The "Implementation" Project
This project references the Abstractions project and uses its types.

```xml
<PropertyGroup>
  <!-- Import types from the shared namespace -->
  <SqlPartialUseSharedNamespace>MyCompany.Data.Abstractions</SqlPartialUseSharedNamespace>
</PropertyGroup>
```

---

## Complex Provider Setup

You can define multiple extensions for the same DBMS. Extensions **must** start with a dot.

```xml
<PropertyGroup>
  <SqlPartialProviders>
    .pg.sql:PostgreSql;
    .pgsql:PostgreSql;
    .ms.sql:SqlServer;
    .sqlserver:SqlServer;
    .my.sql:MySql;
    .lt.sql:Sqlite
  </SqlPartialProviders>
</PropertyGroup>
```

## Troubleshooting Common Issues

### 1. Missing `SqlProviderName` (SQLPG030)
- **Problem**: You used `[Sql]` on a parameter but the analyzer flagged the class.
- **Fix**: Add `public string SqlProviderName { get; set; }` to your `partial class`. It can be static or instance, and can be defined in a base class or interface.

### 2. Inconsistent Accessibility (CS0703)
- **Problem**: Compiler error stating `ISqlString` is less accessible than your method.
- **Cause**: Core types are `internal` by default. If your repo method is `public`, it can't reference an `internal` interface.
- **Fix**: Use `SqlPartialEmitSharedNamespace` to make core types `public`.

### 3. Namespace Mismatch
- **Problem**: SQL properties are not visible on the class.
- **Check**: Ensure the `.sql` file and the `.cs` file are in the same directory. The generator uses folder paths to derive namespaces.
