using Xunit;

namespace SqlPartial.Integration.SingleDbms;

public partial class RealUsageTests
{
    [Fact]
    public void RealUsageTests_SingleDbms_ShouldImplicitlyConvertToFallback()
    {
        // GetSettings only has fallback version
        var sql = SqlGetSettings;
        var expected = "SELECT Value FROM Settings WHERE Key = @Key;";

        Assert.Equal(expected, sql.Fallback);

        // Implicit conversion
        string query = sql;
        Assert.Equal(expected, query);

        // Get unknown provider returns fallback
        Assert.Equal(expected, sql.Get("PostgreSql"));
    }
}
