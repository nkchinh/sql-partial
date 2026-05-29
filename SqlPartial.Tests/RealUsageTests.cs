namespace SqlPartial.Tests
{
    public partial class RealUsageTests
    {
        [Fact]
        public void RealUsageTests_MultiDbms_ShouldResolveCorrectSql()
        {
            var sql = SqlGetUsers;

            Assert.Equal("SELECT * FROM Users;", sql.AnsiSql);
            Assert.Equal("SELECT * FROM Users LIMIT $1;", sql.PostgreSql);
            Assert.Equal("SELECT TOP (@Count) * FROM Users;", sql.SqlServer);

            // Test runtime selection
            Assert.Equal("SELECT * FROM Users LIMIT $1;", sql.Get("PostgreSql"));
            Assert.Equal("SELECT TOP (@Count) * FROM Users;", sql.Get("SqlServer"));
            Assert.Equal("SELECT * FROM Users;", sql.Get("Unknown"));
        }

        [Fact]
        public void RealUsageTests_SingleDbms_ShouldImplicitlyConvertToAnsiSql()
        {
            // GetSettings only has ANSI version
            var sql = SqlGetSettings;

            Assert.Equal("SELECT Value FROM Settings WHERE Key = @Key;", sql.AnsiSql);
            Assert.Null(sql.PostgreSql);
            Assert.Null(sql.SqlServer);

            // Implicit conversion
            string query = sql;
            Assert.Equal("SELECT Value FROM Settings WHERE Key = @Key;", query);

            // Get unknown provider returns ANSI
            Assert.Equal("SELECT Value FROM Settings WHERE Key = @Key;", sql.Get("PostgreSql"));
        }

        [Fact]
        public void RealUsageTests_NoFallback_ShouldReturnEmptyForAnsi()
        {
            // GetPartialProviders only has PG and MS versions
            var sql = SqlGetPartialProviders;

            Assert.Equal(string.Empty, sql.AnsiSql);
            Assert.Equal("SELECT 'Postgres Only' FROM System;", sql.PostgreSql);
            Assert.Equal("SELECT 'MS Only' FROM System;", sql.SqlServer);

            // Implicit conversion should return empty string if no ANSI file exists
            string query = sql;
            Assert.Equal(string.Empty, query);

            // Get unknown provider also returns empty string
            Assert.Equal(string.Empty, sql.Get("Unknown"));
        }
    }
}
