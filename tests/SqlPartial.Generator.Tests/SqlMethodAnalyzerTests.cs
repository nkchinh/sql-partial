using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SqlPartial.Generator.Analyzers;

namespace SqlPartial.Generator.Tests;

public class SqlMethodAnalyzerTests
{
    private const string AttributeMock = @"
namespace SqlPartial.Abstractions { public class SqlAttribute : System.Attribute { } }
";

    [Fact]
    public async Task Analyzer_ShouldErrorWhenSqlProviderNameIsMissing()
    {
        var source = @"
using System;
using SqlPartial.Abstractions;
namespace TestNamespace
{
    public class Repo
    {
        public void Query([Sql] string sql) { }
    }
}
" + AttributeMock;
        var diagnostics = await GetDiagnostics(source);
        Assert.Contains(diagnostics, d => d.Id == "SQLPG030");
    }

    [Fact]
    public async Task Analyzer_ShouldNotErrorWhenSqlProviderNameExistsInClass()
    {
        var source = @"
using System;
using SqlPartial.Abstractions;
namespace TestNamespace
{
    public class Repo
    {
        public string SqlProviderName => ""SqlServer"";
        public void Query([Sql] string sql) { }
    }
}
" + AttributeMock;
        var diagnostics = await GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SQLPG030");
    }

    [Fact]
    public async Task Analyzer_ShouldNotErrorWhenSqlProviderNameExistsInInterface()
    {
        var source = @"
using System;
using SqlPartial.Abstractions;
namespace TestNamespace
{
    public interface IRepo
    {
        string SqlProviderName { get; }
        void Query([Sql] string sql);
    }
}
" + AttributeMock;
        var diagnostics = await GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SQLPG030");
    }

    [Fact]
    public async Task Analyzer_ShouldErrorWhenStaticMethodNeedsStaticProvider()
    {
        var source = @"
using System;
using SqlPartial.Abstractions;
namespace TestNamespace
{
    public class Repo
    {
        public string SqlProviderName => ""SqlServer"";
        public static void Query([Sql] string sql) { } // Static method needs static property
    }
}
" + AttributeMock;
        var diagnostics = await GetDiagnostics(source);
        Assert.Contains(diagnostics, d => d.Id == "SQLPG030");
    }

    [Fact]
    public async Task Analyzer_ShouldNotErrorWhenStaticMethodHasStaticProvider()
    {
        var source = @"
using System;
using SqlPartial.Abstractions;
namespace TestNamespace
{
    public class Repo
    {
        public static string SqlProviderName => ""SqlServer"";
        public static void Query([Sql] string sql) { }
    }
}
" + AttributeMock;
        var diagnostics = await GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SQLPG030");
    }

    [Fact]
    public async Task Analyzer_ShouldNotErrorForExtensionMethodWhenExtendedTypeHasProvider()
    {
        var source = @"
using System;
using SqlPartial.Abstractions;
namespace TestNamespace
{
    public interface IRepo { string SqlProviderName { get; } }
    public static class RepoExtensions
    {
        public static void Query(this IRepo self, [Sql] string sql) { }
    }
}
" + AttributeMock;
        var diagnostics = await GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SQLPG030");
    }

    private async Task<ImmutableArray<Diagnostic>> GetDiagnostics(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
        };

        var compilation = CSharpCompilation.Create("Test")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(references)
            .AddSyntaxTrees(syntaxTree);

        var analyzer = new SqlMethodAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers([analyzer]);
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
