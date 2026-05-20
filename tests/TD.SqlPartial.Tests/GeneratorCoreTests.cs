using TD.SqlPartial.Generator.Core;
using TD.SqlPartial.Generator.Models;

namespace TD.SqlPartial.Tests
{
    public class GeneratorCoreTests
    {
        [Fact]
        public void SqlCleaner_ShouldStripTestPartAndWholeLineCommentsButPreserveTrailingOnes()
        {
            var sql = """
                -- #testpart
                SELECT * FROM Secret;
                -- /testpart
                    SELECT * FROM Users; -- some comment
                -- another comment
                SELECT 'a -- b';
                """;

            var cleaned = SqlCleaner.Clean(sql);

            // Should NOT contain Secret
            Assert.DoesNotContain("Secret", cleaned);

            // Should preserve indentation (except immediately after #testpart due to aggressive original regex)
            // and trailing comments on active lines (original logic)
            Assert.Contains("SELECT * FROM Users; -- some comment", cleaned);

            // Should correctly handle -- inside strings (original logic)
            Assert.Contains("SELECT 'a -- b';", cleaned);

            // Should NOT contain standalone comments
            Assert.DoesNotContain("-- another comment", cleaned);
        }

        [Fact]
        public void SourceBuilder_ShouldHandleNullableEnable()
        {
            var item = new SqlItem(
                "test.sql", "test.sql", "SELECT 1;", "MyNamespace", "MyClass",
                null, "SqlTest", "public", true);

            var source = SourceBuilder.Build(item);

            Assert.Contains("#nullable enable", source);
        }

        [Fact]
        public void SourceBuilder_ShouldOmitClassModifierWhenNull()
        {
            var item = new SqlItem(
                "test.sql", "test.sql", "SELECT 1;", "MyNamespace", "MyClass",
                null, "SqlTest", "public", false);

            var source = SourceBuilder.Build(item);

            Assert.Contains("partial class MyClass", source);
            Assert.DoesNotContain("public partial class MyClass", source);
            Assert.DoesNotContain("internal partial class MyClass", source);
        }

        [Fact]
        public void SourceBuilder_ShouldUseClassModifierWhenProvided()
        {
            var item = new SqlItem(
                "test.sql", "test.sql", "SELECT 1;", "MyNamespace", "MyClass",
                "internal", "SqlTest", "public", false);

            var source = SourceBuilder.Build(item);

            Assert.Contains("internal partial class MyClass", source);
        }
    }
}
