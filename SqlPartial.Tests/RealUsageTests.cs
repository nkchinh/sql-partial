namespace SqlPartial.Tests
{
    public partial class RealUsageTests
    {
        [Fact]
        public void RealUsageTests_ShouldHaveGeneratedPrivateSqlProperty()
        {
            // This property is generated as 'private static readonly' 
            // by SqlPartial.Generator within this partial class.
            var sqlStrings = SqlGetStatus;

            Assert.Equal("SELECT 'Ansi Status' FROM System;", sqlStrings.AnsiSql);
            Assert.Equal("SELECT 'Postgres Status' FROM System;", sqlStrings.PostgreSql);

            // Test runtime selection
            Assert.Equal("SELECT 'Postgres Status' FROM System;", sqlStrings.Get("PostgreSql"));
            Assert.Equal("SELECT 'Ansi Status' FROM System;", sqlStrings.Get("Unknown"));

            // Test implicit conversion (returns AnsiSql)
            string sqlString = sqlStrings;
            Assert.Equal("SELECT 'Ansi Status' FROM System;", sqlString);
        }
    }
}
