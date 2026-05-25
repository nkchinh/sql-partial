# TD.SqlPartial.Generator

Roslyn source generator that turns `.sql` files into strongly-typed, DBMS-aware C# constants — with full IntelliSense and design-time generation on save.

## How it works

Each `.sql` file becomes a `static readonly SqlStrings` property prefixed with `Sql` on a `partial class`. At runtime, call `Get("PostgreSql")` to receive the provider-specific SQL, falling back to ANSI SQL automatically.

```
UserRepo.GetById.sql        → ANSI fallback
UserRepo.GetById.pg.sql     → PostgreSQL override
UserRepo.GetById.ms.sql     → SQL Server override
```

Generated output:

```csharp
partial class UserRepo
{
    private static readonly SqlStrings SqlGetById = new SqlStrings
    {
        AnsiSql    = @"SELECT id, name FROM users WHERE id = @id",
        PostgreSql = @"SELECT id, name FROM users WHERE id = $1",
        SqlServer  = @"SELECT id, name FROM users WHERE id = @id",
    };
}
```

Runtime usage:

```csharp
// providerName comes from appsettings, e.g. "PostgreSql"
var sql = UserRepo.SqlGetById.Get(providerName);
```

---

## Installation

```
dotnet add package TD.SqlPartial.Generator
```

---

## Setup

### 1. Configure DBMS providers

Add to your `.csproj`. ANSI SQL is always available — only declare additional providers:

```xml
<PropertyGroup>
    <!-- slug:DisplayName pairs, semicolon-separated -->
    <SqlPartialProviders>pg:PostgreSql;ms:SqlServer;my:MySql</SqlPartialProviders>
</PropertyGroup>
```

| Slug | Display name used in C# |
|------|-------------------------|
| `pg` | `PostgreSql`            |
| `ms` | `SqlServer`             |
| `my` | `MySql`                 |

### 2. Register your SQL files

```xml
<ItemGroup>
    <AdditionalFiles Include="**/*.*.sql" Exclude="obj/**/*;bin/**/*">
        <SourceItemType>SqlPartial</SourceItemType>
    </AdditionalFiles>
</ItemGroup>
```

### 3. Declare the partial class

The generator produces a `partial class` — you must declare the other half yourself:

```csharp
namespace MyApp.Data
{
    public partial class UserRepo { }
}
```

The namespace is derived automatically from `$(RootNamespace)` + the relative directory of the `.sql` file.

---

## File naming convention

```
ClassName.QueryName.sql          ANSI SQL (shared fallback)
ClassName.QueryName.an.sql       Same as above — explicit ANSI slug
ClassName.QueryName.pg.sql       PostgreSQL-specific
ClassName.QueryName.ms.sql       SQL Server-specific
```

- **ClassName** — must match the `partial class` name exactly.
- **QueryName** — becomes the property name on the class (prefixed with `Sql`).
- **Slug** — must match a slug declared in `SqlPartialProviders`, or `an` for ANSI.

---

## Optional: sharing `SqlStrings` across projects

By default the `SqlStrings` struct is generated in each project that uses the package. If you have multiple projects and want to share the same struct, generate it in one "core" project and reference it from the others.

**Core project** — normal setup, struct is generated here.

**Consumer project** — add this property to skip struct generation:

```xml
<PropertyGroup>
    <SqlPartialStringsType>MyCompany.Core.SqlStrings</SqlPartialStringsType>
</PropertyGroup>
```

### Controlling the namespace of `SqlStrings`

By default the struct is placed in `$(RootNamespace)`. To override:

```xml
<PropertyGroup>
    <SqlPartialStringsNamespace>MyCompany.Data.Sql</SqlPartialStringsNamespace>
</PropertyGroup>
```

---

## SQL authoring tips

### Line comments are stripped

Lines beginning with `--` are removed from the generated constant, so you can annotate freely:

```sql
-- Returns a single user by primary key
SELECT id, name, email
FROM users
WHERE id = @id
```

### Test-only blocks

Wrap SQL that should exist only in tests with `--#testpart` / `--/testpart`:

```sql
SELECT id, name FROM users
--#testpart
WHERE tenant_id = 'test-tenant'
--/testpart
AND id = @id
```

---

## MSBuild property reference

| Property | Required | Default | Description |
|---|---|---|---|
| `SqlPartialProviders` | No | _(none)_ | Semicolon-separated `slug:Name` pairs |
| `SqlPartialStringsNamespace` | No | `$(RootNamespace)` | Namespace for the generated `SqlStrings` struct |
| `SqlPartialStringsType` | No | _(none)_ | Fully-qualified type to use instead of generating `SqlStrings` |

---

## Diagnostics

| Code | Severity | Meaning |
|------|----------|---------|
| `SQLGEN001` | Error | Failed to generate `SqlStrings` struct |
| `SQLGEN002` | Error | Failed to generate a partial class file |
