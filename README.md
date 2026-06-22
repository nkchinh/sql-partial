# SqlPartial.Generator

Modern Roslyn source generator that turns `.sql` files into strongly-typed, DBMS-aware C# constants — with full IntelliSense, zero-boilerplate overloads, and automatic generation on save.

## Why SqlPartial?

Writing SQL as string literals inside C# is a painful experience:
- **No Syntax Highlighting:** SQL strings are just plain text to your editor.
- **No Linting/Validation:** SQL errors are only caught at runtime.
- **Hard to Test:** You can't easily run a string literal against a database without copying it.
- **Messy Code:** Large SQL queries clutter your C# logic.

**SqlPartial** solves this by letting you keep your SQL in dedicated `.sql` files. You get full editor support (highlighting, formatting, schema validation) while the generator seamlessly bridges them into your C# code as strongly-typed constants.

## How it works

### 1. Simple Property Generation
Each `.sql` file becomes a `static readonly SqlStrings` property prefixed with `Sql` on a `partial class`. At runtime, call `Get("PostgreSql")` to receive the provider-specific SQL, falling back to the default SQL automatically.

```
UserRepo.GetActive.ms.sql     → SQL Server version
UserRepo.GetActive.pg.sql     → PostgreSQL version
UserRepo.GetActive.sql        → (Optional) Default fallback
```

Generated output:

```csharp
partial class UserRepo
{
    private static readonly SqlStrings SqlGetActive = new SqlStrings(
        postgresql: @"SELECT * FROM Users WHERE IsActive = true",
        sqlserver:  @"SELECT * FROM Users WHERE IsActive = 1",
        @default:   @"SELECT * FROM Users WHERE IsActive = 1"
    );
}
```

### 2. Zero-Boilerplate Parameter Injection
Use the `[Sql]` attribute from `SqlPartial` to automatically resolve the correct SQL string based on the current DBMS.

```csharp
using SqlPartial;

public partial class UserRepo
{
    // You must define this property (static or instance)
    public string SqlProviderName { get; set; } = "PostgreSql";

    // Mark parameter with [Sql]
    public Task<IEnumerable<User>> Execute([Sql] string query)
    {
         // The generator handles the .Get(SqlProviderName) call for you
         return connection.QueryAsync<User>(query);
    }
}

// USAGE: The generator creates a generic overload automatically!
var users = await repo.Execute(UserRepo.SqlGetActive);
// At runtime, 'query' becomes the PostgreSql string.
```

---

## Installation

