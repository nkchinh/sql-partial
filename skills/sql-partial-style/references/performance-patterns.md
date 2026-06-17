# Performance Patterns

Patterns for index-friendly predicates, safe aggregations, and scalable write operations.
Provider behavior differences are noted where they affect how to write the query.

All examples follow the comment rule: full-line `--` comments outside `-- #exclude` blocks;
end-of-line comments only inside `-- #exclude` where the whole block is stripped.

---

## 1. Sargable Predicates

An indexed column wrapped in a function forces the engine to evaluate the function
per row — the index cannot be used for a seek.
The fix: move the transformation to the parameter, or rewrite as a range comparison.

```sql
-- WRONG — all examples force a full scan
WHERE extract(year from created_at) = 2024
WHERE upper(email)   = upper(@email)
WHERE length(notes)  > 0
-- common in PostgreSQL code but equally non-sargable:
WHERE date(created_at) = @some_date

-- CORRECT — index seek possible
WHERE created_at >= '2024-01-01' AND created_at < '2025-01-01'
-- caller normalizes to lowercase before passing
WHERE email       = @normalized_email
-- caller computes day boundaries from the date parameter
WHERE created_at >= @day_start AND created_at < @day_end
WHERE notes IS NOT NULL AND notes <> ''
```

**Implicit type conversion** silently destroys sargability when a parameter type
does not match the column type. Every major DBMS will scan the column to perform
the conversion rather than seek the index — no warning, no error.

```sql
-- #exclude
-- @account_id must be INT, not TEXT/VARCHAR.
-- A string param causes an implicit cast on accounts.id for every row —
-- the index seek is silently replaced by a scan.
DECLARE @account_id INT = 1;
-- /exclude
```

When a query has non-trivial index requirements, record them in the Notes header:

```sql
-- Notes:   Relies on index on (account_id, status) INCLUDE (id, amount, created_at).
--          Without a covering index the query performs a key lookup per matched row.
```

---

## 2. EXISTS vs COUNT vs IN for Existence Checks

`COUNT(*)` reads every qualifying row to produce a number.
`EXISTS` stops at the first match. On large tables the difference is significant.

```sql
-- WRONG — reads all qualifying rows
WHERE (SELECT COUNT(*) FROM sales.order_lines WHERE order_id = o.id) > 0

-- CORRECT — stops at the first row found
WHERE EXISTS (
    SELECT 1 FROM sales.order_lines ol
    WHERE ol.order_id = o.id
)
```

`IN` with a large subquery is often less optimal than `EXISTS`:

```sql
-- FRAGILE — optimizer may estimate the subquery result set badly at scale
WHERE account_id IN (SELECT account_id FROM billing.invoices WHERE overdue = true)

-- PREFERRED — clearer intent, more predictable plan
WHERE EXISTS (
    SELECT 1 FROM billing.invoices i
    WHERE i.account_id = a.id
      AND i.overdue    = true
)
```

---

## 3. Aggregation Traps: NULL Propagation and COUNT Semantics

`SUM`, `AVG`, `MIN`, `MAX` return NULL — not zero — when all input rows are NULL
or no rows match. `COUNT(col)` skips NULL values; `COUNT(*)` does not.
These behaviors are identical across all providers.

```sql
SELECT
    -- SUM returns NULL when there are no lines or all prices are NULL;
    -- COALESCE ensures the caller always receives a numeric value
    COALESCE(SUM(unit_price * qty), 0)  AS total_amount,

    -- COUNT(*) includes every row regardless of NULLs in any column
    COUNT(*)                             AS line_count,

    -- COUNT(col) skips rows where discount_code IS NULL — use deliberately
    COUNT(discount_code)                 AS discounted_line_count

FROM sales.order_lines
WHERE order_id   = @order_id
  AND voided_at IS NULL
```

Division guard — works on all providers:

```sql
CAST(numerator AS DECIMAL(18,4)) / NULLIF(denominator, 0) AS ratio
```

---

## 4. Eliminate Correlated Subqueries in SELECT

A correlated subquery in the `SELECT` column list re-executes for every output row.
Rewrite as a single aggregation pass using a derived table or CTE.

