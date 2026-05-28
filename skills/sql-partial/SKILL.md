---
name: sql-partial
description: |
  Guides the agent in managing SQL partial files for SqlPartial.Generator. 
  Triggers when:
  - Creating/updating SQL queries in .NET projects.
  - Adding multi-DBMS support (PostgreSQL, SQL Server, etc.) to existing queries.
  - Configuring SqlPartial.Generator in .csproj.
  - Refactoring SQL file organization or naming.
  - Troubleshooting generator issues (missing properties, namespace mismatches).
---

# SqlPartial.Generator Skill

## Overview

SqlPartial.Generator turns `.sql` files into strongly-typed C# constants. This skill helps you bridge the gap between your SQL editor and C# code, ensuring queries are organized, documented, and multi-DBMS ready.

## When to Use This Skill

- **New Feature**: When adding a new database query.
- **Multi-DBMS Transition**: When an existing single-database project needs to support additional providers (e.g., migrating from SQL Server to include PostgreSQL).
- **Optimization**: When you want to use editor-only SQL (testing blocks) that shouldn't leak into C#.
- **Standardization**: When checking if the project follows the `ClassName.QueryName.sql` convention.

---

## Workflow: The "Gather-Refactor-Implement" Loop

### 1. Research & Context Gathering
Before touching any files, verify:
- **Target Class**: Which `partial class` will hold the query?
- **Current Setup**: Does `.csproj` have `SqlPartialProviders` and `AdditionalFiles` configured?
- **Existing Queries**: Does this query already exist? Is it generic or provider-specific?

### 2. The Migration & Transition Rule (CRITICAL)
If you are moving from a single DBMS (e.g., just ANSI or just MS SQL) to supporting multiple:
1.  **Identify & Rename**: If `ClassName.QueryName.sql` exists and contains provider-specific syntax (e.g., T-SQL), **rename it** to `ClassName.QueryName.[slug].sql` (e.g., `.ms.sql`).
2.  **MANDATORY Block Modernization**: You **MUST** convert legacy `--#testpart` / `--/testpart` to `--#exclude` / `--/exclude`. 
    - *Why*: `#testpart` is deprecated. `#exclude` is the modern standard for SqlPartial.Generator.
3.  **Preserve & Sync Testing Logic**: You **MUST** copy the setup logic (DECLAREs, temp tables) from the original exclusion block to **EVERY** new provider-specific file. 
    - *Why*: Developers need to test each DBMS version independently in their editor without re-writing test data.

#### Migration Example:
**Before (UserRepo.GetById.sql):**
```sql
--#testpart
DECLARE @Id INT = 1;
--/testpart
SELECT * FROM Users WHERE Id = @Id
```

**After (UserRepo.GetById.ms.sql):**
```sql
--#exclude
DECLARE @Id INT = 1;
--/exclude
SELECT * FROM Users WHERE Id = @Id
```

**After (UserRepo.GetById.pg.sql):**
```sql
--#exclude
DECLARE @Id INT = 1; -- Preserved from original!
--/exclude
SELECT * FROM Users WHERE Id = $1
```

- **Naming**: Always `ClassName.QueryName.[slug].sql`.
- **Location**: `.sql` and `.cs` files **MUST** be in the same directory.
- **Exclusion Block Placement**: Prefer placing `--#exclude` blocks at the top of the file for setup/variable declarations (e.g., `DECLARE @Id ...`).

---

## Reference Guides

To keep your workflow efficient, consult these detailed guides when needed:

- **[Configuration Guide](references/configuration.md)**: Deep dive into `.csproj` properties and advanced MSBuild setup.
- **[Pattern Library](references/patterns.md)**: Examples of ANSI vs. Provider-specific SQL, and handling exclusion blocks.
- **[Troubleshooting](references/troubleshooting.md)**: Common errors like `SQLGEN001`, namespace mismatches, and trigger failures.

---

## Naming Convention Reference

| Pattern | Role | Example |
| :--- | :--- | :--- |
| `Class.Query.sql` | **ANSI Fallback** (Default) | `UserRepo.GetById.sql` |
| `Class.Query.an.sql` | **ANSI Fallback** (Explicit variant) | `UserRepo.GetById.an.sql` |
| `Class.Query.pg.sql` | **PostgreSQL** specific override | `UserRepo.GetById.pg.sql` |
| `Class.Query.ms.sql` | **SQL Server** specific override | `UserRepo.GetById.ms.sql` |
| `Class.Query.lt.sql` | **SQLite** specific override | `UserRepo.GetById.lt.sql` |

*Note:*
- *`.sql` and `.an.sql` are functionally identical and both serve as the global fallback.*
- *Slugs are user-defined in `.csproj`. Standard suggestions: `pg`, `ms`, `my`, `ora`, `lt`, `mar`, `ch`.*

---

## Best Practices

1.  **Doc comments are free**: Use `--` liberally. They are stripped during generation and won't affect binary size or performance.
2.  **Exclusion blocks as Parameter Docs**: Use `--#exclude` blocks not just for test data, but to **document parameters** and their expected types/values for other developers.
    ```sql
    --#exclude
    DECLARE @Status INT = 1; -- 1: Active, 0: Deleted
    DECLARE @Limit INT = 10;
    --/exclude

    SELECT * FROM Users WHERE Status = @Status LIMIT @Limit
    ```
3.  **Namespace Alignment**: If the generator creates a property but C# can't find it, check if the `.sql` file is in a different folder than the `.cs` file.

---

## Quick Setup Snippet

Add this to `.csproj` to get started:

```xml
<PropertyGroup>
  <SqlPartialProviders>pg:PostgreSql;ms:SqlServer</SqlPartialProviders>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="**/*.*.sql" Exclude="obj/**/*;bin/**/*">
    <SourceItemType>SqlPartial</SourceItemType>
  </AdditionalFiles>
</ItemGroup>
```
