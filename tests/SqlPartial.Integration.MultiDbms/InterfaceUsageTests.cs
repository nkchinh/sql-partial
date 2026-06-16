using Xunit;
using SqlPartial;

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
        var repo = new SqlRepositoryImpl { SqlProviderName = "SqlServer" };
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
        var repo = new SqlRepositoryImpl { SqlProviderName = "SqlServer" };
        var sql = new SqlStrings(postgresql: "PG", sqlserver: "MS", @default: "DEF");

        // Call the generated extension method for the static extension
        string result = repo.QueryExt(sql);
        Assert.Equal("MS", result);

        repo.SqlProviderName = "PostgreSql";
        result = repo.QueryExt(sql);
        Assert.Equal("PG", result);
    }
}
