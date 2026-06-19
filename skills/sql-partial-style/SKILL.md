---
name: sql-partial-style
source: https://github.com/nkchinh/sql-partial
description: |
  SQL code style and writing standards for .sql files used with SqlPartial.Generator.
  Combines sql-partial's build-time features with production SQL best practices.
  DBMS-neutral by default; provider-specific guidance is in the reference files.

  Use this skill when:
  - Writing or reviewing content inside a .sql file for sql-partial
  - Advising on file headers, CTE documentation, or inline comments
  - Deciding what belongs in -- #exclude versus plain -- comments
  - Optimizing SQL for production correctness and performance
  - User asks "how should I write this SQL", "is this query correct", "review this file"

  For file creation, class naming, and generator configuration,
  see the td-sql-partial skill — this skill covers content quality only.

  Triggers: sql style, sql format, write sql, -- #exclude, sql comment, sql review,
            query style, sql file header, sql partial content, CTE documentation.
---

# SQL Code Style for SqlPartial.Generator

## What sql-partial Strips at Build Time

Understanding exactly what gets stripped determines every comment and documentation
decision in this guide.

| Comment form | Example | Stripped? |
|---|---|---|
| Full-line `--` comment | `-- this is a note` (entire line) | ✅ Yes |
| `-- #exclude … -- /exclude` block | entire block | ✅ Yes |
| `-- #testpart … -- /testpart` block | entire block | ✅ Yes |
| End-of-line `--` comment | `col1,  -- note here` | ❌ No — survives into C# string |
| Block comment | `/* this note */` | ❌ No — survives into C# string |
| End-of-line comment **inside** exclude | `DECLARE @x INT = 1;  -- note` | ✅ Yes (whole block stripped) |

**Practical rule**: outside of `-- #exclude` blocks, use only full-line `--` comments
for all documentation. End-of-line comments and `/* */` comments survive the build
and appear in the generated C# SQL string — avoid them outside exclude blocks.

---

## The Two Documentation Tools

**Full-line `--` comments** are the primary documentation tool.
They are stripped unconditionally and cost nothing at runtime.
Use them for file headers, CTE descriptions, and any explanatory note
placed on its own line before or between code lines.

**`-- #exclude … -- /exclude`** strips entire executable blocks.
Use it for `DECLARE` statements, temp table setup, and verification queries —
code that runs in the editor but must not enter the C# string.
Inside this block, end-of-line comments are safe because the whole block is stripped.

```sql
-- This line is documentation. Stripped. Never reaches C#.
-- Another documentation line. Same.

-- #exclude
DECLARE @status INT = 1;    -- enum: 0=Draft 1=Active 2=Archived  (safe: whole block stripped)
DECLARE @limit  INT = 20;   -- max 100; caller enforces            (safe: whole block stripped)
-- /exclude

SELECT id, status
FROM sales.orders
-- WHERE clause filters by both conditions below
WHERE status = @status
  AND deleted_at IS NULL
```

---

## Rule 1 — Every File Starts with a Structured Header

The header is the primary contract between the query and its C# callers.
Plain `--` comment lines, always stripped, outside any exclude block.

```sql
-- =============================================================================
-- Query:   <Imperative verb phrase — what this query does>
-- Returns: <Result shape — model name, column list, or "no rows">
-- Notes:   <Performance requirements, index dependencies, business rules,
--           known edge cases, intentional design decisions worth preserving>
-- =============================================================================
```

**Query** starts with an imperative verb: *Load*, *Insert*, *Update*, *Delete*,
*Check*, *Count*, *Search*, *Upsert*. One sentence, precise about subject and filter.

**Returns** names the C# model the result maps to, or describes the column shape.
Write "no rows" for pure write queries.

**Notes** is where production knowledge lives permanently. Write it for the developer
who will modify this query months from now with no other context:

```sql
-- =============================================================================
-- Query:   Load paginated order list for an account filtered by status
-- Returns: Fields matching OrderSummaryItem model, ordered newest first
-- Notes:   Requires covering index on (account_id, status) INCLUDE (id, total, created_at).
--          Without it the query performs a key lookup per matched row.
--          @status = NULL is not supported — caller must pass an explicit value.
-- =============================================================================
```

Notes is optional only when the query is genuinely self-explanatory.
When in doubt, write it.

---

## Rule 2 — `-- #exclude` Contains Only Runnable Test Code

