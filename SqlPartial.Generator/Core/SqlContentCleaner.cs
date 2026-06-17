using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using SqlPartial.Generator.Models;

namespace SqlPartial.Generator.Core;

internal sealed class CleanResult(string content, ImmutableArray<SqlDiagnosticInfo> diagnostics)
{
    public string Content { get; } = content;
    public ImmutableArray<SqlDiagnosticInfo> Diagnostics { get; } = diagnostics;

    public override bool Equals(object? obj) =>
        obj is CleanResult other &&
        Content == other.Content &&
        Diagnostics.SequenceEqual(other.Diagnostics);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + (Content?.GetHashCode() ?? 0);
            foreach (var diag in Diagnostics)
                hash = hash * 23 + diag.GetHashCode();
            return hash;
        }
    }
}

internal static class SqlContentCleaner
{
    private static readonly Regex CombinedTagRegex = new(
        @"(?i)(?<open>--\s*#(?:testpart|exclude))|(?<close>--\s*/(?:testpart|exclude))",
        RegexOptions.Compiled);

    /// <summary>
    /// Strips #testpart blocks, leading/trailing blank lines, and line comments.
    /// Returns content safe to embed in a C# verbatim string (quotes escaped)
    /// and any diagnostics (like mismatched tags).
    /// </summary>
    public static CleanResult Clean(string sql)
    {
        if (string.IsNullOrEmpty(sql))
            return new CleanResult(string.Empty, ImmutableArray<SqlDiagnosticInfo>.Empty);

        var diagnostics = new List<SqlDiagnosticInfo>();
        var blocksToRemove = new List<(int Start, int End)>();

        // 1. Find all tags and process them sequentially
        var allTags = CombinedTagRegex.Matches(sql).Cast<Match>().OrderBy(m => m.Index).ToList();
        Match? currentOpen = null;

        foreach (var tag in allTags)
        {
            if (tag.Groups["open"].Success)
            {
                if (currentOpen != null)
                {
                    // Found an open tag while another is still open
                    diagnostics.Add(new SqlDiagnosticInfo(
                        "SQLPG013",
                        "Mismatched Exclude Tag",
                        "Found opening tag '-- #exclude' while another block is already open. Nested exclude blocks are not supported.",
                        DiagnosticSeverity.Warning,
                        currentOpen.Index,
                        currentOpen.Length));
                }
                currentOpen = tag;
            }
            else // close tag
            {
                if (currentOpen == null)
                {
                    // Orphan closing tag
                    diagnostics.Add(new SqlDiagnosticInfo(
                        "SQLPG013",
                        "Mismatched Exclude Tag",
                        "Found closing tag '-- /exclude' without a matching opening tag '-- #exclude'.",
                        DiagnosticSeverity.Warning,
                        tag.Index,
                        tag.Length));
                }
                else
                {
                    // Valid pair! Mark the range for removal
                    // We also want to consume any trailing newline after the close tag
                    int endPos = tag.Index + tag.Length;
                    if (endPos < sql.Length)
                    {
                        if (sql[endPos] == '\r')
                        {
                            endPos++;
                            if (endPos < sql.Length && sql[endPos] == '\n') endPos++;
                        }
                        else if (sql[endPos] == '\n')
                        {
                            endPos++;
                        }
                    }

                    blocksToRemove.Add((currentOpen.Index, endPos));
                    currentOpen = null;
                }
            }
        }

        if (currentOpen != null)
        {
            diagnostics.Add(new SqlDiagnosticInfo(
                "SQLPG013",
                "Mismatched Exclude Tag",
                "Found opening tag '-- #exclude' without a matching closing tag '-- /exclude'.",
                DiagnosticSeverity.Warning,
                currentOpen.Index,
                currentOpen.Length));
        }

        // 2. Build cleaned SQL by skipping blocks
        var sbRaw = new StringBuilder(sql.Length);
        int lastPos = 0;
        foreach (var block in blocksToRemove.OrderBy(b => b.Start))
        {
            if (block.Start > lastPos)
            {
                sbRaw.Append(sql.Substring(lastPos, block.Start - lastPos));
            }

            lastPos = block.End;
        }

        if (lastPos < sql.Length)
        {
            sbRaw.Append(sql.Substring(lastPos));
        }

        var cleanedSql = sbRaw.ToString();

        using var reader = new System.IO.StringReader(cleanedSql);
        var sbFinal = new StringBuilder(cleanedSql.Length);
        bool firstLine = true;
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.TrimStart();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("--"))
                continue;

            if (!firstLine) sbFinal.AppendLine();
            else firstLine = false;

            sbFinal.Append(line);
        }

        // Escape double-quotes for C# verbatim string literal
        var finalContent = sbFinal.ToString().Replace("\"", "\"\"");

        return new CleanResult(finalContent, diagnostics.ToImmutableArray());
    }
}
