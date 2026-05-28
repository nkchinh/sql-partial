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
UserRepo.GetActive.sql        → ANSI fallback
UserRepo.GetActive.pg.sql     → PostgreSQL override
UserRepo.GetActive.ms.sql     → SQL Server override
```

Generated output:

```csharp
partial class UserRepo
{
    private static readonly SqlStrings SqlGetActive = new SqlStrings(
        // From UserRepo.GetActive.sql
        ansiSql:    @"SELECT * FROM Users WHERE IsActive = 1",
        postgresql: @"SELECT * FROM Users WHERE IsActive = true",
        sqlserver:  @"SELECT * FROM Users WHERE IsActive = 1"
    );
}
```

Runtime usage:

```csharp
// providerName comes from appsettings, e.g. "PostgreSql"
string sql = UserRepo.SqlGetActive.Get(providerName);

// For single-DBMS projects, use implicit conversion (returns ANSI)
string sqlSimple = UserRepo.SqlGetActive;
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

## Authoring Tips & Patterns

### 1. Parameter Documentation with Exclusion Blocks

Use `--#exclude` to provide test data and document parameter meanings. This block is stripped from C# but remains in your SQL file for IDE use.

**File: `ProductRepo.Search.sql`**
```sql
--#exclude
-- Parameters for local testing & documentation
DECLARE @SearchText NVARCHAR(100) = 'Laptop';
DECLARE @CategoryId INT = 5; -- 5: Electronics
DECLARE @Limit INT = 20;
--/exclude

SELECT p.Id, p.Name, p.Price
FROM Products p
WHERE p.Name LIKE '%' + @SearchText + '%'
  AND (@CategoryId IS NULL OR p.CategoryId = @CategoryId)
ORDER BY p.CreatedDate DESC
OFFSET 0 ROWS FETCH NEXT @Limit ROWS ONLY
```

### 2. DBMS Overrides

**File: `ProductRepo.Search.pg.sql`**
```sql
--#exclude
-- PostgreSQL test parameters
DECLARE SearchText TEXT := 'Laptop';
DECLARE CategoryId INT := 5;
DECLARE LimitCount INT := 20;
--/exclude

SELECT p.Id, p.Name, p.Price
FROM Products p
WHERE p.Name ILIKE '%' || :SearchText || '%'
  AND (:CategoryId IS NULL OR p.CategoryId = :CategoryId)
ORDER BY p.CreatedDate DESC
LIMIT :LimitCount
```

**File: `ProductRepo.Search.ms.sql`**
```sql
--#exclude
-- MS SQL test parameters
DECLARE @SearchText NVARCHAR(255) = 'Laptop';
DECLARE @CategoryId INT = 5;
DECLARE @LimitCount INT = 20;
--/exclude

SELECT p.Id, p.Name, p.Price
FROM Products p
WHERE p.Name LIKE '%' + @SearchText + '%'
  AND (@CategoryId IS NULL OR p.CategoryId = @CategoryId)
ORDER BY p.CreatedDate DESC
OFFSET 0 ROWS FETCH NEXT @LimitCount ROWS ONLY;
```

### 3. Line comments are stripped

Lines beginning with `--` are removed from the generated constant, so you can annotate freely:

```sql
-- Returns a single user by primary key
SELECT id, name, email FROM users WHERE id = @id
```

---

## Advanced Configuration

### Sharing `SqlStrings` across projects

By default the `SqlStrings` struct is generated in each project. If you want to share the same struct:

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

### MSBuild property reference

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
