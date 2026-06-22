using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SqlPartial.Generator.Core;
using SqlPartial.Generator.Models;

namespace SqlPartial.Generator.Tests;

public class OverloadTests
{
    [Fact]
    public void SourceBuilder_BuildOverloads_ShouldHandleGenericMethods()
    {
        var source = @"
using SqlPartial;
namespace TestNamespace
{
    public partial class Repo
    {
        public string SqlProviderName => ""Postgres"";
        public T Execute<T>([Sql] string query) => default;
    }
}

namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";

        var (type, method) = GetSymbols(source, "TestNamespace.Repo", "Execute");
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true);

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        // Should include T from original method AND use explicit types instead of TSql
        Assert.Contains("internal T Execute<T>(TestNamespace.Sql.SqlStrings query)", overloads);
        Assert.Contains("internal T Execute<T>(TestNamespace.Sql.SqlDynamic query)", overloads);

        // Should call original method with generic argument
        Assert.Contains("return Execute<T>(query.Get(this.SqlProviderName));", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_ShouldHandleMultipleGenericParameters()
    {
        var source = @"
using SqlPartial;
namespace TestNamespace
{
    public partial class Repo
    {
        public string SqlProviderName => ""Postgres"";
        public void Multi<T1, T2>([Sql] string query, T1 arg1, T2 arg2) { }
    }
}

namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";

        var (type, method) = GetSymbols(source, "TestNamespace.Repo", "Multi");
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true);

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        Assert.Contains("internal void Multi<T1, T2>(TestNamespace.Sql.SqlStrings query, T1 arg1, T2 arg2)", overloads);
        Assert.Contains("internal void Multi<T1, T2>(TestNamespace.Sql.SqlDynamic query, T1 arg1, T2 arg2)", overloads);
        Assert.Contains("Multi<T1, T2>(query.Get(this.SqlProviderName), arg1, arg2);", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_ShouldBePublicWhenSharedNamespaceIsUsed()
    {
        var source = @"
using SqlPartial;
namespace TestNamespace
{
    public partial class Repo
    {
        public string SqlProviderName => ""Postgres"";
        public void Query([Sql] string query) { }
    }
}

namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";

        var (type, method) = GetSymbols(source, "TestNamespace.Repo", "Query");

        // UseSharedNamespace or EmitSharedNamespace makes it public
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true, false, "Shared");

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        Assert.Contains("public void Query(Shared.SqlStrings query)", overloads);
        Assert.Contains("public void Query(Shared.SqlDynamic query)", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_ShouldHandleInterfaces()
    {
        var source = @"
using SqlPartial;
namespace TestNamespace
{
    public interface IRepo
    {
        void Query([Sql] string query);
    }
}

namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";

        var (type, method) = GetSymbols(source, "TestNamespace.IRepo", "Query");
        var config = new GeneratorConfig(
            "TestNamespace",
            [],
            [],
            "TestNamespace.Sql",
            null,
            true);

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        // Interfaces use extension class which must be static
        Assert.Contains("static void Query(this TestNamespace.IRepo self, TestNamespace.Sql.SqlStrings query)", overloads);
        Assert.Contains("static void Query(this TestNamespace.IRepo self, TestNamespace.Sql.SqlDynamic query)", overloads);
        Assert.Contains("self.SqlProviderName", overloads);
        Assert.Contains("self.Query(query.Get(self.SqlProviderName))", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_ShouldHandleStaticClassExtensions()
    {
        var source = @"
using SqlPartial;
namespace TestNamespace
{
    public interface IRepo { string SqlProviderName { get; } }

    public static class RepoExtensions
    {
        public static void Query(this IRepo self, [Sql] string query) { }
    }
}

namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";

        var (type, method) = GetSymbols(source, "TestNamespace.RepoExtensions", "Query");
        var config = new GeneratorConfig(
            "TestNamespace",
            [],
            [],
            "TestNamespace.Sql",
            null,
            true);

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        // Original extension methods should preserve 'this' parameter
        Assert.Contains("static void Query(this TestNamespace.IRepo self, TestNamespace.Sql.SqlStrings query)", overloads);
        Assert.Contains("static void Query(this TestNamespace.IRepo self, TestNamespace.Sql.SqlDynamic query)", overloads);
        Assert.Contains("Query(self, query.Get(self.SqlProviderName))", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_ShouldPreserveDefaultValues()
    {
        var source = @"
using SqlPartial;
namespace TestNamespace
{
    public partial class Repo
    {
        public string SqlProviderName => ""Postgres"";
        public void Query([Sql] string query, int count = 10, string name = ""default"", bool flag = true) { }
    }
}

namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";

        var (type, method) = GetSymbols(source, "TestNamespace.Repo", "Query");
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true);

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        Assert.Contains("int count = 10", overloads);
        Assert.Contains("string name = \"default\"", overloads);
        Assert.Contains("bool flag = true", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_ShouldHandleStructDefaultValues()
    {
        var source = @"
using SqlPartial;
using System.Threading;
namespace TestNamespace
{
    public partial class Repo
    {
        public string SqlProviderName => ""Postgres"";
        public void Query([Sql] string query, CancellationToken ct = default) { }
    }
}

namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";

        var (type, method) = GetSymbols(source, "TestNamespace.Repo", "Query");
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true);

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        // Should be default or default(CancellationToken), NOT null
        Assert.Contains("CancellationToken ct = default", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_ShouldHandleNullableDefaultValues()
    {
        var source = @"
using SqlPartial;
namespace TestNamespace
{
    public partial class Repo
    {
        public string SqlProviderName => ""Postgres"";
        public void Query([Sql] string query, int? val = null, int? val2 = 5) { }
    }
}

namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";

        var (type, method) = GetSymbols(source, "TestNamespace.Repo", "Query");
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true);

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        Assert.Contains("int? val = null", overloads);
        Assert.Contains("int? val2 = 5", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_ShouldGenerateBuilderOverload()
    {
        var source = @"
using SqlPartial;
namespace TestNamespace
{
    public partial class Repo
    {
        public string SqlProviderName => ""Postgres"";
        public string Execute([Sql] string query) => query;
    }
}

namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";
        var (type, method) = GetSymbols(source, "TestNamespace.Repo", "Execute");
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true);

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        Assert.Contains("internal string Execute(TestNamespace.Sql.SqlStringBuilder query)", overloads);
        Assert.Contains("return Execute(query.Build(this.SqlProviderName));", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_BuilderOverload_ShouldUseNullForDefaultValue()
    {
        var source = @"
using SqlPartial;
namespace TestNamespace
{
    public partial class Repo
    {
        public string SqlProviderName => ""Postgres"";
        public string Execute([Sql] string query = null) => query;
    }
}

namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";
        var (type, method) = GetSymbols(source, "TestNamespace.Repo", "Execute");
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true);

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        // [Sql] + struct → default; [Sql] + class (builder) → null
        Assert.Contains("(TestNamespace.Sql.SqlStrings query = default)", overloads);
        Assert.Contains("(TestNamespace.Sql.SqlDynamic query = default)", overloads);
        Assert.Contains("(TestNamespace.Sql.SqlStringBuilder query = null)", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_BuilderOverload_ShouldHandleInterfaces()
    {
        var source = @"
using SqlPartial;
namespace TestNamespace
{
    public interface IRepo
    {
        void Query([Sql] string sql);
    }
}

namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";
        var (type, method) = GetSymbols(source, "TestNamespace.IRepo", "Query");
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true);

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        Assert.Contains("static void Query(this TestNamespace.IRepo self, TestNamespace.Sql.SqlStringBuilder sql)", overloads);
        Assert.Contains("self.Query(sql.Build(self.SqlProviderName))", overloads);
    }

    [Fact]
    public void SourceBuilder_BuildOverloads_BuilderOverload_ShouldUseSharedNamespace()
    {
        var source = @"
using SqlPartial;
namespace TestNamespace
{
    public partial class Repo
    {
        public string SqlProviderName => ""Postgres"";
        public void Query([Sql] string query) { }
    }
}

namespace SqlPartial { public class SqlAttribute : System.Attribute { } }
";
        var (type, method) = GetSymbols(source, "TestNamespace.Repo", "Query");
        var config = new GeneratorConfig("TestNamespace", [], [], "TestNamespace.Sql", null, true, false, "SharedSql");

        var overloads = SourceBuilder.BuildOverloads("TestNamespace", type, [method], config, true);

        Assert.Contains("public void Query(SharedSql.SqlStringBuilder query)", overloads);
        Assert.Contains("query.Build(this.SqlProviderName)", overloads);
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
