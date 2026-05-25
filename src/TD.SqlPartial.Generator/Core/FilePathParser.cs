using System.IO;

namespace TD.SqlPartial.Generator.Core
{
    internal static class FilePathParser
    {
        private const string AnsiSlug = "an";

        /// <summary>
        /// Parses a .sql file path into (namespace, className, queryName, providerSlug).
        ///
        /// Convention:
        ///   ClassName.QueryName.sql        → providerSlug = "an" (ANSI/default)
        ///   ClassName.QueryName.an.sql     → providerSlug = "an"
        ///   ClassName.QueryName.pg.sql     → providerSlug = "pg"
        ///
        /// Returns null if the filename does not match the expected convention
        /// (must have at least 2 segments before .sql).
        /// </summary>
        public static (string ns, string className, string queryName, string providerSlug)?
            TryParse(string filePath, string rootNamespace, string projectDir)
        {
            var filename = Path.GetFileNameWithoutExtension(filePath); // strips .sql
            // filename is now e.g. "UserRepo.GetById" or "UserRepo.GetById.pg"

            var segments = filename.Split('.');
            if (segments.Length < 2) return null;

            var className = segments[0];
            var queryName = segments[1];
            var providerSlug = segments.Length >= 3 ? segments[2] : AnsiSlug;

            // Derive namespace from directory relative to project root
            var ns = DeriveNamespace(filePath, rootNamespace, projectDir);

            return (ns, className, queryName, providerSlug);
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
}
