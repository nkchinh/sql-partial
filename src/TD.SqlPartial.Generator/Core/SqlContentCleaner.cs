using System.Text;
using System.Text.RegularExpressions;

namespace TD.SqlPartial.Generator.Core
{
    internal static class SqlContentCleaner
    {
        private static readonly Regex TestPartRegex = new(
            @"(?is)--\s*#testpart.*?--\s*/testpart\s*(\r\n|\r|\n)?",
            RegexOptions.Compiled);

        /// <summary>
        /// Strips #testpart blocks, leading/trailing blank lines, and line comments.
        /// Returns content safe to embed in a C# verbatim string (quotes escaped).
        /// </summary>
        public static string Clean(string sql)
        {
            if (string.IsNullOrEmpty(sql))
                return string.Empty;

            // Remove #testpart … /testpart blocks
            sql = TestPartRegex.Replace(sql, string.Empty);

            using var reader = new System.IO.StringReader(sql);
            var sb = new StringBuilder(sql.Length);
            bool firstLine = true;
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                var trimmed = line.TrimStart();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("--"))
                    continue;

                if (!firstLine) sb.AppendLine();
                else firstLine = false;

                sb.Append(line);
            }

            // Escape double-quotes for C# verbatim string literal
            return sb.ToString().Replace("\"", "\"\"");
        }
    }
}
