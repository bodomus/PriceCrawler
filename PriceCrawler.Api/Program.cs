using PriceCrawler.Api.Endpoints;
using PriceCrawler.Api.ErrorHandling;
using PriceCrawler.Api.Health;
using PriceCrawler.Api.Startup;
using PriceCrawler.Application.Features;
using PriceCrawler.Infrastructure.DependencyInjection;

using Scalar.AspNetCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.AddPriceCrawlerPersistence(builder.Configuration);
builder.Services.AddPriceCrawlerFeatures();

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddApiCors(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: [DatabaseHealthCheck.ReadyTag]);
builder.Services.AddHostedService<DatabaseSchemaStartupService>();

WebApplication app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(CorsSetup.PolicyName);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapApiHealthChecks();

RouteGroupBuilder api = app.MapGroup("/api/v1");
api.MapCategoryEndpoints();

await app.RunAsync();

/// <summary>Entry point of the Web API; public so integration tests can host it.</summary>
public partial class Program;
