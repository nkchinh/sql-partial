using Xunit;

namespace SqlPartial.Integration.DuplicateExtensions
{
    public partial class DuplicateTest
    {
        [Fact]
        public void DuplicateExtensions_ShouldDeduplicateProperties_AndIncludeOtherProviders()
        {
            // Verify GetInfo (using .pg.sql and .ms.sql)
            var info = SqlGetInfo;
            Assert.Equal("SELECT 'from .pg.sql' FROM Users;", info.PostgreSql);
            Assert.Equal("SELECT 'from .ms.sql' FROM System;", info.SqlServer);

            // Verify GetOther (using .pgsql)
            var other = SqlGetOther;
            Assert.Equal("SELECT 'from .pgsql' FROM Orders;", other.PostgreSql);
            Assert.Equal(string.Empty, other.SqlServer);

            // Runtime check - all should be accessible via Display Names
            Assert.Equal("SELECT 'from .pg.sql' FROM Users;", info.Get("PostgreSql"));
            Assert.Equal("SELECT 'from .ms.sql' FROM System;", info.Get("SqlServer"));
            Assert.Equal("SELECT 'from .pgsql' FROM Orders;", other.Get("PostgreSql"));
        }
    }
}
