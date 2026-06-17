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

    internal static class Diagnostics
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

        public static readonly DiagnosticDescriptor SQLPG005 = new(
            "SQLPG005", "Naming Collision", "{0}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor SQLPG006 = new(
            "SQLPG006", "Duplicate SQL Mapping", "{0}",
            Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

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
            "SQLPG005" => SQLPG005,
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
        context.RegisterPostInitializationOutput(ctx =>
        {
            var source = SourceBuilder.BuildSqlAttribute();
            ctx.AddSource(SqlAttributeHintName, SourceText.From(source, Encoding.UTF8));
        });

        // ── Config (project-level, stable across file edits) ────────────
        var config = context.AnalyzerConfigOptionsProvider
            .Select((options, _) => ConfigParser.Parse(options));

        // ── Language version detection (nullable support) ───────────────
        var supportsNullable = context.ParseOptionsProvider
            .Select((options, _) =>
            {
                if (options is CSharpParseOptions csOptions)
                {
                    return csOptions.LanguageVersion >= LanguageVersion.CSharp8;
                }
                return false;
            });

        // ── Collect project directory (needed for namespace derivation) ──
        var projectDir = context.AnalyzerConfigOptionsProvider
            .Select((options, _) =>
            {
                options.GlobalOptions.TryGetValue("build_property.MSBuildProjectDirectory", out var dir);
                return dir ?? string.Empty;
            });

        // ── Report SQLPG001 (Invalid Config) ────────────────────────────
        context.RegisterSourceOutput(config, (ctx, cfg) =>
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
            .Where(tuple =>
            {
                var (file, options) = tuple;
                options.GetOptions(file).TryGetValue(
                    "build_metadata.AdditionalFiles.SourceItemType", out var itemType);
                return string.Equals(itemType, "SqlPartial", StringComparison.OrdinalIgnoreCase);
            })
            .Select((tuple, cancellationToken) =>
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
            .Select((tuple, _) =>
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
        context.RegisterSourceOutput(parsedFiles.Combine(config), (ctx, tuple) =>
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
            .Where(r => r.File != null)
            .Select((r, _) => r.File!)
            .Collect()
            .SelectMany((files, _) =>
                files
                    .GroupBy(f => (f.Namespace, f.ClassName, f.QueryName))
                    .Select(g =>
                    {
                        var contents = new System.Collections.Generic.Dictionary<string, string>();
                        var diagnostics = new System.Collections.Generic.List<(DiagnosticDescriptor, string, string)>(); // (Descriptor, Message, FilePath)

                        foreach (var file in g)
                        {
                            if (contents.TryGetValue(file.ProviderName, out var existing))
                            {
                                diagnostics.Add((Diagnostics.SQLPG006,
                                    $"Multiple files map to the provider '{file.ProviderName}' for query '{file.QueryName}' in class '{file.ClassName}'. The first one encountered will be used.",
                                    file.FilePath));
                                continue;
                            }
                            contents.Add(file.ProviderName, file.Content);
                        }

                        var group = new SqlQueryGroup(
                            g.Key.Namespace,
                            g.Key.ClassName,
                            g.Key.QueryName,
                            contents.ToImmutableDictionary()
                        );

                        return (Group: group, Diagnostics: diagnostics.ToImmutableArray());
                    })
            );

        // ── Report SQLPG006 (Duplicate Mapping) ─────────────────────────
        context.RegisterSourceOutput(groups, (ctx, tuple) =>
        {
            foreach (var diag in tuple.Diagnostics)
            {
                ReportDiagnostic(ctx, diag.Item1, diag.Item2, diag.Item3);
            }
        });

        // ── Collect groups per class and combine with config + nullable ──
        var classMetadata = context.SyntaxProvider.CreateSyntaxProvider(
            predicate: static (node, _) =>
                (node is ClassDeclarationSyntax c && c.Modifiers.Any(SyntaxKind.PartialKeyword)) ||
                (node is InterfaceDeclarationSyntax i && i.Modifiers.Any(SyntaxKind.PartialKeyword)),
            transform: static (ctx, _) =>
            {
                var symbol = ctx.SemanticModel.GetDeclaredSymbol(ctx.Node) as INamedTypeSymbol;
                if (symbol == null) return default;

                var ns = symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString();
                var fullName = string.IsNullOrEmpty(ns) ? symbol.Name : $"{ns}.{symbol.Name}";

                // Check for attribute
                var attr = symbol.GetAttributes().FirstOrDefault(a =>
                    a.AttributeClass?.ToDisplayString() == "SqlPartial.SqlPartialAttribute" ||
                    a.AttributeClass?.Name == "SqlPartialAttribute");

                string modifier = "private";
                if (attr != null && attr.ConstructorArguments.Length > 0)
                {
                    var modifierVal = (int)attr.ConstructorArguments[0].Value!;
                    modifier = modifierVal switch
                    {
                        1 => "internal",
                        2 => "protected",
                        3 => "public",
                        _ => "private"
                    };
                }

                var members = symbol.MemberNames.ToImmutableHashSet();
                return (FullTypeName: fullName, Modifier: modifier, ExistingMembers: members);
            })
            .Where(static x => x != default)
            .Collect();

        var classBatches = groups
            .Collect()
            .Combine(config.Combine(supportsNullable))
            .Combine(classMetadata)
            .SelectMany((tuple, _) =>
            {
                var ((allGroups, (cfg, nullableSupport)), metadata) = tuple;

                // Use a dictionary but handle potential duplicates from multiple partial files
                var metadataLookup = new System.Collections.Generic.Dictionary<string, (string Modifier, ImmutableHashSet<string> ExistingMembers)>();
                foreach (var m in metadata)
                {
                    if (!metadataLookup.ContainsKey(m.FullTypeName))
                    {
                        metadataLookup.Add(m.FullTypeName, (m.Modifier, m.ExistingMembers));
                    }
                    else if (m.Modifier != "private")
                    {
                        // If we already have an entry but this one has the attribute, use its modifier.
                        // MemberNames should be identical for all parts.
                        metadataLookup[m.FullTypeName] = (m.Modifier, m.ExistingMembers);
                    }
                }

                return allGroups
                    .GroupBy(g =>
                    {
                        var group = g.Group;
                        return string.IsNullOrEmpty(group.Namespace) ? group.ClassName : $"{group.Namespace}.{group.ClassName}";
                    })
                    .Select(g =>
                    {
                        metadataLookup.TryGetValue(g.Key, out var info);

                        var finalGroups = new System.Collections.Generic.List<SqlQueryGroup>();
                        var diagnostics = new System.Collections.Generic.List<(DiagnosticDescriptor, string)>();

                        foreach (var tuple in g)
                        {
                            var group = tuple.Group;
                            var originalName = $"Sql{group.QueryName}";
                            var currentName = originalName;
                            int counter = 1;

                            // Check if the property name already exists in the user's class
                            if (info.ExistingMembers != null && info.ExistingMembers.Contains(currentName))
                            {
                                while (info.ExistingMembers.Contains($"{originalName}{counter}"))
                                {
                                    counter++;
                                }
                                currentName = $"{originalName}{counter}";
                                diagnostics.Add((Diagnostics.SQLPG005,
                                    $"Property '{originalName}' already exists in class '{group.ClassName}'. Generated property renamed to '{currentName}'."));
                            }

                            if (currentName != originalName)
                            {
                                finalGroups.Add(new SqlQueryGroup(group.Namespace, group.ClassName, currentName.Substring(3), group.ContentByProviderName));
                            }
                            else
                            {
                                finalGroups.Add(group);
                            }
                        }

                        // Split key back to namespace and class name
                        var lastDot = g.Key.LastIndexOf('.');
                        var ns = lastDot == -1 ? "" : g.Key.Substring(0, lastDot);
                        var className = lastDot == -1 ? g.Key : g.Key.Substring(lastDot + 1);

                        return (
                            Namespace: ns,
                            ClassName: className,
                            Groups: finalGroups.ToImmutableArray(),
                            Config: cfg,
                            SupportsNullable: nullableSupport,
                            Modifier: info.Modifier ?? "private",
                            Diagnostics: diagnostics.ToImmutableArray()
                        );
                    });
            });

        // ── Emit SqlStrings struct (once, if not external) ───────────────
        context.RegisterSourceOutput(config.Combine(supportsNullable), (ctx, tuple) =>
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
        context.RegisterSourceOutput(classBatches, (ctx, batch) =>
        {
            try
            {
                // Report SQLPG005 (Naming Collision)
                foreach (var diag in batch.Diagnostics)
                {
                    ReportDiagnostic(ctx, diag.Item1, diag.Item2);
                }

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
                predicate: (s, _) => s is ParameterSyntax,
                transform: (ctx, _) => ctx.TargetSymbol.ContainingSymbol as IMethodSymbol)
            .Where(m => m is not null)
            .Select((m, _) => m!)
            .Collect();

        context.RegisterSourceOutput(methodDeclarations.Combine(config.Combine(supportsNullable)), (ctx, tuple) =>
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
