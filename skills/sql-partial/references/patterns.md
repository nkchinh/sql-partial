# SqlPartial Pattern Library

This guide provides examples of how to write SQL for various scenarios, focusing on the transition between single-DBMS and multi-DBMS support.

## 1. Single-DBMS to Multi-DBMS Transition

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
--#exclude
DECLARE @MinScore INT = 100;
--/exclude
SELECT * FROM Users WHERE IsActive = 1 AND Score >= @MinScore
```

**Step 3: Create PostgreSQL override**
Create `UserRepo.GetActive.pg.sql`:
```sql
-- UserRepo.GetActive.pg.sql
-- Get active users
--#exclude
DECLARE @MinScore INT = 100;
--/exclude
SELECT * FROM Users WHERE IsActive = true AND Score >= :MinScore
```

**Step 4: Create Fallback (Optional but recommended)**
Create `UserRepo.GetActive.sql` with generic SQL.

---

## 2. Using Exclusion Blocks

Exclusion blocks allow you to keep "Playground" code in your SQL file.

```sql
--#exclude
-- This part only runs in your SQL Editor
CREATE TABLE #TempUsers (Id INT);
INSERT INTO #TempUsers VALUES (1);
--/exclude

SELECT * FROM Users WHERE Id IN (SELECT Id FROM #TempUsers) -- Error in most DBs, just an example!

--#exclude
DROP TABLE #TempUsers;
--/exclude
```

---

## 3. Common DBMS Syntax Differences

When creating overrides, watch for:

| Feature | SQL Server (`ms`) | PostgreSQL (`pg`) |
| :--- | :--- | :--- |
| **Parameters** | `@param` | `:param` or `$1` |
| **Booleans** | `1` / `0` | `true` / `false` |
| **String Concatenation** | `+` | `\|\|` |
| **Top/Limit** | `SELECT TOP 10 ...` | `SELECT ... LIMIT 10` |
| **Identity** | `SCOPE_IDENTITY()` | `RETURNING id` |

---

## 4. Usage Patterns (Unified by ISqlString)

### Auto (File-based)
Best for large queries.
```csharp
// Generated from UserRepo.GetUsers.sql
await QueryAsync(UserRepo.SqlGetUsers);
```

### Manual Static (Inline)
Best for one-liners.
```csharp
// Pure fallback string (implicitly converts to SqlStrings)
await QueryAsync("SELECT count(*) FROM users");

// Manual Multi-DBMS in code
await QueryAsync(new SqlStrings(
    postgresql: "SELECT name FROM users LIMIT 10",
    sqlserver:  "SELECT TOP 10 name FROM users",
    fallback:   "SELECT name FROM users"
));
```

### Manual Dynamic (Lazy Factory)
Best for SQL that needs runtime logic.
```csharp
var partitioned = new SqlDynamic(
    postgresql: () => $"SELECT * FROM logs_{DateTime.Now:yyyyMM}",
    fallback: () => "SELECT * FROM logs"
);
await QueryAsync(partitioned);
```
