# Multi-DBMS Decision Guide

How to work across multiple database providers in the same project using
sql-partial's file override mechanism. ANSI SQL is the baseline; overrides
exist only to handle genuine incompatibilities.

---

## How sql-partial Selects a File

The generator picks the most specific file that exists for the current provider slug.
If no override exists, it falls back to the default `.sql` file.

```
orders/OrderRepo.Insert.sql          ← loaded when no override matches
orders/OrderRepo.Insert.pg.sql       ← loaded for the "pg" provider
orders/OrderRepo.Insert.ms.sql       ← loaded for the "ms" provider
```

This means the default `.sql` file is not dead weight — it is the live fallback
for any provider that does not have an explicit override. Write it in portable SQL.

---

## Decision Flow

```
Start: can this query be written in portable ANSI SQL?
            │
            ├─ YES → one .sql file. Done.
            │        (Pass timestamps, limits, and non-deterministic values
            │         as parameters from the app layer — eliminates most
            │         reasons to diverge.)
            │
            └─ NO → genuine syntax incompatibility between providers?
                        │
                        ├─ YES → create provider overrides.
                        │        Keep the default .sql as a documented fallback
                        │        or remove it if all supported providers have overrides.
                        │
                        └─ NO  → document the limitation in Notes and stay ANSI.
```

---

## What Stays ANSI (No Override Needed)

### Pass timestamps as parameters

Instead of calling a provider-specific function (`NOW()`, `GETUTCDATE()`,
`CURRENT_TIMESTAMP`), have the application compute the value and pass it in.
The query becomes identical across all providers.

```sql
-- One .sql file — works everywhere
-- =============================================================================
-- Query:   Insert a new session record
-- Returns: No rows
-- Notes:   @created_at and @expires_at are UTC, computed by the caller.
--          Do not substitute a DB timestamp function inside this file.
-- =============================================================================
-- #exclude
-- Replace <ts> with the appropriate type for your provider:
--   PostgreSQL: TIMESTAMPTZ    SQL Server: DATETIME2    MySQL: DATETIME
DECLARE @user_id    INT  = 1;
DECLARE @token      TEXT = 'test-token-abc';
DECLARE @created_at <ts> = '<utc_value>';
DECLARE @expires_at <ts> = '<utc_value>';
-- /exclude

INSERT INTO auth.sessions (user_id, token, created_at, expires_at)
VALUES (@user_id, @token, @created_at, @expires_at)
```

### ANSI pagination

`OFFSET … ROWS FETCH NEXT … ROWS ONLY` is supported by PostgreSQL 8.4+,
SQL Server 2012+, and recent versions of MySQL and SQLite.
Prefer it over `LIMIT / OFFSET` or `TOP N` in the default file.

```sql
ORDER BY created_at DESC, id DESC
OFFSET @page_offset ROWS FETCH NEXT @page_size ROWS ONLY
```

### Optional filters with NULL

The `(@param IS NULL OR col = @param)` pattern works on all providers.
No override needed for optional filter parameters.

---

## What Requires an Override

### 1. Parameter placeholder syntax

PostgreSQL uses positional `$1, $2, …`; most others use named `@param`.
This is the most common reason to create an override.

```
OrderRepo.GetById.sql      → WHERE id = @id
OrderRepo.GetById.pg.sql   → WHERE id = $1
```

The `.sql` default uses named parameters and serves as the non-PostgreSQL fallback.

### 2. INSERT and return the generated ID

No ANSI standard covers this. Syntax differs completely.

```sql
-- OrderRepo.Insert.sql  (ANSI fallback — does not return ID)
-- =============================================================================
-- Query:   Insert a new order record
-- Returns: No rows
-- Notes:   ANSI fallback. Does not return the generated id.
--          Use .pg.sql or .ms.sql for callers that need the id immediately.
--          Without an override the caller must issue a separate lookup.
-- =============================================================================
-- #exclude
DECLARE @customer_id INT  = 1;
DECLARE @total       DECIMAL(18,4) = 0.00;
DECLARE @created_at  <ts> = '<utc_value>';
-- /exclude

INSERT INTO sales.orders (customer_id, total_amount, created_at)
VALUES (@customer_id, @total, @created_at)
```

