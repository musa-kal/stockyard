using Stockyard.Api;
using Stockyard.Api.Errors;
using Stockyard.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStockyardDatabase(builder.Configuration);
builder.Services.AddStockyardHealthChecks(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<StockyardExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthEndpoints();

await app.RunAsync();
