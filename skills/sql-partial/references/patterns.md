# SqlPartial Pattern Library

This guide provides examples of how to write SQL for various scenarios, focusing on the transition between single-DBMS and multi-DBMS support.

## 1. Zero-Boilerplate Abstraction with `[Sql]`

Instead of manually calling `.Get()` or `.Default`, use the `[Sql]` attribute from `SqlPartial` to handle DBMS resolution automatically.

**C# Repository:**
```csharp
using SqlPartial;

public partial class ProductRepo {
    // Required for [Sql] resolution (static or instance)
    public string SqlProviderName => "PostgreSql"; 

    public Task<Product> GetById([Sql] string query, int id) {
        // At runtime, 'query' is resolved to the correct DBMS string
        return connection.QuerySingleAsync<Product>(query, new { id });
    }
}
```

**Usage:**
```csharp
// The compiler selects the generated generic overload
var product = await repo.GetById(ProductRepo.SqlGetById, 123);
```

---

## 2. Single-DBMS to Multi-DBMS Transition

### Scenario: You have a SQL Server query and want to add PostgreSQL support.

**Original File: `UserRepo.GetActive.sql`** (Contains T-SQL)
```sql
-- Get active users
-- #testpart
DECLARE @MinScore INT = 100;
-- /testpart
SELECT * FROM Users WHERE IsActive = 1 AND Score >= @MinScore
```

**Step 1: Rename the original**
Rename `UserRepo.GetActive.sql` → `UserRepo.GetActive.ms.sql`.

**Step 2: Update content (Modernize)**
```sql
-- UserRepo.GetActive.ms.sql
-- Get active users
--# exclude
DECLARE @MinScore INT = 100;
-- /exclude
SELECT * FROM Users WHERE IsActive = 1 AND Score >= @MinScore
```

**Step 3: Create PostgreSQL override**
Create `UserRepo.GetActive.pg.sql`:
```sql
-- UserRepo.GetActive.pg.sql
-- Get active users
--# exclude
DECLARE @MinScore INT = 100; -- Preserved for testing
-- /exclude
SELECT * FROM Users WHERE IsActive = true AND Score >= :MinScore
```

**Step 4: Create Default Fallback (Recommended)**
Create `UserRepo.GetActive.sql` with generic SQL.

---

## 3. Using Exclusion Blocks

Exclusion blocks allow you to keep "Playground" code in your SQL file.

```sql
--# exclude
-- This part only runs in your SQL Editor
CREATE TABLE #TempUsers (Id INT);
INSERT INTO #TempUsers VALUES (1);
-- /exclude

SELECT * FROM Users WHERE Id IN (SELECT Id FROM #TempUsers) -- Error in most DBs, just an example!

--# exclude
DROP TABLE #TempUsers;
-- /exclude
```

---

## 4. Common DBMS Syntax Differences

When creating overrides, watch for:

| Feature | SQL Server (`ms`) | PostgreSQL (`pg`) |
| :--- | :--- | :--- |
| **Parameters** | `@param` | `:param` or `$1` |
| **Booleans** | `1` / `0` | `true` / `false` |
| **String Concatenation** | `+` | `||` |
| **Top/Limit** | `SELECT TOP 10 ...` | `SELECT ... LIMIT 10` |
| **Identity** | `SCOPE_IDENTITY()` | `RETURNING id` |

---

## 5. Usage Patterns (Unified by ISqlString)

### Auto (File-based)
Best for large queries.
```csharp
// Generated from UserRepo.GetUsers.sql
// Use [Sql] overload or explicit .Default/.Get()
await repo.Execute(UserRepo.SqlGetUsers);
```

### Manual Static (Inline)
Best for one-liners. **Note: Implicit conversion to string is removed.**
```csharp
// Manual Multi-DBMS in code
await QueryAsync(new SqlStrings(
    postgresql: "SELECT name FROM users LIMIT 10",
    sqlserver:  "SELECT TOP 10 name FROM users",
    @default:   "SELECT name FROM users"
));
```

### Manual Dynamic (Lazy Factory)
Best for SQL that needs runtime logic.
```csharp
var partitioned = new SqlDynamic(
    postgresql: () => $"SELECT * FROM logs_{DateTime.Now:yyyyMM}",
    @default: () => "SELECT * FROM logs"
);
await QueryAsync(partitioned);
```

---

## Troubleshooting Common Issues

### 1. `SqlStrings` not found
- **Cause**: The project hasn't been built, or the generator hasn't run.
- **Fix**: Run `dotnet build` or save a `.cs` file to trigger the Incremental Generator.

### 2. Properties missing on the class
- **Cause**: The naming convention was violated.
- **Check**: Is it `ClassName.QueryName.sql`? Note that `ClassName` must match the C# class name exactly (case-sensitive).

### 3. Namespace Mismatch
- **Cause**: `.sql` file is in a subdirectory, and you haven't declared the partial class in that same sub-namespace.
- **Fix**: Move the `.sql` file to the same folder as the `.cs` file, or ensure the `partial class` in C# uses the namespace matching the folder path.