`-- #exclude` is a lightweight development harness embedded in the file.
Its content runs in a SQL editor and never appears in the generated C# string.
Do not put narrative text inside it — the block signals "this SQL runs in development."

### Annotate every DECLARE — end-of-line comments are safe inside the block

```sql
-- #exclude
DECLARE @account_id  INT = 1;
DECLARE @status      INT = 1;      -- 0=Pending 1=Active 2=Suspended 3=Cancelled
DECLARE @page_size   INT = 20;     -- max 100; caller enforces before invoking
DECLARE @page_offset INT = 0;      -- 0-based; page 2 = 1 × @page_size
-- /exclude
```

Document UTC convention for timestamps inside the block:

```sql
-- #exclude
DECLARE @start_date <ts> = '<utc_value>';   -- inclusive; UTC; computed by caller
DECLARE @end_date   <ts> = '<utc_value>';   -- exclusive; UTC; computed by caller
-- /exclude
```

### Setup and teardown for complex queries

```sql
-- #exclude
DECLARE @invoice_id INT = 42;
CREATE TABLE #line_items (product_id INT, qty INT, unit_price DECIMAL(18,4));
INSERT INTO #line_items VALUES (10, 2, 49.99), (11, 1, 129.00);
-- /exclude

WITH line_totals AS ( ... )
SELECT ...

-- #exclude
DROP TABLE IF EXISTS #line_items;
-- /exclude
```

---

## Rule 3 — Full-Line Comments Carry All Documentation Outside Exclude Blocks

Because end-of-line comments survive the build, **all documentation outside
`-- #exclude` must be written as full-line `--` comments** — placed on the line
immediately before the code they describe.

```sql
-- WRONG — end-of-line comment survives into the C# string
LEFT JOIN sales.coupons c ON c.id = o.coupon_id   -- optional: NULL when no coupon

-- CORRECT — full-line comment is stripped at build
-- optional join: NULL when no coupon applied to this order
LEFT JOIN sales.coupons c ON c.id = o.coupon_id
```

```sql
-- WRONG — end-of-line comment survives
WHERE o.deleted_at IS NULL   -- soft delete guard

-- CORRECT
-- soft delete guard
WHERE o.deleted_at IS NULL
```

This applies equally to computed columns, JOIN conditions, WHERE conditions,
ORDER BY clauses, and any other position in the query body.

---

## Rule 4 — CTE Documentation with `-- ---` Separators

Each named CTE gets a documentation block using `-- ---` separators.
Full-line comments — stripped at build, outside any exclude block.

### Single CTE

```sql
-- -----------------------------------------------------------------------------
-- CTE: <name>
-- <What this CTE represents — scope and source, one or two sentences.>
-- <Filtering rationale — WHY rows are included or excluded, not just that they are.>
-- <Maintenance notes: join intent, mirror conditions, edge cases.>
-- -----------------------------------------------------------------------------
cte_name AS (
    SELECT ...
)
```

### UNION ALL — numbered streams

When a CTE merges multiple sources, list streams in the header and annotate
each branch with full-line comments:

```sql
-- -----------------------------------------------------------------------------
-- CTE: all_billable_items
-- Combines billable items from three mutually exclusive sources:
--   [0] Standard order lines  — products purchased directly
--   [1] Service fees          — flat fees attached to an order at checkout
--   [2] Credit adjustments    — negative-value corrections applied post-purchase;
--                               only included when approved = true
-- -----------------------------------------------------------------------------
all_billable_items AS (

    -- -------------------------------------------------------------------------
    -- Stream [0]: Standard order lines
    -- -------------------------------------------------------------------------
    SELECT
        ol.order_id,
        ol.description,
        ol.unit_price * ol.qty  AS amount,
        0                       AS source_order
    FROM sales.order_lines ol
    WHERE ol.voided_at IS NULL

    UNION ALL

    -- -------------------------------------------------------------------------
    -- Stream [1]: Service fees
    -- INNER JOIN is intentional — a fee row without a matching order is a data
    -- integrity problem and must not silently appear in billing output.
    -- -------------------------------------------------------------------------
    SELECT
        sf.order_id,
        sf.fee_label            AS description,
        sf.amount,
        1                       AS source_order
    FROM sales.service_fees sf
        INNER JOIN sales.orders o ON o.id = sf.order_id
    WHERE sf.waived_at IS NULL

    UNION ALL

    -- -------------------------------------------------------------------------
    -- Stream [2]: Credit adjustments
    -- Filter mirrors Stream [0] WHERE clause — if Stream [0] changes its
    -- voided_at condition, update the NOT EXISTS subquery here in sync.
    -- -------------------------------------------------------------------------
    SELECT
        ca.order_id,
        ca.reason               AS description,
        -- negative value: reduces the invoice total
        -ca.amount              AS amount,
        2                       AS source_order
    FROM sales.credit_adjustments ca
    WHERE ca.approved = true
      AND NOT EXISTS (
          SELECT 1 FROM sales.order_lines ol
          WHERE ol.order_id  = ca.order_id
            AND ol.voided_at IS NULL
      )
)
```

