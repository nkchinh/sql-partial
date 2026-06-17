using Xunit;

namespace SqlPartial.Integration.DuplicateExtensions;

public partial class DuplicateTest
{
    // Manual property to cause collision with generated SqlGetInfo
    public static string SqlGetInfo = "Manual";

    [Fact]
    public void DuplicateExtensions_ShouldDeduplicateProperties_AndIncludeOtherProviders()
    {
        // Manual property should still be "Manual"
        Assert.Equal("Manual", SqlGetInfo);

        // Generated property should be renamed to SqlGetInfo1
        var info = SqlGetInfo1;
        Assert.Equal("SELECT 'from .pg.sql' FROM Users;", info.PostgreSql);
        Assert.Equal("SELECT 'from .ms.sql' FROM System;", info.SqlServer);

        // Verify GetOther (using .pgsql and .pg.sql collision)
        // Since .pg.sql is longer than .pgsql, it matches first.
        var other = SqlGetOther;
        Assert.Equal("SELECT 'collision' FROM Collision;", other.PostgreSql);
        Assert.Equal(string.Empty, other.SqlServer);

        // Runtime check - all should be accessible via Display Names
        Assert.Equal("SELECT 'from .pg.sql' FROM Users;", info.Get("PostgreSql"));
        Assert.Equal("SELECT 'from .ms.sql' FROM System;", info.Get("SqlServer"));
        Assert.Equal("SELECT 'collision' FROM Collision;", other.Get("PostgreSql"));
    }
}
