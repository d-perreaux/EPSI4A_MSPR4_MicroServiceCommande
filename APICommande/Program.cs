using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using MongoDB.Bson;
using MongoDB.Driver;
using RabbitMQ.Client.Exceptions;
using Serilog;
using Serilog.Debugging;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using APIOrder.Utilitaires;
using APIOrder.Model;
using APIOrder.Endpoints;
using APIOrder.Services.Mongo;
using APIOrder.Services.RabbitMQ;
using APIOrder.Services;

try
{
    Log.Information("Starting web application");

    var builder = WebApplication.CreateBuilder(args);
    DotNetEnv.Env.Load();

    builder.Configuration
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
        .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
        .AddEnvironmentVariables();

    SelfLog.Enable(Console.Error);

    Log.Logger = new LoggerConfiguration()
        .ReadFrom.Configuration(builder.Configuration)
        .CreateLogger();

    builder.Services.AddSerilog();
   

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("APIMetrics"))
        .WithMetrics(metrics =>
        {
            metrics.AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("APIMetrics.Meter");

            metrics.AddPrometheusExporter();
        });

    builder.Services.AddDatabaseDeveloperPageExceptionFilter();
    builder.Services.AddSingleton<IDataBaseService, MongoOrderService>();
     string rabbitMqHost = builder.Configuration.GetSection("RabbitMQ:Host").Value!;
    builder.Services.AddSingleton(sp => new RabbitMQPublisher(rabbitMqHost));
    builder.Services.AddHostedService<RabbitMQBackgroundService>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApiDocument(config =>
    {
        config.DocumentName = "APIOrder";
        config.Title = "API Order v0.4";
        config.Version = "v1";
    });

    var app = builder.Build();
    
    app.Use(async (context, next) =>
    {
        if (context.Request.Path != "/metrics")
        {
            Console.WriteLine($"Request: {context.Request.Path}");
        }
        await next();
    });
    
    var isDocker = builder.Configuration.GetValue<bool>("App:IsDocker");
    if (app.Environment.IsDevelopment() || isDocker)
    {
        app.UseOpenApi();
        app.UseSwaggerUi(config =>
        {
            config.DocumentTitle = "APIOrder";
            config.Path = "/swagger";
            config.DocumentPath = "/swagger/{documentName}/swagger.json";
            config.DocExpansion = "list";
        });
    }

    app.MapGet("/", () => "Gut");

    var orders = app.MapGroup("/api/v1/orders");
    orders.MapGet("/", OrderEndpoints.GetAllOrders);
    orders.MapGet("/{id}", OrderEndpoints.GetOrderById);
    orders.MapPost("/", OrderEndpoints.CreateOrder);
    orders.MapPut("/{id}", OrderEndpoints.UpdateOrder);
    orders.MapDelete("/{id}", OrderEndpoints.DeleteOrder);

    app.UseOpenTelemetryPrometheusScrapingEndpoint();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}