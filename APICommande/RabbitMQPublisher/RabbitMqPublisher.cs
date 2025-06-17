using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Polly;
using Polly.Retry;
using RabbitMQ.Client.Exceptions;
using System.Threading.Tasks;
using System.Collections.Concurrent;

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

        public RabbitMQPublisher(string hostName)
        {
            var factory = new ConnectionFactory() { HostName = hostName };
            _retryPolicy = Policy
                .Handle<BrokerUnreachableException>()
                .Or<TimeoutException>()
                .WaitAndRetry(RetryCount, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

            _connection = _retryPolicy.Execute(() => factory.CreateConnectionAsync().GetAwaiter().GetResult());
            _channel = _connection.CreateChannelAsync().GetAwaiter().GetResult();

        }

        public async Task PublishFanout(string exchangeName, string message)
        {
            await _channel.ExchangeDeclareAsync(exchange: exchangeName, type: ExchangeType.Fanout, durable: true);
            var body = Encoding.UTF8.GetBytes(message);

            await Task.Run(() => _retryPolicy.Execute(async () =>
            {
                await _channel.BasicPublishAsync(exchange: exchangeName, routingKey: string.Empty, body: body);
            }));
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
                Console.WriteLine($"Message reçu depuis l'exchange '{exchangeName}' dans la queue '{queueName}' : {message}");
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
            await _channel.ExchangeDeclareAsync(exchange: "rpc_exchange", type: ExchangeType.Direct, durable: true);
            if (_replyQueueName == null)
            {
                var replyQueue = await _channel.QueueDeclareAsync(queue: "", exclusive: true);
                _replyQueueName = replyQueue.QueueName;

                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += (model, ea) =>
                {
                    var correlationId = ea.BasicProperties.CorrelationId;
                    Console.WriteLine($"correlation id response : {correlationId}");
                    if (_rpcResponseMapper.TryRemove(correlationId!, out var tcs))
                    {
                        var response = Encoding.UTF8.GetString(ea.Body.ToArray());
                        Console.WriteLine($"message response : {response}");
                        tcs.TrySetResult(response);
                    }
                    return Task.CompletedTask;
                };

                await _channel.BasicConsumeAsync(queue: _replyQueueName, autoAck: true, consumer: consumer);
            }

            var correlation = Guid.NewGuid().ToString();
            Console.WriteLine($"correlation id sender : {correlation}");
            Console.WriteLine($"message sender : {message}");
            var props = new BasicProperties
            {
                CorrelationId = correlation,
                ReplyTo = _replyQueueName
            };

            var bodyBytes = Encoding.UTF8.GetBytes(message);
            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _rpcResponseMapper.TryAdd(correlation, tcs);

            await _channel.BasicPublishAsync(exchange: "rpc_exchange", routingKey: "test.dim", mandatory: false, basicProperties: props, body: bodyBytes);

            using var reg = cancellationToken.Register(() =>
            {
                _rpcResponseMapper.TryRemove(correlation, out _);
                tcs.TrySetCanceled();
            });

            return await tcs.Task;
        }
    }
}