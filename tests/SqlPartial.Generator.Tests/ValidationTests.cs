using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using SqlPartial.Generator.Core;

namespace SqlPartial.Generator.Tests;

public class ValidationTests
{
    [Theory]
    [InlineData("", "UserRepo.GetUsers.sql")]
    [InlineData("namespace MyProject { class UserRepo { } }", "UserRepo.GetUsers.sql")]
    [InlineData("namespace MyProject { partial class userRepo { } }", "UserRepo.GetUsers.sql")]
    [InlineData("namespace Other { partial class UserRepo { } }", "UserRepo.GetUsers.sql")]
    [InlineData("namespace MyProject { partial interface UserRepo { } }", "UserRepo.GetUsers.sql")]
    [InlineData("namespace MyProject { partial class UserRepo<T> { } }", "UserRepo.GetUsers.sql")]
    [InlineData("namespace MyProject { partial class Outer { public partial class UserRepo { } } }", "UserRepo.GetUsers.sql")]
    public void Generator_ShouldRejectSqlWithoutMatchingSupportedPartialClass(string source, string fileName)
    {
        var (result, _) = Run(source, fileName);

        Assert.Contains(result.Diagnostics, d => d.Id == "SQLPG021" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratedTrees, t => t.GetText().ToString().Contains("SqlGetUsers ="));
    }

    [Theory]
    [InlineData("User-Repo.GetUsers.sql")]
    [InlineData("UserRepo.Get-Users.sql")]
    [InlineData("UserRepo..sql")]
    [InlineData(".GetUsers.sql")]
    public void Generator_ShouldRejectInvalidFileIdentifiers(string fileName)
    {
        var (result, _) = Run("namespace MyProject { partial class UserRepo { } }", fileName);

        Assert.Contains(result.Diagnostics, d => d.Id == "SQLPG022");
    }

    [Theory]
    [InlineData("class")]
    [InlineData("namespace")]
    [InlineData("event")]
    public void Generator_ShouldCompileKeywordProviders(string provider)
    {
        var (result, output) = Run("namespace MyProject { public static partial class SharedSql { } }",
            "SharedSql.GetUsers.pg.sql", new() { ["SqlPartialProviders"] = $".pg.sql:{provider}" });

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(result.GeneratedTrees, t => t.GetText().ToString().Contains($"@{provider}:"));
    }

    [Fact]
    public void Generator_ShouldCompileKeywordClassAndQueryNames()
    {
        var (result, output) = Run("namespace MyProject { static partial class @class { } }", "class.event.sql");

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData(".pg.sql:PostgreSql;.pg.sql:SqlServer")]
    [InlineData(".pg.sql:PostgreSql;.PG.SQL:PostgreSql")]
    [InlineData(".pg.sql:PostgreSql;.ms.sql:postgresql")]
    [InlineData(".sql:PostgreSql")]
    [InlineData(".pg.sql:Default")]
    [InlineData(".pg.sql:Get")]
    [InlineData(".pg.sql:Fallback")]
    [InlineData(".pg.sql:_default")]
    [InlineData(".pg.sql:PostgreSql;.ms.sql:_postgresql")]
    [InlineData(".pg.sql:PostgreSql;.ms.sql:_postgresqlFactory")]
    public void Generator_ShouldRejectConflictingProviderConfiguration(string providers)
    {
        var (result, _) = Run("", "UserRepo.GetUsers.sql", new() { ["SqlPartialProviders"] = providers });

        Assert.Contains(result.Diagnostics, d => d.Id == "SQLPG001");
        Assert.DoesNotContain(result.GeneratedTrees, t => t.GetText().ToString().Contains("readonly struct SqlStrings"));
    }

