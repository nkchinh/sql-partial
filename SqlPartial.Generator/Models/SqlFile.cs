using SqlPartial.Generator.Core;

namespace SqlPartial.Generator.Models
{
    /// <summary>
    /// Represents a single .sql AdditionalFile, parsed from its path and content.
    /// Filename convention: ClassName.QueryName.sql        → provider = "AnsiSql" (ANSI, default)
    ///                      ClassName.QueryName.pg.sql     → provider = "PostgreSql"
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
        /// Provider name this file belongs to.
        /// "AnsiSql" means ANSI / shared fallback.
        /// </summary>
        public string ProviderName { get; }

        /// <summary>SQL content, cleaned (comments and blank lines stripped).</summary>
        public string Content { get; }

        public SqlFile(
            string filePath,
            string ns,
            string className,
            string queryName,
            string providerName,
            string content)
        {
            FilePath = filePath;
            Namespace = ns;
            ClassName = className;
            QueryName = queryName;
            ProviderName = providerName;
            Content = content;
        }

        public bool Equals(SqlFile? other) =>
            other is not null &&
            FilePath == other.FilePath &&
            Namespace == other.Namespace &&
            ClassName == other.ClassName &&
            QueryName == other.QueryName &&
            ProviderName == other.ProviderName &&
            Content == other.Content;

        public override bool Equals(object? obj) => Equals(obj as SqlFile);

        public override int GetHashCode() =>
            HashCodeHelper.Combine(FilePath, Namespace, ClassName, QueryName, ProviderName, Content);
    }
}
