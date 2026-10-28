using Microsoft.EntityFrameworkCore;

namespace OmniCommerce.Api.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("CommerceDb")
            ?? throw new InvalidOperationException("Connection string 'CommerceDb' is not configured.");

        services.AddDbContext<CommerceDbContext>(options => options.UseSqlServer(connectionString));
        return services;
    }
}
