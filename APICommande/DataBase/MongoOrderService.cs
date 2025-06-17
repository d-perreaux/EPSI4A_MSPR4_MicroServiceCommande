using MongoDB.Driver;
using APIOrder.Model;

namespace APIOrder.Services.Mongo
{
    public class MongoOrderService : IDataBaseService
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

        public async Task<List<Order>> GetOrdersAsync()
        {
            return await _ordersCollection.Find(_ => true).ToListAsync();
        }
    }
}