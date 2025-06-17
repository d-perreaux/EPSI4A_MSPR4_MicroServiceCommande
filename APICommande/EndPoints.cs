
using MongoDB.Bson;
using MongoDB.Driver;
using RabbitMQ.Client.Exceptions;
using Serilog;
using System.Text.Json;
using APIOrder.Utilitaires;
using APIOrder.Model;
using APIOrder.Services.Mongo;
using APIOrder.Services.RabbitMQ;
using APIOrder.Services;
using Microsoft.AspNetCore.Mvc;

namespace APIOrder.Endpoints
{
    public static class OrderEndpoints{
        public static async Task<IResult> GetAllOrders([FromServices] IDataBaseService orderService)
        {
            AppMetrics.RequestCounter.Add(1, KeyValuePair.Create<string, object?>("endpoint", "get/v1/orders"));
            Log.Information("Ask for all orders");
            var orders = await orderService.GetOrdersAsync();
            var jsonApiOrders = orders
                .Select(o =>
                {
                    var dto = OrderDto.FromOrder(o);
                    return new JsonApiData<OrderDto>
                    {
                        Type = "order",
                        Id = dto.Id,
                        Attributes = dto
                    };
                })
                .ToList();

            var response = new { data = jsonApiOrders };
            return TypedResults.Ok(response);
        }

        public static async Task<IResult> GetOrderById([FromServices] IDataBaseService orderService, string id)
        {
            AppMetrics.RequestCounter.Add(1, KeyValuePair.Create<string, object?>("endpoint", "get/v1/orders/{id}"));
            if (!ObjectId.TryParse(id, out var objectId))
                return TypedResults.BadRequest("Invalid ObjectId format.");

            var order = await orderService.Orders.Find(o => o.Id == objectId).FirstOrDefaultAsync();

            if (order is null)
            {
                return TypedResults.NotFound();
            }

            var orderDto = OrderDto.FromOrder(order);

            return TypedResults.Ok(
                new JsonApiResponse<OrderDto>(
                    type: "order",
                    id: orderDto.Id,
                    attributes: orderDto
                )
            );
        }

        public static async Task<IResult> CreateOrder([FromServices] IDataBaseService orderService, OrderDto orderDto, RabbitMQPublisher publisher)
        {
            AppMetrics.RequestCounter.Add(1, KeyValuePair.Create<string, object?>("endpoint", "post/v1/orders/"));
            var order = orderDto.ToOrder();
            await orderService.Orders.InsertOneAsync(order);

            AppMetrics.ActiveOrders.Add(1);

            var createdDto = OrderDto.FromOrder(order);
            var messageObject = new
            {
                orderId = createdDto.Id,
                products = createdDto.Products
            };

            var message = JsonSerializer.Serialize(messageObject);
            Log.Information("Order posted : \"{Message}\"", message);

            try
            {
                Log.Debug("Send message = \"{MessagePayload}\" to the publisher rcp", message);
                var rcpResponse = await publisher.CallRpcAsync(message);
                Log.Information("{RcpResponse}", rcpResponse);

            }
            catch (BrokerUnreachableException ex)
            {
                Log.Error(ex, "Failled to reach the broker");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Sent order issue");
            }

            return TypedResults.Created(
                $"/api/v1/orders/{createdDto.Id}",
                new JsonApiResponse<OrderDto>(
                    type: "order",
                    id: createdDto.Id,
                    attributes: createdDto
                )
            );
        }

        public static async Task<IResult> UpdateOrder([FromServices] IDataBaseService orderService, string id, OrderDto orderDto)
        {
            AppMetrics.RequestCounter.Add(1, KeyValuePair.Create<string, object?>("endpoint", "put/v1/orders/{id}"));
            if (!ObjectId.TryParse(id, out var objectId))
                return TypedResults.BadRequest("Invalid ObjectId format.");

            var order = orderDto.ToOrder();
            order.Id = objectId;
            await orderService.Orders.ReplaceOneAsync(o => o.Id == objectId, order);

            return TypedResults.Ok(
                new JsonApiResponse<OrderDto>(
                    type: "order",
                    id: orderDto.Id,
                    attributes: orderDto
                )
            );
        }

        public static async Task<IResult> DeleteOrder([FromServices] IDataBaseService orderService, string id)
        {
            AppMetrics.RequestCounter.Add(1, KeyValuePair.Create<string, object?>("endpoint", "delete/v1/orders/{id}"));
            if (!ObjectId.TryParse(id, out var objectId))
                return TypedResults.BadRequest("Invalid ObjectId format.");

            var filter = Builders<Order>.Filter.Eq(o => o.Id, objectId);
            var result = await orderService.Orders.DeleteOneAsync(filter);

            AppMetrics.ActiveOrders.Add(-1);

            return result.DeletedCount > 0 ? TypedResults.NoContent() : TypedResults.NotFound();
        }
    }
}