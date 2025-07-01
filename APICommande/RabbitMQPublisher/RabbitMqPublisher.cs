using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Polly;
using Polly.Retry;
using RabbitMQ.Client.Exceptions;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Serilog;
using APIOrder.Utilitaires;

namespace APIOrder.Services.RabbitMQ
{

    public class RabbitMQPublisher : IDisposable
    {
        private readonly IConnection _connection;
        private readonly IChannel _channel;
        private bool _disposed = false;
        private readonly RetryPolicy _retryPolicy;
        private const int RetryCount = 5;

        private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _rpcResponseMapper = new();

        private string? _replyQueueName;

        private readonly TextMapPropagator _propagator;

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _channel?.Dispose();
            _connection?.Dispose();
        }
        _disposed = true;
    }

        public RabbitMQPublisher(string hostName, TextMapPropagator propagator
            )
        {
            _propagator = propagator;
            
            var factory = new ConnectionFactory() { HostName = hostName };
            _retryPolicy = Policy
                .Handle<BrokerUnreachableException>()
                .Or<TimeoutException>()
                .WaitAndRetry(
                    RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        Log.Warning(exception, "RabbitMQ connection attempt {RetryCount} failed. Retrying in {TimeSpan}...", retryCount, timeSpan);
                        AppMetrics.RabbitMQErrorCounter.Add(
                            1,
                            new KeyValuePair<string, object?>("operation", "ConnectionRetry"),
                            new KeyValuePair<string, object?>("error_type", exception.GetType().Name)
                        );
                    }
                );
            try
            {
                _connection = _retryPolicy.Execute(() => factory.CreateConnectionAsync().GetAwaiter().GetResult());
                _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();
                Log.Information("Successfully connected to RabbitMQ and created channel.");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to connect to RabbitMQ after {RetryCount} retries or due to an unexpected error.", RetryCount);
                AppMetrics.RabbitMQErrorCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("operation", "ConnectionInitFailed"),
                    new KeyValuePair<string, object?>("error_type", ex.GetType().Name)
                );
                throw;
            }
        }

        public async Task PublishFanout(string exchangeName, string message)
        {
            try
            {
                await _channel.ExchangeDeclareAsync(exchange: exchangeName, type: ExchangeType.Fanout, durable: true);
                var body = Encoding.UTF8.GetBytes(message);

                await Task.Run(
                    () => _retryPolicy.Execute(
                        async () =>
                            {
                                await _channel.BasicPublishAsync(exchange: exchangeName, routingKey: string.Empty, body: body);
                            }
                        )
                );
                Log.Information("Published message to fanout exchange '{ExchangeName}'.", exchangeName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to publish message to fanout exchange '{ExchangeName}' after retries.", exchangeName);
                AppMetrics.RabbitMQErrorCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("operation", "PublishFanout"),
                    new KeyValuePair<string, object?>("error_type", ex.GetType().Name)
                );
            }
        }

        public async Task PublishDirect(string exchangeName, string routingKey, string message)
        {
            await _channel.ExchangeDeclareAsync(exchange: exchangeName, type: ExchangeType.Direct, durable: true);
            var body = Encoding.UTF8.GetBytes(message);

            await Task.Run(() => _retryPolicy.Execute(async () =>
            {
                await _channel.BasicPublishAsync(exchange: exchangeName, routingKey: routingKey, body: body);
            }));
        }

        public async Task Subscribe(string exchangeName, string queueName, Action<string> onMessageReceived)
        {
            await _channel.ExchangeDeclareAsync(exchange: exchangeName, type: ExchangeType.Fanout, durable: true);

            await _channel.QueueDeclareAsync(
                queue: queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: null
            );

            await _channel.QueueBindAsync(
                queue: queueName,
                exchange: exchangeName,
                routingKey: string.Empty
            );

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += (model, ea) =>
            {
                var body = ea.Body.ToArray();
                var message = Encoding.UTF8.GetString(body);
                Log.Information("Message reçu depuis l'exchange '{ExchangeName}' dans la queue '{QueueName}' : {Message}", exchangeName, queueName, message);
                onMessageReceived?.Invoke(message);
                return Task.CompletedTask;
            };

            await _channel.BasicConsumeAsync(queue: queueName, autoAck: true, consumer: consumer);
        }
        public async Task PublishToQueue(string queueName, string message)
        {
            try
            {
                await _channel.QueueDeclareAsync(
                    queue: queueName,
                    durable: false,
                    exclusive: false,
                    autoDelete: false,
                    arguments: null
                );

                var body = Encoding.UTF8.GetBytes(message);
                await _channel.BasicPublishAsync(exchange: string.Empty, routingKey: queueName, body: body);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex}");
                Console.WriteLine($"Erreur lors de l'envoi du message dans la queue '{queueName}'");
                throw;
            }
        }

        public async Task<string> CallRpcAsync(string message, CancellationToken cancellationToken = default)
        {
            try
            {
                await _channel.ExchangeDeclareAsync(exchange: "rpc_exchange", type: ExchangeType.Direct, durable: true);
                if (_replyQueueName == null)
                {
                    try
                    {
                        var replyQueue = await _channel.QueueDeclareAsync(queue: "", exclusive: true);
                        _replyQueueName = replyQueue.QueueName;

                        var consumer = new AsyncEventingBasicConsumer(_channel);
                        consumer.ReceivedAsync += (model, ea) =>
                        {
                            var correlationId = ea.BasicProperties.CorrelationId;
                            Log.Information("correlationId response : {CorrelationId}", correlationId);
                            Log.Information("Received RabbitMQ Headers for correlationId {CorrelationId}:", correlationId);
                            if (ea.BasicProperties.Headers != null)
                            {
                                foreach (var header in ea.BasicProperties.Headers)
                                {
                                    if (header.Value is byte[] bytes)
                                    {
                                        Log.Information("  Header: {Key} = {Value}", header.Key, Encoding.UTF8.GetString(bytes));
                                        Log.Information("bytes");
                                    }
                                    else
                                    {
                                        Log.Information("  Header: {Key} = {Value}", header.Key, header.Value);
                                    }
                                }
                            }
                            else
                            {
                                Log.Information("  No headers received.");
                            }
                            var parentContext = _propagator.Extract(default, ea.BasicProperties.Headers, (headers, key) =>
                            {
                                if (headers != null && headers.TryGetValue(key, out var value) && value is byte[] bytes)
                                {
                                    return new List<string> { Encoding.UTF8.GetString(bytes) };
                                }
                                return new List<string>();
                            });
                            var rpcActivitySource = new ActivitySource("MsOrder.ActivitySource");
                            
                            using (var activity = rpcActivitySource.StartActivity("MessageBroker.RPC.ResponseProcess", ActivityKind.Consumer, parentContext.ActivityContext))
                            {
                                if (activity != null)
                                {
                                    activity.SetTag("rabbitmq.correlation_id", correlationId);
                                    activity.SetTag("rabbitmq.queue_name", _replyQueueName);
                                    Log.Information("RPC Response Activity started. TraceId: {ActivityTraceId}, SpanId: {ActivitySpanId}", activity.TraceId, activity.SpanId);
                                }
                                else
                                {
                                    Log.Warning("Activity 'RabbitMQ.RPC.ResponseProcess' was not created. Tracing might be disabled or ActivitySource not configured.");
                                }

                                if (_rpcResponseMapper.TryRemove(correlationId!, out var tcs))
                                {
                                    var response = Encoding.UTF8.GetString(ea.Body.ToArray());
                                    Log.Information("message response : {Response}", response);
                                    tcs.TrySetResult(response);
                                }
                            }
                            return Task.CompletedTask;
                        };

                        await _channel.BasicConsumeAsync(queue: _replyQueueName, autoAck: true, consumer: consumer);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to declare reply queue or start consumer for RPC.");
                        AppMetrics.RabbitMQErrorCounter.Add(
                            1,
                            new KeyValuePair<string, object?>("operation", "RPCReplyQueueSetup"),
                            new KeyValuePair<string, object?>("error_type", ex.GetType().Name)
                        );
                    }
                }

                var correlation = Guid.NewGuid().ToString();
                Log.Information("correlation id sender : {Correlation}", correlation);
                var props = new BasicProperties
                {
                    CorrelationId = correlation,
                    ReplyTo = _replyQueueName
                };

                var currentActivity = Activity.Current;
                if (currentActivity != null)
                {
                    if (props.Headers == null)
                    {
                        props.Headers = new Dictionary<string, object>();
                    }

                    // Inject the current activity context into the message headers
                    _propagator.Inject(new PropagationContext(currentActivity.Context, Baggage.Current), props.Headers, (headers, key, value) =>
                    {
                        // RabbitMQ headers are byte arrays
                        headers[key] = Encoding.UTF8.GetBytes(value);
                    });
                    Log.Information("Injected TraceId: {CurrentActivityTraceId}, SpanId: {CurrentActivitySpanId} into RabbitMQ message headers.", currentActivity.TraceId, currentActivity.SpanId);
                }

                var bodyBytes = Encoding.UTF8.GetBytes(message);
                var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _rpcResponseMapper.TryAdd(correlation, tcs);
                try
                {
                    await _channel.BasicPublishAsync(exchange: "rpc_exchange", routingKey: "test.dim", mandatory: false, basicProperties: props, body: bodyBytes);
                    Log.Information("Published RPC request with correlation ID: {CorrelationId}", correlation);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to publish RPC request with correlation ID: {CorrelationId}.", correlation);
                    AppMetrics.RabbitMQErrorCounter.Add(
                        1,
                        new KeyValuePair<string, object?>("operation", "RPCPublish"),
                        new KeyValuePair<string, object?>("error_type", ex.GetType().Name)
                    );
                    _rpcResponseMapper.TryRemove(correlation, out _);
                }

                using var reg = cancellationToken.Register(() =>
                {
                    _rpcResponseMapper.TryRemove(correlation, out _);
                    tcs.TrySetCanceled();
                });

                return await tcs.Task;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An unhandled error occurred during RPC call for message: {Message}", message);
                AppMetrics.RabbitMQErrorCounter.Add(
                    1,
                    new KeyValuePair<string, object?>("operation", "RPCGeneralError"),
                    new KeyValuePair<string, object?>("error_type", ex.GetType().Name)
                );
                return null;
            }
        }
    }
}