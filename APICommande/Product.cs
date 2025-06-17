using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace APIOrder.Model
{
    [BsonIgnoreExtraElements]
    public class Product
    {
        [BsonElement("idProduct")]
        public string IdProduct { get; set; } = string.Empty;

        [BsonElement("name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("quantity")]
        public string Quantity { get; set; } = string.Empty;
    }
}