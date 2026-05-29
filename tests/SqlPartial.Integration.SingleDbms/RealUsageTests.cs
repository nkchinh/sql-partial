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
            var expected = "SELECT Value FROM Settings WHERE Key = @Key;";

            Assert.Equal(expected, sql.AnsiSql);

            // Implicit conversion
            string query = sql;
            Assert.Equal(expected, query);

            // Get unknown provider returns ANSI
            Assert.Equal(expected, sql.Get("PostgreSql"));
        }
    }
}
