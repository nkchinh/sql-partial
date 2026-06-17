using System.Collections.Immutable;
using System.IO;
using SqlPartial.Generator.Models;

namespace SqlPartial.Generator.Core;

internal static class FilePathParser
{
    public const string FallbackProviderName = "Fallback";

    /// <summary>
    /// Parses a SQL file path into (namespace, className, queryName, providerName).
    ///
    /// Matching Logic:
    ///   1. Combine configured extensions with hardcoded fallback defaults (.sql).
    ///   2. Sort by length descending to ensure the longest extension matches first.
    ///   3. Strip the matched extension and split remaining filename into ClassName.QueryName.
    /// </summary>
    public static (string ns, string className, string queryName, string providerName)?
        TryParse(string filePath, string rootNamespace, string projectDir, ImmutableArray<SqlProvider> sortedProviders)
    {
        var filename = Path.GetFileName(filePath);

        string? matchedExtension = null;
        string providerName = FallbackProviderName;

        foreach (var provider in sortedProviders)
        {
            if (filename.EndsWith(provider.Extension, System.StringComparison.OrdinalIgnoreCase))
            {
                matchedExtension = provider.Extension;
                providerName = provider.Name;
                break;
            }
        }

        if (matchedExtension == null) return null;

        // 3. Strip extension and split ClassName.QueryName
        var baseName = filename.Substring(0, filename.Length - matchedExtension.Length);
        var segments = baseName.Split('.');

        if (segments.Length != 2) return null;

        var className = segments[0];
        var queryName = segments[1];

        // Derive namespace from directory relative to project root
        var ns = DeriveNamespace(filePath, rootNamespace, projectDir);

        return (ns, className, queryName, providerName);
    }

    private static string DeriveNamespace(string filePath, string rootNamespace, string projectDir)
    {
        var dir = Path.GetDirectoryName(filePath) ?? string.Empty;

        // Make relative to project directory
        if (!string.IsNullOrEmpty(projectDir) &&
            dir.StartsWith(projectDir, System.StringComparison.OrdinalIgnoreCase))
        {
            dir = dir.Substring(projectDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        if (string.IsNullOrEmpty(dir))
            return rootNamespace;

        var suffix = dir
            .Replace(Path.AltDirectorySeparatorChar, '.')
            .Replace(Path.DirectorySeparatorChar, '.')
            .Trim('.');

        return string.IsNullOrEmpty(suffix)
            ? rootNamespace
            : $"{rootNamespace}.{suffix}";
    }
}
