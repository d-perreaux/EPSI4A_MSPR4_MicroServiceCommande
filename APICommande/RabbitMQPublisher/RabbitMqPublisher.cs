using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using Polly;
using Polly.Retry;
using RabbitMQ.Client.Exceptions;
using System.Threading.Tasks;

// Implémenter : IDisposable ?
public class RabbitMQPublisher 
{
    private readonly IConnection _connection;
    private readonly IChannel _channel;
    private readonly RetryPolicy _retryPolicy;
    private const int RetryCount = 5;

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

    public async Task SendMessageAsync(string queueName, string message)
    {
        await _channel.QueueDeclareAsync(
            queue: queueName, 
            durable: false, 
            exclusive: false, 
            autoDelete: false, 
            arguments: null
        );
        var body = Encoding.UTF8.GetBytes(message);
        await Task.Run(() => _retryPolicy.Execute(async () =>
        {
            await _channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: queueName,
                body: body
            );
        }));
    }

    public async Task StartListening(string queueName, string routingKey, Action<string> onMessageReceived)
    {
        await _channel.QueueDeclareAsync(queue: queueName, durable: false, exclusive: false, autoDelete: false, arguments: null);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (model, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            Console.WriteLine($" [x] Received {message}");
            return Task.CompletedTask;
        };

        await _channel.BasicConsumeAsync(queue: queueName, autoAck: true, consumer: consumer);
    }

    // Dispose?
}