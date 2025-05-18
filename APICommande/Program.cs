
using MongoDB.Bson;
using MongoDB.Driver;
using RabbitMQ.Client.Exceptions;
using Serilog;
using Serilog.Debugging;
using Microsoft.Extensions.Configuration;

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
    string rabbitMqHost = builder.Configuration.GetSection("RabbitMQ:Host").Value!;

    builder.Services.AddDatabaseDeveloperPageExceptionFilter();
    builder.Services.AddSingleton<MongoOrderService>();
    builder.Services.AddSingleton(sp => new RabbitMQPublisher(rabbitMqHost));
    builder.Services.AddHostedService<RabbitMQBackgroundService>();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddOpenApiDocument(config =>
    {
        config.DocumentName = "APIOrder";
        config.Title = "API Order v0.3";
        config.Version = "v0.3";
    });

    var app = builder.Build();

    var orderService = app.Services.GetRequiredService<MongoOrderService>();
    // if (app.Environment.IsDevelopment())
    // {
    app.UseOpenApi();
    app.UseSwaggerUi(config =>
    {
        config.DocumentTitle = "APIOrder";
        config.Path = "/swagger";
        config.DocumentPath = "/swagger/{documentName}/swagger.json";
        config.DocExpansion = "list";
    });
    // }

    app.MapGet("/", () => "Gut");

    app.MapPost("/send", async (RabbitMQPublisher publisher) =>
    {
        var message = "Achat de 5 bananes";
        Log.Information($"Order posted : {message}");

        try
        {
            Log.Debug($"Send message = {message} to the publisher");
            await publisher.SendMessageAsync("Commande", message);
            return Results.Ok($"Message envoyé : Achat de 5 bananes");
        }
        catch (BrokerUnreachableException ex)
        {
            Log.Error("Failled to reach the broker", ex);
            return Results.StatusCode(503);
        }
        catch (Exception ex)
        {
            Log.Error("Sent order issue", ex);
            return Results.StatusCode(500);
        }
    });

    var client = new HttpClient();

    string todoHost = builder.Configuration.GetSection("Services:TestTodo").Value!;
    app.MapGet("/todo", async () =>
    {
        var response = await client.GetStringAsync($"http://{todoHost}/todoitems");
        return TypedResults.Ok(response);
    });

    string productHost = builder.Configuration.GetSection("Services:Produit").Value!;
    app.MapGet("/produits", async () =>
    {
        var response = await client.GetStringAsync($"http://{productHost}/products");
        return TypedResults.Ok(response);
    });

    var orders = app.MapGroup("/orders");

    orders.MapGet("/", GetAllOrders);
    orders.MapGet("/complete", GetCompletedOrders);
    orders.MapGet("/{id}", GetOrderById);
    orders.MapPost("/", CreateOrder);
    orders.MapPut("/{id}", UpdateOrder);
    orders.MapDelete("/{id}", DeleteOrder);


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

static async Task<IResult> GetAllOrders(MongoOrderService orderService)
{
    Log.Information("Ask for all orders");
    var orders = await orderService.Orders.Find(_ => true).ToListAsync();
    var ordersDto = new List<OrderDto>();
    foreach(var order in orders){
        ordersDto.Add(OrderDto.FromOrder(order));
    }
    return TypedResults.Ok(ordersDto);
}

static async Task<IResult> GetCompletedOrders(MongoOrderService orderService)
{
    return TypedResults.Ok(await orderService.Orders.Find(o => o.Status == "completed").ToListAsync());
}

static async Task<IResult> GetOrderById(MongoOrderService orderService, string id)
{
    if (!ObjectId.TryParse(id, out var objectId))
        return TypedResults.BadRequest("Invalid ObjectId format.");

    var order = await orderService.Orders.Find(o => o.Id == objectId).FirstOrDefaultAsync();
    return order is not null ? TypedResults.Ok(OrderDto.FromOrder(order)) : TypedResults.NotFound();
}

static async Task<IResult> CreateOrder(MongoOrderService orderService, OrderDto orderDto)
{
    var order = orderDto.ToOrder();
    await orderService.Orders.InsertOneAsync(order);
    return TypedResults.Created($"/orders/{order.Id}", OrderDto.FromOrder(order));
}

static async Task<IResult> UpdateOrder(MongoOrderService orderService, string id, OrderDto orderDto)
{
    if (!ObjectId.TryParse(id, out var objectId))
        return TypedResults.BadRequest("Invalid ObjectId format.");

    var order = orderDto.ToOrder();
    order.Id = objectId;
    var result = await orderService.Orders.ReplaceOneAsync(o => o.Id == objectId, order);
    return result.ModifiedCount > 0 ? TypedResults.NoContent() : TypedResults.NotFound();
}

static async Task<IResult> DeleteOrder(MongoOrderService orderService, string id)
{
    if (!ObjectId.TryParse(id, out var objectId))
        return TypedResults.BadRequest("Invalid ObjectId format.");

    var filter = Builders<Order>.Filter.Eq(o => o.Id, objectId);
    var result = await orderService.Orders.DeleteOneAsync(filter);
    return result.DeletedCount > 0 ? TypedResults.NoContent() : TypedResults.NotFound();
}

// var factory = new ConnectionFactory { HostName = "localhost" };
// using var connection = await factory.CreateConnectionAsync();
// using var channel = await connection.CreateChannelAsync();

// await channel.QueueDeclareAsync(queue: "hello", durable: false, exclusive: false, autoDelete: false,
//     arguments: null);

// Console.WriteLine(" [*] Waiting for messages.");

// var consumer = new AsyncEventingBasicConsumer(channel);
// consumer.ReceivedAsync += (model, ea) =>
// {
//     var body = ea.Body.ToArray();
//     var message = Encoding.UTF8.GetString(body);
//     Console.WriteLine($" [x] Received {message}");
//     return Task.CompletedTask;
// };

// await channel.BasicConsumeAsync("hello", autoAck: true, consumer: consumer);

// Console.WriteLine(" Press [enter] to exit.");
// Console.ReadLine();
