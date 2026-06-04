using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
using SqlPartial.Generator.Models;

namespace SqlPartial.Generator.Core;

internal static class ConfigParser
{
    /// <summary>
    /// Parses project-level MSBuild properties from AnalyzerConfigOptionsProvider.
    /// </summary>
    public static GeneratorConfig Parse(AnalyzerConfigOptionsProvider optionsProvider)
    {
        var global = optionsProvider.GlobalOptions;

        global.TryGetValue("build_property.RootNamespace", out var rootNamespace);

        // Try normalized property first (semicolons replaced by commas in .targets)
        if (!global.TryGetValue("build_property.SqlPartialProviders_Normalized", out var providersRaw))
        {
            global.TryGetValue("build_property.SqlPartialProviders", out providersRaw);
        }

        global.TryGetValue("build_property.SqlPartialStringsNamespace", out var stringsNs);
        global.TryGetValue("build_property.SqlPartialStringsType", out var externalType);
        global.TryGetValue("build_property.Nullable", out var nullable);
        global.TryGetValue("build_property.SqlPartialWarnOnUnrecognized", out var warnRaw);

        rootNamespace = string.IsNullOrWhiteSpace(rootNamespace) ? "Generated" : rootNamespace!.Trim();

        var (providers, invalidEntries) = ParseProviders(providersRaw);

        var sqlStringsNamespace = string.IsNullOrWhiteSpace(stringsNs)
            ? rootNamespace
            : stringsNs!.Trim();

        var nullableEnabled = string.Equals(nullable, "enable", System.StringComparison.OrdinalIgnoreCase);
        var warnOnUnrecognized = string.Equals(warnRaw, "true", System.StringComparison.OrdinalIgnoreCase);

        return new GeneratorConfig(
            rootNamespace,
            providers,
            invalidEntries,
            sqlStringsNamespace,
            string.IsNullOrWhiteSpace(externalType) ? null : externalType!.Trim(),
            nullableEnabled,
            warnOnUnrecognized
        );
    }

    /// <summary>
    /// Parses "pg.sql:PostgreSql;pgsql:PostgreSql;ms.sql:SqlServer" into SqlProvider list.
    /// Returns a tuple containing valid providers and any invalid raw entries.
    /// </summary>
    internal static (ImmutableArray<SqlProvider> providers, ImmutableArray<string> invalidEntries) ParseProviders(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (ImmutableArray<SqlProvider>.Empty, ImmutableArray<string>.Empty);

        var validBuilder = ImmutableArray.CreateBuilder<SqlProvider>();
        var invalidBuilder = ImmutableArray.CreateBuilder<string>();

        // Support both semicolon and comma as separators
        var entries = raw!.Split([';', ','], System.StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var trimmed = entry.Trim();
            var parts = trimmed.Split(':');
            if (parts.Length != 2)
            {
                invalidBuilder.Add(trimmed);
                continue;
            }

            var extension = parts[0].Trim();
            var name = parts[1].Trim();

            if (string.IsNullOrEmpty(extension) || string.IsNullOrEmpty(name))
            {
                invalidBuilder.Add(trimmed);
                continue;
            }

            // REQUIRE extension to start with a dot. 
            // This prevents ambiguity and encourages correct usage.
            if (!extension.StartsWith("."))
            {
                invalidBuilder.Add(trimmed);
                continue;
            }

            // Check if name is a valid C# identifier (simplified check)
            if (!IsValidIdentifier(name))
            {
                invalidBuilder.Add(trimmed);
                continue;
            }

            validBuilder.Add(new SqlProvider(extension, name));
        }

        return (validBuilder.ToImmutable(), invalidBuilder.ToImmutable());
    }

    private static bool IsValidIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!char.IsLetter(name[0]) && name[0] != '_') return false;
        for (int i = 1; i < name.Length; i++)
        {
            if (!char.IsLetterOrDigit(name[i]) && name[i] != '_') return false;
        }

        return true;
    }
}
