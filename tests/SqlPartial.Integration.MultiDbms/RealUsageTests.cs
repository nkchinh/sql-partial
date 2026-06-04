using Xunit;

namespace SqlPartial.Integration.MultiDbms;

public partial class RealUsageTests
{
    [Fact]
    public void RealUsageTests_MultiDbms_ShouldResolveCorrectSql()
    {
        var sql = SqlGetUsers;

        Assert.Equal("SELECT * FROM Users;", sql.Fallback);
        Assert.Equal("SELECT * FROM Users LIMIT $1;", sql.PostgreSql);
        Assert.Equal("SELECT TOP (@Count) * FROM Users;", sql.SqlServer);

        // Test runtime selection
        Assert.Equal("SELECT * FROM Users LIMIT $1;", sql.Get("PostgreSql"));
        Assert.Equal("SELECT TOP (@Count) * FROM Users;", sql.Get("SqlServer"));
        Assert.Equal("SELECT * FROM Users;", sql.Get("Unknown"));
    }
}
