namespace OmniCommerce.Api.Infrastructure;

public static class DependencyInjection
{
    // The single DbContext is registered here in a later stage.
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration) => services;
}
