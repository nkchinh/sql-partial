---
name: sql-partial
description: |
  Guides the agent in creating and managing SQL partial files for projects using SqlPartial.Generator.
  Use this skill whenever the user wants to:
  - Add a new SQL query to a class (create .sql file)
  - Add provider-specific SQL for an existing query (pg, ms, my...)
  - Configure SqlPartial.Generator in .csproj
  - Verify if .sql file structure matches the convention
  - List existing queries in the project
  - Refactor SQL files (rename, move, add provider)
  Trigger when mentioning: sql partial, SqlStrings, query file, .sql generator, DBMS provider, AnsiSql.
---

# SqlPartial.Generator Skill

## Quick Overview

The generator transforms `.sql` files into `static readonly SqlStrings` properties prefixed with `Sql` on a `partial class`.
At runtime, call `MyClass.SqlGetUser.Get("PostgreSql")` — the generator handles the rest.

## Installation for Agents

To add this skill to your Gemini CLI environment:
```bash
npx skills add nkchinh/sql-partial --skill sql-partial
```

---

## Naming Convention (IMPORTANT)

```
ClassName.QueryName.sql          ← ANSI SQL, shared fallback for all DBMS
ClassName.QueryName.an.sql       ← Same as above, explicit
ClassName.QueryName.pg.sql       ← PostgreSQL override
ClassName.QueryName.ms.sql       ← SQL Server override
ClassName.QueryName.ora.sql      ← Oracle override
ClassName.QueryName.lt.sql       ← SQLite override
```

- **ClassName** must exactly match the `partial class` name (case-sensitive).
- **QueryName** becomes the property name on the class (automatically prefixed with `Sql`).
- **Slug** must match a slug in the project's `SqlPartialProviders`, or `an`.

### Suggestive List of Providers

