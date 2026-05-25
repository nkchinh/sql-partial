using System;
using System.IO;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using TD.SqlPartial.Generator.Core;
using TD.SqlPartial.Generator.Models;

namespace TD.SqlPartial.Generator
{
    [Generator]
    public class SqlPartialGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Watch for .sql files in AdditionalFiles
            var sqlFiles = context.AdditionalTextsProvider
                .Where(static file => file.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));

            // Read options ONCE into a provider — this is stable and won't change per-file
            var optionsProvider = context.AnalyzerConfigOptionsProvider;

            // Combine file + options, then map to SqlItem.
            // IMPORTANT: use a proper record (not anonymous type) so Roslyn incremental
            // caching can compare values correctly and re-run when .sql content changes.
            var items = sqlFiles
                .Combine(optionsProvider)
                .Select(static (tuple, cancellationToken) =>
                {
                    var file = tuple.Left;
                    var options = tuple.Right;

                    var content = file.GetText(cancellationToken)?.ToString();
                    if (content == null) return null;

                    var fileOptions = options.GetOptions(file);

                    // Only process files explicitly marked as SqlPartial
                    if (!fileOptions.TryGetValue("build_metadata.AdditionalFiles.SourceItemType", out var itemType) ||
                        !string.Equals(itemType, "SqlPartial", StringComparison.OrdinalIgnoreCase))
                    {
                        return null;
                    }

                    fileOptions.TryGetValue("build_metadata.AdditionalFiles.SqlNamespace", out var ns);
                    fileOptions.TryGetValue("build_metadata.AdditionalFiles.SqlClassName", out var className);
                    fileOptions.TryGetValue("build_metadata.AdditionalFiles.SqlClassModifier", out var classModifier);
                    fileOptions.TryGetValue("build_metadata.AdditionalFiles.SqlConstName", out var constName);
                    fileOptions.TryGetValue("build_metadata.AdditionalFiles.SqlConstModifier", out var constModifier);

                    options.GlobalOptions.TryGetValue("build_property.Nullable", out var nullable);
                    var nullableEnabled = string.Equals(nullable, "enable", StringComparison.OrdinalIgnoreCase);

                    if (string.IsNullOrEmpty(ns)) ns = "Generated";
                    if (string.IsNullOrEmpty(className)) className = Path.GetFileNameWithoutExtension(file.Path);
                    if (string.IsNullOrEmpty(constName)) constName = "SqlQuery";

                    return new SqlItem(
                        file.Path,
                        Path.GetFileName(file.Path),
                        content,
                        ns!,
                        className!,
                        classModifier,
                        constName!,
                        constModifier,
                        nullableEnabled
                    );
                })
                .Where(static item => item != null);

            context.RegisterSourceOutput(items, static (productionContext, item) =>
            {
                try
                {
                    var source = SourceBuilder.Build(item!);
                    var hash = GetDeterministicHash(item!.FilePath);
                    var fileName = $"{Path.GetFileNameWithoutExtension(item.FilePath)}.{hash}.g.cs";
                    productionContext.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    var descriptor = new DiagnosticDescriptor(
                        "SQLGEN001", "Generator Error", ex.Message,
                        "Error", DiagnosticSeverity.Error, true);
                    productionContext.ReportDiagnostic(Diagnostic.Create(descriptor, null));
                }
            });
        }

        private static string GetDeterministicHash(string input)
        {
            if (string.IsNullOrEmpty(input)) return "0";
            uint hash = 2166136261;
            foreach (char c in input)
            {
                hash = (hash ^ c) * 16777619;
            }
            return hash.ToString("X8");
        }
    }
}
