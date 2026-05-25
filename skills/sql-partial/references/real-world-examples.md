# Real-World Examples

Complete working examples showing SQL Partial patterns in production use.

## Example 1: Command Handler with Error Handling

This example shows a command handler with validation, error handling, and transaction management.

### C# Handler

```csharp
using MediatR;

namespace MyApp.Application.Features.Products;

public record CreateProductRequest(
    string Code,
    string Name,
    Guid CategoryId)
    : IRequest<Result<Guid>>;

public partial class CreateProductRequestHandler(
    IDbRepository repository,
    ICurrentUser currentUser)
    : IRequestHandler<CreateProductRequest, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(
        CreateProductRequest request,
        CancellationToken cancellationToken)
    {
        // Prepare SQL parameters
        var parameters = new
        {
            request.Code,
            request.Name,
            request.CategoryId,
            CreatedBy = currentUser.GetUserId()
        };

        // Begin transaction
        using var transaction = await repository.BeginTransactionAsync(cancellationToken);

        // Execute SQL - Uses SqlCommand constant from .Command.sql file
        string? result = await repository.ExecuteScalarAsync<string>(
            SqlCommand,
            parameters,
            transaction: transaction,
            cancellationToken: cancellationToken);

        // Handle validation errors
        if (result == "CategoryId")
            return Result.Fail<Guid>("Category not found");

        if (result == "Code")
            return Result.Fail<Guid>("Product code already exists");

        // Parse result as new product ID
        if (!Guid.TryParse(result, out var productId))
            return Result.Fail<Guid>("Failed to create product");

        // Commit transaction
        await transaction.CommitAsync(cancellationToken);

        return Result.Success(productId);
    }
}
```

### SQL File: `CreateProductRequestHandler.Command.sql`

```sql
-- Create a new product
-- Returns: Product ID if successful, error code otherwise

-- #testpart
DECLARE @Code NVARCHAR(50) = 'PROD001';
DECLARE @Name NVARCHAR(200) = 'Test Product';
DECLARE @CategoryId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
DECLARE @CreatedBy UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
-- /testpart

DECLARE @Result NVARCHAR(50);

-- Validate category exists
IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Id] = @CategoryId)
    SET @Result = 'CategoryId'
-- Check for duplicate code
ELSE IF EXISTS (SELECT 1 FROM [dbo].[Products] WHERE [Code] = @Code)
    SET @Result = 'Code'
ELSE
BEGIN
    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();

    INSERT INTO [dbo].[Products] ([Id], [Code], [Name], [CategoryId], [CreatedBy], [CreatedOn])
    VALUES (@NewId, @Code, @Name, @CategoryId, @CreatedBy, GETDATE());

    SET @Result = CAST(@NewId AS NVARCHAR(50));
END

SELECT @Result;
```

**Key features:**
- Validates foreign key (CategoryId)
- Checks for duplicate code
- Returns error codes or new ID
- Uses transaction for data consistency

## Example 2: Query Handler with JOIN

This example shows a simple query handler retrieving data with JOIN.

### C# Handler

```csharp
using MediatR;

namespace MyApp.Application.Features.Products;

public record GetProductRequest(Guid Id) : IRequest<Result<ProductDto>>;

public record ProductDto(
    Guid Id,
    string Code,
    string Name,
    string CategoryName);

public partial class GetProductRequestHandler(IDbRepository repository)
    : IRequestHandler<GetProductRequest, Result<ProductDto>>
{
    public async Task<Result<ProductDto>> Handle(
        GetProductRequest request,
        CancellationToken cancellationToken)
    {
        var parameters = new { request.Id };

        // Execute SQL - Uses SqlQuery constant from .Query.sql file
        var product = await repository.QueryFirstOrDefaultAsync<ProductDto>(
            SqlQuery,
            parameters,
            cancellationToken: cancellationToken);

        if (product == null)
            return Result.Fail<ProductDto>("Product not found");

        return Result.Success(product);
    }
}
```

### SQL File: `GetProductRequestHandler.Query.sql`

```sql
-- Get product details by ID

-- #testpart
DECLARE @Id UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
-- /testpart

SELECT
    p.[Id],
    p.[Code],
    p.[Name],
    c.[Name] AS [CategoryName]
FROM [dbo].[Products] p
INNER JOIN [dbo].[Categories] c ON p.[CategoryId] = c.[Id]
WHERE p.[Id] = @Id AND p.[DeletedOn] IS NULL;
```

**Key features:**
- Simple SELECT with JOIN
- Soft delete filter
- Maps to DTO directly

## Example 3: Multiple SQL Files for One Handler

This example shows using multiple SQL files for different operations in a single handler.

### C# Handler

```csharp
using MediatR;
using System.Data;

namespace MyApp.Application.Features.Orders;

public record ProcessOrderRequest(Guid OrderId) : IRequest<Result>;

public partial class ProcessOrderRequestHandler(IDbRepository repository)
    : IRequestHandler<ProcessOrderRequest, Result>
{
    public async Task<Result> Handle(
        ProcessOrderRequest request,
        CancellationToken cancellationToken)
    {
        var parameters = new { request.OrderId };

        // Step 1: Validate order - Uses SqlValidate from .Validate.sql
        var validationError = await repository.ExecuteScalarAsync<string?>(
            SqlValidate,
            parameters,
            cancellationToken: cancellationToken);

        if (validationError != null)
            return Result.Fail(validationError);

        // Step 2: Update order status - Uses SqlUpdate from .Update.sql
        await repository.ExecuteAsync(
            SqlUpdate,
            parameters,
            cancellationToken: cancellationToken);

        // Step 3: Log action - Uses SqlLog from .Log.sql
        var logParams = new
        {
            request.OrderId,
            Action = "Processed",
            Timestamp = DateTime.UtcNow
        };

        await repository.ExecuteAsync(
            SqlLog,
            logParams,
            cancellationToken: cancellationToken);

        return Result.Success();
    }
}
```

