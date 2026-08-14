namespace WebApi.Events;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public bool Enabled { get; set; } = false;
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string Exchange { get; set; } = "fileuploader.events";
    public string RoutingKeyCompleted { get; set; } = "upload.completed";
    public string RoutingKeyAborted { get; set; } = "upload.aborted";
    public string RoutingKeyFailed { get; set; } = "upload.failed";
}
