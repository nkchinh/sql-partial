using SqlPartial.Generator.Core;

namespace SqlPartial.Generator.Models
{
    /// <summary>
    /// Represents a configured DBMS provider parsed from SqlPartialProviders property.
    /// e.g. "pg.sql:PostgreSql" → Extension=".pg.sql", Name="PostgreSql"
    /// ANSI is always implicitly available and is not stored here.
    /// </summary>
    internal sealed class SqlProvider(string extension, string name) : System.IEquatable<SqlProvider>
    {
        public string Extension { get; } = extension;
        public string Name { get; } = name;

        public bool Equals(SqlProvider? other) =>
            other is not null &&
            Extension == other.Extension &&
            Name == other.Name;

        public override bool Equals(object? obj) => Equals(obj as SqlProvider);
        public override int GetHashCode() => HashCodeHelper.Combine(Extension, Name);
        public override string ToString() => $"{Extension}:{Name}";
    }
}
