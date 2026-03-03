using HUBDTE.Application.Interfaces.Messaging;
using HUBDTE.Infrastructure.Messaging.Options;
using Microsoft.Extensions.Options;

namespace HUBDTE.Infrastructure.Messaging;

public sealed class MessageRoutingResolver : IMessageRoutingResolver
{
    private readonly IReadOnlyDictionary<string, string> _routes;

    public MessageRoutingResolver(IOptions<QueuesOptions> queues)
    {
        var q = queues.Value;

        _routes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Dte39"] = q.Dte39,
            ["Dte33"] = q.Dte33,
            ["Dte34"] = q.Dte34,
            ["Dte110"] = q.Dte110,
            ["Dte61"] = q.Dte61,
            ["Dte56"] = q.Dte56,
            ["Dte111"] = q.Dte111,
            ["Dte112"] = q.Dte112,
            ["Dte52"] = q.Dte52
        };
    }

    public string ResolveRoutingKey(string messageType)
    {
        if (string.IsNullOrWhiteSpace(messageType))
            throw new ArgumentException("messageType vacío", nameof(messageType));

        if (_routes.TryGetValue(messageType, out var routingKey) && !string.IsNullOrWhiteSpace(routingKey))
            return routingKey;

        throw new InvalidOperationException($"No existe routingKey para MessageType={messageType}");
    }
}