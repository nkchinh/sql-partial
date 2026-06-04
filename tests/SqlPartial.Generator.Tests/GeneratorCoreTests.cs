using System.Collections.Immutable;
using SqlPartial.Generator.Core;
using SqlPartial.Generator.Models;

namespace SqlPartial.Tests
{
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

            var cleaned = SqlContentCleaner.Clean(sql);

            Assert.DoesNotContain("Hidden", cleaned);
            Assert.DoesNotContain("Secret2", cleaned);
            Assert.Contains("SELECT * FROM Users;", cleaned);
            Assert.Contains("SELECT * FROM Products;", cleaned);
        }

        [Fact]
        public void SqlCleaner_ShouldStripWholeLineCommentsButPreserveTrailingOnes()
        {
            var sql = """
                    SELECT * FROM Users; -- some comment
                -- another comment
                SELECT 'a -- b';
                """;

            var cleaned = SqlContentCleaner.Clean(sql);

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
            var cleaned = SqlContentCleaner.Clean(sql);

            // Verbatim string in C# escapes " as ""
            Assert.Equal("SELECT * FROM \"\"Users\"\" WHERE Name = 'O\"\"Reilly';", cleaned);
        }

        [Fact]
        public void SourceBuilder_BuildSqlStringsStruct_ShouldGenerateCorrectStructs()
        {
            var config = new GeneratorConfig(
                "MyProject",
                [new SqlProvider(".pg.sql", "PostgreSql")],
                "MyProject.Sql",
                null,
                true);

            var source = SourceBuilder.BuildSqlStringsStruct(config, true);

            Assert.Contains("public interface ISqlString", source);
            Assert.Contains("public readonly struct SqlStrings : ISqlString", source);
            Assert.Contains("public static implicit operator SqlStrings(string fallback)", source);
            Assert.Contains("public readonly struct SqlDynamic : ISqlString", source);
            Assert.Contains("private readonly System.Func<string>? _postgresqlFactory;", source);
            Assert.Contains("public string PostgreSql => _postgresqlFactory?.Invoke() ?? Fallback;", source);
        }

        [Fact]
        public void SourceBuilder_BuildSqlStringsStruct_ShouldNotEmitNullableOnOldCSharp()
        {
            var config = new GeneratorConfig(
                "MyProject",
                [new SqlProvider(".pg.sql", "PostgreSql")],
                "MyProject.Sql",
                null,
                true);

            var source = SourceBuilder.BuildSqlStringsStruct(config, false);

            Assert.DoesNotContain("#nullable", source);
            Assert.Contains("public string PostgreSql => _postgresql ?? Fallback;", source);
            Assert.Contains("public SqlStrings(string fallback, string postgresql = null)", source);
        }

        [Fact]
        public void SourceBuilder_BuildPartialClass_ShouldGenerateCorrectProperties()
        {
            var config = new GeneratorConfig(
                "MyProject",
                [new SqlProvider(".pg.sql", "PostgreSql")],
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

            var result = FilePathParser.TryParse(path, rootNs, projDir, providers);

            Assert.NotNull(result);
            Assert.Equal(expectedNs, result.Value.ns);
            Assert.Equal(expectedClass, result.Value.className);
            Assert.Equal(expectedQuery, result.Value.queryName);
            Assert.Equal(expectedProvider, result.Value.providerName);
        }

        [Fact]
        public void ConfigParser_ParseProviders_ShouldHandleMultipleSeparatorsAndDots()
        {
            // Semicolon and comma mixed, some with dots, some without
            var raw = ".pg.sql:PostgreSql,pgsql:PostgreSql;.ms.sql:SqlServer";
            var providers = ConfigParser.ParseProviders(raw);

            Assert.Equal(3, providers.Length);
            Assert.Equal(".pg.sql", providers[0].Extension);
            Assert.Equal(".pgsql", providers[1].Extension);
            Assert.Equal(".ms.sql", providers[2].Extension);
            Assert.All(providers, p => Assert.NotNull(p.Name));
        }

        [Fact]
        public void SqlQueryGroup_GetContent_ShouldReturnEmptyIfFallbackMissing()
        {
            var contentByProvider = new Dictionary<string, string>
            {
                { "PostgreSql", "SELECT PG;" }
            }.ToImmutableDictionary();

            var group = new SqlQueryGroup("NS", "Class", "Query", contentByProvider);

            // Existing provider
            Assert.Equal("SELECT PG;", group.GetContent("PostgreSql"));

            // Missing provider AND missing fallback -> empty string
            Assert.Equal(string.Empty, group.GetContent("Fallback"));
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

            var config = new GeneratorConfig("NS", providers, "NS", null, true);

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

            var result = FilePathParser.TryParse(path, "NS", @"C:\Proj", providers);

            Assert.NotNull(result);
            Assert.Equal(expectedProvider, result.Value.providerName);
        }

        [Fact]
        public void FilePathParser_ShouldReturnNullOnInvalidFilename()
        {
            var result = FilePathParser.TryParse(@"C:\Proj\Invalid.sql", "MyProj", @"C:\Proj", ImmutableArray<SqlProvider>.Empty);
            Assert.Null(result);
        }
    }
}
