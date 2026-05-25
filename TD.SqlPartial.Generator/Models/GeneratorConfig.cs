using System.Collections.Immutable;
using System.Linq;
using TD.SqlPartial.Generator.Core;

namespace TD.SqlPartial.Generator.Models
{
    /// <summary>
    /// Project-level configuration parsed from MSBuild properties.
    /// </summary>
    internal sealed class GeneratorConfig(
        string rootNamespace,
        ImmutableArray<SqlProvider> providers,
        string sqlStringsNamespace,
        string? externalSqlStringsType,
        bool nullableEnabled) : System.IEquatable<GeneratorConfig>
    {
        public string RootNamespace { get; } = rootNamespace;

        /// <summary>
        /// Configured DBMS providers (excluding ANSI which is always implicit).
        /// Parsed from SqlPartialProviders = "pg:PostgreSql;ms:SqlServer"
        /// </summary>
        public ImmutableArray<SqlProvider> Providers { get; } = providers;

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

        public bool Equals(GeneratorConfig? other) =>
            other is not null &&
            RootNamespace == other.RootNamespace &&
            SqlStringsNamespace == other.SqlStringsNamespace &&
            ExternalSqlStringsType == other.ExternalSqlStringsType &&
            NullableEnabled == other.NullableEnabled &&
            Providers.SequenceEqual(other.Providers);

        public override bool Equals(object? obj) => Equals(obj as GeneratorConfig);

        public override int GetHashCode() => HashCodeHelper.Combine(
            RootNamespace, SqlStringsNamespace, ExternalSqlStringsType, NullableEnabled, Providers.Length);
    }
}
