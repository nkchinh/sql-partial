using Xunit;
using SqlPartial.Shared.Persistence; // The customized namespace

namespace SqlPartial.Integration.Shared.Abstractions;

public partial class NamespaceCustomizationTests
{
    public string SqlProviderName { get; set; } = "PostgreSql";

    public string Query([Sql] string sql) => sql;

    [Fact]
    public void CustomizedNamespaceShouldWork()
    {
        // SqlQuery is the property generated from NamespaceCustomizationTests.Query.sql
        // It should be of type SqlStrings which is in SqlPartial.Shared.Persistence
        var sql = SqlQuery;

        Assert.IsType<SqlStrings>(sql);
        Assert.Equal("SELECT 'From .sql';", sql.Default);

        // Test the overload
        SqlProviderName = "PostgreSql";
        var result = Query(sql);
        Assert.Equal("SELECT 'From .sql';", result);
    }

    [Fact]
    public void SharedTypesShouldBeInCustomNamespace()
    {
        // Verify we can use the types from the custom namespace
        SqlStrings s = new SqlStrings(@default: "test");
        Assert.Equal("test", s.Default);

        ISqlString isql = s;
        Assert.Equal("test", isql.Default);
    }
}
