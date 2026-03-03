namespace HUBDTE.Application.Interfaces.Messaging;

public interface IMessageRoutingResolver
{
    string ResolveRoutingKey(string messageType);
}