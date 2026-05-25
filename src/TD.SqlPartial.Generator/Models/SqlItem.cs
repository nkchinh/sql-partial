using System;

namespace TD.SqlPartial.Generator.Models
{
    public sealed class SqlItem : IEquatable<SqlItem>
    {
        public string FilePath { get; }
        public string FileName { get; }
        public string Content { get; }
        public string Namespace { get; }
        public string ClassName { get; }
        public string? ClassModifier { get; }
        public string ConstName { get; }
        public string? ConstModifier { get; }
        public bool NullableEnabled { get; }

        public SqlItem(
            string filePath,
            string fileName,
            string content,
            string @namespace,
            string className,
            string? classModifier,
            string constName,
            string? constModifier,
            bool nullableEnabled)
        {
            FilePath = filePath;
            FileName = fileName;
            Content = content;
            Namespace = @namespace;
            ClassName = className;
            ClassModifier = classModifier;
            ConstName = constName;
            ConstModifier = constModifier;
            NullableEnabled = nullableEnabled;
        }

        public bool Equals(SqlItem? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return string.Equals(FilePath, other.FilePath, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(Content, other.Content, StringComparison.Ordinal) &&
                   string.Equals(Namespace, other.Namespace, StringComparison.Ordinal) &&
                   string.Equals(ClassName, other.ClassName, StringComparison.Ordinal) &&
                   string.Equals(ClassModifier, other.ClassModifier, StringComparison.Ordinal) &&
                   string.Equals(ConstName, other.ConstName, StringComparison.Ordinal) &&
                   string.Equals(ConstModifier, other.ConstModifier, StringComparison.Ordinal) &&
                   NullableEnabled == other.NullableEnabled;
        }

        public override bool Equals(object? obj)
        {
            return ReferenceEquals(this, obj) || obj is SqlItem other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = StringComparer.OrdinalIgnoreCase.GetHashCode(FilePath);
                hashCode = (hashCode * 397) ^ (Content != null ? StringComparer.Ordinal.GetHashCode(Content) : 0);
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(Namespace);
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(ClassName);
                hashCode = (hashCode * 397) ^ (ClassModifier != null ? StringComparer.Ordinal.GetHashCode(ClassModifier) : 0);
                hashCode = (hashCode * 397) ^ StringComparer.Ordinal.GetHashCode(ConstName);
                hashCode = (hashCode * 397) ^ (ConstModifier != null ? StringComparer.Ordinal.GetHashCode(ConstModifier) : 0);
                hashCode = (hashCode * 397) ^ NullableEnabled.GetHashCode();
                return hashCode;
            }
        }
    }
}
