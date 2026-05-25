# SQL Partial References

Detailed documentation for SQL Partial tool usage.

## Files

### advanced-patterns.md
Complex SQL patterns and advanced usage scenarios:
- Table-Valued Parameters (TVP) for bulk operations
- Common Table Expressions (CTE) for hierarchical queries
- MERGE statements for upsert operations
- Complex aggregations with multiple CTEs

**Load when:** Working with complex SQL scenarios beyond basic CRUD operations.

### configuration-guide.md
Comprehensive guide for configuring SQL Partial in .NET projects:
- Basic and advanced .csproj configuration
- ClassModifier and ConstModifier options
- Multiple SqlPartial configurations
- Override patterns for specific files
- Troubleshooting configuration issues

**Load when:** Setting up SQL Partial in a new project or customizing configuration.

### real-world-examples.md
Complete working examples from the actual codebase:
- CreateReportRequestHandler - Complex command with error handling
- UpdateDataPointsRequestHandler - Bulk update with TVP
- InputByFormRequestHandler - Complex validation logic
- Common patterns observed in production code

**Load when:** Need to see complete, production-ready examples of SQL Partial usage.

## Usage

These reference files are intended to be loaded into context when needed for specific scenarios. The main SKILL.md provides quick start information, while these files provide in-depth details.

**Progressive disclosure pattern:**
1. Start with SKILL.md for quick start
2. Load specific reference files as needed
3. Refer to real-world examples for complete implementations

