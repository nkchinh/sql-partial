using System.Collections.Immutable;
using SqlPartial.Generator.Core;
using SqlPartial.Generator.Models;

namespace SqlPartial.Generator.Tests;

public class GeneratorCoreTests
{
    [Fact]
    public void SqlCleaner_ShouldStripExcludeAndTestPartBlocks()
    {
        var sql = """
            --#exclude
            SELECT * FROM Hidden;
            --/exclude
            SELECT * FROM Users;
            -- #testpart
            SELECT * FROM Secret2;
            --  /testpart
            SELECT * FROM Products;
            """;

        var result = SqlContentCleaner.Clean(sql);
        var cleaned = result.Content;

        Assert.DoesNotContain("Hidden", cleaned);
        Assert.DoesNotContain("Secret2", cleaned);
        Assert.Contains("SELECT * FROM Users;", cleaned);
        Assert.Contains("SELECT * FROM Products;", cleaned);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void SqlCleaner_ShouldReportMismatchedTags()
    {
        var sql = """
            --#exclude
            SELECT * FROM Hidden;
            -- Orphaned opening tag
            --#exclude
            SELECT * FROM Users;
            --/exclude
            -- Orphaned closing tag
            --/exclude
            """;

        var result = SqlContentCleaner.Clean(sql);

        Assert.Equal(2, result.Diagnostics.Length);

        Assert.Contains(result.Diagnostics, d => d.Message.Contains("opening tag") && d.Message.Contains("'-- /exclude'"));
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("closing tag") && d.Message.Contains("'-- #exclude'"));
    }

    [Fact]
    public void SqlCleaner_ShouldStripWholeLineCommentsButPreserveTrailingOnes()
    {
        var sql = """
                SELECT * FROM Users; -- some comment
            -- another comment
            SELECT 'a -- b';
            """;

        var result = SqlContentCleaner.Clean(sql);
        var cleaned = result.Content;

        // Should preserve indentation of active lines
        // and trailing comments on active lines
        Assert.Contains("    SELECT * FROM Users; -- some comment", cleaned);

        // Should correctly handle -- inside strings
        Assert.Contains("SELECT 'a -- b';", cleaned);

        // Should NOT contain standalone comments
        Assert.DoesNotContain("-- another comment", cleaned);
    }

    [Fact]
    public void SqlCleaner_ShouldEscapeDoubleQuotesForVerbatimString()
    {
        var sql = "SELECT * FROM \"Users\" WHERE Name = 'O\"Reilly';";
        var result = SqlContentCleaner.Clean(sql);
        var cleaned = result.Content;

        // Verbatim string in C# escapes " as ""
        Assert.Equal("SELECT * FROM \"\"Users\"\" WHERE Name = 'O\"\"Reilly';", cleaned);
    }

    [Fact]
    public void SourceBuilder_BuildSqlStringsStruct_ShouldGenerateCorrectStructs()
    {
        var config = new GeneratorConfig(
            "MyProject",
            [new SqlProvider(".pg.sql", "PostgreSql")],
            [],
            "MyProject.Sql",
            null,
            true);

        var source = SourceBuilder.BuildSqlStringsStruct(config, true);

        Assert.Contains("internal interface ISqlString", source);
        Assert.Contains("string PostgreSql { get; }", source);
        Assert.Contains("internal readonly struct SqlStrings : ISqlString", source);
        Assert.Contains("internal readonly struct SqlDynamic : ISqlString", source);
        Assert.Contains("private readonly System.Func<string>? _postgresqlFactory;", source);
        Assert.Contains("public string PostgreSql => _postgresqlFactory?.Invoke() ?? Default;", source);

        // SqlAttribute is now separate
        Assert.DoesNotContain("namespace SqlPartial", source);

        var attrSource = SourceBuilder.BuildSqlAttribute();
        Assert.Contains("namespace SqlPartial", attrSource);
        Assert.Contains("sealed class SqlAttribute : System.Attribute { }", attrSource);
    }

    [Fact]
    public void SourceBuilder_BuildSqlStringsStruct_ShouldGenerateSqlStringBuilder()
    {
        var config = new GeneratorConfig(
            "MyProject",
            [new SqlProvider(".pg.sql", "PostgreSql")],
            [],
            "MyProject.Sql",
            null,
            true);

        var source = SourceBuilder.BuildSqlStringsStruct(config, true);

        Assert.Contains("internal sealed class SqlStringBuilder", source);
        Assert.Contains("public SqlStringBuilder Append(ISqlString sql)", source);
        Assert.Contains("public SqlStringBuilder Append(string sql)", source);
        Assert.Contains("public SqlStringBuilder AppendLine(ISqlString sql)", source);
        Assert.Contains("public SqlStringBuilder AppendLine(string sql)", source);
        Assert.Contains("public SqlStringBuilder AppendLine()", source);
        Assert.Contains("public SqlStringBuilder Clear()", source);
        Assert.Contains("public string Build(string providerName)", source);
        Assert.Contains("public int Count", source);
    }

