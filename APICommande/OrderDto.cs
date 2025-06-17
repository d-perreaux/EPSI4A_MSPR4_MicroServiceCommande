using MongoDB.Bson;

namespace APIOrder.Model
{
    public class OrderDto
    {
        public string Id { get; set; } = string.Empty;
        public string IdUser { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty; public string Status { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public List<ProductDto> Products { get; set; } = new List<ProductDto>();

        public static OrderDto FromOrder(Order order)
        {
            return new OrderDto
            {
                Id = order.Id.ToString(),
                IdUser = order.IdUser,
                Timestamp = order.Timestamp.ToString(),
                Status = order.Status,
                Address = order.Address,
                Products = order.Products.Select(p => new ProductDto
                {
                    IdProduct = p.IdProduct,
                    Name = p.Name,
                    Quantity = p.Quantity
                }).ToList()
            };
        }

        public Order ToOrder()
        {
            return new Order
            {
                Id = ObjectId.TryParse(Id, out var objectId) ? objectId : ObjectId.GenerateNewId(),
                IdUser = IdUser,
                Timestamp = long.TryParse(Timestamp, out var ts) ? ts : 0,
                Status = Status,
                Address = Address,
                Products = Products.Select(p => new Product
                {
                    IdProduct = p.IdProduct,
                    Name = p.Name,
                    Quantity = p.Quantity
                }).ToList()
            };
        }
    }
}