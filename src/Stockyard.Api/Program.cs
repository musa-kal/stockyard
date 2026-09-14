using Stockyard.Api;
using Stockyard.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStockyardDatabase(builder.Configuration);
builder.Services.AddStockyardHealthChecks(builder.Configuration);

var app = builder.Build();

app.MapHealthEndpoints();

await app.RunAsync();