---

## Rule 5 — Document Design Decisions, Not Code Narration

Place full-line comments before the code they describe.
A comment that restates the code adds noise. A comment that preserves a design
decision or constraint that is not obvious from the code adds permanent value.

```sql
-- Noise — the code already says this
-- join to coupons table
LEFT JOIN sales.coupons c ON c.id = o.coupon_id

-- Value — explains intent and consequence
-- optional: NULL when no coupon was applied; coupon_amount in SELECT handles NULL
LEFT JOIN sales.coupons c ON c.id = o.coupon_id
```

Common patterns worth documenting:

```sql
-- Intentional JOIN type — explain the consequence of the choice
-- INNER JOIN is intentional: an order with no line items is invalid for invoicing
INNER JOIN sales.order_lines ol ON ol.order_id = o.id

-- Mirror condition
-- mirrors the WHERE clause in Stream [0] — update both when Stream [0] changes
WHERE NOT EXISTS (
    SELECT 1 FROM sales.order_lines ol
    WHERE ol.order_id = ca.order_id AND ol.voided_at IS NULL
)

-- Sort column not returned to caller
-- source_order ensures stable sort within an invoice (lines→fees→credits);
-- not returned to the caller
ORDER BY source_order, created_at, id

-- Intentionally NULL output column
-- members on the guest plan have no stored photo;
-- NULL suppresses the photo sync step in the application layer
NULL AS profile_photo,

-- Business rule
-- policy: contracts expiring today remain valid for today's transactions
-- (half-open start, closed end)
AND contract_end_date >= @as_of_date
```

---

## Rule 6 — The Application-SQL Boundary

SQL retrieves and mutates data. Everything else belongs in the application layer.

**Pass into SQL as parameters — never compute inside the query:**

| Category | Wrong | Correct |
|---|---|---|
| Current time | `WHERE created_at > <NOW_FUNC> - 30` | Pass `@cutoff_date` (UTC, from app) |
| Time boundaries | `WHERE extract(year from created_at) = 2024` | Pass `@start_date` / `@end_date` |
| Pagination limit | Hardcoded `LIMIT 100` | Pass `@page_size` with app-enforced max |
| Status default | `COALESCE(@status, 1)` inside SQL | App passes an explicit value |

Document the boundary contract in `-- #exclude` (end-of-line comments safe here):

```sql
-- #exclude
-- @cutoff_date: UTC; caller computes as UtcNow.AddDays(-90).
-- Do not substitute a DB timestamp function:
--   (1) server local time is not UTC
--   (2) function calls on indexed columns prevent index seeks
DECLARE @cutoff_date <ts> = '<utc_value>';
-- /exclude
```

---

## Rule 7 — Sargable Predicates: Keep WHERE Index-Friendly

Wrapping an indexed column in a function forces a full scan regardless of indexes.

```sql
-- WRONG — full scan
WHERE extract(year from created_at) = 2024
WHERE upper(email)   = upper(@email)
WHERE length(notes)  > 0

-- CORRECT — index seek possible
WHERE created_at >= '2024-01-01' AND created_at < '2025-01-01'
WHERE email       = @normalized_email
WHERE notes IS NOT NULL AND notes <> ''
```

**Implicit type conversion** silently destroys sargability when a parameter type
does not match the column type. Document type expectations in `-- #exclude`:

```sql
-- #exclude
-- @account_id must be INT, not TEXT/VARCHAR — a string param causes an implicit
-- cast on accounts.id for every row, replacing the index seek with a full scan
DECLARE @account_id INT = 1;
-- /exclude
```

---

## Rule 8 — NULL Semantics: Three-Valued Logic in Practice

SQL NULL means *unknown*. NULL compared to anything yields UNKNOWN, filtered out by WHERE.

