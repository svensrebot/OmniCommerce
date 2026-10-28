using System.Net;
using OmniCommerce.Tests.Infrastructure;

namespace OmniCommerce.Tests.Api;

public class HealthTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
{
    // Nothing listens on port 1, so any database access fails fast. No SQL Server is needed, so these always run.
    private const string UnreachableDatabase =
        "Server=127.0.0.1,1;Database=Unreachable;User Id=sa;Password=unused;Connect Timeout=2;TrustServerCertificate=True";

    [Fact]
    public async Task Live_StaysHealthy_WhenSqlServerIsUnreachable()
    {
        using var factory = new CartApiFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_IsUnavailable_WhenSqlServerIsUnreachable()
    {
        using var factory = new CartApiFactory(UnreachableDatabase);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [SqlServerFact]
    public async Task Ready_IsHealthy_WhenSqlServerIsReachable()
    {
        using var client = sql.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }
}
