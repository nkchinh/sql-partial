using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SqlPartial.Generator.Core;
using SqlPartial.Generator.Models;

namespace SqlPartial.Generator.Tests;

public class GenericOverloadTests
{
    [Fact]
    public void BuildOverloads_ShouldIncludeGenericConstraints()
    {
        var source = @"
using SqlPartial;
namespace Test
{
    public class Repo
    {
        public string SqlProviderName => ""SqlServer"";
        public T Query<T>([Sql] string sql) where T : class, new() => default;
    }
}
namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);

        var method = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Select(m => model.GetDeclaredSymbol(m))
            .First(m => m != null && m.Name == "Query")!;

        var config = new GeneratorConfig("Test", [], [], "Test.Sql", null, true);
        var result = SourceBuilder.BuildOverloads("Test", method.ContainingType!, [method], config, true);

        Assert.Contains("where T : class, new()", result);
    }

    [Fact]
    public void BuildOverloads_ShouldHandleMultipleConstraints()
    {
        var source = @"
using SqlPartial;
using System.Collections.Generic;
namespace Test
{
    public class Repo
    {
        public string SqlProviderName => ""SqlServer"";
        public void Multi<T, U>([Sql] string sql) 
            where T : IEnumerable<U>
            where U : struct
        { }
    }
}
namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";

        var compilation = CreateCompilation(source);
        var tree = compilation.SyntaxTrees.First();
        var model = compilation.GetSemanticModel(tree);

        var method = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Select(m => model.GetDeclaredSymbol(m))
            .First(m => m != null && m.Name == "Multi")!;

        var config = new GeneratorConfig("Test", [], [], "Test.Sql", null, true);
        var result = SourceBuilder.BuildOverloads("Test", method.ContainingType!, [method], config, true);

        Assert.Contains("where T : System.Collections.Generic.IEnumerable<U>", result);
        Assert.Contains("where U : struct", result);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(Assembly.Load("netstandard").Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
        };

        return CSharpCompilation.Create("Test")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(references)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source));
    }
}
