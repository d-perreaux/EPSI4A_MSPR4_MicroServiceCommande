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

namespace APIOrder.TestEndpoints
{
    public static class TestEndpoints
    {
        /// <summary>
        /// Endpoint pour déclencher des erreurs de test.
        /// </summary>
        /// <param name="errorType">Type d'erreur à déclencher: "app", "db", "rabbitmq"</param>
        /// <response code="500">Erreur interne du serveur</response>
        /// <response code="400">Type d'erreur inconnu</response>
        public static async Task<IResult> TriggerError(
            [FromRoute] string errorType)
        {
            await Task.Delay(0);
            string endpointValue = $"trigger-error/{errorType}";
                try
                {
                    switch (errorType.ToLowerInvariant())
                    {
                        case "app":
                            Log.Error("Simulating application error.");

                            AppMetrics.AppErrorCounter.Add(
                                1,
                                new KeyValuePair<string, object?>("endpoint", endpointValue),
                                new KeyValuePair<string, object?>("error_type", "SimulatedAppError")
                            );
                            throw new InvalidOperationException("This is a simulated application error for testing purposes.");

                        case "db":
                            Log.Error("Simulating database error.");
                            AppMetrics.DatabaseErrorCounter.Add(
                                1,
                                new KeyValuePair<string, object?>("operation", "SimulatedDbOperation"),
                                new KeyValuePair<string, object?>("db_error_type", "SimulatedMongoException")
                            );
                            throw new MongoException("This is a simulated MongoDB error for testing purposes.");

                        case "rabbitmq":
                            Log.Error("Simulating RabbitMQ error.");

                           AppMetrics.RabbitMQErrorCounter.Add(
                                1,
                                new KeyValuePair<string, object?>("operation", "SimulatedRabbitMQPublish"),
                                new KeyValuePair<string, object?>("error_type", "SimulatedBrokerUnreachable")
                            );
                            throw new BrokerUnreachableException(new Exception("This is a simulated RabbitMQ broker unreachable error."));

                        default:
                            Log.Warning("Unknown error type requested: {ErrorType}", errorType);
                            
                            AppMetrics.AppErrorCounter.Add(
                                1,
                                new KeyValuePair<string, object?>("endpoint", endpointValue),
                                new KeyValuePair<string, object?>("error_type", "UnknownTriggerTypeError")
                            );
                            return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Caught simulated error in TriggerError endpoint: {ErrorType}", errorType);

                    return TypedResults.StatusCode(StatusCodes.Status500InternalServerError);
                }
            }
        
    }
}