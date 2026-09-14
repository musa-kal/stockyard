using Stockyard.Infrastructure;
using Stockyard.Messaging;
using Stockyard.Projections;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStockyardDatabase(builder.Configuration);
builder.Services.AddStockyardMessaging(builder.Configuration);
builder.Services.AddMessageConsumer<StockProjectionHandler>();

await builder.Build().RunAsync();
