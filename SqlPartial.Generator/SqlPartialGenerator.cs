using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SqlPartial.Generator.Core;
using SqlPartial.Generator.Models;

namespace SqlPartial.Generator;

[Generator]
public class SqlPartialGenerator : IIncrementalGenerator
{
    private const string SqlStringsHintName = "SqlStrings.g.cs";
    private const string SqlAttributeHintName = "SqlAttribute.g.cs";

    private static class Diagnostics
    {
        private const string Category = "SqlPartial";

        public static readonly DiagnosticDescriptor SQLPG001 = new(
            "SQLPG001", "Invalid Provider Configuration", "{0}",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SQLPG002 = new(
            "SQLPG002", "Struct Generation Failed", "{0}",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SQLPG003 = new(
            "SQLPG003", "Class Generation Failed", "{0}",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SQLPG004 = new(
            "SQLPG004", "Overload Generation Failed", "{0}",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SQLPG010 = new(
            "SQLPG010", "Missing Fallback SQL", "{0}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SQLPG011 = new(
            "SQLPG011", "Empty SQL content", "{0}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SQLPG013 = new(
            "SQLPG013", "Mismatched Exclude Tag", "{0}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SQLPG020 = new(
            "SQLPG020", "Unrecognized SQL file extension", "{0}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static DiagnosticDescriptor GetDescriptor(string id) => id switch
        {
            "SQLPG001" => SQLPG001,
            "SQLPG002" => SQLPG002,
            "SQLPG003" => SQLPG003,
            "SQLPG004" => SQLPG004,
            "SQLPG010" => SQLPG010,
            "SQLPG011" => SQLPG011,
            "SQLPG013" => SQLPG013,
            "SQLPG020" => SQLPG020,
            _ => new DiagnosticDescriptor(id, "Generator Error", "{0}", Category, DiagnosticSeverity.Error, true)
        };
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ── Post Initialization (SqlAttribute) ───────────────────────────
        context.RegisterPostInitializationOutput(static ctx =>
        {
            var source = SourceBuilder.BuildSqlAttribute();
            ctx.AddSource(SqlAttributeHintName, SourceText.From(source, Encoding.UTF8));
        });

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
                ReportDiagnostic(ctx, Diagnostics.SQLPG001,
                    $"The provider configuration '{invalidEntry}' is invalid. It must follow the format 'extension:DisplayName' (e.g., '.pg.sql:PostgreSql').");
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
                var sourceText = file.GetText(cancellationToken);
                var contentString = sourceText?.ToString() ?? string.Empty;
                var cleanResult = SqlContentCleaner.Clean(contentString);
                return (FilePath: file.Path, SourceText: sourceText, CleanResult: cleanResult);
            });

        // ── Parse each file into SqlFile model ───────────────────────────
        var parsedFiles = sqlFiles
            .Combine(config.Combine(projectDir))
            .Select(static (tuple, _) =>
            {
                var ((filePath, sourceText, cleanResult), (cfg, projDir)) = tuple;
                var parsed = FilePathParser.TryParse(filePath, cfg.RootNamespace, projDir, cfg.SortedProviders);

                if (parsed is null)
                {
                    return new SqlFileResult(filePath, null, isUnrecognized: true);
                }

                var (ns, className, queryName, providerName) = parsed.Value;
                var file = new SqlFile(filePath, ns, className, queryName, providerName, cleanResult.Content);
                return new SqlFileResult(filePath, file, isUnrecognized: false, sourceText, cleanResult.Diagnostics);
            });

        // ── Report SQLPG011 (Empty), SQLPG013 (Mismatched) & SQLPG020 (Unrecognized) ──
        context.RegisterSourceOutput(parsedFiles.Combine(config), static (ctx, tuple) =>
        {
            var (result, cfg) = tuple;

            // Report SqlContentCleaner diagnostics (SQLPG013)
            foreach (var diag in result.Diagnostics)
            {
                var descriptor = Diagnostics.GetDescriptor(diag.Id);
                ReportDiagnostic(ctx, descriptor, diag.Message,
                    result.FilePath, diag.Offset, diag.Length, result.SourceText);
            }

            if (result.IsUnrecognized)
            {
                if (cfg.WarnOnUnrecognized)
                {
                    ReportDiagnostic(ctx, Diagnostics.SQLPG020,
                        $"File '{System.IO.Path.GetFileName(result.FilePath)}' does not match any configured provider or fallback extension.",
                        result.FilePath);
                }
                return;
            }

            if (result.File != null && string.IsNullOrWhiteSpace(result.File.Content))
            {
                ReportDiagnostic(ctx, Diagnostics.SQLPG011,
                    $"File '{System.IO.Path.GetFileName(result.File.FilePath)}' is empty after cleaning.",
                    result.File.FilePath);
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
        var modifierConfigs = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "SqlPartial.SqlPartialAttribute",
                predicate: static (s, _) => s is ClassDeclarationSyntax || s is InterfaceDeclarationSyntax,
                transform: static (ctx, _) =>
                {
                    if (ctx.TargetSymbol is not INamedTypeSymbol symbol) return null;

                    var attr = symbol.GetAttributes().FirstOrDefault(a =>
                        a.AttributeClass?.ToDisplayString() == "SqlPartial.SqlPartialAttribute" ||
                        a.AttributeClass?.Name == "SqlPartialAttribute");

                    if (attr == null || attr.ConstructorArguments.Length == 0) return null;

                    var modifierVal = (int)attr.ConstructorArguments[0].Value!;
                    var modifier = modifierVal switch
                    {
                        1 => "internal",
                        2 => "protected",
                        3 => "public",
                        _ => "private"
                    };

                    return ((string Namespace, string ClassName, string Modifier)?)(symbol.ContainingNamespace.ToDisplayString(), symbol.Name, modifier);
                })
            .Where(static x => x != null)
            .Select(static (x, _) => x!.Value)
            .Collect();

        var classBatches = groups
            .Collect()
            .Combine(config.Combine(supportsNullable))
            .Combine(modifierConfigs)
            .SelectMany(static (tuple, _) =>
            {
                var ((allGroups, (cfg, nullableSupport)), modifiers) = tuple;

                var modifierLookup = modifiers.ToDictionary(
                    m => (m.Namespace, m.ClassName),
                    m => m.Modifier);

                return allGroups
                    .GroupBy(g => (g.Namespace, g.ClassName))
                    .Select(g =>
                    {
                        modifierLookup.TryGetValue((g.Key.Namespace, g.Key.ClassName), out var modifier);
                        return (
                            Namespace: g.Key.Namespace,
                            ClassName: g.Key.ClassName,
                            Groups: g.ToImmutableArray(),
                            Config: cfg,
                            SupportsNullable: nullableSupport,
                            Modifier: modifier ?? "private"
                        );
                    });
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
                ReportDiagnostic(ctx, Diagnostics.SQLPG002, ex.Message);
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
                        ReportDiagnostic(ctx, Diagnostics.SQLPG010,
                            $"Query '{group.QueryName}' in class '{group.ClassName}' is missing a fallback and does not cover all configured providers. Runtime may return empty strings.");
                    }
                }

                var source = SourceBuilder.BuildPartialClass(
                    batch.Namespace,
                    batch.ClassName,
                    batch.Groups,
                    batch.Config,
                    batch.SupportsNullable,
                    batch.Modifier);

                var hash = GetHash($"{batch.Namespace}.{batch.ClassName}");
                var hintName = $"{batch.ClassName}.{hash}.g.cs";
                ctx.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                ReportDiagnostic(ctx, Diagnostics.SQLPG003, ex.Message);
            }
        });

        // ── Emit Overloads for [Sql] parameters ──────────────────────────
        var methodDeclarations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "SqlPartial.SqlAttribute",
                predicate: static (s, _) => s is ParameterSyntax,
                transform: static (ctx, _) => ctx.TargetSymbol.ContainingSymbol as IMethodSymbol)
            .Where(static m => m is not null)
            .Select(static (m, _) => m!)
            .Collect();

        context.RegisterSourceOutput(methodDeclarations.Combine(config.Combine(supportsNullable)), static (ctx, tuple) =>
        {
            var (methods, (cfg, nullableSupport)) = tuple;
            if (methods.IsDefaultOrEmpty) return;

            var methodsByType = methods
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                .GroupBy(m => m.ContainingType, SymbolEqualityComparer.Default);

            foreach (var group in methodsByType)
            {
                var type = (ITypeSymbol)group.Key!;
                var ns = type.ContainingNamespace?.ToDisplayString() ?? "Generated";

                // Final safety check: does it have SqlProviderName?
                // (Already checked by Analyzer, but good to be safe for generation)
                bool hasProviderName = type.GetMembers("SqlProviderName")
                    .OfType<IPropertySymbol>()
                    .Any(p => p.Type.SpecialType == SpecialType.System_String);

                // Check interfaces if not in class
                if (!hasProviderName)
                {
                    hasProviderName = type.AllInterfaces.Any(i => i.GetMembers("SqlProviderName")
                        .OfType<IPropertySymbol>()
                        .Any(p => p.Type.SpecialType == SpecialType.System_String));
                }

                // If it's an extension method in a static class, we should also check the extended type
                if (!hasProviderName)
                {
                    foreach (var method in group)
                    {
                        if (method.IsExtensionMethod && method.Parameters.Length > 0)
                        {
                            var extendedType = method.Parameters[0].Type;
                            bool extendedTypeHasProvider = extendedType.GetMembers("SqlProviderName")
                                .OfType<IPropertySymbol>()
                                .Any(p => p.Type.SpecialType == SpecialType.System_String);

                            if (!extendedTypeHasProvider)
                            {
                                extendedTypeHasProvider = extendedType.AllInterfaces.Any(i => i.GetMembers("SqlProviderName")
                                    .OfType<IPropertySymbol>()
                                    .Any(p => p.Type.SpecialType == SpecialType.System_String));
                            }

                            if (extendedTypeHasProvider)
                            {
                                hasProviderName = true;
                                break;
                            }
                        }
                    }
                }

                if (!hasProviderName) continue;

                try
                {
                    var source = SourceBuilder.BuildOverloads(ns, type, group.ToList(), cfg, nullableSupport);
                    var hash = GetHash($"{ns}.{type.Name}.Overloads");
                    ctx.AddSource($"{type.Name}.{hash}.Overloads.g.cs", SourceText.From(source, Encoding.UTF8));
                }
                catch (Exception ex)
                {
                    ReportDiagnostic(ctx, Diagnostics.SQLPG004, ex.Message);
                }
            }
        });
    }

