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
            var staticSql = new SqlStrings("SELECT ANSI", "SELECT PG");
            Assert.Equal("SELECT PG", Execute(staticSql, "PostgreSql"));
            Assert.Equal("SELECT ANSI", Execute(staticSql, "Unknown"));

            // 2. Ad-hoc static SQL via implicit conversion
            SqlStrings adhocStatic = "SELECT ADHOC";
            Assert.Equal("SELECT ADHOC", Execute(adhocStatic, "PostgreSql"));

            // 3. Dynamic SQL via factories
            var dynamicSql = new SqlDynamic(
                postgresql: () => "SELECT DYNAMIC PG",
                ansi: () => "SELECT DYNAMIC ANSI"
            );
            Assert.Equal("SELECT DYNAMIC PG", Execute(dynamicSql, "PostgreSql"));
            Assert.Equal("SELECT DYNAMIC ANSI", Execute(dynamicSql, "SqlServer")); // Fallback
        }

        [Fact]
        public void SqlDynamic_PureAnsi_ShouldWork()
        {
            var dynamicSql = new SqlDynamic("SELECT PURE ANSI");
            Assert.Equal("SELECT PURE ANSI", dynamicSql.PostgreSql);
            Assert.Equal("SELECT PURE ANSI", dynamicSql.AnsiSql);
        }
    }
}
