using RabbitMQ.Client;
using System.Text;

namespace HUBDTE.Infrastructure.Messaging
{
    public sealed class RabbitMqPublisher : IDisposable
    {
        private readonly RabbitConnectionFactoryProvider _provider;
        private readonly string _exchange;

        private IConnection? _connection;
        private IModel? _channel;
        private readonly object _lock = new();

        public RabbitMqPublisher(RabbitConnectionFactoryProvider provider)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));

            var opt = _provider.Options;

            if (string.IsNullOrWhiteSpace(opt.HostName))
                throw new ArgumentException("RabbitMq HostName vacío");
            if (string.IsNullOrWhiteSpace(opt.UserName))
                throw new ArgumentException("RabbitMq UserName vacío");
            if (string.IsNullOrWhiteSpace(opt.Password))
                throw new ArgumentException("RabbitMq Password vacío");
            if (string.IsNullOrWhiteSpace(opt.Exchange))
                throw new ArgumentException("RabbitMq Exchange vacío");

            _exchange = opt.Exchange;
        }

        public bool IsConnected()
        {
            return _connection is { IsOpen: true } && _channel is { IsOpen: true };
        }

        public string GetEndpointInfo()
        {
            return _provider.GetEndpointInfo();
        }

        public void Publish(string routingKey, string bodyJson)
        {
            Publish(routingKey, bodyJson, correlationId: null, messageType: null, headers: null);
        }

        public void Publish(
            string routingKey,
            string bodyJson,
            string? correlationId,
            string? messageType,
            IDictionary<string, object>? headers = null)
        {
            if (string.IsNullOrWhiteSpace(routingKey))
                throw new ArgumentException("routingKey vacío", nameof(routingKey));

            EnsureConnected();

            var body = Encoding.UTF8.GetBytes(bodyJson ?? "");
            var props = _channel!.CreateBasicProperties();
            props.Persistent = true;

            if (!string.IsNullOrWhiteSpace(correlationId))
                props.CorrelationId = correlationId;

            props.Headers = new Dictionary<string, object>();

            if (!string.IsNullOrWhiteSpace(messageType))
                props.Headers[RabbitHeaders.MessageType] = Encoding.UTF8.GetBytes(messageType);

            if (headers is not null)
            {
                foreach (var kv in headers)
                    props.Headers[kv.Key] = kv.Value;
            }

            _channel.BasicPublish(
                exchange: _exchange,
                routingKey: routingKey,
                basicProperties: props,
                body: body);
        }

        public void PublishConfirmed(
            string routingKey,
            string bodyJson,
            string? correlationId,
            string? messageType,
            IDictionary<string, object>? headers = null,
            TimeSpan? confirmTimeout = null)
        {
            Publish(routingKey, bodyJson, correlationId, messageType, headers);

            var timeout = confirmTimeout ?? TimeSpan.FromSeconds(5);
            if (!_channel!.WaitForConfirms(timeout))
                throw new Exception($"RabbitMQ publish NOT confirmed (timeout {timeout.TotalSeconds}s). RoutingKey={routingKey}");
        }

        public void WarmUp() => EnsureConnected();

        private void EnsureConnected()
        {
            if (IsConnected())
                return;

            lock (_lock)
            {
                if (IsConnected())
                    return;

                Cleanup();

                var factory = _provider.Create(dispatchConsumersAsync: true);

                _connection = factory.CreateConnection(clientProvidedName: "HUBDTE.Api.Publisher");
                _channel = _connection.CreateModel();

                _channel.ExchangeDeclare(
                    exchange: _exchange,
                    type: ExchangeType.Direct,
                    durable: true,
                    autoDelete: false);

                _channel.ConfirmSelect();
            }
        }

        private void Cleanup()
        {
            try { _channel?.Close(); } catch { }
            try { _connection?.Close(); } catch { }
            try { _channel?.Dispose(); } catch { }
            try { _connection?.Dispose(); } catch { }

            _channel = null;
            _connection = null;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                Cleanup();
            }
        }
    }
}