```sql
-- never matches rows where assigned_to IS NULL
WHERE assigned_to = @assigned_to

-- optional filter: skip condition when parameter is NULL
WHERE (@assigned_to IS NULL OR assigned_to = @assigned_to)

-- ANSI: match NULL-to-NULL explicitly
WHERE assigned_to IS NOT DISTINCT FROM @assigned_to
```

`SUM`, `AVG`, `MIN`, `MAX` return NULL when no rows match — apply `COALESCE`:

```sql
SELECT
    COALESCE(SUM(unit_price * qty), 0)  AS total_amount,
    COUNT(*)                             AS line_count,
    -- COUNT(col) skips NULLs; COUNT(*) does not — use deliberately
    COUNT(discount_code)                 AS discounted_line_count
FROM sales.order_lines
WHERE order_id = @order_id
```

Division guard:

```sql
CAST(numerator AS DECIMAL(18,4)) / NULLIF(denominator, 0) AS ratio
```

---

## Rule 9 — Schema-Qualify Every Table Reference

```sql
-- WRONG — resolution depends on the connected user's default schema
FROM orders o INNER JOIN customers c ON c.id = o.customer_id

-- CORRECT
FROM sales.orders o INNER JOIN sales.customers c ON c.id = o.customer_id
```

Quoting convention follows provider:
- SQL Server: `[schema].[table]`
- PostgreSQL / standard SQL: `"schema"."table"` (only required for mixed-case or reserved words; lowercase unquoted identifiers are idiomatic)
- MySQL: `` `schema`.`table` ``

---

## Rule 10 — Explicit Everything: JOINs, Column Lists, Aliases

### Explicit JOIN type with documentation before the line

```sql
-- mandatory: every order must have a customer; missing customer = data error
INNER JOIN sales.customers c ON c.id = o.customer_id
-- optional: NULL when no promotion applied
LEFT  JOIN sales.promotions p ON p.id = o.promotion_id
```

### No SELECT *

```sql
-- WRONG
SELECT * FROM sales.orders WHERE id = @id

-- CORRECT
SELECT
    o.id,
    o.customer_id,
    o.status,
    o.total_amount,
    o.created_at
FROM sales.orders o
WHERE o.id = @id
```

### Keyword case, pivot alignment, one column per line

```sql
SELECT
    o.id,
    o.total_amount,
    c.email         AS customer_email
FROM   sales.orders    o
    INNER JOIN sales.customers c ON c.id = o.customer_id
WHERE  o.status     = @status
  AND  o.created_at >= @start_date
  AND  o.deleted_at IS NULL
ORDER BY o.created_at DESC, o.id DESC
```

---

## Rule 11 — Soft Delete Consistency

Use exactly one soft-delete pattern across the entire project.
Document it in the Notes header the first time it appears.

```sql
-- Pattern A: nullable timestamp (records when deletion occurred)
WHERE deleted_at IS NULL

-- Pattern B: boolean flag
WHERE is_deleted = false
```

Never mix patterns across tables in the same query.

---

## Rule 12 — NOLOCK / Dirty Reads Must Be Documented

When dirty-read hints are used, the Notes header must explain the justification:

```sql
-- =============================================================================
-- Query:   Load approximate event counts for the dashboard overview
-- Returns: Fields matching DashboardStats model
-- Notes:   WITH (NOLOCK) on audit.events is intentional — the table sustains
--          very high write volume; shared locks cause read timeouts.
--          Acceptable: counts are approximate by design; dirty reads on bigint
--          aggregates do not produce corrupted business decisions.
--          Do NOT apply dirty-read hints to queries that feed financial
--          calculations or that determine downstream write operations.
-- =============================================================================
```

---

## Rule 13 — Formatting Details

### DECIMAL precision always explicit

```sql
-- WRONG
CAST(amount AS DECIMAL)

-- CORRECT
CAST(amount AS DECIMAL(18, 4))
```

### Stable ORDER BY for pagination

Always include a unique tiebreaker:

```sql
-- created_at alone is non-unique; id as tiebreaker guarantees stable pages
ORDER BY created_at DESC, id DESC
OFFSET @page_offset ROWS FETCH NEXT @page_size ROWS ONLY
```

---

## Rule 14 — QueryName Naming Convention

| Pattern | Examples |
|---|---|
| `Get<Entity>By<Key>` | `GetById`, `GetByEmail` |
| `Get<Adjective>List` | `GetActivePaged`, `GetByStatusList` |
| `Insert<Entity>` | `Insert`, `InsertAndReturnId` |
| `Update<Field>` | `UpdateStatus`, `UpdateLastLogin` |
| `Delete<Qualifier>` | `SoftDelete`, `PurgeExpiredBatch` |
| `Exists<Condition>` | `ExistsByEmail`, `ExistsByToken` |
| `Count<Condition>` | `CountActive`, `CountByStatus` |
| `Search<Qualifier>` | `SearchByKeyword`, `SearchPaged` |
| `Upsert<Entity>` | `Upsert` |

