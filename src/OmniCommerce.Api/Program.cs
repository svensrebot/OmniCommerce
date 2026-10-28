using OmniCommerce.Api.Application;
using OmniCommerce.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

// Local development only: Docker Compose sets Database:MigrateOnStartup=true.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Database:MigrateOnStartup is only allowed in the Development environment.");
    }

    await DatabaseInitializer.MigrateAsync(app.Services);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapControllers();

app.Run();

// Makes the entry point visible to WebApplicationFactory<Program> in the integration tests.
public partial class Program;
