using System.Text.Json.Serialization;
using Stockyard.Api;
using Stockyard.Api.Endpoints;
using Stockyard.Api.Errors;
using Stockyard.Infrastructure;

if (args is [MigrateCommand.Name, ..])
{
    return await MigrateCommand.RunAsync(args[1..]);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStockyardDatabase(builder.Configuration);
builder.Services.AddStockyardHealthChecks(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<StockyardExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthEndpoints();

var api = app.MapGroup("/api/v1");
api.MapMasterDataEndpoints();
api.MapPurchaseOrderEndpoints();
api.MapStockEndpoints();

await app.RunAsync();
return 0;
