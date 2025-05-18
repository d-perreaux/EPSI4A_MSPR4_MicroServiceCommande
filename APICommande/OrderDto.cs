using MongoDB.Bson;

public class OrderDto
{
    public string Id { get; set; } = string.Empty;
    public string IdUser { get; set; } = string.Empty;
    public long Timestamp { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<int> Products { get; set; } = new List<int>();

    public static OrderDto FromOrder(Order order){
        return new OrderDto
        {
            Id  = order.Id.ToString(),
            IdUser = order.IdUser,
            Timestamp = order.Timestamp,
            Status = order.Status,
            Products = order.Products
        };
    }

    public Order ToOrder()
    {
        return new Order
        {
            Id = ObjectId.TryParse(Id, out var objectId) ? objectId : ObjectId.GenerateNewId(),
            IdUser = IdUser,
            Timestamp = Timestamp,
            Status = Status,
            Products = Products
        };
    }
}