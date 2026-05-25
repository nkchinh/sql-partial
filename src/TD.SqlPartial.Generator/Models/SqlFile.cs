using TD.SqlPartial.Generator.Core;

namespace TD.SqlPartial.Generator.Models
{
    /// <summary>
    /// Represents a single .sql AdditionalFile, parsed from its path and content.
    /// Filename convention: ClassName.QueryName.sql        → provider = "an" (ANSI, default)
    ///                      ClassName.QueryName.pg.sql     → provider = "pg"
    /// </summary>
    internal sealed class SqlFile : System.IEquatable<SqlFile>
    {
        /// <summary>Full path on disk — used for diagnostics only.</summary>
        public string FilePath { get; }

        /// <summary>Target namespace, derived from RootNamespace + RelativeDir.</summary>
        public string Namespace { get; }

        /// <summary>Partial class name — first segment of filename before first dot.</summary>
        public string ClassName { get; }

        /// <summary>Property name on the partial class — second segment.</summary>
        public string QueryName { get; }

        /// <summary>
        /// Provider slug this file belongs to.
        /// "an" means ANSI / shared fallback.
        /// </summary>
        public string ProviderSlug { get; }

        /// <summary>SQL content, cleaned (comments and blank lines stripped).</summary>
        public string Content { get; }

        public SqlFile(
            string filePath,
            string ns,
            string className,
            string queryName,
            string providerSlug,
            string content)
        {
            FilePath = filePath;
            Namespace = ns;
            ClassName = className;
            QueryName = queryName;
            ProviderSlug = providerSlug;
            Content = content;
        }

        public bool Equals(SqlFile? other) =>
            other is not null &&
            FilePath == other.FilePath &&
            Namespace == other.Namespace &&
            ClassName == other.ClassName &&
            QueryName == other.QueryName &&
            ProviderSlug == other.ProviderSlug &&
            Content == other.Content;

        public override bool Equals(object? obj) => Equals(obj as SqlFile);

        public override int GetHashCode() =>
            HashCodeHelper.Combine(FilePath, Namespace, ClassName, QueryName, ProviderSlug, Content);
    }
}
