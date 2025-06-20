
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
using Sprache;

namespace APIOrder.Endpoints
{
    public static class OrderEndpoints{
        /// <summary>
        /// Return all orders.
        /// </summary>
        /// <response code="200">A list of all orders, wrapped in a data object following JSON:API specification</response>
        public static async Task<IResult> GetAllOrders([FromServices] IDataBaseService orderService)
        {
            var startTime = ValueStopwatch.StartNew();
            AppMetrics.RequestCounter.Add(1, KeyValuePair.Create<string, object?>("endpoint", "get/v1/orders"));
            try
            {
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
                var result = TypedResults.Ok(response);
                AppMetrics.HttpResponseCounter.Add(1,
                    new KeyValuePair<string, object?>("endpoint", "get/v1/orders"),
                    new KeyValuePair<string, object?>("status_code", result.StatusCode.ToString()));
                return result;
            }
            catch (Exception ex)
            {
                AppMetrics.HttpResponseCounter.Add(1,
                    new KeyValuePair<string, object?>("endpoint", "get/v1/orders"),
                    new KeyValuePair<string, object?>("status_code", "500"));
                Log.Error(ex, "Error getting all orders");
                throw;
            }
            finally
            {
                AppMetrics.ResponseTimes.Record(startTime.GetElapsedTime().TotalMilliseconds,
                    KeyValuePair.Create<string, object?>("endpoint", "get/v1/orders"));
            }

        }

        /// <summary>
        /// Get an order by his id.
        /// </summary>
        /// <response code="200">Return the order, wrapped in a data object following JSON:API specification</response>
        public static async Task<IResult> GetOrderById([FromServices] IDataBaseService orderService, string id)
        {
            var startTime = ValueStopwatch.StartNew();
            AppMetrics.RequestCounter.Add(1, KeyValuePair.Create<string, object?>("endpoint", "get/v1/orders/{id}"));
            try
            {
                if (!ObjectId.TryParse(id, out var objectId))
                {
                    return TypedResults.BadRequest("Invalid ObjectId format.");
                }

                var order = await orderService.Orders.Find(o => o.Id == objectId).FirstOrDefaultAsync();

                if (order is null)
                {
                    return TypedResults.NotFound();
                }

                var orderDto = OrderDto.FromOrder(order);

                var result = TypedResults.Ok(
                    new JsonApiResponse<OrderDto>(
                        type: "order",
                        id: orderDto.Id,
                        attributes: orderDto
                    )
                );
                AppMetrics.HttpResponseCounter.Add(1,
                    new KeyValuePair<string, object?>("endpoint", "get/v1/orders/{id}"),
                    new KeyValuePair<string, object?>("status_code", result.StatusCode.ToString()));
                return result;
            }
            catch (Exception ex)
            {
                AppMetrics.HttpResponseCounter.Add(1,
                    new KeyValuePair<string, object?>("endpoint", "get/v1/orders/{id}"),
                    new KeyValuePair<string, object?>("status_code", "500"));
                Log.Error(ex, "Error getting an order");
                throw;
            }
            finally
            {
                AppMetrics.ResponseTimes.Record(startTime.GetElapsedTime().TotalMilliseconds,
                    KeyValuePair.Create<string, object?>("endpoint", "get/v1/orders/{id}"));
            }
            
        }

        /// <summary>
        /// Create an order.
        /// </summary>
        /// <response code="201">Return the created order, wrapped in a data object following JSON:API specification</response>
        public static async Task<IResult> CreateOrder([FromServices] IDataBaseService orderService, OrderDto orderDto, RabbitMQPublisher publisher)
        {
            var startTime = ValueStopwatch.StartNew();
            AppMetrics.RequestCounter.Add(1, KeyValuePair.Create<string, object?>("endpoint", "post/v1/orders/"));
            try
            {
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

                var result = TypedResults.Created(
                    $"/api/v1/orders/{createdDto.Id}",
                    new JsonApiResponse<OrderDto>(
                        type: "order",
                        id: createdDto.Id,
                        attributes: createdDto
                    )
                );
                AppMetrics.HttpResponseCounter.Add(1,
                    new KeyValuePair<string, object?>("endpoint", "post/v1/orders/"),
                    new KeyValuePair<string, object?>("status_code", result.StatusCode.ToString()));
                return result;
            }
            catch (Exception ex)
            {
                AppMetrics.HttpResponseCounter.Add(1,
                    new KeyValuePair<string, object?>("endpoint", "post/v1/orders/"),
                    new KeyValuePair<string, object?>("status_code", "500"));
                Log.Error(ex, "Error getting an order");
                throw;
            }
            finally
            {
                AppMetrics.ResponseTimes.Record(startTime.GetElapsedTime().TotalMilliseconds,
                    KeyValuePair.Create<string, object?>("endpoint", "post/v1/orders/"));
            }
            
        }
        /// <summary>
        /// Update an order by his id.
        /// </summary>
        /// <response code="200">Return the updated order, wrapped in a data object following JSON:API specification</response>
        public static async Task<IResult> UpdateOrder([FromServices] IDataBaseService orderService, string id, OrderDto orderDto)
        {
            var startTime = ValueStopwatch.StartNew();
            AppMetrics.RequestCounter.Add(1, KeyValuePair.Create<string, object?>("endpoint", "put/v1/orders/{id}"));
            if (!ObjectId.TryParse(id, out var objectId))
                return TypedResults.BadRequest("Invalid ObjectId format.");

            var order = orderDto.ToOrder();
            order.Id = objectId;
            await orderService.Orders.ReplaceOneAsync(o => o.Id == objectId, order);

            var result = TypedResults.Ok(
                new JsonApiResponse<OrderDto>(
                    type: "order",
                    id: orderDto.Id,
                    attributes: orderDto
                )
            );
            AppMetrics.HttpResponseCounter.Add(1,
                    new KeyValuePair<string, object?>("endpoint", "put/v1/orders/{id}"),
                    new KeyValuePair<string, object?>("status_code", result.StatusCode.ToString()));
            return result;
        }

