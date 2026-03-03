using HUBDTE.Infrastructure.Messaging;
using HUBDTE.Infrastructure.Messaging.Options;
using Microsoft.Extensions.Options;

namespace HUBDTE.WorkerHost.HostedServices;

public sealed class RabbitTopologyInitializerHostedService : IHostedService
{
    private readonly IRabbitTopologyService _topology;
    private readonly QueuesOptions _queues;
    private readonly RetryPolicyOptions _retry;
    private readonly ILogger<RabbitTopologyInitializerHostedService> _logger;

    public RabbitTopologyInitializerHostedService(
        IRabbitTopologyService topology,
        IOptions<QueuesOptions> queues,
        IOptions<RetryPolicyOptions> retry,
        ILogger<RabbitTopologyInitializerHostedService> logger)
    {
        _topology = topology;
        _queues = queues.Value;
        _retry = retry.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        var allQueues = new[]
        {
            _queues.Dte39,
            _queues.Dte33,
            _queues.Dte34,
            _queues.Dte110,
            _queues.Dte61,
            _queues.Dte56,
            _queues.Dte111,
            _queues.Dte112,
            _queues.Dte52
        };

        _topology.EnsureBaseExchanges();

        foreach (var mainQueue in allQueues.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            _topology.EnsureMainQueueWithDlq(mainQueue);
            _topology.EnsureRetryQueues(mainQueue, _retry.DelaysSeconds ?? Array.Empty<int>());

            _logger.LogInformation("✅ Topology OK: main={Main} retryCount={RetryCount}",
                mainQueue, _retry.DelaysSeconds?.Length ?? 0);
        }

        _logger.LogInformation("✅ Rabbit topology inicializada");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}