---
name: sql-partial
description: Guide for working with SQL Partial tool - a code generator that converts .sql files into C# partial classes containing SQL query constants. Use when creating request handlers with SQL queries, organizing complex SQL in separate files, testing SQL queries directly in SQL editor, or managing SQL queries using the SqlPartial pattern in .NET projects.
license: MIT
---

# SQL Partial

## Overview

SQL Partial is a .NET code generator tool that automatically creates C# partial classes containing string constants from `.sql` files. This enables better SQL organization, type-safe references, and testability.

**Key benefits:**
- Keep SQL queries in separate `.sql` files with proper syntax highlighting
- Access SQL queries as C# constants in your code
- Test SQL directly in SQL editor with `#testpart` directives (auto-removed during build)
- Separate SQL logic from C# business logic

## When to Use This Skill

Use when:
- Creating new request handlers that need SQL queries
- Organizing complex SQL queries in separate files
- Working with Dapper or raw SQL in .NET projects
- Testing SQL queries directly in SQL editor before using in code

## Quick Start

### 1. File Naming Convention

SQL Partial files follow this pattern: `{ClassName}.{Suffix}.sql`

**Example:** `CreateProductRequestHandler.Command.sql`
- **ClassName**: Must match C# class name exactly
- **Suffix**: Describes query purpose (`Command`, `Query`, `Update`, etc.)
- **Generated constant**: `SqlCommand`, `SqlQuery`, `SqlUpdate`

### 2. Basic SQL File Structure

```sql
-- Description of what this SQL does

-- #testpart
DECLARE @Id UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
DECLARE @Name NVARCHAR(200) = 'Test Product';
-- /testpart

SELECT * FROM [dbo].[Products] WHERE [Id] = @Id;
```

**Key feature:** `#testpart` / `/testpart` block allows testing SQL directly in SQL editor. This block is automatically removed during build.

### 3. Use in C# Code

```csharp
public partial class CreateProductRequestHandler(IDbRepository repository)
    : IRequestHandler<CreateProductRequest, Result<Guid>>
{
    public async Task<Result<Guid>> Handle(...)
    {
        // Use the generated SqlCommand constant
        var result = await repository.ExecuteScalarAsync<string>(
            SqlCommand,  // Auto-generated from .Command.sql file
            parameters,
            cancellationToken: cancellationToken);

        return Result.Success(Guid.Parse(result));
    }
}
```

## Configuration

Add to your `.csproj` file:

```xml
<ItemGroup>
    <!-- Add the SqlPartial tool package -->
    <PackageReference Include="TD.SqlPartial.Tool" Version="1.0.3" />

    <!-- Configure SQL Partial for your project structure -->
    <SqlPartial Include="**/*.*.sql">
        <ClassModifier>public</ClassModifier>
        <ConstModifier>private</ConstModifier>
    </SqlPartial>
</ItemGroup>
```

## Common Patterns

### Pattern 1: Command with Error Handling

**File: `CreateProductRequestHandler.Command.sql`**

```sql
-- Create a new product
-- Returns: Product ID if successful, error code otherwise

-- #testpart
DECLARE @Code NVARCHAR(50) = 'PROD001';
DECLARE @Name NVARCHAR(200) = 'Test Product';
DECLARE @CategoryId UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
-- /testpart

DECLARE @Result NVARCHAR(50);

IF NOT EXISTS (SELECT 1 FROM [dbo].[Categories] WHERE [Id] = @CategoryId)
    SET @Result = 'CategoryNotFound'
ELSE IF EXISTS (SELECT 1 FROM [dbo].[Products] WHERE [Code] = @Code)
    SET @Result = 'DuplicateCode'
ELSE
BEGIN
    DECLARE @NewId UNIQUEIDENTIFIER = NEWID();
    INSERT INTO [dbo].[Products] ([Id], [Code], [Name], [CategoryId], [CreatedOn])
    VALUES (@NewId, @Code, @Name, @CategoryId, GETDATE());
    SET @Result = CAST(@NewId AS NVARCHAR(50));
END

SELECT @Result;
```

### Pattern 2: Query with JOIN

**File: `GetProductRequestHandler.Query.sql`**

```sql
-- Get product details by ID

-- #testpart
DECLARE @Id UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
-- /testpart

SELECT
    p.[Id], p.[Code], p.[Name],
    c.[Name] AS [CategoryName]
FROM [dbo].[Products] p
INNER JOIN [dbo].[Categories] c ON p.[CategoryId] = c.[Id]
WHERE p.[Id] = @Id AND p.[DeletedOn] IS NULL;
```

### Pattern 3: Multiple SQL Files for One Handler

You can have multiple SQL files for a single handler:

```
ProcessOrderRequestHandler.cs
ProcessOrderRequestHandler.Validate.sql  → SqlValidate
ProcessOrderRequestHandler.Update.sql    → SqlUpdate
ProcessOrderRequestHandler.Log.sql       → SqlLog
```

## Best Practices

### 1. Always Use Test Parts

Include `#testpart` blocks with realistic test data to enable direct SQL testing:

```sql
-- #testpart
DECLARE @Id UNIQUEIDENTIFIER = '090fc69d-c63b-4cbf-a253-5e92d9b0439a';
DECLARE @Period NVARCHAR(30) = '2022';
-- /testpart
```

### 2. Document SQL Purpose

Start each SQL file with clear comments explaining what it does and what it returns:

```sql
-- Create a new report and return its ID
-- Returns:
--   - Report ID (GUID as string) if successful
--   - 'TemplateId' if template not found
```

### 3. Use Meaningful Suffixes

Choose suffixes that clearly indicate the SQL purpose:
- `.Query.sql` - SELECT queries
- `.Command.sql` - INSERT/UPDATE/DELETE with complex logic
- `.Update.sql` - UPDATE operations
- `.Validate.sql` - Validation queries

### 4. Handle Errors in SQL

Return error codes or messages from SQL for better error handling:

```sql
DECLARE @Result NVARCHAR(50);

IF NOT EXISTS (SELECT 1 FROM [Table] WHERE [Id] = @Id)
    SET @Result = 'NotFound'
ELSE
BEGIN
    -- Perform operation
    SET @Result = NULL; -- Success
END

SELECT @Result;
```



## Troubleshooting

### SQL constant not generated

**Causes:**
- File naming doesn't match pattern `{ClassName}.{Suffix}.sql`
- Class doesn't exist or isn't marked as `partial`
- SqlPartial not configured in `.csproj`

**Solution:**
- Verify file name matches class name exactly
- Ensure class is declared as `partial`
- Check `.csproj` has `<SqlPartial Include="...">` for the file path
- Rebuild the project

### Build errors with SQL syntax

**Solution:**
- Test SQL file directly in SQL editor first
- Check for unclosed strings or comments
- Verify all parameters are declared in `#testpart`

### Test part not removed

**Solution:**
- Use exact syntax: `-- #testpart` and `-- /testpart`
- Ensure directives are on separate lines
- No extra spaces before `#` or `/`

## Resources

### References

Detailed documentation loaded as needed:

- `references/advanced-patterns.md` - Complex SQL patterns (CTE, MERGE, TVP)
- `references/configuration-guide.md` - Detailed .csproj configuration options
- `references/real-world-examples.md` - Complete examples from the codebase

### Scripts

Utility script for working with SQL Partial:

- `scripts/validate_sql.py` - Validate SQL file structure and naming


