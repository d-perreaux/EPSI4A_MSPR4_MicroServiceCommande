using MongoDB.Driver;
using APIOrder.Model;

namespace APIOrder.Services
{
    public interface IDataBaseService
    {
        IMongoCollection<Order> Orders { get; }
        Task<List<Order>> GetOrdersAsync();
    }
}