### SQL Files

**ProcessOrderRequestHandler.Validate.sql:**
```sql
-- Validate order can be processed
-- Returns: NULL if valid, error message otherwise

-- #testpart
DECLARE @OrderId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
-- /testpart

DECLARE @Result NVARCHAR(200);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Orders] WHERE [Id] = @OrderId)
    SET @Result = 'Order not found'
ELSE IF EXISTS (SELECT 1 FROM [dbo].[Orders] WHERE [Id] = @OrderId AND [Status] = 'Cancelled')
    SET @Result = 'Cannot process cancelled order'
ELSE
    SET @Result = NULL;

SELECT @Result;
```

**ProcessOrderRequestHandler.Update.sql:**
```sql
-- Update order status to processed

-- #testpart
DECLARE @OrderId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
-- /testpart

UPDATE [dbo].[Orders]
SET [Status] = 'Processed',
    [ProcessedOn] = GETDATE()
WHERE [Id] = @OrderId;
```

**ProcessOrderRequestHandler.Log.sql:**
```sql
-- Log order action

-- #testpart
DECLARE @OrderId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
DECLARE @Action NVARCHAR(50) = 'Processed';
DECLARE @Timestamp DATETIME = GETDATE();
-- /testpart

INSERT INTO [dbo].[OrderLogs] ([OrderId], [Action], [Timestamp])
VALUES (@OrderId, @Action, @Timestamp);
```

**Key features:**
- Three SQL files for one handler
- Generated constants: `SqlValidate`, `SqlUpdate`, `SqlLog`
- Separation of concerns
- Reusable SQL components

## Common Patterns

### Pattern 1: Error Code Returns

Return error codes as strings for specific validation failures:

```csharp
string? result = await repository.ExecuteScalarAsync<string>(SqlCommand, parameters);

if (result == "NotFound")
    return Result.Fail("Entity not found");
if (result == "Duplicate")
    return Result.Fail("Duplicate entry");

// NULL or valid ID means success
```

**SQL side:**
```sql
DECLARE @Result NVARCHAR(50);

IF NOT EXISTS (SELECT 1 FROM [Table] WHERE [Id] = @Id)
    SET @Result = 'NotFound'
ELSE IF EXISTS (SELECT 1 FROM [Table] WHERE [Code] = @Code AND [Id] <> @Id)
    SET @Result = 'Duplicate'
ELSE
BEGIN
    -- Perform operation
    SET @Result = NULL; -- Success
END

SELECT @Result;
```

### Pattern 2: Transaction Management

Use transactions for complex operations:

```csharp
using var transaction = await repository.BeginTransactionAsync(cancellationToken);

var result = await repository.ExecuteScalarAsync<string>(
    SqlCommand,
    parameters,
    transaction: transaction,
    cancellationToken: cancellationToken);

if (result != null)
    return Result.Fail(result); // Transaction auto-rollback on dispose

await transaction.CommitAsync(cancellationToken);
```

### Pattern 3: Soft Delete

Common pattern for soft delete operations:

**SQL File: `DeleteProductRequestHandler.Delete.sql`**
```sql
-- Soft delete a product

-- #testpart
DECLARE @Id UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
DECLARE @DeletedBy UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
-- /testpart

UPDATE [dbo].[Products]
SET [DeletedOn] = GETDATE(),
    [DeletedBy] = @DeletedBy
WHERE [Id] = @Id AND [DeletedOn] IS NULL;

SELECT @@ROWCOUNT;
```

**C# Handler:**
```csharp
var rowsAffected = await repository.ExecuteScalarAsync<int>(
    SqlDelete,
    new { request.Id, DeletedBy = currentUser.GetUserId() });

if (rowsAffected == 0)
    return Result.Fail("Product not found or already deleted");
```

## Tips for Adapting to Your Project

### 1. Replace Repository Interface

The examples use generic `IDbRepository`. Replace with your repository interface:

```csharp
// Example uses
IDbRepository repository

// Replace with your interface, e.g.:
IAppDbSqlRepository repo
IDapperRepository dapper
IDbConnection connection
```

### 2. Replace Result Type

The examples use generic `Result<T>`. Replace with your result type:

```csharp
// Example uses
Result<Guid>
Result.Success()
Result.Fail()

// Replace with your result type, e.g.:
IApiResult<DefaultIdType>
OperationResult<T>
Response<T>
```

### 3. Adjust Namespace and Schema

Update namespaces and database schemas to match your project:

```csharp
// Namespace
namespace MyApp.Application.Features.Products;

// Database schema
FROM [dbo].[Products]      // or [YourSchema].[Products]
FROM [Business].[Reports]  // project-specific schema
```

### 4. Customize Parameter Patterns

Adapt parameter objects to your conventions:

```csharp
// Anonymous object
var parameters = new { request.Id, UserId = currentUser.GetUserId() };

// Or use DynamicParameters (Dapper)
var parameters = new DynamicParameters();
parameters.Add("@Id", request.Id);
parameters.Add("@UserId", currentUser.GetUserId());
```

