using System;
using System.Collections.Generic;
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
        global.TryGetValue("build_property.SqlPartialEmitSharedNamespace", out var emitNs);
        global.TryGetValue("build_property.SqlPartialUseSharedNamespace", out var useNs);

        rootNamespace = string.IsNullOrWhiteSpace(rootNamespace) ? "Generated" : rootNamespace!.Trim();

        var (providers, invalidEntries) = ParseProviders(providersRaw);

        var sqlStringsNamespace = string.IsNullOrWhiteSpace(stringsNs)
            ? rootNamespace
            : stringsNs!.Trim();

        var nullableEnabled = string.Equals(nullable, "enable", System.StringComparison.OrdinalIgnoreCase);
        var warnOnUnrecognized = string.Equals(warnRaw, "true", System.StringComparison.OrdinalIgnoreCase);

        var errors = ImmutableArray.CreateBuilder<string>();
        foreach (var (property, value) in new[]
        {
            ("RootNamespace", rootNamespace), ("SqlPartialStringsNamespace", sqlStringsNamespace),
            ("SqlPartialEmitSharedNamespace", emitNs), ("SqlPartialUseSharedNamespace", useNs)
        })
        {
            if (!string.IsNullOrWhiteSpace(value) && !CSharpNames.IsNamespace(value!.Trim()))
                errors.Add($"'{property}' must be a valid C# namespace, but was '{value}'.");
        }

        if (!string.IsNullOrWhiteSpace(externalType) && !CSharpNames.IsTypeName(externalType!.Trim()))
            errors.Add($"'SqlPartialStringsType' must be a valid C# type name, but was '{externalType}'.");

        if (!string.IsNullOrWhiteSpace(emitNs) && !string.IsNullOrWhiteSpace(useNs))
            errors.Add("SqlPartialEmitSharedNamespace and SqlPartialUseSharedNamespace cannot be configured together.");

        return new GeneratorConfig(
            rootNamespace,
            providers,
            invalidEntries,
            sqlStringsNamespace,
            string.IsNullOrWhiteSpace(externalType) ? null : externalType!.Trim(),
            nullableEnabled,
            warnOnUnrecognized,
            string.IsNullOrWhiteSpace(emitNs) ? null : emitNs!.Trim(),
            string.IsNullOrWhiteSpace(useNs) ? null : useNs!.Trim(),
            errors.ToImmutable()
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
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".sql" };
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var members = new HashSet<string>(StringComparer.Ordinal) { "_default" };

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

            // Provider names must not collide after generating properties, fields or parameters.
            var normalizedName = name.ToLowerInvariant();
            var propertyName = char.ToUpperInvariant(name[0]) + name.Substring(1);
            var fieldName = "_" + normalizedName;
            var factoryName = fieldName + "Factory";

            if (!CSharpNames.IsIdentifier(name) ||
                normalizedName is "default" or "get" or "sqlstrings" or "sqldynamic" or "isqlstring" or "fallback" ||
                extensions.Contains(extension) ||
                (names.TryGetValue(normalizedName, out var existingName) && existingName != name) ||
                (!names.ContainsKey(normalizedName) &&
                (members.Contains(propertyName) || members.Contains(fieldName) || members.Contains(factoryName))))
            {
                invalidBuilder.Add(trimmed);
                continue;
            }

            validBuilder.Add(new SqlProvider(extension, name));
            extensions.Add(extension);
            names[normalizedName] = name;
            members.Add(propertyName);
            members.Add(fieldName);
            members.Add(factoryName);
        }

        return (validBuilder.ToImmutable(), invalidBuilder.ToImmutable());
    }
}
