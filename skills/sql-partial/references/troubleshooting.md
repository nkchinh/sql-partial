# SqlPartial Troubleshooting Guide

This guide covers common issues encountered when using `SqlPartial.Generator`.

## 1. Property Not Generated (Property Missing on Class)

If you've created a `.sql` file but the corresponding `Sql{QueryName}` property is missing:

- **Check Filename**: It must follow `ClassName.QueryName.[extension]`. `ClassName` is case-sensitive and must match the C# class exactly.
- **Check Extension**: Ensure the extension (e.g., `.pg.sql`) is correctly registered in `SqlPartialProviders`.
- **Check Namespace**: The generator places the partial class in a namespace based on the file's relative path. If your `.cs` file is in a different namespace, they won't merge.
  - *Fix*: Move the `.sql` file to the same folder as the `.cs` file.
- **Check MSBuild Metadata**: Ensure the `.sql` file is included in `.csproj` with `<SourceItemType>SqlPartial</SourceItemType>`.
- **Trigger Generator**: Sometimes the Language Server needs a nudge. Save a `.cs` file or rebuild the project.

## 2. `SQLPG030`: Missing `SqlProviderName`

This error occurs when you mark a method parameter with `[Sql]` but the containing type does not define a `SqlProviderName` property.

- **Requirement**: You must define `public string SqlProviderName { get; }` (or `set`). It can be static or an instance property.
- **Why**: The generated overload needs to know which DBMS provider is currently active at runtime to select the correct SQL string.

```csharp
public partial class UserRepo {
    public string SqlProviderName => "PostgreSql"; // Fix
    public void Get([Sql] string query) => ...
}
```

## 3. Inconsistent Accessibility (CS0703)

If you get an error saying `ISqlString` is less accessible than the generated overload:

- **Cause**: By default, `ISqlString` and `SqlStrings` are `internal`. If your repository method is `public`, the generated overload (which references `ISqlString`) would also be `public`, which is invalid.
- **Fix**: Either make your repository method `internal`, or use `SqlPartialEmitSharedNamespace` to make the core types `public`.

## 4. `SqlStrings` Struct Missing or Ambiguous

- **Struct Missing**: If the project doesn't have a `SqlStrings` type, the generated class will error.
  - *Fix*: Ensure `SqlPartialProviders` is defined in `.csproj`, then build.
- **Ambiguous / Multiple Structs**: If you have multiple projects referencing each other, you might have multiple `SqlStrings` types.
  - *Fix*: Use `SqlPartialEmitSharedNamespace` in your Abstractions project and `SqlPartialUseSharedNamespace` in consumer projects. Alternatively, use `<SqlPartialStringsType>` to point to a specific existing struct.

## 5. SQL Syntax Errors in Generated C#

- **Unescaped Quotes**: The generator uses verbatim strings (`@""`). If your SQL has double quotes, they must be escaped as `""`.
- **Character Encoding**: Ensure `.sql` files are saved as UTF-8.

## 6. Generator Diagnostics

The generator emits these codes during build. If you encounter an Error, the build will fail. Warnings indicate potential runtime issues.

| Code | Severity | Category | Meaning |
| :--- | :---: | :---: | :--- |
| **SQLPG001** | `Error` | Config | Invalid `SqlPartialProviders` syntax. Ensure format is `ext:Name`. |
| **SQLPG002** | `Error` | Tooling | Internal failure generating `SqlStrings` struct. |
| **SQLPG003** | `Error` | Tooling | Internal failure generating partial class file. |
| **SQLPG030** | `Error` | Design | Missing `SqlProviderName` property for `[Sql]` usage. |
| **SQLPG010** | `Warning` | Logic | Missing Default SQL & incomplete DBMS coverage. |
| **SQLPG011** | `Warning` | Quality | SQL file is empty after cleaning comments/excludes. |
| **SQLPG012** | `Warning` | Logic | Missing Default SQL in manual instantiation (`new SqlStrings`). |
| **SQLPG020** | `Warning` | Usage | Unrecognized SQL extension found (Disabled by default). |