    [Fact]
    public void SourceBuilder_BuildSqlStringsStruct_ShouldGeneratePublicBuilderWhenSharedNamespace()
    {
        var config = new GeneratorConfig(
            "MyProject",
            [],
            [],
            "MyProject.Sql",
            null,
            true,
            false,
            "SharedSql");

        var source = SourceBuilder.BuildSqlStringsStruct(config, true);

        Assert.Contains("public sealed class SqlStringBuilder", source);
    }

    [Fact]
    public void SourceBuilder_BuildSqlStringsStruct_ShouldBePublicWhenEmittingSharedNamespace()
    {
        var config = new GeneratorConfig(
            "MyProject",
            [],
            [],
            "MyProject.Sql",
            null,
            true,
            false,
            "SharedSql");

        var source = SourceBuilder.BuildSqlStringsStruct(config, true);

        Assert.Contains("namespace SharedSql", source);
        Assert.Contains("public interface ISqlString", source);
        Assert.Contains("public readonly struct SqlStrings", source);
    }

    [Fact]
    public void SourceBuilder_BuildSqlStringsStruct_ShouldNotEmitWhenUsingSharedNamespace()
    {
        var config = new GeneratorConfig(
            "MyProject",
            [new SqlProvider(".pg.sql", "PostgreSql")],
            [],
            "MyProject.Sql",
            null,
            true);

        var source = SourceBuilder.BuildSqlStringsStruct(config, false);

        Assert.DoesNotContain("#nullable", source);
        Assert.Contains("public string PostgreSql => _postgresql ?? Default;", source);
        Assert.Contains("public SqlStrings(string postgresql = null, string @default = null)", source);
    }

    [Fact]
    public void SourceBuilder_BuildPartialClass_ShouldGenerateCorrectProperties()
    {
        var config = new GeneratorConfig(
            "MyProject",
            [new SqlProvider(".pg.sql", "PostgreSql")],
            [],
            "MyProject.Sql",
            null,
            false);

        var contentByProvider = new Dictionary<string, string>
        {
            { FilePathParser.FallbackProviderName, "SELECT 1;" },
            { "PostgreSql", "SELECT 2;" }
        }.ToImmutableDictionary();

        var groups = ImmutableArray.Create(new SqlQueryGroup(
            "MyProject.Repos",
            "UserRepo",
            "GetUsers",
            contentByProvider));

        var source = SourceBuilder.BuildPartialClass(
            "MyProject.Repos",
            "UserRepo",
            groups,
            config,
            true);

        Assert.Contains("#nullable disable", source);
        Assert.Contains("namespace MyProject.Repos", source);
        Assert.Contains("partial class UserRepo", source);
        Assert.Contains("private static readonly MyProject.Sql.SqlStrings SqlGetUsers = new MyProject.Sql.SqlStrings(", source);
        Assert.Contains("@\"SELECT 1;\"", source);
        Assert.Contains("postgresql: @\"SELECT 2;\"", source);
    }

    [Fact]
    public void SourceBuilder_BuildPartialClass_ShouldUseExternalStringsType()
    {
        var config = new GeneratorConfig(
            "MyProject",
            [],
            [],
            "MyProject.Sql",
            "Shared.SqlStrings",
            true);

        var groups = ImmutableArray.Create(new SqlQueryGroup(
            "MyProject.Repos",
            "UserRepo",
            "GetUsers",
            new Dictionary<string, string> { { FilePathParser.FallbackProviderName, "SELECT 1;" } }.ToImmutableDictionary()));

        var source = SourceBuilder.BuildPartialClass(
            "MyProject.Repos",
            "UserRepo",
            groups,
            config,
            true);

        Assert.Contains("private static readonly Shared.SqlStrings SqlGetUsers = new Shared.SqlStrings(", source);
    }

