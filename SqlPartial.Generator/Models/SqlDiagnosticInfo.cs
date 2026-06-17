using Microsoft.CodeAnalysis;

namespace SqlPartial.Generator.Models;

internal sealed class SqlDiagnosticInfo(
    string id, string title, string message, DiagnosticSeverity severity, int offset, int length)
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Message { get; } = message;
    public DiagnosticSeverity Severity { get; } = severity;
    public int Offset { get; } = offset;
    public int Length { get; } = length;

    public override bool Equals(object? obj)
    {
        return obj is SqlDiagnosticInfo other &&
               Id == other.Id &&
               Title == other.Title &&
               Message == other.Message &&
               Severity == other.Severity &&
               Offset == other.Offset &&
               Length == other.Length;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + (Id?.GetHashCode() ?? 0);
            hash = hash * 23 + (Title?.GetHashCode() ?? 0);
            hash = hash * 23 + (Message?.GetHashCode() ?? 0);
            hash = hash * 23 + Severity.GetHashCode();
            hash = hash * 23 + Offset.GetHashCode();
            hash = hash * 23 + Length.GetHashCode();
            return hash;
        }
    }
}
