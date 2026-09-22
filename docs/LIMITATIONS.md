# Limitations and Known Issues

This document tracks known limitations and architectural decisions for the current version of SqlPartial.

## Known Issues

### VS Code (C# Dev Kit) Synchronization
**Status:** Known Issue (Design-time only)

When using VS Code with the C# Dev Kit extension, the Incremental Generator may not always trigger an update immediately when a `.sql` file is modified.
- **Symptom:** IntelliSense for `SqlGetUsers` (or similar) stays red even after modifying the SQL file.
- **Workaround:** Briefly modify the corresponding `.cs` file (e.g., add/remove a space) or restart the C# Language Server.
- **Root Cause:** The Roslyn LSP in VS Code sometimes fails to pass custom MSBuild metadata (`SourceItemType`) during live analysis.

## Limitations

### Nested Classes
**Status:** Under Consideration

The current version does not support SQL file targets or `[Sql]` overload generation for types declared inside other types.
- **Recommendation:** Declare SQL classes and `[Sql]` method containers directly in a namespace.

### Generic Target Types

SQL files and `[Sql]` overload generation cannot target generic classes or generic interfaces. Use a non-generic partial class to hold SQL queries and `[Sql]` methods. Generic methods on a supported non-generic class remain supported.

### Record and Struct Support
**Status:** Not Implemented (by design)

SqlPartial is currently optimized for `class` and traditional `interface` usage. SQL file targets and `[Sql]` overload generation do not support `record`, `record struct`, or `struct` in this release.

### Traditional Interfaces Only
**Status:** Modern interface members are not supported

`[Sql]` overload generation supports top-level, non-generic interfaces with ordinary public instance methods. It does not support private, protected, or internal interface members; default interface implementations; or static abstract/virtual interface members.

### Partial Method Containers

A class containing a method with a `[Sql]` parameter must be declared `partial`. This also applies to static classes containing extension methods. The generator reports `SQLPG024` and skips overload generation when the containing class is not partial. Interfaces do not need to be partial because their overloads are emitted into a separate extension class.
