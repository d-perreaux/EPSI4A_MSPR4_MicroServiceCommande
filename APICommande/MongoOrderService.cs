using MongoDB.Driver;

public class MongoOrderService
{
    private readonly IMongoCollection<Order> _ordersCollection;

    public MongoOrderService(IConfiguration configuration)
    {
        var mongoDbUrl = Environment.GetEnvironmentVariable("ORDER_MONGO_CONNECTION_STRING");
        var mongoClient = new MongoClient(mongoDbUrl);
        var mongoDatabase = mongoClient.GetDatabase("orders");
        _ordersCollection = mongoDatabase.GetCollection<Order>("orders");
    }

    public IMongoCollection<Order> Orders => _ordersCollection;
}