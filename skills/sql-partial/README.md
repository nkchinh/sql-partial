# SQL Partial Skill

A comprehensive guide for working with SQL Partial - a .NET code generator that converts `.sql` files into C# partial classes containing SQL query constants.

## What is SQL Partial?

SQL Partial is a build-time code generator that:
- Converts `.sql` files into C# string constants
- Enables SQL syntax highlighting and testing in SQL editors
- Supports test parameters that are auto-removed during build
- Follows naming convention: `{ClassName}.{Suffix}.sql` → `Sql{Suffix}` constant

## Quick Example

**SQL File:** `GetProductRequestHandler.Query.sql`
```sql
-- #testpart
DECLARE @Id UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000000';
-- /testpart

SELECT * FROM [dbo].[Products] WHERE [Id] = @Id;
```

**C# Usage:**
```csharp
public partial class GetProductRequestHandler
{
    public async Task Handle(...)
    {
        var product = await repo.QueryAsync<Product>(SqlQuery, new { Id });
    }
}
```

## Skill Structure

```
sql-partial/
├── SKILL.md                      # Main skill documentation (quick start, patterns, best practices)
├── references/                   # Detailed documentation
│   ├── README.md                 # Index of reference files
│   ├── advanced-patterns.md      # TVP, CTE, MERGE patterns
│   ├── configuration-guide.md    # .csproj configuration details
│   └── real-world-examples.md    # Complete working examples
└── scripts/                      # Utility tools
    ├── README.md                 # Script usage guide
    └── validate_sql.py           # Validate SQL file structure
```

## Progressive Disclosure

1. **Start with SKILL.md** - Quick start and common patterns
2. **Load references/** as needed - Detailed documentation for specific scenarios
3. **Use scripts/** - Validate SQL files before committing

## Key Features

✅ **Generic Examples** - All examples use generic namespaces and types  
✅ **Reusable** - Can be applied to any .NET project using SQL Partial  
✅ **Comprehensive** - Covers basic to advanced patterns  
✅ **Practical** - Includes validation script and troubleshooting guide  

## License

MIT - Share knowledge freely

