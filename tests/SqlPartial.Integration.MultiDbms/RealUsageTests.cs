using Xunit;
using SqlPartial.Abstractions;

namespace SqlPartial.Integration.MultiDbms;

public partial class RealUsageTests
{
    public string SqlProviderName { get; set; } = "SqlServer";

    public string Execute([Sql] string query) => query;

    [Fact]
    public void RealUsageTests_MultiDbms_ShouldResolveCorrectSql()
    {
        var sql = SqlGetUsers;

        Assert.Equal("SELECT * FROM Users;", sql.Default);
        Assert.Equal("SELECT * FROM Users LIMIT $1;", sql.PostgreSql);
        Assert.Equal("SELECT TOP (@Count) * FROM Users;", sql.SqlServer);

        // Test attribute-based overload
        SqlProviderName = "PostgreSql";
        Assert.Equal("SELECT * FROM Users LIMIT $1;", Execute(sql));

        SqlProviderName = "SqlServer";
        Assert.Equal("SELECT TOP (@Count) * FROM Users;", Execute(sql));

        SqlProviderName = "Unknown";
        Assert.Equal("SELECT * FROM Users;", Execute(sql));
    }
}
