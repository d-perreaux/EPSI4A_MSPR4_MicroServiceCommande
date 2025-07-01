using MongoDB.Driver;
using APIOrder.Model;
using Serilog;
using APIOrder.Utilitaires;

namespace APIOrder.Services.Mongo
{
    public class MongoOrderService : IDataBaseService
    {
        private readonly IMongoCollection<Order> _ordersCollection;

        public MongoOrderService(IConfiguration configuration)
        {
            var mongoDbUrl = Environment.GetEnvironmentVariable("ORDER_MONGO_CONNECTION_STRING");
            if (string.IsNullOrEmpty(mongoDbUrl))
            {
                Log.Error("ORDER_MONGO_CONNECTION_STRING environment variable is not set.");
                throw new InvalidOperationException("ORDER_MONGO_CONNECTION_STRING environment variable is not set.");
            }
            try
            {
                var mongoClient = new MongoClient(mongoDbUrl);
                var mongoDatabase = mongoClient.GetDatabase("orders");
                _ordersCollection = mongoDatabase.GetCollection<Order>("orders");
            }
            catch (MongoConnectionException ex)
            {
                Log.Fatal(ex, "Failed to connect to MongoDB at startup.");
                AppMetrics.DatabaseErrorCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("operation", "MongoConnection"),
                    new KeyValuePair<string, object?>("error_type", ex.GetType().Name)
                );
                throw; // Rejeter l'erreur pour que l'application ne démarre pas sans DB
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "An unexpected error occurred during MongoDB initialization.");
                AppMetrics.DatabaseErrorCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("operation", "MongoInitialization"),
                    new KeyValuePair<string, object?>("error_type", ex.GetType().Name)
                );
                throw;
            }
        }
        

        public IMongoCollection<Order> Orders => _ordersCollection;

        public async Task<List<Order>> GetOrdersAsync()
        {
            try
            {
                return await _ordersCollection.Find(_ => true).ToListAsync();
            }
            catch (MongoException ex)
            {
                Log.Error(ex, "Error fetching all orders from MongoDb");
                AppMetrics.DatabaseErrorCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("operation", "GetOrderAsync"),
                    new KeyValuePair<string, object?>("error_type", ex.GetType().Name)
                );
                throw;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An unexpected error occurred while fetching orders from MongoDB.");
                AppMetrics.DatabaseErrorCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("operation", "GetOrdersAsync"),
                    new KeyValuePair<string, object?>("error_type", ex.GetType().Name)
                );
                throw;
            }
        }
    }
}