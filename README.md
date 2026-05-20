# TD.SqlPartial.Tool

Source Generator for generating partial classes from SQL files with configurable modifiers. Modern replacement for MSBuild task version.

## Installation

```xml
<PackageReference Include="TD.SqlPartial.Tool" Version="2.0.0" />
```

## Features

- **Incremental Source Generator**: Live IDE feedback and high performance.
- **Automatic Modifier Matching**: Partial classes now match the main part's modifier automatically if not specified.
- **Nullable Context Support**: Automatically adds `#nullable enable` if the project supports it.
- **Backward Compatible**: Works with existing `SqlPartial` configurations.
- Generates partial classes from SQL files.
- Configurable class and const modifiers.
- SQL cleanup support (strips comments and `#testpart` blocks).
