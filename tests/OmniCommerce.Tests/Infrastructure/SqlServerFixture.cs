using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OmniCommerce.Api.Infrastructure;

namespace OmniCommerce.Tests.Infrastructure;

// Runs a test only when a real SQL Server is configured. EF InMemory/SQLite cannot prove rowversion,
// transaction or locking behavior, so these tests are skipped (visibly) rather than faked.
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "OMNICOMMERCE_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
        {
            Skip = $"Set {ConnectionVariable} to a SQL Server connection string (no Database=) to run SQL Server-backed tests.";
        }
    }
}

// Creates a uniquely named database through the real migrations and drops it afterwards.
public sealed class SqlServerFixture : IDisposable
{
    private readonly string? _connectionString;

    public SqlServerFixture()
    {
        var baseConnection = Environment.GetEnvironmentVariable(SqlServerFactAttribute.ConnectionVariable);
        if (string.IsNullOrWhiteSpace(baseConnection))
        {
            return;
        }

        var builder = new SqlConnectionStringBuilder(baseConnection)
        {
            InitialCatalog = $"OmniCommerce_Test_{Guid.NewGuid():N}",
        };
        _connectionString = builder.ConnectionString;

        using var context = CreateContext();
        context.Database.Migrate();
    }

    public CommerceDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CommerceDbContext>().UseSqlServer(_connectionString!).Options);

    public void Dispose()
    {
        if (_connectionString is null)
        {
            return;
        }

        using var context = CreateContext();
        context.Database.EnsureDeleted();
    }
}
