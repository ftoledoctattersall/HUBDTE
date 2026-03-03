using HUBDTE.Infrastructure.Messaging.Options;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace HUBDTE.Infrastructure.Messaging;

public sealed class RabbitConnectionFactoryProvider
{
    private readonly RabbitMqOptions _opt;

    public RabbitConnectionFactoryProvider(IOptions<RabbitMqOptions> opt)
    {
        _opt = opt.Value;
    }

    public RabbitMqOptions Options => _opt;

    public ConnectionFactory Create(bool dispatchConsumersAsync = true)
    {
        return new ConnectionFactory
        {
            HostName = _opt.HostName,
            Port = _opt.Port,
            VirtualHost = _opt.VirtualHost,
            UserName = _opt.UserName,
            Password = _opt.Password,
            DispatchConsumersAsync = dispatchConsumersAsync,
            AutomaticRecoveryEnabled = true,
            TopologyRecoveryEnabled = true,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
            HandshakeContinuationTimeout = TimeSpan.FromSeconds(5)
        };
    }

    public string GetEndpointInfo()
        => $"Host={_opt.HostName}:{_opt.Port} VHost={_opt.VirtualHost} User={_opt.UserName} Exchange={_opt.Exchange}";
}