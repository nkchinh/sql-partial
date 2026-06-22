using Xunit;

namespace SqlPartial.Integration.MultiDbms;

public class DynamicSqlTests
{
    // Simulated generic repository method
    private static string Execute<TSql>(TSql query, string provider) where TSql : ISqlString
    {
        return query.Get(provider);
    }

    [Fact]
    public void GenericMethod_ShouldWorkWithAllSqlTypes()
    {
        // 1. Static SQL (from generator - simulated here by manual ctor for convenience)
        var staticSql = new SqlStrings(postgresql: "SELECT PG", @default: "SELECT FALLBACK");
        Assert.Equal("SELECT PG", Execute(staticSql, "PostgreSql"));
        Assert.Equal("SELECT FALLBACK", Execute(staticSql, "Unknown"));

        // 2. Ad-hoc static SQL
        var adhocStatic = new SqlStrings("SELECT ADHOC");
        Assert.Equal("SELECT ADHOC", Execute(adhocStatic, "PostgreSql"));

        // 3. Dynamic SQL via factories
        var dynamicSql = new SqlDynamic(
            postgresql: () => "SELECT DYNAMIC PG",
            @default: () => "SELECT DYNAMIC FALLBACK"
        );
        Assert.Equal("SELECT DYNAMIC PG", Execute(dynamicSql, "PostgreSql"));
        Assert.Equal("SELECT DYNAMIC FALLBACK", Execute(dynamicSql, "sqlServer")); // Default
    }

    [Fact]
    public void SqlDynamic_PureFallback_ShouldWork()
    {
        var dynamicSql = new SqlDynamic(@default: "SELECT PURE FALLBACK");
        Assert.Equal("SELECT PURE FALLBACK", dynamicSql.PostgreSql);
        Assert.Equal("SELECT PURE FALLBACK", dynamicSql.Default);
    }

    [Fact]
    public void SqlStringBuilder_ShouldDeferProviderResolutionToBuiltTime()
    {
        var pgSql = new SqlStrings(postgresql: "SELECT PG", @default: "SELECT FALLBACK");
        var msSql = new SqlStrings(sqlserver: "SELECT MS", @default: "SELECT FALLBACK");

        var builder = new SqlStringBuilder()
            .Append(pgSql)
            .Append(" WHERE ")
            .Append(msSql);

        // Same builder, different providers resolved at Build() time
        Assert.Equal("SELECT PG WHERE SELECT FALLBACK", builder.Build("PostgreSql"));
        Assert.Equal("SELECT FALLBACK WHERE SELECT MS", builder.Build("sqlServer"));
        Assert.Equal("SELECT FALLBACK WHERE SELECT FALLBACK", builder.Build("Unknown"));
    }

    [Fact]
    public void SqlStringBuilder_AppendLine_ShouldAddNewline()
    {
        var sql = new SqlStrings(postgresql: "SELECT PG", @default: "SELECT DEFAULT");

        var result = new SqlStringBuilder()
            .AppendLine(sql)
            .Append("ORDER BY Id")
            .Build("PostgreSql");

        Assert.Equal("SELECT PG" + System.Environment.NewLine + "ORDER BY Id", result);
    }

    [Fact]
    public void SqlStringBuilder_Clear_ShouldResetState()
    {
        var builder = new SqlStringBuilder()
            .Append(new SqlStrings(@default: "SELECT 1"));

        Assert.Equal(1, builder.Count);
        builder.Clear();
        Assert.Equal(0, builder.Count);
        Assert.Equal(string.Empty, builder.Build("PostgreSql"));
    }

    [Fact]
    public void SqlStringBuilder_Empty_ShouldReturnEmpty()
    {
        var builder = new SqlStringBuilder();
        Assert.Equal(string.Empty, builder.Build("PostgreSql"));
        Assert.Equal(string.Empty, builder.ToString());
    }

    [Fact]
    public void SqlStringBuilder_ToString_ShouldUseFallbackProvider()
    {
        var builder = new SqlStringBuilder()
            .Append(new SqlStrings(postgresql: "SELECT PG", @default: "SELECT DEFAULT"));

        // ToString() resolves with empty provider name → falls back to Default
        Assert.Equal("SELECT DEFAULT", builder.ToString());
    }

    [Fact]
    public void SqlStringBuilder_CapacityGrowth_ShouldHandleManySegments()
    {
        var builder = new SqlStringBuilder(1); // Start with capacity 1
        for (int i = 0; i < 20; i++)
            builder.Append(new SqlStrings(@default: i.ToString()));

        Assert.Equal(20, builder.Count);
        Assert.Equal(string.Concat(System.Linq.Enumerable.Range(0, 20)), builder.Build("Unknown"));
    }

    [Fact]
    public void SqlStringBuilder_ShouldAcceptDynamicSql()
    {
        int callCount = 0;
        var dynamic = new SqlDynamic(
            postgresql: () => { callCount++; return "SELECT PG"; },
            @default: () => "SELECT DEFAULT");

        var builder = new SqlStringBuilder().Append(dynamic);

        // Factory is called only at Build() time
        Assert.Equal(0, callCount);
        Assert.Equal("SELECT PG", builder.Build("PostgreSql"));
        Assert.Equal(1, callCount);
    }
}
