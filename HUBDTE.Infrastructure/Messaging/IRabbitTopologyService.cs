namespace HUBDTE.Infrastructure.Messaging;

public interface IRabbitTopologyService
{
    void EnsureBaseExchanges();
    void EnsureMainQueueWithDlq(string mainQueue);
    void EnsureRetryQueues(string mainQueue, int[] delaysSeconds);
}