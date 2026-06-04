using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SqlPartial.Generator.Analyzers;

namespace SqlPartial.Generator.Tests;

public class ManualInstantiationAnalyzerTests
{
    [Fact]
    public async Task Analyzer_ShouldWarnOnMissingFallbackAndMissingProvider()
    {
        var source = @"
using System;
namespace TestNamespace
{
    public interface ISqlString { string Fallback { get; } string Get(string p); }

    public struct SqlStrings : ISqlString
    {
        public string Fallback => _fallback ?? """";
        private readonly string _fallback;
        private readonly string _pg;
        private readonly string _ms;
        public SqlStrings(string pg = null, string ms = null, string fallback = null) 
        { 
            _pg = pg; _ms = ms; _fallback = fallback; 
        }
        public string Get(string p) => Fallback;
    }

    public class Usage
    {
        public void M()
        {
            var s = new SqlStrings(pg: ""SELECT 1""); // Missing 'ms' and 'fallback'
        }
    }
}";
        var diagnostics = await GetDiagnostics(source);
        var sqlpg012 = diagnostics.Where(d => d.Id == "SQLPG012").ToList();

        Assert.Single(sqlpg012);
        Assert.Contains("SqlStrings", sqlpg012[0].GetMessage());
    }

    [Fact]
    public async Task Analyzer_ShouldNotWarnWhenFallbackIsProvided()
    {
        var source = @"
using System;
namespace TestNamespace
{
    public interface ISqlString { string Fallback { get; } string Get(string p); }
    public struct SqlStrings : ISqlString
    {
        public string Fallback => """";
        public SqlStrings(string pg = null, string fallback = null) { }
        public string Get(string p) => Fallback;
    }

    public class Usage
    {
        public void M()
        {
            var s = new SqlStrings(pg: ""SELECT 1"", fallback: ""SELECT 2"");
            var s2 = new SqlStrings(fallback: ""SELECT 3"");
        }
    }
}";
        var diagnostics = await GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SQLPG012");
    }

    [Fact]
    public async Task Analyzer_ShouldNotWarnWhenAllProvidersAreCovered()
    {
        var source = @"
using System;
namespace TestNamespace
{
    public interface ISqlString { string Fallback { get; } string Get(string p); }
    public struct SqlStrings : ISqlString
    {
        public string Fallback => """";
        public SqlStrings(string pg = null, string ms = null, string fallback = null) { }
        public string Get(string p) => Fallback;
    }

    public class Usage
    {
        public void M()
        {
            // All providers (pg, ms) are covered, even without fallback
            var s = new SqlStrings(pg: ""SELECT 1"", ms: ""SELECT 2"");
        }
    }
}";
        var diagnostics = await GetDiagnostics(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "SQLPG012");
    }

    [Fact]
    public async Task Analyzer_ShouldWarnOnSqlDynamic()
    {
        var source = @"
using System;
namespace TestNamespace
{
    public interface ISqlString { string Fallback { get; } string Get(string p); }
    public struct SqlDynamic : ISqlString
    {
        public string Fallback => """";
        public SqlDynamic(Func<string> pg = null, Func<string> ms = null, Func<string> fallback = null) { }
        public string Get(string p) => Fallback;
    }

    public class Usage
    {
        public void M()
        {
            var s = new SqlDynamic(pg: () => ""SELECT 1""); // Missing ms and fallback
        }
    }
}";
        var diagnostics = await GetDiagnostics(source);
        Assert.Contains(diagnostics, d => d.Id == "SQLPG012");
    }

    private async Task<ImmutableArray<Diagnostic>> GetDiagnostics(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Basic references needed for compilation
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
        };

        var compilation = CSharpCompilation.Create("Test")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(references)
            .AddSyntaxTrees(syntaxTree);

        var analyzer = new ManualInstantiationAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
}
