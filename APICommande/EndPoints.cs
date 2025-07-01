
using MongoDB.Bson;
using MongoDB.Driver;
using RabbitMQ.Client.Exceptions;
using Serilog;
using System.Text.Json;
using System.Diagnostics;
using APIOrder.Utilitaires;
using APIOrder.Model;
using APIOrder.Services.Mongo;
using APIOrder.Services.RabbitMQ;
using APIOrder.Services;
using Microsoft.AspNetCore.Mvc;
using Sprache;
using OpenTelemetry.Trace;

namespace APIOrder.Endpoints
{
    public static class OrderEndpoints{

        private static readonly ActivitySource _activitySource = new ActivitySource("MsOrder.ActivitySource");
        private const string EndpointKey = "endpoint";
        private const string OrderKey = "order";

        /// <summary>
        /// Return all orders.
        /// </summary>
        /// <response code="200">A list of all orders, wrapped in a data object following JSON:API specification</response>
        public static async Task<IResult> GetAllOrders([FromServices] IDataBaseService orderService)
        {
            string endpointValue = "get/v1/orders";

            using (var activity = _activitySource.StartActivity("GetAllOrders"))
            {
                activity?.SetTag("http.method", "GET");
                activity?.SetTag(EndpointKey, endpointValue);

                var startTime = ValueStopwatch.StartNew();
                try
                {
                    List<Order> orders;
                    using (var dbActivity = _activitySource.StartActivity("Database.GetOrders"))
                    {
                        dbActivity?.SetTag("db.system", "mongodb");
                        dbActivity?.SetTag("db.operation", "find");
                        dbActivity?.SetTag("db.collection", "orders");
                        try
                        {
                            orders = await orderService.GetOrdersAsync();

                            dbActivity?.SetStatus(ActivityStatusCode.Ok);
                            activity?.AddEvent(new ActivityEvent("OrdersFetchedFromDB", DateTimeOffset.UtcNow, new ActivityTagsCollection { { "order.count", orders.Count } }));
                        }
                        catch (Exception dbEx)
                        {
                            Log.Error(dbEx, "Database error while fetching all orders in endpoint.");
                            dbActivity?.SetStatus(ActivityStatusCode.Error, dbEx.Message);
                            dbActivity?.AddException(dbEx);

                            activity?.SetStatus(ActivityStatusCode.Error, "Database error");
                            activity?.AddException(dbEx);
                            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
                        }
                    }

                    var jsonApiOrders = orders
                        .Select(o =>
                        {
                            var dto = OrderDto.FromOrder(o);
                            return new JsonApiData<OrderDto>
                            {
                                Type = OrderKey,
                                Id = dto.Id,
                                Attributes = dto
                            };
                        })
                        .ToList();

                    var response = new { data = jsonApiOrders };
                    var result = TypedResults.Ok(response);

                    activity?.SetStatus(ActivityStatusCode.Ok);
                    activity?.AddEvent(new ActivityEvent("ResponseSent", DateTimeOffset.UtcNow, new ActivityTagsCollection { { "http.status_code", result.StatusCode.ToString() } }));

                    return result;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Unexpected error in GetAllOrders endpoint.");
                    
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity?.AddException(ex);

                    AppMetrics.AppErrorCounter.Add(
                        1,
                        new KeyValuePair<string, object?>("endpoint", endpointValue),
                        new KeyValuePair<string, object?>("error_type", ex.GetType().Name)
                    );

                    return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
                }
                finally
                {
                    AppMetrics.ResponseTimes.Record(startTime.GetElapsedTime().TotalMilliseconds,
                        KeyValuePair.Create<string, object?>(EndpointKey, endpointValue));
                }
            }
        }

