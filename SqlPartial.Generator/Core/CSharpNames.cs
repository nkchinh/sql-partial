using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SqlPartial.Generator.Core;

internal static class CSharpNames
{
    public static bool IsIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || name.StartsWith("@")) return false;
        var token = SyntaxFactory.ParseToken("@" + name);
        return token.IsKind(SyntaxKind.IdentifierToken) && !token.ContainsDiagnostics &&
               token.FullSpan.Length == name.Length + 1 && token.ValueText == name;
    }

    public static string Escape(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ||
        SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    public static bool IsNamespace(string name)
    {
        var syntax = SyntaxFactory.ParseName(name);
        return IsCleanName(syntax, name) &&
               IsNamespaceName(syntax);
    }

    private static bool IsNamespaceName(NameSyntax name) => name switch
    {
        IdentifierNameSyntax => true,
        QualifiedNameSyntax qualified => IsNamespaceName(qualified.Left) && qualified.Right is IdentifierNameSyntax,
        _ => false
    };

    public static bool IsTypeName(string name)
    {
        var syntax = SyntaxFactory.ParseTypeName(name);
        return syntax is NameSyntax && IsCleanName(syntax, name);
    }

    private static bool IsCleanName(SyntaxNode syntax, string name) =>
        !syntax.ContainsDiagnostics && syntax.ToFullString() == name &&
        syntax.DescendantTrivia().All(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
}
