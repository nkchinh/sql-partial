namespace TD.SqlPartial.Generator.Models
{
    public sealed class SqlItem(
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
        public string FilePath { get; } = filePath;
        public string FileName { get; } = fileName;
        public string Content { get; } = content;
        public string Namespace { get; } = @namespace;
        public string ClassName { get; } = className;
        public string? ClassModifier { get; } = classModifier;
        public string ConstName { get; } = constName;
        public string? ConstModifier { get; } = constModifier;
        public bool NullableEnabled { get; } = nullableEnabled;
    }
}
