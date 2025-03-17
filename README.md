# TD.SqlPartial.Tool

MSBuild task for generating partial classes from SQL files with configurable modifiers.

## Installation

```xml
<PackageReference Include="TD.SqlPartial.Tool" Version="1.0.3" />
```

## Usage

1. Add SQL files to your project:

```xml
<ItemGroup>
    <SqlPartial Include="Sql/**/*.*.sql"
                ClassModifier="internal"
                ConstModifier="public" />
</ItemGroup>
```

2. SQL files will be automatically converted to partial classes:

```csharp
namespace YourNamespace.Sql
{
    internal partial class QueryFile
    {
        public const string SqlQuery = @"SELECT * FROM Users";
    }
}
```

## Features

- Generates partial classes from SQL files
- Configurable class and const modifiers
- Automatic cleanup of orphaned files
- Incremental build support
- Test part exclusion support
- SQL cleanup support

## Configuration

| Property | Default | Description |
|----------|---------|-------------|
| ClassModifier | public | Access modifier for generated class |
| ConstModifier | internal | Access modifier for SQL constant |
