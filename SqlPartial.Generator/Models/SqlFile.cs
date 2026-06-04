using SqlPartial.Generator.Core;

namespace SqlPartial.Generator.Models;

/// <summary>
/// Represents a single .sql AdditionalFile, parsed from its path and content.
/// Filename convention: ClassName.QueryName.sql        → provider = "Fallback" (default)
///                      ClassName.QueryName.pg.sql     → provider = "PostgreSql"
/// </summary>
internal sealed class SqlFile(
    string filePath,
    string ns,
    string className,
    string queryName,
    string providerName,
    string content) : System.IEquatable<SqlFile>
{
    /// <summary>Full path on disk — used for diagnostics only.</summary>
    public string FilePath { get; } = filePath;

    /// <summary>Target namespace, derived from RootNamespace + RelativeDir.</summary>
    public string Namespace { get; } = ns;

    /// <summary>Partial class name — first segment of filename before first dot.</summary>
    public string ClassName { get; } = className;

    /// <summary>Property name on the partial class — second segment.</summary>
    public string QueryName { get; } = queryName;

    /// <summary>
    /// Provider name this file belongs to.
    /// "Fallback" means the shared default.
    /// </summary>
    public string ProviderName { get; } = providerName;

    /// <summary>SQL content, cleaned (comments and blank lines stripped).</summary>
    public string Content { get; } = content;

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
