using Microsoft.Extensions.Configuration;

var builder = DistributedApplication.CreateBuilder(args);

var api = builder.AddDotnetProject("api", "../SmartBowl.Api/SmartBowl.Api.csproj")
    .WithHttpHealthCheck("/health");

if (builder.Configuration.GetConnectionString("bowldb") is { Length: > 0 })
{
    api.WithReference(builder.AddConnectionString("bowldb"));
}
else
{
    var database = builder.AddPostgres("postgres")
        .WithImageTag("17.6")
        .WithDataVolume()
        .AddDatabase("bowldb");

    api.WithReference(database).WaitFor(database);
}

builder.AddViteApp("web", "../../web")
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithReference(api)
    .WithEnvironment("API_BASE_URL", api.GetEndpoint("http"))
    .WaitFor(api);

builder.Build().Run();