Available on [NuGet](https://www.nuget.org/packages/NkChinh.SqlPartial.Generator).

```bash
dotnet add package NkChinh.SqlPartial.Generator
```

## AI Agent Skills

If you use an agent-based development environment, you can install specialized skills to help you manage SQL files and maintain high-quality SQL code:

### Core Management
Handle file creation, class naming, and generator configuration.

```bash
npx skills add nkchinh/sql-partial --skill sql-partial
```

### Style & Quality (Optional)
Ensure production-grade SQL with best practices for documentation, performance, and multi-DBMS support.

```bash
npx skills add nkchinh/sql-partial --skill sql-partial-style
```

---

## Setup

### 1. Configure DBMS providers

Add to your `.csproj`. A default is always available — only declare additional providers for multi-DBMS support.

**SqlPartial is DBMS-agnostic.** You can define any provider by choosing an **extension** (matched at the end of the filename) and a **Display Name** (used in C# code and `Get()` calls). Multiple extensions can map to the same DBMS.

```xml
<PropertyGroup>
    <!-- extension:DisplayName pairs, semicolon or comma separated -->
    <SqlPartialProviders>.pg.sql:PostgreSql,.pgsql:PostgreSql,.ms.sql:SqlServer</SqlPartialProviders>
</PropertyGroup>
```

#### Suggestive List of Providers

| Extension | Display Name (C#) | DBMS Reference |
|-----------|-------------------|----------------|
| `.pg.sql` | `PostgreSql`      | PostgreSQL     |
| `.pgsql`  | `PostgreSql`      | PostgreSQL     |
| `.ms.sql` | `SqlServer`       | SQL Server     |
| `.my.sql` | `MySql`           | MySQL          |
| `.lt.sql` | `Sqlite`          | SQLite         |
| `.ora.sql`| `Oracle`          | Oracle         |
|  ...      | ...               | Any other      |

### 2. Register your SQL files

```xml
<ItemGroup>
    <!-- Include all extensions you configured -->
    <AdditionalFiles Include="**/*.sql;**/*.*.pgsql" Exclude="obj/**/*;bin/**/*">
        <SourceItemType>SqlPartial</SourceItemType>
    </AdditionalFiles>
</ItemGroup>
```

### 3. Declare the partial class

The generator produces a `partial class` — you must declare the other half yourself. The namespace is derived automatically from `$(RootNamespace)` + the relative directory of the `.sql` file.

```csharp
namespace MyApp.Data
{
    public partial class UserRepo { }
}
```

---

## File naming convention

```
ClassName.QueryName.sql          Default fallback (ANSI SQL recommended)
ClassName.QueryName.pg.sql       PostgreSQL-specific
ClassName.QueryName.pgsql        Also PostgreSQL (if configured)
ClassName.QueryName.ms.sql       SQL Server-specific
```

- **ClassName** — must match the `partial class` name exactly.
- **QueryName** — becomes the property name on the class (prefixed with `Sql`).
- **Extension** — must match an extension declared in `SqlPartialProviders`, or use `.sql` for the default.

---

## Authoring Tips & Patterns

### 1. Parameter Documentation with Exclusion Blocks

Use `-- #exclude` to provide test data and document parameter meanings. This block is stripped from C# but remains in your SQL file for IDE use.

**File: `ProductRepo.GetById.sql`** (Shared default SQL)
```sql
-- #exclude
-- Parameters for local testing & documentation
DECLARE @Id INT = 1;
-- /exclude

SELECT p.Id, p.Name, p.Price
FROM Products p
WHERE p.Id = @Id
```

### 2. Handling Multi-DBMS Transitions

If your original query was written for a specific DBMS (e.g., SQL Server), **rename it** when adding support for a second one. This prevents your "generic default" from containing incompatible syntax.

**File: `ProductRepo.Search.ms.sql`** (SQL Server version)
```sql
-- #exclude
DECLARE @SearchText NVARCHAR(100) = 'Laptop';
-- /exclude

SELECT p.Id, p.Name FROM Products p
WHERE p.Name LIKE '%' + @SearchText + '%'
ORDER BY p.CreatedDate DESC
OFFSET 0 ROWS FETCH NEXT 10 ROWS ONLY
```

**File: `ProductRepo.Search.pg.sql`** (PostgreSQL version)
```sql
-- #exclude
DECLARE SearchText TEXT := 'Laptop';
-- /exclude

SELECT p.Id, p.Name FROM Products p
WHERE p.Name ILIKE '%' || :SearchText || '%'
ORDER BY p.CreatedDate DESC
LIMIT 10
```

> **Rule of thumb**: The base `.sql` file should only contain code that works across **all** your configured providers. If the syntax differs, use explicit extensions.

### 3. Line comments are stripped

Lines beginning with `--` are removed from the generated constant, so you can annotate freely:

```sql
-- Returns a single user by primary key
SELECT id, name, email FROM users WHERE id = @Id
```

---

## Advanced Configuration

### Customizing Access Modifiers
By default, generated SQL properties are `private static readonly`. If you need to share them across classes or projects, use the `[SqlPartial]` attribute.

```csharp
using SqlPartial;

[SqlPartial(AccessModifier.Public)]
public partial class SharedQueries
{
    // Any .sql files targeting SharedQueries will generate PUBLIC properties
}
```

Available modifiers: `Private` (default), `Internal`, `Protected`, `Public`.

### Sharing types across projects (Recommended)

To avoid duplicating core types and enable cross-project attribute sharing, use the Shared Namespace model:

**Abstractions project:**
```xml
<PropertyGroup>
    <SqlPartialEmitSharedNamespace>MyCompany.Data.Abstractions</SqlPartialEmitSharedNamespace>
</PropertyGroup>
```

**Implementation project:**
```xml
<PropertyGroup>
    <SqlPartialUseSharedNamespace>MyCompany.Data.Abstractions</SqlPartialUseSharedNamespace>
</PropertyGroup>
```

### MSBuild property reference

| Property | Required | Default | Description |
|---|---|---|---|
| `SqlPartialProviders` | No | _(none)_ | Semicolon-separated `extension:DisplayName` pairs |
| `SqlPartialEmitSharedNamespace` | No | _(none)_ | Emit public shared types in this namespace |
| `SqlPartialUseSharedNamespace` | No | _(none)_ | Import shared types from this namespace |
| `SqlPartialStringsNamespace` | No | `$(RootNamespace)` | Namespace for the generated `SqlStrings` struct |
| `SqlPartialStringsType` | No | _(none)_ | Fully-qualified type to use instead of generating `SqlStrings` |
| `SqlPartialWarnOnUnrecognized` | No | `false` | If `true`, emits `SQLPG020` for unknown extensions |

---

## Diagnostics

| Code | Severity | Category | Meaning |
|:---|:---:|:---:|:---|
| `SQLPG001` | **Error** | Config | Invalid `SqlPartialProviders` syntax. Format must be `ext:Name`. |
| `SQLPG002` | **Error** | Tooling | Internal failure generating `SqlStrings` struct. |
| `SQLPG003` | **Error** | Tooling | Internal failure generating partial class file. |
| `SQLPG004` | **Error** | Tooling | Internal failure generating method overloads. |
| `SQLPG005` | Warning | Logic | **Naming Collision:** Generated property name exists in user code. Renamed automatically. |
| `SQLPG006` | Warning | Logic | **Duplicate Mapping:** Multiple files map to the same DBMS provider. Longest extension wins. |
| `SQLPG030` | **Error** | Design | Missing `SqlProviderName` property when using `[Sql]`. |
| `SQLPG010` | Warning | Logic | Missing Default SQL & incomplete DBMS coverage. |
| `SQLPG011` | Warning | Quality | SQL file is empty after cleaning comments/excludes. |
| `SQLPG012` | Warning | Logic | Missing Default SQL in manual instantiation (`new SqlStrings`). |
| `SQLPG013` | Warning | Quality | Mismatched `-- #exclude` or `-- /exclude` tags in SQL file. |
| `SQLPG020` | Warning | Usage | Unrecognized extension (Disabled by default). |

---

## Robustness & Conflict Handling

SqlPartial is designed to be "silent but helpful," ensuring your project builds even with imperfect configurations.

### 1. Naming Collisions
If a generated property (e.g., `SqlGetUsers`) would conflict with an existing field, property, or method in your C# class, the generator will:
1. Emit a **SQLPG005** warning.
2. Automatically append a numeric suffix to the generated property (e.g., `SqlGetUsers1`).

This ensures that your manual code always takes precedence and the project remains compilable.

### 2. SQL Mapping Collisions
If multiple files resolve to the same DBMS provider for the same query (e.g., `GetUsers.pg.sql` and `GetUsers.pgsql` both mapping to `PostgreSql`), the generator will:
1. Emit a **SQLPG006** warning.
2. Select the file with the **longest extension** (the most specific one).

Example: `GetUsers.pg.sql` (length 7) will be chosen over `GetUsers.sql` (length 4) if both are considered candidates for a specific provider.

---

## Usage Patterns

SqlPartial supports three main ways to author and consume SQL, all unified under the `ISqlString` interface.

### 1. Auto (From `.sql` files)
Best for large, complex queries. You get full IDE support (syntax highlighting, formatting).
- **Setup:** Create `ClassName.QueryName.sql` (or `.pg.sql`, `.ms.sql`, etc.)
- **Consumption:** Use the generated static properties.
```csharp
await QueryAsync(SqlGetActiveUsers);
```

### 2. Manual Static (Inline strings)
Best for simple one-liners where a separate file would be overkill.
- **Consumption:** Use `.Default` or `.Get()` or `[Sql]` overloads. **Implicit conversion to string is removed.**
```csharp
// Manual Multi-DBMS in code
await QueryAsync(new SqlStrings(
    postgresql: "SELECT name FROM users LIMIT 10",
    sqlserver:  "SELECT TOP 10 name FROM users",
    @default:   "SELECT name FROM users"
));
```

### 3. Manual Dynamic (Logic-based)
Best for SQL that needs runtime calculations (table partitioning, dynamic filtering).
- **Consumption:** Use `SqlDynamic` with factories (`Func<string>`).
```csharp
var dynamicQuery = new SqlDynamic(
    postgresql: () => $"SELECT * FROM sales_{DateTime.Now:yyyyMM}",
    sqlserver:  () => $"SELECT * FROM sales_{DateTime.Now:yyyyMM}",
    @default: () => "SELECT * FROM sales"
);
await QueryAsync(dynamicQuery);
```

### 4. Builder (SQL Composition)
Best for assembling a final SQL string from multiple `ISqlString` parts — e.g., a base query, a filter clause, and an ORDER BY, each potentially DBMS-specific.  
The DBMS provider is **not needed while building**; it is resolved only at the final `Build()` call.
```csharp
var builder = new SqlStringBuilder()
    .Append(UserRepo.SqlGetActive)      // generated SqlStrings (from .sql files)
    .Append(" WHERE status = @status")  // literal — same for all providers
    .Append(new SqlStrings(             // inline static SQL per provider
        postgresql: "LIMIT $1",
        sqlserver:  "FETCH NEXT @n ROWS ONLY",
        @default:   ""))
    .AppendLine(new SqlDynamic(         // inline dynamic SQL (evaluated at Build time)
        postgresql: () => $"-- generated {DateTime.Now:yyyy-MM-dd}",
        @default:   () => ""));

// Pass to a [Sql] overload — provider resolved at call site
await repo.Execute(builder);

// Or resolve manually for a specific provider
string pg  = builder.Build("PostgreSql");
string ms  = builder.Build("SqlServer");
```

`SqlStringBuilder` is thread-safe: `Append`/`AppendLine`/`Clear` and `Build` can be called from multiple threads. `ToString()` resolves against the default (fallback) provider.

---

## Generic Execution Pattern

To get the most out of SqlPartial, use `ISqlString` as a generic constraint in your data access layer. This allows your methods to accept SQL from any source (Auto or Manual) with zero allocation overhead for static cases.

```csharp
public async Task<T> QueryAsync<TSql>(TSql sql) where TSql : struct, ISqlString
{
    // 'dbProvider' is resolved at runtime from your configuration (e.g., "PostgreSql", "SqlServer")
    string rawSql = sql.Get(dbProvider);
    return await connection.QueryAsync<T>(rawSql);
}
```

---

## License

MIT