```sql
-- OrderRepo.Insert.pg.sql
-- =============================================================================
-- Query:   Insert a new order record and return its generated id (PostgreSQL)
-- Returns: Newly inserted orders.id
-- =============================================================================
-- #exclude
-- $1 = customer_id INT
-- $2 = total_amount DECIMAL
-- $3 = created_at   TIMESTAMPTZ
DECLARE @customer_id INT         = 1;
DECLARE @total       DECIMAL(18,4) = 0.00;
DECLARE @created_at  TIMESTAMPTZ = '<utc_value>';
-- /exclude

INSERT INTO sales.orders (customer_id, total_amount, created_at)
VALUES ($1, $2, $3)
RETURNING id
```

```sql
-- OrderRepo.Insert.ms.sql
-- =============================================================================
-- Query:   Insert a new order record and return its generated Id (SQL Server)
-- Returns: Newly inserted orders.Id via OUTPUT clause
-- =============================================================================
-- #exclude
DECLARE @customer_id INT          = 1;
DECLARE @total       DECIMAL(18,4) = 0.00;
DECLARE @created_at  DATETIME2    = '<utc_value>';
-- /exclude

INSERT INTO sales.orders (customer_id, total_amount, created_at)
OUTPUT INSERTED.id
VALUES (@customer_id, @total, @created_at)
```

---

### 3. Upsert (insert-or-update)

```sql
-- SessionRepo.Upsert.sql  (non-atomic fallback)
-- =============================================================================
-- Query:   Insert or refresh a session token
-- Returns: No rows
-- Notes:   NON-ATOMIC — concurrent writes on the same token can produce a
--          duplicate key error. Use .pg.sql or .ms.sql for production.
--          This file exists only as a fallback for unsupported providers.
-- =============================================================================
-- #exclude
DECLARE @user_id    INT  = 1;
DECLARE @token      TEXT = 'test-token';
DECLARE @expires_at <ts> = '<utc_value>';
DECLARE @updated_at <ts> = '<utc_value>';
-- /exclude

UPDATE auth.sessions
SET expires_at = @expires_at, updated_at = @updated_at
WHERE token = @token
-- Caller checks affected rows; if 0, issues a separate INSERT.
-- This two-step is not atomic — prefer .pg.sql or .ms.sql.
```

```sql
-- SessionRepo.Upsert.pg.sql
-- =============================================================================
-- Query:   Insert or refresh a session token (PostgreSQL)
-- Returns: No rows
-- Notes:   ON CONFLICT is atomic — safe under concurrent writes.
-- =============================================================================
-- #exclude
-- $1 = user_id    INT
-- $2 = token      TEXT
-- $3 = expires_at TIMESTAMPTZ
-- $4 = updated_at TIMESTAMPTZ
DECLARE @user_id    INT         = 1;
DECLARE @token      TEXT        = 'test-token';
DECLARE @expires_at TIMESTAMPTZ = '<utc_value>';
DECLARE @updated_at TIMESTAMPTZ = '<utc_value>';
-- /exclude

INSERT INTO auth.sessions (user_id, token, expires_at, updated_at)
VALUES ($1, $2, $3, $4)
ON CONFLICT (token)
    DO UPDATE SET
        expires_at = EXCLUDED.expires_at,
        updated_at = EXCLUDED.updated_at
```

