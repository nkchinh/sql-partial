using System.Collections.Immutable;
using System.Linq;
using SqlPartial.Generator.Core;

namespace SqlPartial.Generator.Models
{
    /// <summary>
    /// All .sql files that belong to one (Namespace, ClassName, QueryName) triple,
    /// keyed by provider slug.
    /// e.g. UserRepo.GetById → { "an": "SELECT …", "pg": "SELECT …" }
    /// </summary>
    internal sealed class SqlQueryGroup(
        string ns,
        string className,
        string queryName,
        ImmutableDictionary<string, string> contentBySlug) : System.IEquatable<SqlQueryGroup>
    {
        public string Namespace { get; } = ns;
        public string ClassName { get; } = className;
        public string QueryName { get; } = queryName;

        /// <summary>slug → cleaned SQL content</summary>
        public ImmutableDictionary<string, string> ContentBySlug { get; } = contentBySlug;


        /// <summary>
        /// Returns content for a slug, falling back to "an" (ANSI) if not present.
        /// </summary>
        public string GetContent(string slug) =>
            ContentBySlug.TryGetValue(slug, out var v) ? v :
            ContentBySlug.TryGetValue("an", out var ansi) ? ansi :
            string.Empty;

        public bool Equals(SqlQueryGroup? other) =>
            other is not null &&
            Namespace == other.Namespace &&
            ClassName == other.ClassName &&
            QueryName == other.QueryName &&
            ContentBySlug.Count == other.ContentBySlug.Count &&
            !ContentBySlug.Keys.Any(k =>
                !other.ContentBySlug.TryGetValue(k, out var v) ||
                v != ContentBySlug[k]);

        public override bool Equals(object? obj) => Equals(obj as SqlQueryGroup);

        public override int GetHashCode() =>
            HashCodeHelper.Combine(Namespace, ClassName, QueryName, ContentBySlug.Count);
    }
}
