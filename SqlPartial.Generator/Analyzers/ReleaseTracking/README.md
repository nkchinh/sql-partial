# Analyzer Release Tracking (RS2008)

The warning `RS2008: Enable analyzer release tracking` has been suppressed in `SqlPartial.Generator.csproj`. 

## Why it was suppressed
Roslyn's built-in analyzers expect diagnostic-providing projects to track their rules' lifecycle (added, removed, changed) using specific files (`AnalyzerReleases.Shipped.md` and `AnalyzerReleases.Unshipped.md`). This is primarily used for large-scale analyzer libraries to manage compatibility and documentation.

Given the current scale of `SqlPartial`, formal release tracking is not yet a priority.

## How to implement properly in the future
If the project grows and requires strict rule versioning, follow these steps:

1. Remove `RS2008` from `<NoWarn>` in `SqlPartial.Generator.csproj`.
2. Create two files in the root of the project:
   - `AnalyzerReleases.Shipped.md`
   - `AnalyzerReleases.Unshipped.md`
3. Mark them as `AdditionalFiles` in the project file:
   ```xml
   <ItemGroup>
     <AdditionalFiles Include="AnalyzerReleases.Shipped.md" />
     <AdditionalFiles Include="AnalyzerReleases.Unshipped.md" />
   </ItemGroup>
   ```
4. Document the diagnostics in these files using the standard format:
   ```markdown
   ## Release 1.0.1

   ### New Rules
   Rule ID | Category | Severity | Notes
   --------|----------|----------|-------
   SQLPG012| Logic    | Warning  | Missing Fallback SQL in manual instantiation.
   ```

Reference: [Microsoft documentation on RS2008](https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md)
