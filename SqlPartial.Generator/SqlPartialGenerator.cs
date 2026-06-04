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

            // ── Report SQLPG001 (Invalid Config) ────────────────────────────
            context.RegisterSourceOutput(config, static (ctx, cfg) =>
            {
                foreach (var invalidEntry in cfg.InvalidProviderEntries)
                {
                    ReportDiagnostic(ctx, "SQLPG001", "Invalid Provider Configuration", 
                        $"The provider configuration '{invalidEntry}' is invalid. It must follow the format 'extension:DisplayName' (e.g., '.pg.sql:PostgreSql').", 
                        DiagnosticSeverity.Error);
                }
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
                    
                    if (parsed is null)
                    {
                        return new SqlFileResult(filePath, null, isUnrecognized: true);
                    }

                    var (ns, className, queryName, providerName) = parsed.Value;
                    var file = new SqlFile(filePath, ns, className, queryName, providerName, content);
                    return new SqlFileResult(filePath, file, isUnrecognized: false);
                });

            // ── Report SQLPG011 (Empty) & SQLPG020 (Unrecognized) ────────────
            context.RegisterSourceOutput(parsedFiles.Combine(config), static (ctx, tuple) =>
            {
                var (result, cfg) = tuple;
                if (result.IsUnrecognized)
                {
                    if (cfg.WarnOnUnrecognized)
                    {
                        ReportDiagnostic(ctx, "SQLPG020", "Unrecognized SQL file extension", 
                            $"File '{System.IO.Path.GetFileName(result.FilePath)}' does not match any configured provider or fallback extension.", 
                            DiagnosticSeverity.Warning, result.FilePath);
                    }
                    return;
                }

                if (result.File != null && string.IsNullOrWhiteSpace(result.File.Content))
                {
                    ReportDiagnostic(ctx, "SQLPG011", "Empty SQL content", 
                        $"File '{System.IO.Path.GetFileName(result.File.FilePath)}' is empty after cleaning.", 
                        DiagnosticSeverity.Warning, result.File.FilePath);
                }
            });

            // ── Filter valid files and group into SqlQueryGroup ──────────────
            var groups = parsedFiles
                .Where(static r => r.File != null)
                .Select(static (r, _) => r.File!)
                .Collect()
                .SelectMany(static (files, _) =>
                    files
                        .GroupBy(f => (f.Namespace, f.ClassName, f.QueryName))
                        .Select(g =>
                        {
                            var contents = new System.Collections.Generic.Dictionary<string, string>();
                            foreach (var file in g)
                            {
                                // If multiple extensions map to same provider name, first one wins
                                if (!contents.ContainsKey(file.ProviderName))
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
                    ReportDiagnostic(ctx, "SQLPG002", "Struct Generation Failed", ex.Message, DiagnosticSeverity.Error);
                }
            });

            // ── Emit one partial class file per (Namespace, ClassName) ───────
            context.RegisterSourceOutput(classBatches, static (ctx, batch) =>
            {
                try
                {
                    // Report SQLPG010 (Missing Fallback)
                    foreach (var group in batch.Groups)
                    {
                        var hasFallback = group.ContentByProviderName.ContainsKey(FilePathParser.FallbackProviderName);
                        var providersCount = group.ContentByProviderName.Count(kvp => kvp.Key != FilePathParser.FallbackProviderName);
                        var totalConfigured = batch.Config.DistinctProviderNames.Count();

                        if (!hasFallback && providersCount < totalConfigured)
                        {
                            ReportDiagnostic(ctx, "SQLPG010", "Missing Fallback SQL", 
                                $"Query '{group.QueryName}' in class '{group.ClassName}' is missing a fallback and does not cover all configured providers. Runtime may return empty strings.", 
                                DiagnosticSeverity.Warning);
                        }
                    }

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
                    ReportDiagnostic(ctx, "SQLPG003", "Class Generation Failed", ex.Message, DiagnosticSeverity.Error);
                }
            });
        }

        private static void ReportDiagnostic(SourceProductionContext ctx, string id, string title, string message, DiagnosticSeverity severity, string? filePath = null)
        {
            var descriptor = new DiagnosticDescriptor(
                id, title, message,
                "SqlPartial", severity, isEnabledByDefault: true);
            
            Location? location = null;
            if (filePath != null)
            {
                location = Location.Create(filePath, new Microsoft.CodeAnalysis.Text.TextSpan(0, 0), new Microsoft.CodeAnalysis.Text.LinePositionSpan(new Microsoft.CodeAnalysis.Text.LinePosition(0, 0), new Microsoft.CodeAnalysis.Text.LinePosition(0, 0)));
            }

            ctx.ReportDiagnostic(Diagnostic.Create(descriptor, location));
        }

        private static string GetHash(string input)
        {
            if (string.IsNullOrEmpty(input)) return "00000000";
            uint hash = 2166136261;
            foreach (char c in input) hash = (hash ^ c) * 16777619;
            return hash.ToString("X8");
        }

        private sealed class SqlFileResult(string filePath, SqlFile? file, bool isUnrecognized)
        {
            public string FilePath { get; } = filePath;
            public SqlFile? File { get; } = file;
            public bool IsUnrecognized { get; } = isUnrecognized;
        }
    }
}