    private static void ReportDiagnostic(
        SourceProductionContext ctx, DiagnosticDescriptor descriptor, string message,
        string? filePath = null, int offset = 0, int length = 0, SourceText? sourceText = null)
    {
        Location? location = null;
        if (filePath != null)
        {
            var span = new TextSpan(offset, length);
            LinePositionSpan lineSpan;

            if (sourceText != null && offset + length <= sourceText.Length)
            {
                lineSpan = sourceText.Lines.GetLinePositionSpan(span);
            }
            else
            {
                lineSpan = new LinePositionSpan(new LinePosition(0, 0), new LinePosition(0, 0));
            }

            location = Location.Create(filePath, span, lineSpan);
        }

        ctx.ReportDiagnostic(Diagnostic.Create(descriptor, location, message));
    }

    private static string GetHash(string input)
    {
        if (string.IsNullOrEmpty(input)) return "00000000";
        uint hash = 2166136261;
        foreach (char c in input) hash = (hash ^ c) * 16777619;
        return hash.ToString("X8");
    }

    private sealed class SqlFileResult(
        string filePath,
        SqlFile? file,
        bool isUnrecognized,
        SourceText? sourceText = null,
        ImmutableArray<SqlDiagnosticInfo>? diagnostics = null)
    {
        public string FilePath { get; } = filePath;
        public SqlFile? File { get; } = file;
        public bool IsUnrecognized { get; } = isUnrecognized;
        public SourceText? SourceText { get; } = sourceText;
        public ImmutableArray<SqlDiagnosticInfo> Diagnostics { get; } = diagnostics ?? ImmutableArray<SqlDiagnosticInfo>.Empty;
    }
}
