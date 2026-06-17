using Xunit;

namespace SqlPartial.Integration.MultiDbms;

[SqlPartial(AccessModifier.Internal)]
public partial class GenericTests
{
    public string SqlProviderName { get; set; } = "PostgreSql";

    // Generic method with multiple constraints
    public T? QueryWithConstraints<T, U>(U data, [Sql] string? sql = null)
        where T : class, new()
        where U : struct
    {
        // Simple mock implementation
        if (sql == "SELECT 'pg' FROM Users;")
        {
            return new T();
        }

        return null;
    }

    [Fact]
    public void GenericOverload_ShouldWorkWithConstraints()
    {
        var repo = new GenericTests();

        // This call uses the generated overload:
        // public T QueryWithConstraints<T, U>(U data, SqlStrings sql = default) ...
        var result = repo.QueryWithConstraints<List<string>, int>(123, SqlQueryWithConstraints);

        Assert.NotNull(result);
        Assert.IsType<List<string>>(result);
    }
}
