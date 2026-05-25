using SqlPartial.Generator.Core;

namespace SqlPartial.Generator.Models
{
    /// <summary>
    /// Represents a configured DBMS provider parsed from SqlPartialProviders property.
    /// e.g. "pg:PostgreSql" → Slug="pg", Name="PostgreSql"
    /// ANSI is always implicitly available and is not stored here.
    /// </summary>
    internal sealed class SqlProvider : System.IEquatable<SqlProvider>
    {
        public string Slug { get; }
        public string Name { get; }

        public SqlProvider(string slug, string name)
        {
            Slug = slug;
            Name = name;
        }

        public bool Equals(SqlProvider? other) =>
            other is not null &&
            Slug == other.Slug &&
            Name == other.Name;

        public override bool Equals(object? obj) => Equals(obj as SqlProvider);
        public override int GetHashCode() => HashCodeHelper.Combine(Slug, Name);
        public override string ToString() => $"{Slug}:{Name}";
    }
}
