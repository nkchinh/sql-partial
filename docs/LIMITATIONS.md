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

The current version of the generator does not fully support types declared inside other classes (Nested Classes).
- **Behavior:** The generated partial class may have an incorrect hierarchy, leading to compilation errors.
- **Recommendation:** Keep classes using `[SqlPartial]` at the namespace level.

### Record and Struct Support
**Status:** Not Implemented (by design)

SqlPartial is currently optimized for `class` and `interface` usage. Support for `record`, `record struct`, and `struct` is not provided in this release as the primary use cases focus on Data Access Objects (DAOs) and Repositories typically implemented as classes.
