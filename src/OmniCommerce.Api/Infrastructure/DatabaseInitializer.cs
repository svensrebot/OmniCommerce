using Microsoft.EntityFrameworkCore;

namespace OmniCommerce.Api.Infrastructure;

public static class DatabaseInitializer
{
    // Applies pending migrations (which also insert the sample products). Safe to run on every start:
    // already-applied migrations are skipped. Intended for local development only.
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CommerceDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
