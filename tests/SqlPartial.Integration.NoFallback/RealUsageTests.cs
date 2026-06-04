using Xunit;

namespace SqlPartial.Integration.NoFallback
{
    public partial class RealUsageTests
    {
        [Fact]
        public void RealUsageTests_NoFallback_ShouldReturnEmptyForFallback()
        {
            // GetPartialProviders only has PG and MS versions
            var sql = SqlGetPartialProviders;

            Assert.Equal(string.Empty, sql.Fallback);
            Assert.Equal("SELECT 'Postgres Only' FROM System;", sql.PostgreSql);
            Assert.Equal("SELECT 'MS Only' FROM System;", sql.SqlServer);

            // Implicit conversion should return empty string if no fallback file exists
            string query = sql;
            Assert.Equal(string.Empty, query);

            // Get unknown provider also returns empty string
            Assert.Equal(string.Empty, sql.Get("Unknown"));
        }
    }
}
