using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OmniCommerce.Api.Infrastructure;

namespace OmniCommerce.Tests.Infrastructure;

public static class SqlServerTestConfig
{
    // SQL Server connection string WITHOUT a Database= entry; each fixture creates its own throwaway database.
    public const string ConnectionVariable = "OMNICOMMERCE_TEST_SQLSERVER";

    // When "1", the SQL Server tests never skip: missing or unreachable configuration makes them FAIL.
    public const string RequireVariable = "OMNICOMMERCE_REQUIRE_SQLSERVER_TESTS";

    public static string? BaseConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionVariable) is { Length: > 0 } value ? value : null;

    public static bool IsRequired => Environment.GetEnvironmentVariable(RequireVariable) == "1";

    public static string? SkipReason =>
        BaseConnectionString is not null || IsRequired
            ? null
            : $"INCOMPLETE VERIFICATION: SQL Server-backed tests were skipped because {ConnectionVariable} is not set. " +
              "Run scripts/verify-docker.ps1 for the full database-backed run.";
}

// EF InMemory/SQLite cannot prove rowversion, transaction or locking behavior, so SQL Server tests
// are skipped (loudly) when no server is configured instead of being faked.
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute() => Skip = SqlServerTestConfig.SkipReason;
}

public sealed class SqlServerTheoryAttribute : TheoryAttribute
{
    public SqlServerTheoryAttribute() => Skip = SqlServerTestConfig.SkipReason;
}

public sealed class CartApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("ConnectionStrings:CommerceDb", connectionString);
}

// Creates a uniquely named database through the real migrations, drops it afterwards, and hosts the
// real API pipeline on top of it. Existing databases are never touched.
public sealed class SqlServerFixture : IDisposable
{
    private readonly Lazy<CartApiFactory> _factory;

    public SqlServerFixture()
    {
        var baseConnection = SqlServerTestConfig.BaseConnectionString;
        if (baseConnection is null)
        {
            if (SqlServerTestConfig.IsRequired)
            {
                throw new InvalidOperationException(
                    $"{SqlServerTestConfig.RequireVariable}=1 but {SqlServerTestConfig.ConnectionVariable} is not set.");
            }

            ConnectionString = string.Empty;
            _factory = new(() => throw new InvalidOperationException("SQL Server is not configured."));
            return;
        }

        ConnectionString = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"OmniCommerce_Test_{Guid.NewGuid():N}",
        }.ConnectionString;
        _factory = new(() => new CartApiFactory(ConnectionString));

        using var context = CreateContext();
        context.Database.Migrate();
    }

    public string ConnectionString { get; }

    public CommerceDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CommerceDbContext>().UseSqlServer(ConnectionString).Options);

    public HttpClient CreateClient() => _factory.Value.CreateClient();

    public void Dispose()
    {
        if (_factory.IsValueCreated)
        {
            _factory.Value.Dispose();
        }

        if (ConnectionString.Length == 0)
        {
            return;
        }

        using var context = CreateContext();
        context.Database.EnsureDeleted();
    }
}