| Slug | Display Name (C#) | DBMS Reference |
|------|-------------------|----------------|
| `pg` | `PostgreSql`      | PostgreSQL     |
| `ms` | `SqlServer`       | SQL Server     |
| `my` | `MySql`           | MySQL          |
| `ora`| `Oracle`          | Oracle         |
| `lt` | `Sqlite`          | SQLite         |
| `mar`| `MariaDb`         | MariaDB        |
| `fb` | `Firebird`        | Firebird       |
| `ch` | `ClickHouse`      | ClickHouse     |
| `dk` | `DuckDb`          | DuckDB         |

Files in the same directory as the class → namespace matches automatically.
Files in a subdirectory → namespace = `RootNamespace.SubDirName`.

---

## Workflow: Adding New SQL

### Step 1 — Gather Information

Ask the user (or infer from context) 4 things:
1. **ClassName** — which class will contain the query?
2. **QueryName** — desired property name (will be prefixed with `Sql`)?
3. **Provider** — common ANSI or provider-specific? If specific, which slug?
4. **SQL Content** — provided by the user or needs to be drafted?

### Step 2 — Determine Directory

```bash
# Find .csproj to identify project root
find . -name "*.csproj" -not -path "*/obj/*" | head -5

# Find target class to see where it's located
find . -name "ClassName.cs" -not -path "*/obj/*"
```

The `.sql` file must be placed in the same directory as the class for the namespace to match.

### Step 3 — Check Project Configuration

```bash
# Check if SqlPartialProviders and AdditionalFiles are configured
grep -A5 "SqlPartialProviders\|SqlPartial\|AdditionalFiles.*sql" *.csproj
```

If not configured → see **.csproj Configuration** below.

### Step 4 — Create File

Create the file with the correct naming convention in the target directory. SQL content should:
- **Keep and encourage helpful documentation**: You are encouraged to include or add descriptive comments (`--`). Since the generator automatically strips them during code generation, you can document your SQL extensively without any runtime performance penalty.
- Keep `--#exclude … --/exclude` (or the legacy `--#testpart … --/testpart` blocks) if there's SQL only for testing/editor support.

### Step 5 — Confirm Partial Class Exists

```bash
grep -r "partial class ClassName" --include="*.cs" -l
```

If it doesn't exist → remind the user to create it or add the `partial` keyword to the existing class.

---

## .csproj Configuration

When the project is not yet configured, add this to `.csproj`:

```xml
<PropertyGroup>
    <!-- Declare DBMS providers: slug:DisplayName, separated by ; -->
    <!-- ANSI SQL is always available, no need to declare -->
    <SqlPartialProviders>pg:PostgreSql;ms:SqlServer</SqlPartialProviders>
</PropertyGroup>

<ItemGroup>
    <!-- Include directly as AdditionalFiles — DO NOT use an intermediate custom item type -->
    <AdditionalFiles Include="**/*.*.sql" Exclude="obj/**/*;bin/**/*">
        <SourceItemType>SqlPartial</SourceItemType>
    </AdditionalFiles>
</ItemGroup>
```

**Important Note**: the pattern `**/*.*.sql` (two dots) ensures the file has at least `ClassName.QueryName.sql` — avoiding unrelated SQL files with a single name segment.

### Optional: Namespace for SqlStrings struct

```xml
<PropertyGroup>
    <!-- Default: RootNamespace -->
    <SqlPartialStringsNamespace>MyCompany.Data</SqlPartialStringsNamespace>
</PropertyGroup>
```

### Optional: Use SqlStrings from another project

```xml
<PropertyGroup>
    <!-- When set, the generator does NOT emit the SqlStrings struct in this project -->
    <SqlPartialStringsType>MyCompany.Core.SqlStrings</SqlPartialStringsType>
</PropertyGroup>
```

---

## Generated Output

With `SqlPartialProviders=pg:PostgreSql;ms:SqlServer` and files:
```
Data/UserRepo.GetById.sql
Data/UserRepo.GetById.pg.sql
```

The generator produces:

```csharp
// SqlStrings.g.cs
namespace MyApp
{
    public readonly struct SqlStrings
    {
        public string AnsiSql { get; }
        public string? PostgreSql { get; }
        public string? SqlServer { get; }

        public SqlStrings(string ansiSql, string? postgresql = null, string? sqlserver = null)
        {
            AnsiSql = ansiSql;
            PostgreSql = postgresql;
            SqlServer = sqlserver;
        }

        public string Get(string providerName) { ... }
    }
}

// UserRepo.{hash}.g.cs
namespace MyApp.Data
{
    partial class UserRepo
    {
        private static readonly SqlStrings SqlGetById = new SqlStrings(
            @"SELECT ...",   // from GetById.sql
            postgresql: @"SELECT ..."   // from GetById.pg.sql
            // SqlServer has no separate file → falls back to AnsiSql at runtime
        );
    }
}
```

Runtime:
```csharp
// providerName read from appsettings, e.g., "PostgreSql"
var sql = UserRepo.SqlGetById.Get(providerName);

// If project uses only 1 DBMS, can implicitly cast to string (returns AnsiSql)
string sqlSimple = UserRepo.SqlGetById;
```

---

## Verification and Debugging

### List all existing SQL files

```bash
find . -name "*.*.sql" -not -path "*/obj/*" -not -path "*/bin/*" | sort
```

### Verify naming convention

```bash
# File must have at least 2 segments before .sql
# Correct: UserRepo.GetById.sql | UserRepo.GetById.pg.sql
# Wrong:   GetById.sql | queries.sql
find . -name "*.sql" -not -path "*/obj/*" | while read f; do
    base=$(basename "$f" .sql)
    count=$(echo "$base" | tr -cd '.' | wc -c)
    if [ "$count" -lt 1 ]; then
        echo "WARN: $f — missing segment, will be ignored by generator"
    fi
done
```

### Enable EmitCompilerGeneratedFiles to see output

Add to `.csproj` for debugging:
```xml
<PropertyGroup>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

Files are generated at: `obj/Debug/{tfm}/generated/SqlPartial.Generator/`

### Common Issues

| Symptom | Cause | Fix |
|---|---|---|
| Property missing on class | File `.sql` doesn't match convention | Check if filename is `ClassName.QueryName[.slug].sql` |
| Namespace doesn't match main class | File `.sql` in wrong directory | Move `.sql` file to the same directory as the `.cs` file |
| `SqlStrings` not found | Haven't built after adding file | Build project or save any `.cs` file to trigger generator |
| Provider property null | No `.slug.sql` file for that provider | Expected behavior — `Get()` automatically falls back to `AnsiSql` |
| Generator doesn't trigger on edit | `AdditionalFiles` declared incorrectly | Ensure using direct `<AdditionalFiles Include="...">`, not via custom item type |

---

## Complete Example

**Requirement**: Add `GetByEmail` query to `UserRepo` class, with specific SQL for PostgreSQL.

**Files to create**:

`Data/UserRepo.GetByEmail.sql` (ANSI fallback):
```sql
SELECT id, email, name
FROM users
WHERE email = @email
```

`Data/UserRepo.GetByEmail.pg.sql` (PostgreSQL override):
```sql
SELECT id, email, name
FROM users
WHERE email = $1
```

**Usage**:
```csharp
public partial class UserRepo
{
    public User? FindByEmail(string email, string dbProvider)
    {
        var sql = SqlGetByEmail.Get(dbProvider); // "PostgreSql" or any other
        // ... execute sql
    }
}
```
