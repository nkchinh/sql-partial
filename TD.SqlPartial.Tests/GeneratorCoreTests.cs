using System.Collections.Immutable;
using TD.SqlPartial.Generator.Core;
using TD.SqlPartial.Generator.Models;

namespace TD.SqlPartial.Tests
{
    public class GeneratorCoreTests
    {
        [Fact]
        public void SqlCleaner_ShouldStripTestPartBlocks()
        {
            var sql = """
                --#testpart
                SELECT * FROM Secret1;
                --/testpart
                SELECT * FROM Users;
                -- #testpart
                SELECT * FROM Secret2;
                --  /testpart
                SELECT * FROM Products;
                """;

            var cleaned = SqlContentCleaner.Clean(sql);

            Assert.DoesNotContain("Secret1", cleaned);
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
        public void SourceBuilder_BuildSqlStringsStruct_ShouldGenerateCorrectStruct()
        {
            var config = new GeneratorConfig(
                "MyProject",
                [new SqlProvider("pg", "PostgreSql")],
                "MyProject.Sql",
                null,
                true);

            var source = SourceBuilder.BuildSqlStringsStruct(config);

            Assert.Contains("#nullable enable", source);
            Assert.Contains("namespace MyProject.Sql", source);
            Assert.Contains("public readonly struct SqlStrings", source);
            Assert.Contains("public string AnsiSql { get; init; }", source);
            Assert.Contains("public string? PostgreSql { get; init; }", source);
            Assert.Contains("case \"PostgreSql\":", source);
            Assert.Contains("return PostgreSql ?? AnsiSql;", source);
        }

        [Fact]
        public void SourceBuilder_BuildPartialClass_ShouldGenerateCorrectProperties()
        {
            var config = new GeneratorConfig(
                "MyProject",
                [new SqlProvider("pg", "PostgreSql")],
                "MyProject.Sql",
                null,
                false);

            var contentBySlug = new Dictionary<string, string>
            {
                { "an", "SELECT 1;" },
                { "pg", "SELECT 2;" }
            }.ToImmutableDictionary();

            var groups = ImmutableArray.Create(new SqlQueryGroup(
                "MyProject.Repos",
                "UserRepo",
                "GetUsers",
                contentBySlug));

            var source = SourceBuilder.BuildPartialClass(
                "MyProject.Repos",
                "UserRepo",
                groups,
                config);

            Assert.Contains("#nullable disable", source);
            Assert.Contains("namespace MyProject.Repos", source);
            Assert.Contains("partial class UserRepo", source);
            Assert.Contains("private static readonly MyProject.Sql.SqlStrings SqlGetUsers = new MyProject.Sql.SqlStrings", source);
            Assert.Contains("AnsiSql = @\"SELECT 1;\"", source);
            Assert.Contains("PostgreSql = @\"SELECT 2;\"", source);
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
                new Dictionary<string, string> { { "an", "SELECT 1;" } }.ToImmutableDictionary()));

            var source = SourceBuilder.BuildPartialClass(
                "MyProject.Repos",
                "UserRepo",
                groups,
                config);

            Assert.Contains("private static readonly Shared.SqlStrings SqlGetUsers = new Shared.SqlStrings", source);
        }

        [Theory]
        [InlineData(@"C:\Proj\Repos\UserRepo.GetUsers.sql", "MyProj", @"C:\Proj", "MyProj.Repos", "UserRepo", "GetUsers", "an")]
        [InlineData(@"C:\Proj\Repos\UserRepo.GetUsers.pg.sql", "MyProj", @"C:\Proj", "MyProj.Repos", "UserRepo", "GetUsers", "pg")]
        [InlineData(@"C:\Proj\UserRepo.GetUsers.sql", "MyProj", @"C:\Proj", "MyProj", "UserRepo", "GetUsers", "an")]
        public void FilePathParser_ShouldParseCorrectly(
            string path, string rootNs, string projDir,
            string expectedNs, string expectedClass, string expectedQuery, string expectedSlug)
        {
            var result = FilePathParser.TryParse(path, rootNs, projDir);

            Assert.NotNull(result);
            Assert.Equal(expectedNs, result.Value.ns);
            Assert.Equal(expectedClass, result.Value.className);
            Assert.Equal(expectedQuery, result.Value.queryName);
            Assert.Equal(expectedSlug, result.Value.providerSlug);
        }

        [Fact]
        public void FilePathParser_ShouldReturnNullOnInvalidFilename()
        {
            var result = FilePathParser.TryParse(@"C:\Proj\Invalid.sql", "MyProj", @"C:\Proj");
            Assert.Null(result);
        }
    }
}
