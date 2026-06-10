using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SqlPartial.Generator.Core;
using SqlPartial.Generator.Models;
using Xunit;

namespace SqlPartial.Generator.Tests;

public class OverloadTests
{
    [Fact]
    public void SourceBuilder_BuildOverloads_ShouldHandleGenericMethods()
    {
        var source = @"
using SqlPartial.Abstractions;
namespace TestNamespace
{
    public partial class Repo
    {
        public string SqlProviderName => ""Postgres"";
        public T Execute<T>([Sql] string query) => default;
    }
}

namespace SqlPartial.Abstractions { public class SqlAttribute : System.Attribute { } }
";

        var (type, method) = GetSymbols(source, "TestNamespace.Repo", "Execute");
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true);

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        // Should include T from original method AND TSql for the SQL parameter
        // It is capped to internal because ISqlString is internal
        Assert.Contains("internal T Execute<T, TSql>(TSql query) where TSql : struct, ISqlString", overloads);

        // Should call original method with generic argument
        Assert.Contains("return Execute<T>(query.Get(this.SqlProviderName));", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_ShouldHandleMultipleGenericParameters()
    {
        var source = @"
using SqlPartial.Abstractions;
namespace TestNamespace
{
    public partial class Repo
    {
        public string SqlProviderName => ""Postgres"";
        public void Multi<T1, T2>([Sql] string query, T1 arg1, T2 arg2) { }
    }
}

namespace SqlPartial.Abstractions { public class SqlAttribute : System.Attribute { } }
";

        var (type, method) = GetSymbols(source, "TestNamespace.Repo", "Multi");
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true);

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        Assert.Contains("internal void Multi<T1, T2, TSql>(TSql query, T1 arg1, T2 arg2) where TSql : struct, ISqlString", overloads);
        Assert.Contains("Multi<T1, T2>(query.Get(this.SqlProviderName), arg1, arg2);", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_ShouldBePublicWhenSharedNamespaceIsUsed()
    {
        var source = @"
using SqlPartial.Abstractions;
namespace TestNamespace
{
    public partial class Repo
    {
        public string SqlProviderName => ""Postgres"";
        public void Query([Sql] string query) { }
    }
}

namespace SqlPartial.Abstractions { public class SqlAttribute : System.Attribute { } }
";

        var (type, method) = GetSymbols(source, "TestNamespace.Repo", "Query");

        // UseSharedNamespace or EmitSharedNamespace makes it public
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true, false, "Shared");

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        Assert.Contains("public void Query<TSql>(TSql query) where TSql : struct, ISqlString", overloads);
    }

    private (ITypeSymbol, IMethodSymbol) GetSymbols(string source, string typeName, string methodName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("Test")
            .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
            .AddSyntaxTrees(syntaxTree);

        var type = compilation.GetTypeByMetadataName(typeName)
            ?? throw new Exception($"Type '{typeName}' not found in compilation");

        var method = type.GetMembers(methodName).OfType<IMethodSymbol>().First();

        return (type, method);
    }
}
