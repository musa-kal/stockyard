using System.Text.Json.Serialization;
using Scalar.AspNetCore;
using Stockyard.Api;
using Stockyard.Api.Endpoints;
using Stockyard.Api.Errors;
using Stockyard.Exporter;
using Stockyard.Infrastructure;
using Stockyard.Messaging;

if (args is [MigrateCommand.Name, ..])
{
    return await MigrateCommand.RunAsync(args[1..]);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStockyardDatabase(builder.Configuration);
builder.Services.AddStockyardMessaging(builder.Configuration);
builder.Services.AddOutboxRelay(builder.Configuration);
builder.Services.AddStockyardExports(builder.Configuration);
builder.Services.AddStockyardHealthChecks(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddExceptionHandler<StockyardExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapOpenApi();
if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference();
}

app.MapHealthEndpoints();

var api = app.MapGroup("/api/v1");
api.MapMasterDataEndpoints();
api.MapPurchaseOrderEndpoints();
api.MapStockEndpoints();
api.MapOperationsEndpoints();

await app.RunAsync();
return 0;
