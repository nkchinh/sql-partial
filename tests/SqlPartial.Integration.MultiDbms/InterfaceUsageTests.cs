using Xunit;

namespace SqlPartial.Integration.MultiDbms;

public interface ISqlRepository
{
    string SqlProviderName { get; }
    string Query([Sql] string sql);
}

public static partial class RepositoryExtensions
{
    public static string QueryExt(this ISqlRepository repo, [Sql] string sql) => sql;
}

public class SqlRepositoryImpl : ISqlRepository
{
    public string SqlProviderName { get; set; } = "PostgreSql";
    public string Query(string sql) => sql;
}

public class InterfaceUsageTests
{
    [Fact]
    public void InterfaceMethod_Extension_ShouldResolveCorrectSql()
    {
        var repo = new SqlRepositoryImpl { SqlProviderName = "sqlServer" };
        var sql = new SqlStrings(postgresql: "PG", sqlserver: "MS", @default: "DEF");

        // Call the generated extension method for the interface
        // We expect MS because SqlProviderName is "SqlServer"
        string result = repo.Query(sql);
        Assert.Equal("MS", result);

        repo.SqlProviderName = "PostgreSql";
        result = repo.Query(sql);
        Assert.Equal("PG", result);

        repo.SqlProviderName = "Unknown";
        result = repo.Query(sql);
        Assert.Equal("DEF", result);
    }

    [Fact]
    public void StaticExtension_Extension_ShouldResolveCorrectSql()
    {
        var repo = new SqlRepositoryImpl { SqlProviderName = "sqlServer" };
        var sql = new SqlStrings(postgresql: "PG", sqlserver: "MS", @default: "DEF");

        // Call the generated extension method for the static extension
        string result = repo.QueryExt(sql);
        Assert.Equal("MS", result);

        repo.SqlProviderName = "PostgreSql";
        result = repo.QueryExt(sql);
        Assert.Equal("PG", result);
    }

    [Fact]
    public void BuilderOverload_ShouldResolveAtCallTime()
    {
        var repo = new SqlRepositoryImpl();
        var builder = new SqlStringBuilder()
            .Append(new SqlStrings(postgresql: "PG", sqlserver: "MS", @default: "DEF"))
            .Append(" LIMIT 10");

        repo.SqlProviderName = "PostgreSql";
        Assert.Equal("PG LIMIT 10", repo.Query(builder));

        repo.SqlProviderName = "sqlServer";
        Assert.Equal("MS LIMIT 10", repo.Query(builder));

        repo.SqlProviderName = "Unknown";
        Assert.Equal("DEF LIMIT 10", repo.Query(builder));
    }

    [Fact]
    public void BuilderOverload_Interface_ShouldResolveAtCallTime()
    {
        var repo = new SqlRepositoryImpl();
        var builder = new SqlStringBuilder()
            .Append(new SqlStrings(postgresql: "PG-EXT", @default: "DEF-EXT"));

        repo.SqlProviderName = "PostgreSql";
        Assert.Equal("PG-EXT", repo.QueryExt(builder));

        repo.SqlProviderName = "Unknown";
        Assert.Equal("DEF-EXT", repo.QueryExt(builder));
    }
}
