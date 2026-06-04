using System.Collections.Immutable;
using System.Linq;
using SqlPartial.Generator.Core;

namespace SqlPartial.Generator.Models
{
    /// <summary>
    /// Project-level configuration parsed from MSBuild properties.
    /// </summary>
    internal sealed class GeneratorConfig(
        string rootNamespace,
        ImmutableArray<SqlProvider> providers,
        string sqlStringsNamespace,
        string? externalSqlStringsType,
        bool nullableEnabled,
        bool warnOnUnrecognized = false) : System.IEquatable<GeneratorConfig>
    {
        public string RootNamespace { get; } = rootNamespace;

        /// <summary>
        /// Configured DBMS providers (excluding fallback which is always implicit).
        /// Parsed from SqlPartialProviders = "pg.sql:PostgreSql;pgsql:PostgreSql"
        /// </summary>
        public ImmutableArray<SqlProvider> Providers { get; } = providers;

        /// <summary>
        /// Returns the unique DBMS provider names (e.g., if both .pg.sql and .pgsql map to PostgreSql).
        /// </summary>
        public System.Collections.Generic.IEnumerable<string> DistinctProviderNames => Providers.Select(p => p.Name).Distinct();

        /// <summary>
        /// Namespace for the SqlStrings struct.
        /// Falls back to RootNamespace if not specified via SqlPartialStringsNamespace.
        /// </summary>
        public string SqlStringsNamespace { get; } = sqlStringsNamespace;

        /// <summary>
        /// If set, generator will NOT emit the SqlStrings struct definition —
        /// instead it uses this fully-qualified type from another assembly.
        /// Configured via SqlPartialStringsType MSBuild property.
        /// </summary>
        public string? ExternalSqlStringsType { get; } = externalSqlStringsType;

        public bool NullableEnabled { get; } = nullableEnabled;

        public bool WarnOnUnrecognized { get; } = warnOnUnrecognized;

        public bool Equals(GeneratorConfig? other) =>
            other is not null &&
            RootNamespace == other.RootNamespace &&
            SqlStringsNamespace == other.SqlStringsNamespace &&
            ExternalSqlStringsType == other.ExternalSqlStringsType &&
            NullableEnabled == other.NullableEnabled &&
            WarnOnUnrecognized == other.WarnOnUnrecognized &&
            Providers.SequenceEqual(other.Providers);

        public override bool Equals(object? obj) => Equals(obj as GeneratorConfig);

        public override int GetHashCode() => HashCodeHelper.Combine(
            RootNamespace, SqlStringsNamespace, ExternalSqlStringsType, NullableEnabled, Providers.Length, WarnOnUnrecognized);
    }
}
