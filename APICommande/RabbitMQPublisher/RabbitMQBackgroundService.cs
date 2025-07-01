using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace APIOrder.Services.RabbitMQ
{
    public class RabbitMQBackgroundService : BackgroundService
    {
        private readonly RabbitMQPublisher _publisher;

        public RabbitMQBackgroundService(RabbitMQPublisher publisher)
        {
            _publisher = publisher;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("Démarrage du service en arrière-plan pour l'écoute RabbitMQ...");

            // Démarrer l'écoute sur une file et clé de routage spécifique
            try
            {
                await _publisher.Subscribe(
                    exchangeName: "BirthdayExchange",
                    queueName: "BirthdayQueue",
                    onMessageReceived: (message) =>
                    {
                        Console.WriteLine($"Message traité dans BackgroundService : {message}");
                    }
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur lors de l'écoute des messages : {ex.Message}");
            }
        }
    }
}
