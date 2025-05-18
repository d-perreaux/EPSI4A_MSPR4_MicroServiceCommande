using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

// namespace API.Model {
    public class Order
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public ObjectId Id { get; set; }

        [BsonElement("id_user")]
        public string IdUser { get; set; } = string.Empty;

        [BsonRepresentation(BsonType.Int64)]
        [BsonElement("timestamp")]
        public long Timestamp { get; set; }

        [BsonElement("status")]
        public string Status { get; set; } = string.Empty;

        [BsonElement("products")]
        public List<int> Products { get; set; } = new List<int>();
    }
// }