    [Theory]
    [InlineData("RootNamespace", "My-Project")]
    [InlineData("SqlPartialStringsNamespace", "MyProject..Sql")]
    [InlineData("SqlPartialEmitSharedNamespace", "Bad.Namespace;")]
    [InlineData("SqlPartialUseSharedNamespace", "Bad.Namespace;")]
    [InlineData("SqlPartialStringsType", "External.Type;")]
    [InlineData("SqlPartialStringsType", "External.Type // comment")]
    [InlineData("SqlPartialStringsNamespace", "MyProject /* comment */")]
    public void Generator_ShouldRejectInvalidNamespaceOrTypeConfiguration(string property, string value)
    {
        var (result, _) = Run("", "UserRepo.GetUsers.sql", new() { [property] = value });

        Assert.Contains(result.Diagnostics, d => d.Id == "SQLPG023");
        Assert.DoesNotContain(result.GeneratedTrees, t => t.GetText().ToString().Contains("readonly struct SqlStrings"));
    }

    [Fact]
    public void Generator_ShouldRejectSimultaneousEmitAndUseSharedNamespace()
    {
        var (result, _) = Run("", "UserRepo.GetUsers.sql", new()
        {
            ["SqlPartialEmitSharedNamespace"] = "Shared.Sql",
            ["SqlPartialUseSharedNamespace"] = "Shared.Sql"
        });

        Assert.Contains(result.Diagnostics, d => d.Id == "SQLPG023");
    }

    [Fact]
    public void Generator_ShouldCompilePublicStaticCatalogWithProviderAliases()
    {
        var (result, output) = Run("""
            namespace MyProject
            {
                [SqlPartial.SqlPartial(SqlPartial.AccessModifier.Public)]
                public static partial class SharedSql { }
                public class Consumer
                {
                    public string Query => SharedSql.SqlGetUsers.Get("PostgreSql");
                }
            }
            """, "SharedSql.GetUsers.pg.sql", new()
        {
            ["SqlPartialProviders"] = ".pg.sql:PostgreSql;.pgsql:PostgreSql",
            ["SqlPartialEmitSharedNamespace"] = "MyProject"
        });

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(output.GetDiagnostics(), d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void FilePathParser_ShouldNotTreatSiblingPrefixAsProjectDirectory()
    {
        var config = new SqlPartial.Generator.Models.GeneratorConfig("MyProject", [], [], "MyProject", null, true);
        var projectDir = Path.Combine(Path.GetTempPath(), "SqlPartialProject");
        var siblingFile = Path.Combine(projectDir + "2", "UserRepo.Query.sql");
        var childFile = Path.Combine(projectDir, "Queries", "UserRepo.Query.sql");

        var sibling = FilePathParser.TryParse(siblingFile, "MyProject", projectDir, config.SortedProviders);
        var child = FilePathParser.TryParse(childFile, "MyProject", projectDir + Path.DirectorySeparatorChar, config.SortedProviders);

        Assert.Null(sibling);
        Assert.Equal("MyProject.Queries", child?.ns);
    }

    private static (GeneratorDriverRunResult result, Compilation output) Run(string source, string fileName,
        Dictionary<string, string>? properties = null)
    {
        var projectDir = Path.Combine(Path.GetTempPath(), "SqlPartialValidation");
        var options = new Dictionary<string, string>
        {
            ["build_property.RootNamespace"] = "MyProject",
            ["build_property.MSBuildProjectDirectory"] = projectDir,
            ["build_metadata.AdditionalFiles.SourceItemType"] = "SqlPartial"
        };
        foreach (var p in properties ?? []) options["build_property." + p.Key] = p.Value;
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Where(p => Path.GetDirectoryName(p) == Path.GetDirectoryName(typeof(object).Assembly.Location))
            .Select(p => MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create("Validation", [CSharpSyntaxTree.ParseText(source)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new SqlPartialGenerator().AsSourceGenerator()],
            [new SqlText(Path.Combine(projectDir, fileName))], optionsProvider: new Options(options));
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        return (driver.GetRunResult(), output);
    }

    private sealed class SqlText(string path) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From("SELECT 1;");
    }

    private sealed class Options(Dictionary<string, string> values) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions => new Values(values);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new Values(values);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new Values(values);
        private sealed class Values(Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
