using RabbitMQ.Client;

namespace HUBDTE.Infrastructure.Messaging;

public sealed class RabbitChannelFactory
{
    private readonly RabbitConnectionFactoryProvider _provider;

    public RabbitChannelFactory(RabbitConnectionFactoryProvider provider)
    {
        _provider = provider;
    }

    public (IConnection connection, IModel channel) Create(string clientProvidedName)
    {
        var factory = _provider.Create(dispatchConsumersAsync: true);

        var conn = factory.CreateConnection(clientProvidedName: clientProvidedName);
        var ch = conn.CreateModel();

        return (conn, ch);
    }

    public string GetEndpointInfo() => _provider.GetEndpointInfo();
}