# Exclude Block Patterns

Templates for common `--# exclude` scenarios.
The block contains only executable SQL — documentation belongs outside as full-line `--` comments.
End-of-line comments inside `--# exclude` are safe because the entire block is stripped.

---

## Pattern 1 — Minimal: single parameter

```sql
-- =============================================================================
-- Query:   Find a user by internal ID
-- Returns: Fields matching UserProfile model; empty result if not found or deleted
-- =============================================================================
--# exclude
-- internal users.id, not the external UUID
DECLARE @id INT = 1;
-- /exclude

SELECT
    u.id,
    u.email,
    u.display_name,
    u.role_id,
    u.created_at
FROM auth.users u
WHERE u.id         = @id
  AND u.deleted_at IS NULL
```

---

## Pattern 2 — Enum parameters with all values listed

End-of-line comments on DECLARE lines are safe — the entire block is stripped.

```sql
-- =============================================================================
-- Query:   Load paginated article list filtered by status and optional author
-- Returns: Fields matching ArticleListItem model
-- Notes:   @author_id = NULL returns articles from all authors.
--          @page_size max is 50; the application layer must enforce this.
-- =============================================================================
--# exclude
DECLARE @status      INT = 1;     -- 0=Draft 1=Published 2=Archived 3=Deleted
DECLARE @author_id   INT = NULL;  -- NULL = all authors
DECLARE @page_size   INT = 20;    -- max 50; caller enforces
DECLARE @page_offset INT = 0;     -- 0-based; page 2 = 1 × @page_size
-- /exclude

SELECT
    a.id,
    a.title,
    a.status,
    a.published_at,
    u.display_name AS author_name
FROM content.articles a
    INNER JOIN auth.users u ON u.id = a.author_id
WHERE
    a.status     = @status
    AND a.deleted_at IS NULL
    AND (@author_id IS NULL OR a.author_id = @author_id)
ORDER BY a.published_at DESC, a.id DESC
OFFSET @page_offset ROWS FETCH NEXT @page_size ROWS ONLY
```

---

## Pattern 3 — UTC timestamp range

```sql
-- =============================================================================
-- Query:   Load payment history for an account within a date range
-- Returns: Fields matching PaymentHistoryItem model, newest first
-- Notes:   @start_date is inclusive; @end_date is exclusive (half-open range).
--          Both values must be UTC — computed by the caller, not derived from
--          a DB timestamp function inside this file.
--          Relies on index on (account_id, paid_at).
-- =============================================================================
--# exclude
DECLARE @account_id  INT  = 1;
DECLARE @start_date  <ts> = '<utc_value>';   -- inclusive
DECLARE @end_date    <ts> = '<utc_value>';   -- exclusive
DECLARE @page_size   INT  = 50;
DECLARE @page_offset INT  = 0;
-- /exclude

SELECT
    p.id,
    p.amount,
    p.currency,
    p.status,
    p.paid_at
FROM billing.payments p
WHERE
    p.account_id = @account_id
    AND p.paid_at    >= @start_date
    AND p.paid_at    <  @end_date
    AND p.deleted_at IS NULL
ORDER BY p.paid_at DESC, p.id DESC
OFFSET @page_offset ROWS FETCH NEXT @page_size ROWS ONLY
```

---

## Pattern 4 — Temp table seed for complex CTE testing

```sql
-- =============================================================================
-- Query:   Calculate line totals and invoice summary for a given order
-- Returns: SubTotal, TaxAmount, GrandTotal, LineCount
-- =============================================================================
--# exclude
DECLARE @order_id INT = 101;

CREATE TABLE #test_lines (
    product_id INT,
    qty        INT,
    unit_price DECIMAL(18,4)
);
INSERT INTO #test_lines VALUES
    (10, 2,  49.9900),
    (11, 1, 129.0000),
    (12, 3,  19.9900);
-- /exclude

WITH line_totals AS (
    SELECT
        ol.product_id,
        ol.qty,
        ol.unit_price,
        ol.qty * ol.unit_price AS line_total
    FROM sales.order_lines ol
    WHERE ol.order_id   = @order_id
      AND ol.voided_at IS NULL
)
SELECT
    COALESCE(SUM(line_total),       0) AS sub_total,
    COALESCE(SUM(line_total), 0) * 0.1 AS tax_amount,
    COALESCE(SUM(line_total), 0) * 1.1 AS grand_total,
    COUNT(*)                            AS line_count
FROM line_totals

--# exclude
DROP TABLE IF EXISTS #test_lines;
-- /exclude
```