```sql
-- WRONG — two passes per row; 2,000 extra executions for a 1,000-row result set
SELECT
    a.id,
    a.name,
    (SELECT COUNT(*)    FROM sales.orders   WHERE account_id = a.id) AS order_count,
    (SELECT SUM(amount) FROM billing.payments WHERE account_id = a.id) AS total_paid
FROM sales.accounts a
WHERE a.active = true

-- CORRECT — single aggregation pass per derived table
SELECT
    a.id,
    a.name,
    COALESCE(o.order_count, 0)  AS order_count,
    COALESCE(p.total_paid,  0)  AS total_paid
FROM sales.accounts a
    LEFT JOIN (
        SELECT account_id, COUNT(*) AS order_count
        FROM sales.orders
        GROUP BY account_id
    ) o ON o.account_id = a.id
    LEFT JOIN (
        SELECT account_id, SUM(amount) AS total_paid
        FROM billing.payments
        WHERE status = 'settled'
        GROUP BY account_id
    ) p ON p.account_id = a.id
WHERE a.active = true
```

---

## 5. EXISTS Over DISTINCT to Eliminate Join-Introduced Duplicates

`SELECT DISTINCT` on a one-to-many join masks the symptom rather than fixing it.
It forces a sort or hash deduplication over the full result set.

```sql
-- WRONG — DISTINCT hides duplicates introduced by the one-to-many roles join
SELECT DISTINCT u.id, u.email
FROM auth.users u
    INNER JOIN auth.user_roles ur ON ur.user_id = u.id
WHERE ur.role_id IN (1, 2, 3)

-- CORRECT — EXISTS answers the question without producing duplicates
SELECT u.id, u.email
FROM auth.users u
WHERE EXISTS (
    SELECT 1 FROM auth.user_roles ur
    WHERE ur.user_id = u.id
      AND ur.role_id IN (1, 2, 3)
)
```

---

## 6. CTE Materialization — Provider Differences

Whether a CTE is cached or inlined at each reference differs by provider.
Document the risk when a CTE is expensive and referenced more than once.

**PostgreSQL** — CTEs are materialized by default (since v12 with some exceptions).
Force behavior explicitly when needed:

```sql
-- force materialization when the planner would inline and re-evaluate
WITH expensive_base AS MATERIALIZED (
    SELECT ... FROM large_table WHERE ...
)
SELECT ... FROM expensive_base
UNION ALL
-- reads the cached result; not re-evaluated
SELECT ... FROM expensive_base WHERE ...
```

**SQL Server** — CTEs are never guaranteed to materialize.
When the execution plan shows repeated evaluation, use a temp table:

```sql
-- Notes:   On SQL Server, base_cte may be evaluated more than once if referenced
--          in multiple downstream CTEs. If the execution plan confirms this,
--          replace with SELECT … INTO #base_cte and reference #base_cte instead.
```

**MySQL** — CTEs supported from v8.0; the `NO_MERGE` hint forces materialization.

For cross-provider portability: if the CTE is expensive and referenced more than once,
document the risk in the Notes header and consider a provider-specific override.

---

## 7. Batch Writes to Avoid Lock Contention

Processing large datasets in a single statement holds locks for the full duration.
Process in batches with a delay between iterations.

```sql
-- =============================================================================
-- Query:   Purge one batch of expired audit entries
-- Returns: Affected row count; caller loops until 0
-- Notes:   Batch size of 200–500 keeps statements below the lock escalation
--          threshold on SQL Server (~5,000 row locks → table lock).
--          Caller should delay ~100ms between iterations.
--          Condition prevents re-deletion on re-run.
-- =============================================================================
-- #exclude
DECLARE @cutoff_date <ts>  = '<utc_value>';
DECLARE @batch_size  INT   = 300;
-- /exclude

-- PostgreSQL / MySQL syntax:
DELETE FROM audit.entries
WHERE recorded_at < @cutoff_date
  AND exported    = true
LIMIT @batch_size

-- SQL Server requires a provider override (.ms.sql):
-- DELETE TOP (@batch_size) FROM audit.entries
-- WHERE recorded_at < @cutoff_date AND exported = 1
```

When the project targets multiple providers, create a `.ms.sql` override for the
DELETE syntax rather than leaving dual-syntax comments in the default file.

