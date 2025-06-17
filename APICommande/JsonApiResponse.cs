namespace APIOrder.Utilitaires
{

    public class JsonApiResponse<T>
    {
        public JsonApiData<T> Data { get; set; }
        public JsonApiResponse(string type, string id, T attributes)
        {
            Data = new JsonApiData<T>
            {
                Type = type,
                Id = id,
                Attributes = attributes
            };
        }
    }

    public class JsonApiData<T>
    {
        public string Type { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public T? Attributes { get; set; }
    }
}