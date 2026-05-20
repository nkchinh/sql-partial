using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace TD.SqlPartial.Generator.Core
{
    public static class SqlCleaner
    {
        private static readonly Regex TestPartRegex =
            new(@"--\s*#testpart.*?--\s*/testpart\s*", RegexOptions.Singleline | RegexOptions.Compiled);

        public static string Clean(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            // Strip #testpart blocks
            var result = TestPartRegex.Replace(content, string.Empty);

            // Ported logic from original .targets file
            using var reader = new StringReader(result);

            var builder = new StringBuilder(result.Length);
            string line;
            bool isFirstLine = true;

            while ((line = reader.ReadLine()) != null)
            {
                // Skip if line is empty, whitespace only, or starts with --
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("--"))
                    continue;

                // Add newline before non-first lines
                if (!isFirstLine)
                    builder.AppendLine();
                else
                    isFirstLine = false;

                builder.Append(line);
            }

            return builder.ToString().Replace("\"", "\"\"");
        }
    }
}