        /// <summary>
        /// Get an order by his id.
        /// </summary>
        /// <response code="200">Return the order, wrapped in a data object following JSON:API specification</response>
        public static async Task<IResult> GetOrderById([FromServices] IDataBaseService orderService, string id)
        {
            string endpointValue = "get/v1/orders/{id}";
            using (var activity = _activitySource.StartActivity("GetOrderById"))
            {
                activity?.SetTag("http.method", "GET");
                activity?.SetTag(EndpointKey, endpointValue);

                var startTime = ValueStopwatch.StartNew();

                Order order;
                try
                {
                    if (!ObjectId.TryParse(id, out var objectId))
                    {
                        return TypedResults.BadRequest("Invalid ObjectId format.");
                    }
                    using (var dbActivity = _activitySource.StartActivity("Database.GetOrders"))
                    {
                        dbActivity?.SetTag("db.system", "mongodb");
                        dbActivity?.SetTag("db.operation", "find");
                        dbActivity?.SetTag("db.collection", "orders");

                        order = await orderService.Orders.Find(o => o.Id == objectId).FirstOrDefaultAsync();
                        if (order is null)
                        {
                            activity?.SetStatus(ActivityStatusCode.Error);
                            activity?.AddEvent(new ActivityEvent("FailDBRequestFromId", DateTimeOffset.UtcNow, new ActivityTagsCollection { { "order.id", id.ToString() } }));
                            return TypedResults.NotFound();
                        }

                        dbActivity?.SetStatus(ActivityStatusCode.Ok);
                        activity?.AddEvent(new ActivityEvent("OrdersFetchedFromDB", DateTimeOffset.UtcNow, new ActivityTagsCollection { { "order.id", id.ToString() } }));
                    }

                    var orderDto = OrderDto.FromOrder(order);

                    var result = TypedResults.Ok(
                        new JsonApiResponse<OrderDto>(
                            type: OrderKey,
                            id: orderDto.Id,
                            attributes: orderDto
                        )
                    );

                    activity?.SetStatus(ActivityStatusCode.Ok);
                    activity?.AddEvent(new ActivityEvent("ResponseSent", DateTimeOffset.UtcNow, new ActivityTagsCollection { { "http.status_code", result.StatusCode.ToString() } }));
                    return result;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error getting an orders : {OrderId}", id);
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity?.AddException(ex);
                    throw;
                }
                finally
                {
                    AppMetrics.ResponseTimes.Record(startTime.GetElapsedTime().TotalMilliseconds,
                        KeyValuePair.Create<string, object?>(EndpointKey, endpointValue));
                }
            }
            
        }

        /// <summary>
        /// Create an order.
        /// </summary>
        /// <response code="201">Return the created order, wrapped in a data object following JSON:API specification</response>
        public static async Task<IResult> CreateOrder([FromServices] IDataBaseService orderService, OrderDto orderDto, RabbitMQPublisher publisher)
        {
            string endpointValue = "post/v1/orders/";

            using (var activity = _activitySource.StartActivity("CreateOrder"))
            {
                activity?.SetTag("http.method", "POST");
                activity?.SetTag(EndpointKey, endpointValue);

                var startTime = ValueStopwatch.StartNew();
                try
                {
                    var order = orderDto.ToOrder();

                    using (var dbActivity = _activitySource.StartActivity("Database.CreateOrder"))
                    {
                        dbActivity?.SetTag("db.system", "mongodb");
                        dbActivity?.SetTag("db.operation", "create");
                        dbActivity?.SetTag("db.collection", "orders");
                        try
                        {
                            await orderService.Orders.InsertOneAsync(order);
                            AppMetrics.ActiveOrders.Add(1);
                            Log.Information("Order with ID {OrderId} successfully inserted into the database.", order.Id);
                            dbActivity?.SetStatus(ActivityStatusCode.Ok);
                            activity?.AddEvent(new ActivityEvent("CreateOrder", DateTimeOffset.UtcNow, new ActivityTagsCollection { { "order.id", order.Id } }));
                        }
                        catch (Exception dbEx)
                        {
                            Log.Error(dbEx, "An unexpected database error occurred when creating order {ErrorMessage}", dbEx.Message);
                            activity?.SetStatus(ActivityStatusCode.Error, $"Unexpected database error: {dbEx.Message}");
                            activity?.AddException(dbEx);
                            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
                        }
                    }

                    var createdDto = OrderDto.FromOrder(order);
                    var messageObject = new
                    {
                        orderId = createdDto.Id,
                        products = createdDto.Products
                    };

                    using (var mbActivity = _activitySource.StartActivity("MessageBroker.CreateOrder"))
                    {
                        mbActivity?.SetTag("MessageBroker.system", "RabitMQ");
                        mbActivity?.SetTag("MessageBroker.type", "rcp");

                        var message = JsonSerializer.Serialize(messageObject);
                        Log.Information("Order posted : \"{Message}\"", message);
                        activity?.AddEvent(new ActivityEvent("RcpMessageSent", DateTimeOffset.UtcNow, new ActivityTagsCollection { { "RcpMessage", message } }));

                        try
                        {
                            Log.Information("Send message = \"{MessagePayload}\" to the publisher rcp", message);
                            var rcpResponse = await publisher.CallRpcAsync(message);
                            Log.Information("{RcpResponse}", rcpResponse);
                            using (JsonDocument doc = JsonDocument.Parse(rcpResponse))
                            {
                                if (doc.RootElement.TryGetProperty("status", out JsonElement statusElement))
                                {
                                    var status = statusElement.GetString();
                                    if (status == "error")
                                    {
                                        Log.Error("Products not available");
                                        activity?.SetStatus(ActivityStatusCode.Error);
                                    }
                                    else if (status == "partially")
                                    {
                                        Log.Information("{RcpResponse}", rcpResponse);

                                    }
                                    else if (status == "failed")
                                    {
                                        Log.Information("{RcpResponse}", rcpResponse);
                                    }
                                    else if (status == "ok")
                                    {
                                        Log.Information("{RcpResponse}", rcpResponse);
                                    }
                                }
                            }
                            mbActivity?.SetStatus(ActivityStatusCode.Ok);
                            activity?.AddEvent(new ActivityEvent("RcpResponseReceived", DateTimeOffset.UtcNow, new ActivityTagsCollection { { "RcpResponse", rcpResponse } }));

                        }
                        catch (BrokerUnreachableException ex)
                        {
                            Log.Error(ex, "Failled to reach the broker");
                            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                            activity?.AddException(ex);
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Sent order issue");
                            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                            activity?.AddException(ex);
                        }
                    }

                    var result = TypedResults.Created(
                        $"/api/v1/orders/{createdDto.Id}",
                        new JsonApiResponse<OrderDto>(
                            type: OrderKey,
                            id: createdDto.Id,
                            attributes: createdDto
                        )
                    );
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return result;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error getting an order");
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    activity?.AddException(ex);
                    throw;
                }
                finally
                {
                    AppMetrics.ResponseTimes.Record(startTime.GetElapsedTime().TotalMilliseconds,
                        KeyValuePair.Create<string, object?>(EndpointKey, endpointValue));
                }
            }
        }

