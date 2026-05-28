# SqlPartial Configuration Guide

## MSBuild Properties

| Property | Description | Default |
| :--- | :--- | :--- |
| `SqlPartialProviders` | Semicolon-separated `slug:Name` pairs. | (none) |
| `SqlPartialStringsNamespace` | Namespace for the `SqlStrings` struct. | `$(RootNamespace)` |
| `SqlPartialStringsType` | Use an existing `SqlStrings` type from another assembly. | (none) |
| `SqlPartialStringsNamespace` | Custom namespace for the generated struct. | `$(RootNamespace)` |

## Complex Provider Setup

You can define as many providers as you want:

```xml
<PropertyGroup>
  <SqlPartialProviders>
    pg:PostgreSql;
    ms:SqlServer;
    my:MySql;
    ora:Oracle;
    lt:Sqlite
  </SqlPartialProviders>
</PropertyGroup>
```

## Troubleshooting Common Issues

### 1. `SqlStrings` not found
- **Cause**: The project hasn't been built, or the generator hasn't run.
- **Fix**: Run `dotnet build` or save a `.cs` file to trigger the Incremental Generator.

### 2. Properties missing on the class
- **Cause**: The naming convention was violated.
- **Check**: Is it `ClassName.QueryName.sql`? Note that `ClassName` must match the C# class name exactly (case-sensitive).

### 3. Namespace Mismatch
- **Cause**: `.sql` file is in a subdirectory, and you haven't declared the partial class in that same sub-namespace.
- **Fix**: Move the `.sql` file to the same folder as the `.cs` file, or ensure the `partial class` in C# uses the namespace matching the folder path.