```sql
-- SessionRepo.Upsert.ms.sql
-- =============================================================================
-- Query:   Insert or refresh a session token (SQL Server)
-- Returns: No rows
-- Notes:   MERGE is atomic within a single statement.
-- =============================================================================
-- #exclude
DECLARE @user_id    INT          = 1;
DECLARE @token      NVARCHAR(256) = N'test-token';
DECLARE @expires_at DATETIME2    = '<utc_value>';
DECLARE @updated_at DATETIME2    = '<utc_value>';
-- /exclude

MERGE INTO auth.sessions AS target
USING (VALUES (@user_id, @token, @expires_at, @updated_at))
    AS source (user_id, token, expires_at, updated_at)
    ON target.token = source.token
WHEN MATCHED THEN
    UPDATE SET expires_at = source.expires_at,
               updated_at = source.updated_at
WHEN NOT MATCHED THEN
    INSERT (user_id, token, expires_at, updated_at)
    VALUES (source.user_id, source.token, source.expires_at, source.updated_at);
```

---

### 4. Full-text search

The ANSI fallback uses `LIKE`, which cannot use full-text indexes.
Override when dataset size makes this unacceptable.

```sql
-- ArticleRepo.SearchByKeyword.sql  (ANSI fallback — LIKE only)
-- =============================================================================
-- Query:   Search articles by keyword across title and body
-- Returns: Fields matching ArticleSearchResult model
-- Notes:   FALLBACK — uses LIKE; no full-text index benefit.
--          Acceptable for tables with fewer than ~50k rows.
--          Use .pg.sql or .ms.sql for production scale.
-- =============================================================================
-- #exclude
DECLARE @keyword     TEXT = 'budget';
DECLARE @page_size   INT  = 20;
DECLARE @page_offset INT  = 0;
-- /exclude

SELECT id, title, summary
FROM content.articles
WHERE (title   LIKE '%' || @keyword || '%'
   OR  summary LIKE '%' || @keyword || '%')
  AND deleted_at IS NULL
ORDER BY published_at DESC, id DESC
OFFSET @page_offset ROWS FETCH NEXT @page_size ROWS ONLY
```

```sql
-- ArticleRepo.SearchByKeyword.pg.sql
-- =============================================================================
-- Query:   Search articles by keyword with full-text ranking (PostgreSQL)
-- Returns: Fields matching ArticleSearchResult model, ordered by relevance
-- Notes:   Requires GIN index on search_vector:
--            CREATE INDEX ix_articles_fts ON content.articles USING GIN (search_vector);
--          search_vector maintained by trigger or generated column.
--          $1 uses tsquery syntax: 'budget & forecast', 'budget | estimate'.
-- =============================================================================
-- #exclude
-- $1 = keyword     TEXT  (tsquery syntax)
-- $2 = page_size   INT
-- $3 = page_offset INT
DECLARE @keyword     TEXT = 'budget & forecast';
DECLARE @page_size   INT  = 20;
DECLARE @page_offset INT  = 0;
-- /exclude

SELECT
    id,
    title,
    ts_headline('english', summary, to_tsquery('english', $1), 'MaxWords=25') AS excerpt,
    ts_rank(search_vector, to_tsquery('english', $1))                          AS rank
FROM content.articles
WHERE search_vector @@ to_tsquery('english', $1)
  AND deleted_at IS NULL
ORDER BY rank DESC, id DESC
LIMIT $2 OFFSET $3
```

```sql
-- ArticleRepo.SearchByKeyword.ms.sql
-- =============================================================================
-- Query:   Search articles by keyword with full-text ranking (SQL Server)
-- Returns: Fields matching ArticleSearchResult model, ordered by relevance
-- Notes:   Requires Full-Text Index on (title, summary):
--            CREATE FULLTEXT INDEX ON content.articles(title, summary)
--                KEY INDEX PK_articles;
--          @keyword uses CONTAINS syntax: '"budget" AND "forecast"', '"budg*"'.
-- =============================================================================
-- #exclude
DECLARE @keyword     NVARCHAR(256) = N'"budget" AND "forecast"';
DECLARE @page_size   INT           = 20;
DECLARE @page_offset INT           = 0;
-- /exclude

SELECT
    a.id,
    a.title,
    a.summary,
    ft.[RANK]
FROM content.articles a
    INNER JOIN FREETEXTTABLE(content.articles, (title, summary), @keyword) AS ft
        ON a.id = ft.[KEY]
WHERE a.deleted_at IS NULL
ORDER BY ft.[RANK] DESC, a.id DESC
OFFSET @page_offset ROWS FETCH NEXT @page_size ROWS ONLY
```

