# SqlPartial.Generator

Modern Roslyn source generator that turns `.sql` files into strongly-typed, DBMS-aware C# constants — with full IntelliSense and automatic generation on save.

## Why SqlPartial?

Writing SQL as string literals inside C# is a painful experience:
- **No Syntax Highlighting:** SQL strings are just plain text to your editor.
- **No Linting/Validation:** SQL errors are only caught at runtime.
- **Hard to Test:** You can't easily run a string literal against a database without copying it.
- **Messy Code:** Large SQL queries clutter your C# logic.

**SqlPartial** solves this by letting you keep your SQL in dedicated `.sql` files. You get full editor support (highlighting, formatting, schema validation) while the generator seamlessly bridges them into your C# code as strongly-typed constants.

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
    private static readonly SqlStrings SqlGetById = new SqlStrings(
        @"SELECT id, name FROM users WHERE id = @id",
        postgresql: @"SELECT id, name FROM users WHERE id = $1",
        sqlserver:  @"SELECT id, name FROM users WHERE id = @id"
    );
}
```

Runtime usage:

```csharp
// providerName comes from appsettings, e.g. "PostgreSql"
var sql = UserRepo.SqlGetById.Get(providerName);

// For single-DBMS projects, use implicit conversion (returns AnsiSql)
string sqlSimple = UserRepo.SqlGetById;
```

---

## Installation

```bash
dotnet add package NkChinh.SqlPartial.Generator
```

## AI Agent Skill

If you use Gemini CLI or a similar agent-based environment, you can install the specialized skill to help you manage SQL files:

```bash
npx skills add nkchinh/sql-partial --skill sql-partial
```

---

## Setup

### 1. Configure DBMS providers

Add to your `.csproj`. ANSI SQL is always available — only declare additional providers for multi-DBMS support.

**SqlPartial is DBMS-agnostic.** You can define any provider by choosing a **slug** (used in file naming) and a **Display Name** (used in C# code and `Get()` calls).

```xml
<PropertyGroup>
    <!-- slug:DisplayName pairs, semicolon-separated -->
    <SqlPartialProviders>pg:PostgreSql;ms:SqlServer;my:MySql;lt:Sqlite</SqlPartialProviders>
</PropertyGroup>
```

#### Suggestive List of Providers

| Slug | Display Name (C#) | DBMS Reference |
|------|-------------------|----------------|
| `pg` | `PostgreSql`      | PostgreSQL     |
| `ms` | `SqlServer`       | SQL Server     |
| `my` | `MySql`           | MySQL          |
| `ora`| `Oracle`          | Oracle         |
| `lt` | `Sqlite`          | SQLite         |
| ...  | ...               | Any other      |

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

### Exclude blocks

Wrap SQL that should exist only in your SQL editor/tests (and be stripped from the generated C#) with `--#exclude` / `--/exclude`.

> **Note**: `--#testpart` / `--/testpart` is also supported for backward compatibility, but `#exclude` is the official marker.

```sql
SELECT id, name FROM users
--#exclude
WHERE tenant_id = 'test-tenant'
--/exclude
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

---

## License

This project is licensed under the MIT License.
