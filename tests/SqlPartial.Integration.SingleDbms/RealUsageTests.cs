using Xunit;

namespace SqlPartial.Integration.SingleDbms
{
    public partial class RealUsageTests
    {
        [Fact]
        public void RealUsageTests_SingleDbms_ShouldImplicitlyConvertToAnsiSql()
        {
            // GetSettings only has ANSI version
            var sql = SqlGetSettings;

            Assert.Equal("SELECT Value FROM Settings WHERE Key = @Key;", sql.AnsiSql);

            // Implicit conversion
            string query = sql;
            Assert.Equal("SELECT Value FROM Settings WHERE Key = @Key;", query);

            // Get unknown provider returns ANSI
            Assert.Equal("SELECT Value FROM Settings WHERE Key = @Key;", sql.Get("PostgreSql"));
        }
    }
}