---

## Rule 15 — ANSI-First, Override Only When Necessary

Write the default `.sql` file in portable SQL. Create provider overrides only
when a genuine syntax incompatibility exists (INSERT + return ID, upsert, full-text search).

Pass timestamps, limits, and other non-deterministic values as parameters —
this eliminates most reasons to diverge between providers.

See `references/multi-dbms-guide.md` for the decision table and examples.

---

## Complete File Template

```sql
-- =============================================================================
-- Query:   <Imperative verb + subject + key filter>
-- Returns: <Model name, column list, or "no rows">
-- Notes:   <Index requirements, business rules, intentional design choices.
--           Omit only when the query is genuinely self-explanatory.>
-- =============================================================================
-- #exclude
DECLARE @param1 <type> = <test_value>;   -- constraint or enum values
DECLARE @param2 <type> = <test_value>;   -- UTC; computed by caller
-- /exclude

-- -----------------------------------------------------------------------------
-- CTE: <name>
-- <Purpose — scope and data source.>
-- <Filtering rationale — WHY rows are included or excluded.>
-- <Maintenance notes — mirror conditions, join intent, edge cases.>
-- -----------------------------------------------------------------------------
WITH cte_name AS (
    SELECT
        t1.col1,
        t1.col2,
        t2.col3
    FROM schema_a.table_one t1
        -- INNER JOIN is intentional: rows without a match are invalid for this query
        INNER JOIN schema_a.table_two t2 ON t2.id = t1.fk_id
    WHERE t1.deleted_at IS NULL
)
-- final SELECT
-- <note about sort columns not returned, or other output decisions>
SELECT
    col1,
    col2,
    col3
FROM cte_name
WHERE col1 = @param1
-- col1 DESC as primary, col2 as tiebreaker for stable pagination
ORDER BY col1 DESC, col2 DESC
OFFSET @param2 ROWS FETCH NEXT @param1 ROWS ONLY
```

---

## Pre-commit Checklist

```
Comment correctness
[ ] No end-of-line -- comments outside -- #exclude blocks
[ ] No /* */ comments anywhere in the file
[ ] All documentation outside -- #exclude is written as full-line -- comments

Content
[ ] File header present: Query / Returns / Notes
[ ] Notes records index requirements, business rules, or non-obvious design decisions
[ ] -- #exclude contains DECLARE for every parameter
[ ] Each DECLARE annotated with constraints, enum values, or UTC convention
[ ] No documentation prose inside -- #exclude blocks

SQL Correctness
[ ] No SELECT * — explicit column list
[ ] No function wrapping on indexed columns in WHERE (sargability)
[ ] Parameter types match column types — no implicit conversions
[ ] COALESCE on SUM/AVG/MIN/MAX that can receive NULL input
[ ] NULLIF guard on every denominator in division expressions
[ ] Soft-delete filter uses the project's canonical pattern consistently

Formatting
[ ] Keywords uppercase
[ ] Each selected column on its own line
[ ] All tables schema-qualified: schema.table
[ ] All tables aliased; ON clauses visually aligned
[ ] ORDER BY present before OFFSET; includes a unique tiebreaker column
[ ] DECIMAL casts specify explicit (precision, scale)

Documentation
[ ] Explicit JOIN type on every join; intent documented as full-line comment if non-obvious
[ ] Each CTE has a -- --- documentation block
[ ] UNION ALL streams numbered [0], [1], [2] … in the CTE header
[ ] Mirror conditions documented with a "keep in sync" note
[ ] NOLOCK / dirty-read hints justified in Notes header
[ ] Provider override files (.pg.sql / .ms.sql) have -- #exclude synced from default
```

---

## Reference Guides

- **[Exclude Block Patterns](references/exclude-patterns.md)** — Templates for common `-- #exclude` scenarios.
- **[Multi-DBMS Decision Guide](references/multi-dbms-guide.md)** — Override decision flow; syntax differences; RETURNING, ON CONFLICT, MERGE examples.
- **[Performance Patterns](references/performance-patterns.md)** — Sargable predicates, aggregation traps, N+1 avoidance, batch writes, pagination.
