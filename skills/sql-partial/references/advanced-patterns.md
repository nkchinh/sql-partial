# Advanced SQL Partial Patterns

This document covers complex SQL patterns and advanced usage scenarios for SQL Partial.

## Table-Valued Parameters (TVP)

### Pattern: Bulk Insert with TVP

**Use when:** Inserting multiple rows in a single operation.

**Step 1: Define TVP Type in SQL Server**

```sql
-- Create user-defined table type
CREATE TYPE [dbo].[ProductItemsType] AS TABLE
(
    [Code] NVARCHAR(50),
    [Name] NVARCHAR(200),
    [Price] DECIMAL(18,2)
);
```

**Step 2: SQL File using TVP**

**File: `BulkInsertProductsRequestHandler.Insert.sql`**

```sql
-- Bulk insert products using TVP
-- Returns: Number of rows inserted

-- #testpart
DECLARE @Items [dbo].[ProductItemsType];
INSERT INTO @Items VALUES
    ('PROD001', 'Product 1', 100.00),
    ('PROD002', 'Product 2', 200.00),
    ('PROD003', 'Product 3', 300.00);
DECLARE @CategoryId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
-- /testpart

INSERT INTO [dbo].[Products] ([Id], [Code], [Name], [Price], [CategoryId], [CreatedOn])
SELECT NEWID(), [Code], [Name], [Price], @CategoryId, GETDATE()
FROM @Items;

SELECT @@ROWCOUNT;
```

**Step 3: C# Usage with Dapper**

```csharp
using System.Data;
using Dapper;

public partial class BulkInsertProductsRequestHandler(IDbConnection connection)
{
    public async Task<int> Handle(BulkInsertProductsRequest request)
    {
        // Create DataTable matching TVP structure
        var itemsTable = new DataTable();
        itemsTable.Columns.Add("Code", typeof(string));
        itemsTable.Columns.Add("Name", typeof(string));
        itemsTable.Columns.Add("Price", typeof(decimal));

        foreach (var item in request.Items)
        {
            itemsTable.Rows.Add(item.Code, item.Name, item.Price);
        }

        var parameters = new DynamicParameters();
        parameters.Add("@Items", itemsTable.AsTableValuedParameter("[dbo].[ProductItemsType]"));
        parameters.Add("@CategoryId", request.CategoryId);

        var rowCount = await connection.ExecuteScalarAsync<int>(
            SqlInsert,
            parameters);

        return rowCount;
    }
}
```

## Common Table Expressions (CTE)

### Pattern: Hierarchical Query with Recursive CTE

**Use when:** Querying hierarchical data (categories, organizational structures, etc.).

**SQL File: `GetCategoryHierarchyRequestHandler.Query.sql`**

```sql
-- Get category hierarchy starting from a root category
-- Returns: All categories in the hierarchy with level and path information

-- #testpart
DECLARE @RootId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
-- /testpart

WITH CategoryHierarchy AS (
    -- Anchor: Root category
    SELECT
        [Id],
        [Code],
        [Name],
        [ParentId],
        0 AS [Level],
        CAST([Name] AS NVARCHAR(MAX)) AS [Path]
    FROM [dbo].[Categories]
    WHERE [Id] = @RootId AND [IsActive] = 1

    UNION ALL

    -- Recursive: Child categories
    SELECT
        c.[Id],
        c.[Code],
        c.[Name],
        c.[ParentId],
        ch.[Level] + 1,
        ch.[Path] + ' > ' + c.[Name]
    FROM [dbo].[Categories] c
    INNER JOIN CategoryHierarchy ch ON c.[ParentId] = ch.[Id]
    WHERE c.[IsActive] = 1
)
SELECT
    [Id],
    [Code],
    [Name],
    [ParentId],
    [Level],
    [Path]
FROM CategoryHierarchy
ORDER BY [Path];
```

### Pattern: Complex Aggregation with Multiple CTEs

**Use when:** Need to combine data from multiple sources with aggregations.

**SQL File: `GetSalesSummaryRequestHandler.Query.sql`**

