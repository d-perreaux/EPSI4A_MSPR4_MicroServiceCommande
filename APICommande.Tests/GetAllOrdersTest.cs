using APIOrder.Services;
using APIOrder.Utilitaires;
using Xunit.Abstractions;

namespace APICommande.Tests
{
    public class OrderEndpointTests
    {
        private readonly ITestOutputHelper _output;
        public OrderEndpointTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task GetAllOrders_ReturnsListOfOrders_AsExpectedJson()
        {
            var sampleOrders = new List<Order>
            {
                new Order
                {
                    Id = MongoDB.Bson.ObjectId.GenerateNewId(),
                    Status = "pending",
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Address = "12 boulevard des tulipes",
                    Products = new List<Product>
                    {
                        new Product { IdProduct = "123456", Name = "Test1", Quantity = "1" }
                    }
                }
            };
            var mockService = new Mock<IDataBaseService>();
            mockService.Setup(s => s.GetOrdersAsync()).ReturnsAsync(sampleOrders);

            // Act : appelle ton endpoint
            var result = await OrderEndpoints.GetAllOrders(mockService.Object);

            // Assert : vérifie que tu obtiens bien ce que tu attends
            Assert.StartsWith("Microsoft.AspNetCore.Http.HttpResults.Ok`1", result.GetType().FullName!);

            var value = result.GetType().GetProperty("Value")?.GetValue(result);
            var data = value?.GetType().GetProperty("data")?.GetValue(value);
            _output.WriteLine($"data : {data}");

            var dataList = Assert.IsAssignableFrom<List<JsonApiData<OrderDto>>>(data);
            _output.WriteLine($"dataList : {dataList}");
            Assert.Single(dataList);
        }
    }
}