    [Theory]
    [InlineData(@"C:\Proj\Repos\UserRepo.GetUsers.sql", "MyProj", @"C:\Proj", "MyProj.Repos", "UserRepo", "GetUsers", FilePathParser.FallbackProviderName)]
    [InlineData(@"C:\Proj\Repos\UserRepo.GetUsers.pg.sql", "MyProj", @"C:\Proj", "MyProj.Repos", "UserRepo", "GetUsers", "PostgreSql")]
    [InlineData(@"C:\Proj\Repos\UserRepo.GetUsers.pgsql", "MyProj", @"C:\Proj", "MyProj.Repos", "UserRepo", "GetUsers", "PostgreSql")]
    [InlineData(@"C:\Proj\UserRepo.GetUsers.sql", "MyProj", @"C:\Proj", "MyProj", "UserRepo", "GetUsers", FilePathParser.FallbackProviderName)]
    public void FilePathParser_ShouldParseCorrectly(
        string path, string rootNs, string projDir,
        string expectedNs, string expectedClass, string expectedQuery, string expectedProvider)
    {
        var providers = ImmutableArray.Create(
            new SqlProvider(".pg.sql", "PostgreSql"),
            new SqlProvider(".pgsql", "PostgreSql")
        );

        var config = new GeneratorConfig(rootNs, providers, [], rootNs, null, true);
        var result = FilePathParser.TryParse(path, rootNs, projDir, config.SortedProviders);

        Assert.NotNull(result);
        Assert.Equal(expectedNs, result.Value.ns);
        Assert.Equal(expectedClass, result.Value.className);
        Assert.Equal(expectedQuery, result.Value.queryName);
        Assert.Equal(expectedProvider, result.Value.providerName);
    }

    [Fact]
    public void ConfigParser_ParseProviders_ShouldReportInvalidOnMissingDot()
    {
        var raw = "ms.sql:SqlServer";
        var (providers, invalid) = ConfigParser.ParseProviders(raw);

        Assert.Empty(providers);
        Assert.Single(invalid);
        Assert.Equal("ms.sql:SqlServer", invalid[0]);
    }

    [Fact]
    public void ConfigParser_ParseProviders_ShouldReportInvalidOnInvalidIdentifier()
    {
        var raw = ".sql:123SqlServer";
        var (providers, invalid) = ConfigParser.ParseProviders(raw);

        Assert.Empty(providers);
        Assert.Single(invalid);
        Assert.Equal(".sql:123SqlServer", invalid[0]);
    }

    [Fact]
    public void SqlQueryGroup_GetContent_ShouldReturnEmptyIfDefaultMissing()
    {
        var contentByProvider = new Dictionary<string, string>
        {
            { "PostgreSql", "SELECT PG;" }
        }.ToImmutableDictionary();

        var group = new SqlQueryGroup("NS", "Class", "Query", contentByProvider);

        // Existing provider
        Assert.Equal("SELECT PG;", group.GetContent("PostgreSql"));

        // Missing provider AND missing default -> empty string
        Assert.Equal(string.Empty, group.GetContent(FilePathParser.FallbackProviderName));
        Assert.Equal(string.Empty, group.GetContent("Unknown"));
    }

    [Fact]
    public void GeneratorConfig_DistinctProviderNames_ShouldDeduplicate()
    {
        var providers = ImmutableArray.Create(
            new SqlProvider(".pg.sql", "PostgreSql"),
            new SqlProvider(".pgsql", "PostgreSql"),
            new SqlProvider(".ms.sql", "SqlServer")
        );

        var config = new GeneratorConfig("NS", providers, [], "NS", null, true);

        var distinct = config.DistinctProviderNames.ToList();
        Assert.Equal(2, distinct.Count);
        Assert.Contains("PostgreSql", distinct);
        Assert.Contains("SqlServer", distinct);
    }

    [Theory]
    [InlineData(@"C:\Proj\User.Get.pg.sql", "PostgreSql")] // Longest match
    [InlineData(@"C:\Proj\User.Get.sql", FilePathParser.FallbackProviderName)] // Fallback
    public void FilePathParser_ShouldHandleAmbiguousExtensions(string path, string expectedProvider)
    {
        var providers = ImmutableArray.Create(
            new SqlProvider(".pg.sql", "PostgreSql"),
            new SqlProvider(".sql", "MySql") // Ambiguous with fallback default
        );

        var config = new GeneratorConfig("NS", providers, [], "NS", null, true);
        var result = FilePathParser.TryParse(path, "NS", @"C:\Proj", config.SortedProviders);

        Assert.NotNull(result);
        Assert.Equal(expectedProvider, result.Value.providerName);
    }

    [Fact]
    public void FilePathParser_ShouldReturnNullOnInvalidFilename()
    {
        var config = new GeneratorConfig("MyProj", [], [], "MyProj", null, true);
        var result = FilePathParser.TryParse(@"C:\Proj\Invalid.sql", "MyProj", @"C:\Proj", config.SortedProviders);
        Assert.Null(result);
    }
}
