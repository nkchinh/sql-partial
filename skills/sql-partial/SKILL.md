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
- **DBMS Abstraction**: When you want to use `[Sql]` on method parameters to automatically resolve the correct SQL string based on `SqlProviderName`.
- **Multi-DBMS Transition**: When an existing single-database project needs to support additional providers (e.g., migrating from SQL Server to include PostgreSQL).
- **Optimization**: When you want to use editor-only SQL (testing blocks) that shouldn't leak into C#.
- **Sharing**: When you need to share SQL abstractions across multiple projects using `SqlPartialEmitSharedNamespace`.
- **Standardization**: When checking if the project follows the `ClassName.QueryName.sql` convention.

---

## Workflow: The "Gather-Refactor-Implement" Loop

### 1. Research & Context Gathering
Before touching any files, verify:
- **Usage Pattern**: Decide if this query belongs in a `.sql` file (**Auto**), an inline string (**Manual Static**), or requires logic-based generation (**Manual Dynamic**).
- **Target Class**: Which `partial class` will hold the query?
- **Current Setup**: Does `.csproj` have `SqlPartialProviders` and `AdditionalFiles` configured?

### 2. The Abstraction Rule (Zero-Boilerplate & ISqlString)
Always leverage the provided abstraction mechanisms to handle DBMS-specific resolution. 

**Option A: Zero-Boilerplate (Modern)**
Use `[Sql]` (from `SqlPartial.Abstractions`) on `string` parameters. The generator handles the `.Get()` call for you. 
- **Requirement**: The containing type must define a `string SqlProviderName` property.

```csharp
using SqlPartial.Abstractions;

public partial class UserRepo {
    public string SqlProviderName { get; set; } = "PostgreSql";

    public void Execute([Sql] string query) => ...
}
```

**Option B: Generic Execution (Manual)**
Encapsulate DB calls in generic methods using `where TSql : struct, ISqlString`. 
- **Benefit**: This allows the project to mix Auto (generated) and Manual (static/dynamic) SQL seamlessly with zero-allocation performance.

```csharp
public async Task<T> QueryAsync<TSql>(TSql sql) where TSql : struct, ISqlString {
    string rawSql = sql.Get(_provider);
    return await _connection.QueryAsync<T>(rawSql);
}
```

### 3. Usage Pattern Selection
| Pattern | Best For... | Implementation |
| :--- | :--- | :--- |
| **Auto** | Large, complex queries | Create `.sql` files; use generated `SqlStrings` |
| **Manual Static** | Simple one-liners | Use `new SqlStrings(@default: "sql")` |
| **Manual Dynamic** | Logic/Calculation based | Use `new SqlDynamic(@default: () => ...)` |

### 4. The Migration & Transition Rule (CRITICAL)
If you are moving from a single DBMS (e.g., just default) to supporting multiple:
1.  **Identify & Rename**: If `ClassName.QueryName.sql` exists and contains provider-specific syntax (e.g., T-SQL), **rename it** to `ClassName.QueryName.[extension]` (e.g., `.ms.sql`).
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

- **Naming**: Always `ClassName.QueryName.[extension]`. (e.g., `.pg.sql`, `.pgsql`).
- **Location**: `.sql` and `.cs` files **MUST** be in the same directory.
- **Exclusion Block Placement**: Prefer placing `--#exclude` blocks at the top of the file for setup/variable declarations (e.g., `DECLARE @Id ...`).

---

## Reference Guides

To keep your workflow efficient, consult these detailed guides when needed:

- **[Configuration Guide](references/configuration.md)**: Deep dive into `.csproj` properties and advanced MSBuild setup.
- **[Pattern Library](references/patterns.md)**: Examples of Default vs. Provider-specific SQL, and handling exclusion blocks.
- **[Troubleshooting](references/troubleshooting.md)**: Common errors like `SQLPG030` (missing property) and `SQLPG001`.

---

## Naming Convention Reference

| Pattern | Role | Example |
| :--- | :--- | :--- |
| `Class.Query.sql` | **Default Fallback** | `UserRepo.GetById.sql` |
| `Class.Query.pg.sql` | **PostgreSQL** specific override | `UserRepo.GetById.pg.sql` |
| `Class.Query.pgsql` | **PostgreSQL** (Custom extension) | `UserRepo.GetById.pgsql` |
| `Class.Query.ms.sql` | **SQL Server** specific override | `UserRepo.GetById.ms.sql` |
| `Class.Query.lt.sql` | **SQLite** specific override | `UserRepo.GetById.lt.sql` |

*Note:*
- *`.sql` serves as the global fallback.*
- *Extensions are user-defined in `.csproj`. Standard suggestions: `.pg.sql`, `.pgsql`, `.ms.sql`, `.my.sql`, `.ora.sql`, `.lt.sql`.*

---

## Best Practices

1.  **Doc comments are free**: Use `--` liberally. They are stripped during generation and won't affect binary size or performance.
2.  **Implicit Conversion is REMOVED**: Always use `.Default` or `.Get()` when working with `SqlStrings` directly, or use `[Sql]` overloads.
3.  **Exclusion blocks as Parameter Docs**: Use `--#exclude` blocks not just for test data, but to **document parameters** and their expected types/values for other developers.
    ```sql
    --#exclude
    DECLARE @Status INT = 1; -- 1: Active, 0: Deleted
    DECLARE @Limit INT = 10;
    --/exclude

    SELECT * FROM Users WHERE Status = @Status LIMIT @Limit
    ```
4.  **Namespace Alignment**: If the generator creates a property but C# can't find it, check if the `.sql` file is in a different folder than the `.cs` file.

---

## Quick Setup Snippet

Add this to `.csproj` to get started:

```xml
<PropertyGroup>
  <SqlPartialProviders>.pg.sql:PostgreSql;.pgsql:PostgreSql;.ms.sql:SqlServer</SqlPartialProviders>
</PropertyGroup>

<ItemGroup>
  <AdditionalFiles Include="**/*.sql;**/*.*.pgsql" Exclude="obj/**/*;bin/**/*">
    <SourceItemType>SqlPartial</SourceItemType>
  </AdditionalFiles>
</ItemGroup>
```
