// COPY OF — src/Orders/Infrastructure/Messaging/DeadLetter/DeadLetterKafkaOptions.cs
namespace OrderToCash.Notifications.Infrastructure.Messaging.DeadLetter;

/// <summary>Producer settings for <see cref="KafkaDeadLetterPublisher"/> — a DEDICATED client id (design.md §3.3).</summary>
public sealed class DeadLetterKafkaOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";

    public string ClientId { get; set; } = "otc-notifications-dlq";
}
