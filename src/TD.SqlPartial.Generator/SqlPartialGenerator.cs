using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
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
            var sqlFiles = context.AdditionalTextsProvider
                .Where(static file => file.Path.EndsWith(".sql", StringComparison.OrdinalIgnoreCase));

            var provider = sqlFiles.Select(static (text, cancellationToken) =>
            {
                var content = text.GetText(cancellationToken)?.ToString();
                return new { Text = text, Content = content };
            });

            var metadata = context.AnalyzerConfigOptionsProvider.Combine(provider.Collect());

            var items = metadata.SelectMany(static (tuple, cancellationToken) =>
            {
                var optionsProvider = tuple.Left;
                var files = tuple.Right;
                var results = ImmutableArray.CreateBuilder<SqlItem>();

                foreach (var file in files)
                {
                    if (file.Content == null) continue;

                    var fileOptions = optionsProvider.GetOptions(file.Text);
                    fileOptions.TryGetValue("build_metadata.SqlPartial.Namespace", out var ns);
                    fileOptions.TryGetValue("build_metadata.SqlPartial.ClassName", out var className);
                    fileOptions.TryGetValue("build_metadata.SqlPartial.ClassModifier", out var classModifier);
                    fileOptions.TryGetValue("build_metadata.SqlPartial.ConstName", out var constName);
                    fileOptions.TryGetValue("build_metadata.SqlPartial.ConstModifier", out var constModifier);

                    optionsProvider.GlobalOptions.TryGetValue("build_property.Nullable", out var nullable);
                    var nullableEnabled = string.Equals(nullable, "enable", StringComparison.OrdinalIgnoreCase);

                    // Fallback for namespace/classname if not provided via metadata
                    if (string.IsNullOrEmpty(ns)) ns = "Generated";
                    if (string.IsNullOrEmpty(className)) className = Path.GetFileNameWithoutExtension(file.Text.Path);
                    if (string.IsNullOrEmpty(constName)) constName = "SqlQuery";

                    results.Add(new SqlItem(
                        file.Text.Path,
                        Path.GetFileName(file.Text.Path),
                        file.Content,
                        ns!,
                        className!,
                        classModifier,
                        constName!,
                        constModifier,
                        nullableEnabled
                    ));
                }
                return results.ToImmutable();
            });

            context.RegisterSourceOutput(items, static (productionContext, item) =>
            {
                var source = SourceBuilder.Build(item!);
                var fileName = $"{Path.GetFileNameWithoutExtension(item!.FilePath)}.g.cs";
                productionContext.AddSource(fileName, SourceText.From(source, Encoding.UTF8));
            });
        }
    }
}
