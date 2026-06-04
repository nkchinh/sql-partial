using System.Collections.Immutable;
using System.Linq;
using SqlPartial.Generator.Core;

namespace SqlPartial.Generator.Models;

/// <summary>
/// All .sql files that belong to one (Namespace, ClassName, QueryName) triple,
/// keyed by provider name.
/// e.g. UserRepo.GetById → { "Fallback": "SELECT …", "PostgreSql": "SELECT …" }
/// </summary>
internal sealed class SqlQueryGroup(
    string ns,
    string className,
    string queryName,
    ImmutableDictionary<string, string> contentByProviderName) : System.IEquatable<SqlQueryGroup>
{
    public string Namespace { get; } = ns;
    public string ClassName { get; } = className;
    public string QueryName { get; } = queryName;

    /// <summary>provider name → cleaned SQL content</summary>
    public ImmutableDictionary<string, string> ContentByProviderName { get; } = contentByProviderName;


    /// <summary>
    /// Returns content for a provider name, falling back to "Fallback" if not present.
    /// </summary>
    public string GetContent(string providerName) =>
        ContentByProviderName.TryGetValue(providerName, out var v) ? v :
        ContentByProviderName.TryGetValue(FilePathParser.FallbackProviderName, out var fallback) ? fallback :
        string.Empty;

    public bool Equals(SqlQueryGroup? other) =>
        other is not null &&
        Namespace == other.Namespace &&
        ClassName == other.ClassName &&
        QueryName == other.QueryName &&
        ContentByProviderName.Count == other.ContentByProviderName.Count &&
        !ContentByProviderName.Keys.Any(k =>
            !other.ContentByProviderName.TryGetValue(k, out var v) ||
            v != ContentByProviderName[k]);

    public override bool Equals(object? obj) => Equals(obj as SqlQueryGroup);

    public override int GetHashCode() =>
        HashCodeHelper.Combine(Namespace, ClassName, QueryName, ContentByProviderName.Count);
}