---

## Pattern 5 — Batch delete with loop contract

```sql
-- =============================================================================
-- Query:   Delete one batch of expired session records
-- Returns: Affected row count; caller loops until 0
-- Notes:   DESTRUCTIVE — call from background jobs only, never request handlers.
--          Recommended batch size: 200–500 to avoid lock escalation.
--          Caller should delay ~100ms between iterations to release locks.
--          Condition prevents re-deletion on re-run.
-- =============================================================================
--# exclude
DECLARE @cutoff_date <ts>  = '<utc_value>';
DECLARE @batch_size  INT   = 300;

-- scope check: uncomment to verify row count before running
-- SELECT COUNT(*) AS eligible FROM auth.sessions
-- WHERE expired_at < @cutoff_date AND is_revoked = true;
-- /exclude

-- PostgreSQL / MySQL
DELETE FROM auth.sessions
WHERE expired_at  < @cutoff_date
  AND is_revoked  = true
LIMIT @batch_size

-- SQL Server: use a provider override file (.ms.sql) with:
-- DELETE TOP (@batch_size) FROM auth.sessions
-- WHERE expired_at < @cutoff_date AND is_revoked = 1
```

---

## Pattern 6 — Multi-step write with verification block

```sql
-- =============================================================================
-- Query:   Migrate one batch of members from a deprecated plan to its replacement
-- Returns: Affected row count; caller loops until 0
-- Notes:   Non-atomic — safe to interrupt and resume. The plan_id guard prevents
--          double-migration on re-run. Run during low-traffic windows or wrap in
--          a transaction at the application layer if atomicity is required.
-- =============================================================================
--# exclude
DECLARE @old_plan_id INT  = 5;
DECLARE @new_plan_id INT  = 10;
DECLARE @expired_on  <ts> = '<utc_value>';
DECLARE @updated_at  <ts> = '<utc_now>';    -- pass UtcNow from app
DECLARE @batch_size  INT  = 200;
-- /exclude

UPDATE members
SET
    plan_id    = @new_plan_id,
    updated_at = @updated_at
WHERE
    plan_id         = @old_plan_id
    AND plan_expires_at < @expired_on
LIMIT @batch_size

--# exclude
-- verify remaining rows after each test run
SELECT COUNT(*) AS remaining_to_migrate
FROM members
WHERE plan_id         = @old_plan_id
  AND plan_expires_at < @expired_on;
-- /exclude
```

---

## Pattern 7 — Multi-stream UNION ALL

The `--# exclude` block stays compact — declarations only.
All stream documentation lives outside as full-line `--` comments.

```sql
-- =============================================================================
-- Query:   Load all billable items for an invoice across three source streams
-- Returns: Fields matching BillableItem model, ordered by stream then date
-- Notes:   source_order drives sort stability only; not returned to the caller.
--          Stream [2] filter mirrors Stream [0] — keep both in sync on changes.
-- =============================================================================
--# exclude
DECLARE @invoice_id  INT = 99;
DECLARE @page_size   INT = 100;
DECLARE @page_offset INT = 0;
-- /exclude

-- -----------------------------------------------------------------------------
-- CTE: all_billable_items
-- Combines billable items from three mutually exclusive sources:
--   [0] Standard order lines — direct product purchases
--   [1] Service fees         — flat fees applied at checkout
--   [2] Credit adjustments   — negative-value post-purchase corrections
-- -----------------------------------------------------------------------------
WITH all_billable_items AS (
    ...
)
-- source_order not returned; used only for stable sort within an invoice
SELECT
    order_id,
    description,
    amount
FROM all_billable_items
ORDER BY source_order, created_at, id
OFFSET @page_offset ROWS FETCH NEXT @page_size ROWS ONLY
```
