# SQL Partial Configuration Guide

Detailed guide for configuring SQL Partial in your .NET projects.

## Basic Configuration

### Minimal Setup

Add to your `.csproj` file:

```xml
<ItemGroup>
    <PackageReference Include="TD.SqlPartial.Tool" Version="1.0.3" />
    
    <SqlPartial Include="**/*.*.sql">
        <ClassModifier>public</ClassModifier>
        <ConstModifier>private</ConstModifier>
    </SqlPartial>
</ItemGroup>
```

This configuration:
- Includes all `.sql` files matching the `{ClassName}.{Suffix}.sql` pattern
- Generates `public partial class`
- Creates `private const string` for SQL constants

## Advanced Configuration

### Multiple SqlPartial Configurations

You can have different configurations for different directories:

```xml
<ItemGroup>
    <!-- Business layer: private constants -->
    <SqlPartial Include="Business/**/*.*.sql">
        <ClassModifier>public</ClassModifier>
        <ConstModifier>private</ConstModifier>
    </SqlPartial>
    
    <!-- Catalog layer: private constants -->
    <SqlPartial Include="Catalog/**/*.*.sql">
        <ClassModifier>public</ClassModifier>
        <ConstModifier>private</ConstModifier>
    </SqlPartial>
    
    <!-- Shared SQL: public constants for reuse -->
    <SqlPartial Include="Shared/**/*.*.sql">
        <ClassModifier>internal</ClassModifier>
        <ConstModifier>public</ConstModifier>
    </SqlPartial>
</ItemGroup>
```

### Override Specific Files

Use `Update` to override configuration for specific files:

```xml
<ItemGroup>
    <!-- Default configuration -->
    <SqlPartial Include="**/*.*.sql">
        <ClassModifier>public</ClassModifier>
        <ConstModifier>private</ConstModifier>
    </SqlPartial>
    
    <!-- Override for shared SQL files -->
    <SqlPartial Update="Business/SharedSql/*.*.sql">
        <ClassModifier>internal</ClassModifier>
        <ConstModifier>public</ConstModifier>
    </SqlPartial>
</ItemGroup>
```

## Configuration Options

### ClassModifier

Controls the access modifier for the generated partial class.

**Values:**
- `public` - Class is accessible from other assemblies
- `internal` - Class is accessible only within the same assembly
- `private` - Class is accessible only within the containing class (for nested classes)

**Example:**

```xml
<SqlPartial Include="**/*.*.sql">
    <ClassModifier>public</ClassModifier>
</SqlPartial>
```

Generates:

```csharp
public partial class MyRequestHandler
{
    // ...
}
```

### ConstModifier

Controls the access modifier for the generated SQL constant.

**Values:**
- `public` - Constant is accessible from other classes
- `internal` - Constant is accessible only within the same assembly
- `private` - Constant is accessible only within the class

**Example:**

```xml
<SqlPartial Include="**/*.*.sql">
    <ConstModifier>private</ConstModifier>
</SqlPartial>
```

Generates:

```csharp
public partial class MyRequestHandler
{
    private const string SqlQuery = "...";
}
```

## Common Configuration Patterns

### Pattern 1: Private by Default

Most common pattern - keep SQL constants private to the handler:

```xml
<SqlPartial Include="**/*.*.sql">
    <ClassModifier>public</ClassModifier>
    <ConstModifier>private</ConstModifier>
</SqlPartial>
```

**Use when:** SQL is only used within the handler class.

### Pattern 2: Shared SQL Utilities

For SQL that needs to be reused across multiple handlers:

```xml
<SqlPartial Include="Shared/**/*.*.sql">
    <ClassModifier>internal</ClassModifier>
    <ConstModifier>public</ConstModifier>
</SqlPartial>
```

**Use when:** Creating shared SQL utilities or common queries.

**Example:**

```csharp
// Shared/SqlUtilities.cs
internal partial class SqlUtilities
{
    // SqlCheckExists generated from SqlUtilities.CheckExists.sql
}

// Usage in other classes
var exists = await repo.ExecuteScalarAsync<bool>(
    SqlUtilities.SqlCheckExists, 
    parameters);
```

### Pattern 3: Layer-Specific Configuration

Different configurations for different architectural layers:

```xml
<ItemGroup>
    <!-- Application layer -->
    <SqlPartial Include="Application/**/*.*.sql">
        <ClassModifier>public</ClassModifier>
        <ConstModifier>private</ConstModifier>
    </SqlPartial>
    
    <!-- Infrastructure layer -->
    <SqlPartial Include="Infrastructure/**/*.*.sql">
        <ClassModifier>internal</ClassModifier>
        <ConstModifier>private</ConstModifier>
    </SqlPartial>
</ItemGroup>
```

## Troubleshooting Configuration

### Issue: Changes not reflected after build

**Solution:**
1. Clean the solution: `dotnet clean`
2. Rebuild: `dotnet build`
3. Check that `.csproj` was saved
4. Verify file path matches the `Include` pattern

### Issue: Multiple configurations conflict

**Solution:**
- Use more specific patterns in `Include`
- Use `Update` to override specific files
- Order matters - later configurations override earlier ones

### Issue: SQL files not included

**Solution:**
- Check the glob pattern in `Include`
- Verify file naming follows `{ClassName}.{Suffix}.sql`
- Ensure files are in the project directory
- Check file properties: Build Action should be "None" or "Content"

## Best Practices

### 1. Use Consistent Patterns

Stick to one configuration pattern across your project:

```xml
<!-- Good: Consistent pattern -->
<SqlPartial Include="**/*.*.sql">
    <ClassModifier>public</ClassModifier>
    <ConstModifier>private</ConstModifier>
</SqlPartial>
```

### 2. Document Exceptions

If you override configuration for specific files, add comments:

```xml
<!-- Default: private constants -->
<SqlPartial Include="**/*.*.sql">
    <ClassModifier>public</ClassModifier>
    <ConstModifier>private</ConstModifier>
</SqlPartial>

<!-- Shared utilities: public constants for reuse across handlers -->
<SqlPartial Update="Shared/**/*.*.sql">
    <ClassModifier>internal</ClassModifier>
    <ConstModifier>public</ConstModifier>
</SqlPartial>
```

### 3. Keep It Simple

Start with the simplest configuration and only add complexity when needed:

```xml
<!-- Start simple -->
<SqlPartial Include="**/*.*.sql">
    <ClassModifier>public</ClassModifier>
    <ConstModifier>private</ConstModifier>
</SqlPartial>
```

