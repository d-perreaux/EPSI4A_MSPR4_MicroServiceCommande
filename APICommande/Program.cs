using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using Serilog;
using Serilog.Debugging;
using APIOrder.Endpoints;
using APIOrder.TestEndpoints;
using APIOrder.Services.Mongo;
using APIOrder.Services.RabbitMQ;
using APIOrder.Services;
using OpenTelemetry.Trace;
using OpenTelemetry.Exporter;
using OpenTelemetry.Context.Propagation;

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
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .CreateLogger();

    builder.Services.AddSerilog();
   
    var writeToSections = builder.Configuration.GetSection("Serilog:WriteTo").GetChildren();
    string? requestLogUri = writeToSections
        .FirstOrDefault(s => s.GetValue<string>("Name") == "Http")?
        .GetSection("Args")?
        .GetValue<string>("requestUri");

    var traceExporter = builder.Configuration.GetSection("Trace:Jaeger").Value!;

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService("APIMetrics"))
        .WithMetrics(metrics =>
        {
            metrics.AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("APIMetrics.Meter");

            metrics.AddPrometheusExporter();
        })
        .WithTracing(tracerProviderBuilder =>
        {
            tracerProviderBuilder
                .AddSource("MsOrder.ActivitySource")
                .SetResourceBuilder(
                    ResourceBuilder.CreateDefault()
                        .AddService(serviceName: "ms-order",
                                    serviceVersion: "0.5",
                                    serviceInstanceId: Environment.MachineName))
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Exclure les requêtes vers le chemin /metrics des traces
                    options.Filter = (httpContext) =>
                    {
                        return httpContext.Request.Path != "/metrics";
                    };
                })
                // Si MsOrder fait des appels HttpClient vers d'autres services/DB:
                .AddHttpClientInstrumentation(options =>
                {
                    // Règle pour exclure les requêtes HTTP SORTANTES vers l'endpoint du sink http Serilog
                    options.FilterHttpRequestMessage = (httpRequestMessage) =>
                    {
                        if (httpRequestMessage.RequestUri == null || string.IsNullOrEmpty(requestLogUri))
                        {
                            return true;
                        }
                        return !httpRequestMessage.RequestUri.ToString().StartsWith(requestLogUri, StringComparison.OrdinalIgnoreCase);
                    };
                })

                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(traceExporter);
                    options.Protocol = OtlpExportProtocol.Grpc;
                });
        });

    builder.Services.AddSingleton(Propagators.DefaultTextMapPropagator);

    builder.Services.AddDatabaseDeveloperPageExceptionFilter();
    builder.Services.AddSingleton<IDataBaseService, MongoOrderService>();

    string rabbitMqHost = builder.Configuration.GetSection("RabbitMQ:Host").Value!;
    
    builder.Services.AddSingleton(sp => {
        var propagator = sp.GetRequiredService<TextMapPropagator>();
        return new RabbitMQPublisher(rabbitMqHost,propagator);
    });
    builder.Services.AddHostedService<RabbitMQBackgroundService>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApiDocument(config =>
    {
        config.DocumentName = "APIOrder";
        config.Title = "API Order";
        config.Version = "v 0.5";
    });

    var app = builder.Build();

    app.Use(async (context, next) =>
    {
        var correlationId = context.TraceIdentifier;
        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            bool isMetricsEndpoint = context.Request.Path == "/metrics";

            if (!isMetricsEndpoint)
            {
                Log.Information("Incoming HTTP Request: {RequestMethod} {RequestPath}", context.Request.Method, context.Request.Path);
                APIOrder.Utilitaires.AppMetrics.RequestCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("method", context.Request.Method),
                    new KeyValuePair<string, object?>("path", context.Request.Path)
                );
            }

            await next();

            if (!isMetricsEndpoint)
            {
                Log.Information("Outgoing HTTP Response: {StatusCode} for {RequestMethod} {RequestPath}", context.Response.StatusCode, context.Request.Method, context.Request.Path);
                APIOrder.Utilitaires.AppMetrics.HttpResponseCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("status_code", context.Response.StatusCode),
                    new KeyValuePair<string, object?>("method", context.Request.Method),
                    new KeyValuePair<string, object?>("path", context.Request.Path)
                );
            }
        }
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

    var nameApi = "API Orders";
    var orders = app.MapGroup("/api/v1/orders");
    orders.MapGet("/", OrderEndpoints.GetAllOrders)
        .WithTags(nameApi);
    orders.MapGet("/{id}", OrderEndpoints.GetOrderById)
        .WithTags(nameApi);
    orders.MapPost("/", OrderEndpoints.CreateOrder)
        .WithTags(nameApi);
    orders.MapPut("/{id}", OrderEndpoints.UpdateOrder)
        .WithTags(nameApi);
    orders.MapDelete("/{id}", OrderEndpoints.DeleteOrder)
        .WithTags(nameApi);

    app.MapGet("/", () =>
    {
        return Results.Ok("Réponse du microservice Order");
    })
        .WithTags("Test");

    app.MapGet("/trigger-error/{errorType}", TestEndpoints.TriggerError).WithTags("Test");

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