        /// <summary>
        /// Update an order by his id.
        /// </summary>
        /// <response code="200">Return the updated order, wrapped in a data object following JSON:API specification</response>
        public static async Task<IResult> UpdateOrder([FromServices] IDataBaseService orderService, string id, OrderDto orderDto)
        {
            string endpointValue = "put/v1/orders/{id}";
            var startTime = ValueStopwatch.StartNew();

            if (!ObjectId.TryParse(id, out var objectId))
                return TypedResults.BadRequest("Invalid ObjectId format.");

            var order = orderDto.ToOrder();
            order.Id = objectId;
            await orderService.Orders.ReplaceOneAsync(o => o.Id == objectId, order);

            var result = TypedResults.Ok(
                new JsonApiResponse<OrderDto>(
                    type: OrderKey,
                    id: orderDto.Id,
                    attributes: orderDto
                )
            );
            return result;
        }

        /// <summary>
        /// Delete an order by his id.
        /// </summary>
        /// <response code="204">No body in the return</response>
        public static async Task<IResult> DeleteOrder([FromServices] IDataBaseService orderService, string id)
        {
            string endpointValue = "delete/v1/orders/{id}";
            var startTime = ValueStopwatch.StartNew();
            
            try
            {
                if (!ObjectId.TryParse(id, out var objectId))
                {
                    Log.Warning("Attempted to delete order with invalid ObjectId format: {Id}", id);
                    var badRequestResult = TypedResults.BadRequest("Invalid ObjectId format.");
                    return badRequestResult;
                }

                var filter = Builders<Order>.Filter.Eq(o => o.Id, objectId);
                var deleteResult = await orderService.Orders.DeleteOneAsync(filter);

                if (deleteResult.DeletedCount > 0)
                {
                    AppMetrics.ActiveOrders.Add(-1);
                    Log.Information("Order with ID {OrderId} deleted successfully.", id);
                    var noContentResult = TypedResults.NoContent();
                    return noContentResult;
                }
                else
                {
                    Log.Warning("Attempted to delete order with ID {OrderId} but it was not found.", id);
                    var notFoundResult = TypedResults.NotFound();
                    return notFoundResult;
                }
            }
            catch (MongoException ex)
            {
                Log.Error(ex, "MongoDB error while deleting order with ID {OrderId}", id);
                var internalServerErrorResult = TypedResults.StatusCode(500);
                return internalServerErrorResult;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An unexpected error occurred while deleting order with ID {OrderId}", id);
                var internalServerErrorResult = TypedResults.StatusCode(500); // Internal Server Error
                return internalServerErrorResult;
            }
            finally
            {
                AppMetrics.ResponseTimes.Record(startTime.GetElapsedTime().TotalMilliseconds,
                    KeyValuePair.Create<string, object?>(EndpointKey, endpointValue));
            }
        }
    }
}