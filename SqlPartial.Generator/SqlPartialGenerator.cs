using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using SqlPartial.Generator.Core;
using SqlPartial.Generator.Models;

namespace SqlPartial.Generator
{
    [Generator]
    public class SqlPartialGenerator : IIncrementalGenerator
    {
        private const string SqlStringsHintName = "SqlStrings.g.cs";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // ── Config (project-level, stable across file edits) ────────────
            var config = context.AnalyzerConfigOptionsProvider
                .Select(static (options, _) => ConfigParser.Parse(options));

            // ── Language version detection (nullable support) ───────────────
            var supportsNullable = context.ParseOptionsProvider
                .Select(static (options, _) =>
                {
                    if (options is CSharpParseOptions csOptions)
                    {
                        return csOptions.LanguageVersion >= LanguageVersion.CSharp8;
                    }
                    return false;
                });

            // ── Collect project directory (needed for namespace derivation) ──
            var projectDir = context.AnalyzerConfigOptionsProvider
                .Select(static (options, _) =>
                {
                    options.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var dir);
                    return dir ?? string.Empty;
                });

            // ── AdditionalFiles: marked as SqlPartial ────────────────────────
            var sqlFiles = context.AdditionalTextsProvider
                .Combine(context.AnalyzerConfigOptionsProvider)
                .Where(static tuple =>
                {
                    var (file, options) = tuple;
                    options.GetOptions(file).TryGetValue(
                        "build_metadata.AdditionalFiles.SourceItemType", out var itemType);
                    return string.Equals(itemType, "SqlPartial", StringComparison.OrdinalIgnoreCase);
                })
                .Select(static (tuple, cancellationToken) =>
                {
                    var (file, _) = tuple;
                    var content = file.GetText(cancellationToken)?.ToString() ?? string.Empty;
                    return (FilePath: file.Path, Content: SqlContentCleaner.Clean(content));
                });

            // ── Parse each file into SqlFile model ───────────────────────────
            var parsedFiles = sqlFiles
                .Combine(config.Combine(projectDir))
                .Select(static (tuple, _) =>
                {
                    var ((filePath, content), (cfg, projDir)) = tuple;
                    var parsed = FilePathParser.TryParse(filePath, cfg.RootNamespace, projDir, cfg.Providers);
                    if (parsed is null) return null;

                    var (ns, className, queryName, providerName) = parsed.Value;
                    return new SqlFile(filePath, ns, className, queryName, providerName, content);
                })
                .Where(static f => f is not null);

            // ── Collect all files, group into SqlQueryGroup ──────────────────
            var groups = parsedFiles
                .Collect()
                .SelectMany(static (files, _) =>
                    files
                        .GroupBy(f => (f!.Namespace, f.ClassName, f.QueryName))
                        .Select(g =>
                        {
                            var contents = new System.Collections.Generic.Dictionary<string, string>();
                            foreach (var file in g)
                            {
                                // If multiple extensions map to same provider name, first one wins
                                if (!contents.ContainsKey(file!.ProviderName))
                                {
                                    contents.Add(file.ProviderName, file.Content);
                                }
                            }

                            return new SqlQueryGroup(
                                g.Key.Namespace,
                                g.Key.ClassName,
                                g.Key.QueryName,
                                contents.ToImmutableDictionary()
                            );
                        })
                );

            // ── Collect groups per class and combine with config + nullable ──
            var classBatches = groups
                .Collect()
                .Combine(config.Combine(supportsNullable))
                .SelectMany(static (tuple, _) =>
                {
                    var (allGroups, (cfg, nullableSupport)) = tuple;
                    return allGroups
                        .GroupBy(g => (g.Namespace, g.ClassName))
                        .Select(g => (
                            Namespace: g.Key.Namespace,
                            ClassName: g.Key.ClassName,
                            Groups: g.ToImmutableArray(),
                            Config: cfg,
                            SupportsNullable: nullableSupport
                        ));
                });

            // ── Emit SqlStrings struct (once, if not external) ───────────────
            context.RegisterSourceOutput(config.Combine(supportsNullable), static (ctx, tuple) =>
            {
                var (cfg, nullableSupport) = tuple;
                if (cfg.ExternalSqlStringsType is not null) return;
                try
                {
                    var source = SourceBuilder.BuildSqlStringsStruct(cfg, nullableSupport);
                    ctx.AddSource(SqlStringsHintName, SourceText.From(source, Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    ReportError(ctx, "SQLGEN001", ex.Message);
                }
            });

            // ── Emit one partial class file per (Namespace, ClassName) ───────
            context.RegisterSourceOutput(classBatches, static (ctx, batch) =>
            {
                try
                {
                    var source = SourceBuilder.BuildPartialClass(
                        batch.Namespace,
                        batch.ClassName,
                        batch.Groups,
                        batch.Config,
                        batch.SupportsNullable);

                    var hash = GetHash($"{batch.Namespace}.{batch.ClassName}");
                    var hintName = $"{batch.ClassName}.{hash}.g.cs";
                    ctx.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    ReportError(ctx, "SQLGEN002", ex.Message);
                }
            });
        }

        private static void ReportError(SourceProductionContext ctx, string id, string message)
        {
            var descriptor = new DiagnosticDescriptor(
                id, "SqlPartial Generator Error", message,
                "SqlPartial", DiagnosticSeverity.Error, isEnabledByDefault: true);
            ctx.ReportDiagnostic(Diagnostic.Create(descriptor, location: null));
        }

        private static string GetHash(string input)
        {
            if (string.IsNullOrEmpty(input)) return "00000000";
            uint hash = 2166136261;
            foreach (char c in input) hash = (hash ^ c) * 16777619;
            return hash.ToString("X8");
        }
    }
}
