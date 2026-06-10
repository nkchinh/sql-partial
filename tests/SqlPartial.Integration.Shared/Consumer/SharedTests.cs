using Xunit;
using SqlPartial.Shared;
using SqlPartial.Abstractions;

namespace SqlPartial.Integration.Shared.Consumer;

public partial class SharedRepo(string provider)
{
    public string SqlProviderName { get; } = provider;

    public string GetQuery([Sql] string query)
    {
        return query;
    }

    // Original method with generic parameter that CANNOT be inferred
    public T? Execute<T>([Sql] string query)
    {
        return default;
    }

    // Original method with generic parameter that CAN be inferred
    public T? Process<T>([Sql] string query, T input)
    {
        return input;
    }
}

public partial class SharedTests
{
    [Fact]
    public void ShouldUseSharedTypes()
    {
        Assert.Equal(string.Empty, SqlGetData.Default);
        Assert.IsType<SqlStrings>(SqlGetData);
    }

    [Fact]
    public void ShouldGetQuery_PostgreSql()
    {
        var repo = new SharedRepo("PostgreSql");
        var query = repo.GetQuery(SqlGetData);

        Assert.Equal("SELECT 'From .pg.sql'", query);
    }

    [Fact]
    public void ShouldGetQuery_SqlServer()
    {
        var repo = new SharedRepo("SqlServer");
        var query = repo.GetQuery(SqlGetData);

        Assert.Equal("SELECT 'From .ms.sql'", query);
    }

    [Fact]
    public void ShouldWorkWithGenericOverload_ExplicitTypes()
    {
        var repo = new SharedRepo("PostgreSql");

        // Since T cannot be inferred from arguments, we must provide both T and TSql explicitly.
        // This is a known limitation of C# partial generic type inference.
        var result = repo.Execute<string, SqlStrings>(SqlGetData);

        Assert.Null(result);
    }

    [Fact]
    public void ShouldWorkWithGenericOverload_InferredTypes()
    {
        var repo = new SharedRepo("PostgreSql");

        // Both T and TSql can be inferred from the arguments!
        var result = repo.Process(SqlGetData, "test string");

        Assert.Equal("test string", result);
    }
}