```sql
-- Get sales summary by region with product details
-- Returns: Aggregated sales data with latest order information

-- #testpart
DECLARE @StartDate DATE = '2024-01-01';
DECLARE @EndDate DATE = '2024-12-31';
-- /testpart

WITH RegionSales AS (
    -- Aggregate sales by region
    SELECT
        r.[Id] AS RegionId,
        r.[Name] AS RegionName,
        COUNT(DISTINCT o.[Id]) AS OrderCount,
        SUM(oi.[Quantity] * oi.[UnitPrice]) AS TotalSales
    FROM [dbo].[Regions] r
    LEFT JOIN [dbo].[Customers] c ON c.[RegionId] = r.[Id]
    LEFT JOIN [dbo].[Orders] o ON o.[CustomerId] = c.[Id]
    LEFT JOIN [dbo].[OrderItems] oi ON oi.[OrderId] = o.[Id]
    WHERE o.[OrderDate] BETWEEN @StartDate AND @EndDate
    GROUP BY r.[Id], r.[Name]
),
LatestOrders AS (
    -- Get latest order per region
    SELECT
        c.[RegionId],
        o.[Id] AS OrderId,
        o.[OrderDate],
        o.[TotalAmount],
        ROW_NUMBER() OVER (
            PARTITION BY c.[RegionId]
            ORDER BY o.[OrderDate] DESC
        ) AS rn
    FROM [dbo].[Orders] o
    INNER JOIN [dbo].[Customers] c ON o.[CustomerId] = c.[Id]
    WHERE o.[OrderDate] BETWEEN @StartDate AND @EndDate
)
SELECT
    rs.[RegionId],
    rs.[RegionName],
    rs.[OrderCount],
    rs.[TotalSales],
    lo.[OrderDate] AS LastOrderDate,
    lo.[TotalAmount] AS LastOrderAmount
FROM RegionSales rs
LEFT JOIN LatestOrders lo ON rs.[RegionId] = lo.[RegionId] AND lo.rn = 1
ORDER BY rs.[TotalSales] DESC;
```

## MERGE Statement

### Pattern: Upsert with MERGE

**Use when:** Need to insert or update based on existence (upsert operation).

**SQL File: `UpsertProductPricesRequestHandler.Merge.sql`**

```sql
-- Upsert product prices using MERGE
-- Returns: Number of rows affected

-- #testpart
DECLARE @Items [dbo].[ProductPriceType];
INSERT INTO @Items VALUES
    ('PROD001', 100.00, '2024-01-01'),
    ('PROD002', 200.00, '2024-01-01');
-- /testpart

MERGE [dbo].[ProductPrices] AS target
USING (
    SELECT
        p.[Id] AS ProductId,
        i.[Price],
        i.[EffectiveDate]
    FROM @Items i
    INNER JOIN [dbo].[Products] p ON p.[Code] = i.[ProductCode]
) AS source
ON target.[ProductId] = source.ProductId
   AND target.[EffectiveDate] = source.EffectiveDate
WHEN MATCHED THEN
    UPDATE SET
        target.[Price] = source.Price,
        target.[ModifiedOn] = GETDATE()
WHEN NOT MATCHED THEN
    INSERT ([Id], [ProductId], [Price], [EffectiveDate], [CreatedOn])
    VALUES (NEWID(), source.ProductId, source.Price, source.EffectiveDate, GETDATE());

SELECT @@ROWCOUNT;
```

**TVP Type Definition:**

```sql
CREATE TYPE [dbo].[ProductPriceType] AS TABLE
(
    [ProductCode] NVARCHAR(50),
    [Price] DECIMAL(18,2),
    [EffectiveDate] DATE
);
```

**C# Usage:**

```csharp
var pricesTable = new DataTable();
pricesTable.Columns.Add("ProductCode", typeof(string));
pricesTable.Columns.Add("Price", typeof(decimal));
pricesTable.Columns.Add("EffectiveDate", typeof(DateTime));

foreach (var item in request.Prices)
{
    pricesTable.Rows.Add(item.ProductCode, item.Price, item.EffectiveDate);
}

var parameters = new DynamicParameters();
parameters.Add("@Items", pricesTable.AsTableValuedParameter("[dbo].[ProductPriceType]"));

var rowsAffected = await connection.ExecuteScalarAsync<int>(SqlMerge, parameters);
```

