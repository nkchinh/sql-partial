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

## 2. `SqlStrings` Struct Missing or Ambiguous

- **Struct Missing**: If the project doesn't have a `SqlStrings` type, the generated class will error.
  - *Fix*: Ensure `SqlPartialProviders` is defined in `.csproj`, then build.
- **Multiple Structs**: If you have multiple projects referencing each other, you might have multiple `SqlStrings` types.
  - *Fix*: Use `<SqlPartialStringsType>` in one project to point to the other's struct.

## 3. SQL Syntax Errors in Generated C#

- **Unescaped Quotes**: The generator uses verbatim strings (`@""`). If your SQL has double quotes, they must be escaped as `""`.
- **Character Encoding**: Ensure `.sql` files are saved as UTF-8.

## 4. Generator Diagnostics

The generator emits these codes during build. If you encounter an Error, the build will fail. Warnings indicate potential runtime issues.

| Code | Severity | Category | Meaning |
| :--- | :---: | :---: | :--- |
| **SQLPG001** | `Error` | Config | Invalid `SqlPartialProviders` syntax. Ensure format is `ext:Name`. |
| **SQLPG002** | `Error` | Tooling | Internal failure generating `SqlStrings` struct. |
| **SQLPG003** | `Error` | Tooling | Internal failure generating partial class file. |
| **SQLPG010** | `Warning` | Logic | Missing Fallback SQL & incomplete DBMS coverage. |
| **SQLPG011** | `Warning` | Quality | SQL file is empty after cleaning comments/excludes. |
| **SQLPG012** | `Warning` | Logic | Missing Fallback SQL in manual instantiation (`new SqlStrings`). |
| **SQLPG020** | `Warning` | Usage | Unrecognized SQL extension found (Disabled by default). |
