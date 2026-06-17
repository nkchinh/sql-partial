using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SqlPartial.Generator.Core;

namespace SqlPartial.Generator.Tests;

public class CollisionTests
{
    [Fact]
    public async Task Generator_ShouldReportWarningOnNamingCollision_WithAttribute()
    {
        var source = @"
using System;
using SqlPartial;
namespace MyProject
{
    [SqlPartial(AccessModifier.Public)]
    public partial class UserRepo
    {
        public string SqlProviderName => ""SqlServer"";
        public static string SqlGetUsers = ""Manual"";
    }
}
" + SourceBuilder.BuildSqlAttribute();

        var sqlFile = @"C:\Proj\UserRepo.GetUsers.sql";
        var sqlContent = "SELECT * FROM Users;";

        var compilation = CreateCompilation(source);
        var generator = new SqlPartialGenerator();

        var optionsProvider = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.SourceItemType"] = "SqlPartial",
            ["build_property.RootNamespace"] = "MyProject",
            ["build_property.MSBuildProjectDirectory"] = @"C:\Proj"
        });

        var driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            [new InMemoryAdditionalText(sqlFile, sqlContent)],
            null,
            optionsProvider
        );

        var runResult = driver.RunGenerators(compilation).GetRunResult();

        // Should have a warning SQLPG005
        Assert.Contains(runResult.Diagnostics, d => d.Id == "SQLPG005");

        // Should have generated trees
        Assert.NotEmpty(runResult.GeneratedTrees);
    }

    [Fact]
    public async Task Generator_ShouldReportWarningOnNamingCollision_WithoutAttribute()
    {
        var source = @"
namespace MyProject
{
    public partial class UserRepo
    {
        public string SqlProviderName => ""SqlServer"";
        private static string SqlGetUsers = ""Manual"";
    }
}
" + SourceBuilder.BuildSqlAttribute();

        var sqlFile = @"C:\Proj\UserRepo.GetUsers.sql";
        var sqlContent = "SELECT * FROM Users;";

        var compilation = CreateCompilation(source);
        var generator = new SqlPartialGenerator();

        var optionsProvider = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.SourceItemType"] = "SqlPartial",
            ["build_property.RootNamespace"] = "MyProject",
            ["build_property.MSBuildProjectDirectory"] = @"C:\Proj"
        });

        var driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            [new InMemoryAdditionalText(sqlFile, sqlContent)],
            null,
            optionsProvider
        );

        var runResult = driver.RunGenerators(compilation).GetRunResult();

        // Should have a warning SQLPG005
        Assert.Contains(runResult.Diagnostics, d => d.Id == "SQLPG005");

        // Should have generated trees
        Assert.NotEmpty(runResult.GeneratedTrees);
    }

    [Fact]
    public async Task Generator_ShouldReportWarningOnDuplicateSqlMapping()
    {
        var source = @"
using System;
using SqlPartial;
namespace MyProject
{
    [SqlPartial(AccessModifier.Public)]
    public partial class UserRepo
    {
        public string SqlProviderName => ""SqlServer"";
    }
}
" + SourceBuilder.BuildSqlAttribute();

        // Two files mapping to the same provider (PostgreSql)
        var sqlFile1 = @"C:\Proj\UserRepo.GetUsers.pg.sql";
        var sqlFile2 = @"C:\Proj\UserRepo.GetUsers.pgsql";

        var compilation = CreateCompilation(source);
        var generator = new SqlPartialGenerator();

        var optionsProvider = new TestAnalyzerConfigOptionsProvider(new Dictionary<string, string>
        {
            ["build_metadata.AdditionalFiles.SourceItemType"] = "SqlPartial",
            ["build_property.RootNamespace"] = "MyProject",
            ["build_property.MSBuildProjectDirectory"] = @"C:\Proj",
            ["build_property.SqlPartialProviders"] = ".pg.sql:PostgreSql;.pgsql:PostgreSql"
        });

        var driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            [
                new InMemoryAdditionalText(sqlFile1, "SELECT 1;"),
                new InMemoryAdditionalText(sqlFile2, "SELECT 2;")
            ],
            null,
            optionsProvider
        );

        var runResult = driver.RunGenerators(compilation).GetRunResult();

        // Should have a warning SQLPG006
        Assert.Contains(runResult.Diagnostics, d => d.Id == "SQLPG006");
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("System.Runtime").Location),
            MetadataReference.CreateFromFile(System.Reflection.Assembly.Load("netstandard").Location),
        };

        return CSharpCompilation.Create("Test")
            .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
            .AddReferences(references)
            .AddSyntaxTrees(CSharpSyntaxTree.ParseText(source));
    }

    private class InMemoryAdditionalText(string path, string content) : AdditionalText
    {
        private readonly string _content = content;

        public override string Path { get; } = path;

        public override Microsoft.CodeAnalysis.Text.SourceText GetText(CancellationToken cancellationToken = default)
            => Microsoft.CodeAnalysis.Text.SourceText.From(_content);
    }

    private class TestAnalyzerConfigOptionsProvider(Dictionary<string, string> options) : AnalyzerConfigOptionsProvider
    {
        private readonly Dictionary<string, string> _options = options;

        public override AnalyzerConfigOptions GlobalOptions => new TestAnalyzerConfigOptions(_options);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new TestAnalyzerConfigOptions(_options);
        public override AnalyzerConfigOptions GetOptions(AdditionalText text) => new TestAnalyzerConfigOptions(_options);

        private class TestAnalyzerConfigOptions(Dictionary<string, string> options) : AnalyzerConfigOptions
        {
            private readonly Dictionary<string, string> _options = options;

            public override bool TryGetValue(string key, out string value) => _options.TryGetValue(key, out value!);
        }
    }
}
