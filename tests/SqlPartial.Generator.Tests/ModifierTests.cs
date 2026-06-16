using SqlPartial.Generator.Core;
using SqlPartial.Generator.Models;
using System.Collections.Immutable;

namespace SqlPartial.Generator.Tests;

public class ModifierTests
{
    [Fact]
    public void SourceBuilder_BuildPartialClass_ShouldUseSpecifiedModifier()
    {
        var config = new GeneratorConfig("NS", [], [], "NS.Sql", null, true);
        var groups = ImmutableArray.Create(new SqlQueryGroup("NS", "Repo", "Query",
            new Dictionary<string, string> { { FilePathParser.FallbackProviderName, "SELECT 1" } }.ToImmutableDictionary()));

        // Test Public
        var sourcePublic = SourceBuilder.BuildPartialClass("NS", "Repo", groups, config, true, "public");
        Assert.Contains("public static readonly NS.Sql.SqlStrings SqlQuery", sourcePublic);

        // Test Internal
        var sourceInternal = SourceBuilder.BuildPartialClass("NS", "Repo", groups, config, true, "internal");
        Assert.Contains("internal static readonly NS.Sql.SqlStrings SqlQuery", sourceInternal);

        // Test Private (Default)
        var sourcePrivate = SourceBuilder.BuildPartialClass("NS", "Repo", groups, config, true);
        Assert.Contains("private static readonly NS.Sql.SqlStrings SqlQuery", sourcePrivate);
    }
}
