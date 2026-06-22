---
name: sql-partial
source: https://github.com/nkchinh/sql-partial
description: |
  Guides the agent in managing SQL partial files for SqlPartial.Generator.
  Triggers when:
  - Creating/updating SQL queries in .NET projects.
  - Adding multi-DBMS support (PostgreSQL, SQL Server, etc.) to existing queries.
  - Configuring SqlPartial.Generator in .csproj.
  - Refactoring SQL file organization or naming.
  - Troubleshooting generator issues (missing properties, namespace mismatches).
  - Composing SQL from multiple ISqlString parts using SqlStringBuilder.
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
Use `[Sql]` (from `SqlPartial`) on `string` parameters. The generator handles the `.Get()` call for you.
- **Requirement**: The containing type must define a `string SqlProviderName` property.

```csharp
using SqlPartial;

public partial class UserRepo {
    public string SqlProviderName { get; set; } = "PostgreSql";

    public void Execute([Sql] string query) => ...
}
```

- **Advanced Sharing**: By default, SQL properties are `private`. To share them across classes, use `[SqlPartial(AccessModifier.Public)]` on the class. Supported modifiers are `Private` (default), `Internal`, `Protected`, `Public`.
- **Collision Protection**: The generator automatically detects if a property name (e.g., `SqlGetUsers`) already exists in your manual code and will rename the generated property to `SqlGetUsers1` (and report `SQLPG005`). This works for **all** target classes.


**Option B: Generic Execution (Manual)**
Encapsulate DB calls in generic methods using `where TSql : struct, ISqlString`.
- **Benefit**: This allows the project to mix Auto (generated) and Manual (static/dynamic) SQL seamlessly with zero-allocation performance.

```csharp
public async Task<T> QueryAsync<TSql>(TSql sql) where TSql : struct, ISqlString {
    string rawSql = sql.Get(_provider);
    return await _connection.QueryAsync<T>(rawSql);
}
```

**Option C: Builder (SQL Composition)**
Use `SqlStringBuilder` to assemble a query from multiple `ISqlString` segments. The DBMS is not required during composition — only at the final `Build()` or when passing to a `[Sql]` overload.
- **When to use**: The final SQL is assembled from parts (base query + filter + order), each potentially DBMS-specific.

```csharp
var builder = new SqlStringBuilder()
    .Append(UserRepo.SqlGetActive)           // generated SqlStrings (from .sql files)
    .Append(" WHERE status = @status")       // literal — same for all providers
    .Append(new SqlStrings(                  // inline static SQL per provider
        postgresql: "LIMIT $1",
        sqlserver:  "FETCH NEXT @n ROWS ONLY",
        @default:   ""))
    .Append(new SqlDynamic(                  // inline dynamic SQL (evaluated at Build time)
        postgresql: () => BuildPgHint(),
        @default:   () => ""));

// Pass directly to a [Sql]-annotated method — provider resolved at call site
await repo.Execute(builder);
// Or resolve manually:
string sql = builder.Build("PostgreSql");
```

### 3. Pattern Selection Guide

Câu hỏi đầu tiên: **cấu trúc SQL có cố định lúc compile không?**

#### SQL tĩnh (cấu trúc cố định)

File `.sql` là lựa chọn mặc định cho mọi SQL tĩnh — từ đơn giản đến rất phức tạp — vì IDE hỗ trợ đầy đủ: syntax highlight, schema validation, test trực tiếp trên IDE.

```
SQL tĩnh
├─ Syntax tương thích tất cả DBMS → một file .sql chung
├─ Syntax khác nhau theo DBMS     → file riêng (.pg.sql, .ms.sql...)
└─ Quá nhỏ để tạo file (true one-liner)
   ├─ Cùng cho mọi DBMS  → new SqlStrings(@default: "...")
   └─ Khác theo DBMS     → new SqlStrings(postgresql: "...", sqlserver: "...")
```

> File `.sql` phù hợp SQL ở mọi mức độ phức tạp. Chỉ dùng inline string khi lợi ích của IDE không đáng bằng chi phí tạo thêm file.

#### SQL động (cấu trúc thay đổi lúc runtime)

```
SQL động
├─ Ghép các đoạn ISqlString cố định (base + filter + ORDER BY...)
│  → SqlStringBuilder
├─ Nhúng giá trị runtime vào cấu trúc SQL (tên bảng, partition, timestamp)
│  → SqlDynamic  (factory Func<string> được gọi mỗi lần .Get())
└─ WHERE/JOIN phức tạp với điều kiện hoàn toàn động
   → Thư viện query builder (SqlKata, Dapper, v.v.)
```

**SqlStringBuilder vs SqlDynamic:**
- `SqlStringBuilder`: ghép *nhiều* `ISqlString` đã biết thành một; DBMS resolve lúc `Build()`.
- `SqlDynamic`: *một* SQL nhưng giá trị bên trong được tính lại mỗi lần (e.g., tên bảng theo tháng).

**Tóm tắt nhanh:**

| | Tĩnh | Động |
|---|---|---|
| Phức tạp, cần IDE | `.sql` file | SqlStringBuilder (ghép) |
| Đơn giản, inline | `new SqlStrings()` | `new SqlDynamic()` |
| Điều kiện hoàn toàn động | — | Query builder |

### 4. The Migration & Transition Rule (CRITICAL)
If you are moving from a single DBMS (e.g., just default) to supporting multiple:
1.  **Identify & Rename**: If `ClassName.QueryName.sql` exists and contains provider-specific syntax (e.g., T-SQL), **rename it** to `ClassName.QueryName.[extension]` (e.g., `.ms.sql`).
2.  **MANDATORY Block Modernization**: You **MUST** convert legacy `-- #testpart` / `-- /testpart` to `-- #exclude` / `-- /exclude`.
    - *Why*: `#testpart` is deprecated. `#exclude` is the modern standard for SqlPartial.Generator.
3.  **Preserve & Sync Testing Logic**: You **MUST** copy the setup logic (DECLAREs, temp tables) from the original exclusion block to **EVERY** new provider-specific file.
    - *Why*: Developers need to test each DBMS version independently in their editor without re-writing test data.

#### Migration Example:
**Before (UserRepo.GetById.sql):**
```sql
-- #testpart
DECLARE @Id INT = 1;
-- /testpart
SELECT * FROM Users WHERE Id = @Id
```

**After (UserRepo.GetById.ms.sql):**
```sql
-- #exclude
DECLARE @Id INT = 1;
-- /exclude
SELECT * FROM Users WHERE Id = @Id
```

**After (UserRepo.GetById.pg.sql):**
```sql
-- #exclude
DECLARE @Id INT = 1; -- Preserved from original!
-- /exclude
SELECT * FROM Users WHERE Id = $1
```

- **Naming**: Always `ClassName.QueryName.[extension]`. (e.g., `.pg.sql`, `.pgsql`).
- **Location**: `.sql` and `.cs` files **MUST** be in the same directory.
- **Exclusion Block Placement**: Prefer placing `-- #exclude` blocks at the top of the file for setup/variable declarations (e.g., `DECLARE @Id ...`).

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
3.  **Exclusion blocks as Parameter Docs**: Use `-- #exclude` blocks not just for test data, but to **document parameters** and their expected types/values for other developers.
    ```sql
    -- #exclude
    DECLARE @Status INT = 1; -- 1: Active, 0: Deleted
    DECLARE @Limit INT = 10;
    -- /exclude

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
