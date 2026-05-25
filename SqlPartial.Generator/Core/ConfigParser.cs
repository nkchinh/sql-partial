using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;
using SqlPartial.Generator.Models;

namespace SqlPartial.Generator.Core
{
    internal static class ConfigParser
    {
        /// <summary>
        /// Parses project-level MSBuild properties from AnalyzerConfigOptionsProvider.
        /// </summary>
        public static GeneratorConfig Parse(AnalyzerConfigOptionsProvider optionsProvider)
        {
            var global = optionsProvider.GlobalOptions;

            global.TryGetValue("build_property.RootNamespace", out var rootNamespace);
            global.TryGetValue("build_property.SqlPartialProviders", out var providersRaw);
            global.TryGetValue("build_property.SqlPartialStringsNamespace", out var stringsNs);
            global.TryGetValue("build_property.SqlPartialStringsType", out var externalType);
            global.TryGetValue("build_property.Nullable", out var nullable);

            rootNamespace = string.IsNullOrWhiteSpace(rootNamespace) ? "Generated" : rootNamespace!.Trim();

            var providers = ParseProviders(providersRaw);

            var sqlStringsNamespace = string.IsNullOrWhiteSpace(stringsNs)
                ? rootNamespace
                : stringsNs!.Trim();

            var nullableEnabled = string.Equals(nullable, "enable", System.StringComparison.OrdinalIgnoreCase);

            return new GeneratorConfig(
                rootNamespace,
                providers,
                sqlStringsNamespace,
                string.IsNullOrWhiteSpace(externalType) ? null : externalType!.Trim(),
                nullableEnabled
            );
        }

        /// <summary>
        /// Parses "pg:PostgreSql;ms:SqlServer;my:MySql" into SqlProvider list.
        /// </summary>
        private static ImmutableArray<SqlProvider> ParseProviders(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return ImmutableArray<SqlProvider>.Empty;

            var builder = ImmutableArray.CreateBuilder<SqlProvider>();

            foreach (var entry in raw!.Split(';'))
            {
                var parts = entry.Trim().Split(':');
                if (parts.Length != 2) continue;

                var slug = parts[0].Trim();
                var name = parts[1].Trim();

                if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(name)) continue;

                builder.Add(new SqlProvider(slug, name));
            }

            return builder.ToImmutable();
        }
    }
}
