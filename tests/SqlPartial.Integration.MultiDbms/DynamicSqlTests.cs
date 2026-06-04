using Xunit;

namespace SqlPartial.Integration.MultiDbms
{
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
            var staticSql = new SqlStrings(postgresql: "SELECT PG", fallback: "SELECT FALLBACK");
            Assert.Equal("SELECT PG", Execute(staticSql, "PostgreSql"));
            Assert.Equal("SELECT FALLBACK", Execute(staticSql, "Unknown"));

            // 2. Ad-hoc static SQL via implicit conversion
            SqlStrings adhocStatic = "SELECT ADHOC";
            Assert.Equal("SELECT ADHOC", Execute(adhocStatic, "PostgreSql"));

            // 3. Dynamic SQL via factories
            var dynamicSql = new SqlDynamic(
                postgresql: () => "SELECT DYNAMIC PG",
                fallback: () => "SELECT DYNAMIC FALLBACK"
            );
            Assert.Equal("SELECT DYNAMIC PG", Execute(dynamicSql, "PostgreSql"));
            Assert.Equal("SELECT DYNAMIC FALLBACK", Execute(dynamicSql, "SqlServer")); // Fallback
        }

        [Fact]
        public void SqlDynamic_PureFallback_ShouldWork()
        {
            var dynamicSql = new SqlDynamic("SELECT PURE FALLBACK");
            Assert.Equal("SELECT PURE FALLBACK", dynamicSql.PostgreSql);
            Assert.Equal("SELECT PURE FALLBACK", dynamicSql.Fallback);
        }
    }
}