---

## Full Syntax Difference Reference

| Feature | ANSI default `.sql` | PostgreSQL `.pg.sql` | SQL Server `.ms.sql` | MySQL `.my.sql` |
|---|---|---|---|---|
| **Param placeholder** | `@param` | `$1, $2, …` | `@param` | `@param` or `?` |
| **Current UTC time** | pass as parameter | `NOW()` | `GETUTCDATE()` | `UTC_TIMESTAMP()` |
| **String concat** | `\|\|` | `\|\|` | `+` | `CONCAT()` |
| **Boolean literal** | `1` / `0` or `TRUE`/`FALSE` | `true` / `false` | `1` / `0` | `1` / `0` |
| **Top N rows** | `FETCH NEXT N ROWS ONLY` | `LIMIT N` or ANSI | `TOP N` or ANSI | `LIMIT N` |
| **Insert + return ID** | no standard | `RETURNING id` | `OUTPUT INSERTED.id` | `LAST_INSERT_ID()` |
| **Upsert** | non-atomic two-step | `ON CONFLICT DO UPDATE` | `MERGE` | `ON DUPLICATE KEY UPDATE` |
| **Case-insensitive search** | `upper(col) = upper(@v)` | `col ILIKE $1` | `col LIKE @v` (default CI) | `col LIKE @v` (default CI) |
| **JSON access** | not standardized | `->` / `->>` / `jsonb_*` | `JSON_VALUE()` / `OPENJSON()` | `JSON_EXTRACT()` |
| **Full-text search** | `LIKE` (no FT index) | `to_tsvector / to_tsquery` | `CONTAINS / FREETEXT` | `MATCH … AGAINST` |
| **Regex** | not standardized | `~` / `~*` / `SIMILAR TO` | no native | `REGEXP` |
| **Array param** | not standardized | `= ANY($1::int[])` | table-valued param | not supported |

---

## Syncing `-- #exclude` Across Provider Files

Every provider file must be independently runnable in its own SQL editor.
When you create an override, copy the `-- #exclude` block from the default file
and adjust the data types for that provider.

For PostgreSQL files using `$N` placeholders, add a parameter map comment
at the top of the block so the file is self-documenting:

```sql
-- #exclude
-- $1 = account_id  INT
-- $2 = status      INT     -- 0=Pending 1=Active 2=Suspended
-- $3 = page_size   INT     -- max 100
-- $4 = page_offset INT     -- 0-based
DECLARE @account_id  INT = 1;
DECLARE @status      INT = 1;
DECLARE @page_size   INT = 20;
DECLARE @page_offset INT = 0;
-- /exclude
```

Type mapping reference for common fields:

| Concept | PostgreSQL | SQL Server | MySQL |
|---|---|---|---|
| Integer ID | `INT` | `INT` | `INT` |
| Text (short) | `TEXT` or `VARCHAR(n)` | `NVARCHAR(n)` | `VARCHAR(n)` |
| Timestamp with TZ | `TIMESTAMPTZ` | `DATETIME2` | `DATETIME` |
| Decimal amount | `DECIMAL(18,4)` | `DECIMAL(18,4)` | `DECIMAL(18,4)` |
| Boolean flag | `BOOLEAN` | `BIT` | `TINYINT(1)` |
| Large text | `TEXT` | `NVARCHAR(MAX)` | `LONGTEXT` |