        /// <summary>
        /// Delete an order by his id.
        /// </summary>
        /// <response code="204">No body in the return</response>
        public static async Task<IResult> DeleteOrder([FromServices] IDataBaseService orderService, string id)
        {
            var startTime = ValueStopwatch.StartNew();
            AppMetrics.RequestCounter.Add(1, KeyValuePair.Create<string, object?>("endpoint", "delete/v1/orders/{id}"));
            
            try
            {
                if (!ObjectId.TryParse(id, out var objectId))
                {
                    Log.Warning("Attempted to delete order with invalid ObjectId format: {Id}", id);
                    var badRequestResult = TypedResults.BadRequest("Invalid ObjectId format.");
                    AppMetrics.HttpResponseCounter.Add(1,
                        new KeyValuePair<string, object?>("endpoint", "delete/v1/orders/{id}"),
                        new KeyValuePair<string, object?>("status_code", badRequestResult.StatusCode.ToString()));
                    return badRequestResult;
                }

                var filter = Builders<Order>.Filter.Eq(o => o.Id, objectId);
                var deleteResult = await orderService.Orders.DeleteOneAsync(filter);

                if (deleteResult.DeletedCount > 0)
                {
                    AppMetrics.ActiveOrders.Add(-1);
                    Log.Information("Order with ID {OrderId} deleted successfully.", id);
                    var noContentResult = TypedResults.NoContent();
                    AppMetrics.HttpResponseCounter.Add(1,
                        new KeyValuePair<string, object?>("endpoint", "delete/v1/orders/{id}"),
                        new KeyValuePair<string, object?>("status_code", noContentResult.StatusCode.ToString()));
                    return noContentResult;
                }
                else
                {
                    Log.Warning("Attempted to delete order with ID {OrderId} but it was not found.", id);
                    var notFoundResult = TypedResults.NotFound();
                    AppMetrics.HttpResponseCounter.Add(1,
                        new KeyValuePair<string, object?>("endpoint", "delete/v1/orders/{id}"),
                        new KeyValuePair<string, object?>("status_code", notFoundResult.StatusCode.ToString()));
                    return notFoundResult;
                }
            }
            catch (MongoException ex)
            {
                Log.Error(ex, "MongoDB error while deleting order with ID {OrderId}", id);
                var internalServerErrorResult = TypedResults.StatusCode(500);
                AppMetrics.HttpResponseCounter.Add(1,
                    new KeyValuePair<string, object?>("endpoint", "delete/v1/orders/{id}"),
                    new KeyValuePair<string, object?>("status_code", internalServerErrorResult.StatusCode.ToString()));
                return internalServerErrorResult;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An unexpected error occurred while deleting order with ID {OrderId}", id);
                var internalServerErrorResult = TypedResults.StatusCode(500); // Internal Server Error
                AppMetrics.HttpResponseCounter.Add(1,
                    new KeyValuePair<string, object?>("endpoint", "delete/v1/orders/{id}"),
                    new KeyValuePair<string, object?>("status_code", internalServerErrorResult.StatusCode.ToString()));
                return internalServerErrorResult;
            }
            finally
            {
                AppMetrics.ResponseTimes.Record(startTime.GetElapsedTime().TotalMilliseconds,
                    KeyValuePair.Create<string, object?>("endpoint", "delete/v1/orders/{id}"));
            }
        }
    }
}