---

## 8. Pagination: Offset vs Keyset

`OFFSET N` reads and discards N rows every call — linear cost at depth.
Keyset pagination seeks directly to the cursor position regardless of depth.

### Offset — simple, supports random access, degrades at depth

```sql
-- =============================================================================
-- Notes:   Offset pagination. Acceptable under ~100k qualifying rows or when
--          random page access is required. Performance degrades linearly with
--          @page_offset — consider keyset for feeds and sync endpoints.
-- =============================================================================
-- #exclude
DECLARE @page_size   INT = 20;
DECLARE @page_offset INT = 0;
-- /exclude

SELECT id, title, created_at
FROM content.posts
WHERE deleted_at IS NULL
ORDER BY created_at DESC, id DESC
OFFSET @page_offset ROWS FETCH NEXT @page_size ROWS ONLY
```

### Keyset — scales to any depth, sequential access only

```sql
-- =============================================================================
-- Notes:   Keyset pagination. @last_created_at and @last_id are the values from
--          the last row of the previous page. Pass (NULL, 0) for the first page.
--          Does not support random page access — suitable for infinite scroll,
--          cursor APIs, and incremental sync endpoints.
-- =============================================================================
-- #exclude
DECLARE @last_created_at <ts>  = NULL;   -- NULL = first page
DECLARE @last_id         INT   = 0;
DECLARE @page_size       INT   = 20;
-- /exclude

SELECT id, title, created_at
FROM content.posts
WHERE
    deleted_at IS NULL
    AND (
        @last_created_at IS NULL
        OR created_at < @last_created_at
        OR (created_at = @last_created_at AND id < @last_id)
    )
ORDER BY created_at DESC, id DESC
FETCH FIRST @page_size ROWS ONLY
```

---

## 9. Parameter Sniffing (SQL Server only)

SQL Server caches the plan compiled for the first parameter values it sees.
If those values are unrepresentative (highly selective test data), subsequent
calls with typical values may use a suboptimal plan.
This risk does not apply to PostgreSQL (per-session plans) or MySQL.

```sql
-- Notes:   [SQL Server] @account_id selectivity varies widely — some accounts
--          have 10 rows, others 200,000. If production query time becomes erratic
--          (same query, 10× variance), add OPTION (OPTIMIZE FOR UNKNOWN) to force
--          a parameter-agnostic plan at the cost of a slightly less optimal plan
--          for any single value.
SELECT ...
FROM sales.orders
WHERE account_id = @account_id
-- OPTION (OPTIMIZE FOR UNKNOWN)   -- uncomment if parameter sniffing is confirmed
```

Keep this annotation in the `.ms.sql` override, not in the ANSI default file.

---

## 10. Covering Index Documentation

When a query depends on a specific index, document it in the Notes header.

```sql
-- =============================================================================
-- Query:   Load invoice list for an account filtered by status
-- Returns: Fields matching InvoiceListItem model
-- Notes:   Requires a covering index on (account_id, status):
--
--            PostgreSQL:
--              CREATE INDEX ix_invoices_account_status
--                  ON billing.invoices (account_id, status)
--                  INCLUDE (id, total_amount, issued_at);
--
--            SQL Server:
--              CREATE INDEX IX_Invoices_AccountId_Status
--                  ON billing.invoices (account_id, status)
--                  INCLUDE (id, total_amount, issued_at);
--
--          Without this index the query performs a key lookup per matched row.
--          Correct results either way; approximately 8× slower above 500k rows.
-- =============================================================================
-- #exclude
DECLARE @account_id  INT = 1;
DECLARE @status      INT = 1;   -- 0=Draft 1=Issued 2=Paid 3=Void
DECLARE @page_size   INT = 20;
DECLARE @page_offset INT = 0;
-- /exclude

SELECT
    i.id,
    i.total_amount,
    i.issued_at
FROM billing.invoices i
WHERE
    i.account_id = @account_id
    AND i.status     = @status
    AND i.deleted_at IS NULL
ORDER BY i.issued_at DESC, i.id DESC
OFFSET @page_offset ROWS FETCH NEXT @page_size ROWS ONLY
```
