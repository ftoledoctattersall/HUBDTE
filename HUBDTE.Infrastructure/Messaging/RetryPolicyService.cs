using HUBDTE.Infrastructure.Messaging.Options;
using Microsoft.Extensions.Options;

namespace HUBDTE.Infrastructure.Messaging;

public sealed class RetryPolicyService : IRetryPolicyService
{
    private readonly RetryPolicyOptions _opt;

    public RetryPolicyService(IOptions<RetryPolicyOptions> opt)
    {
        _opt = opt.Value;
    }

    public bool ShouldRetry(int attempt)
        => attempt < _opt.MaxAttempts;

    public string GetRetryQueueForAttempt(string mainQueueName, int attempt)
    {
        var idx = attempt - 1;
        if (_opt.DelaysSeconds is null || idx < 0 || idx >= _opt.DelaysSeconds.Length)
            return string.Empty;

        return QueueNames.Retry(mainQueueName, attempt);
    }

    public TimeSpan ComputeBackoffWithJitter(int attemptFromHeader)
    {
        var attempt = attemptFromHeader + 1;

        TimeSpan baseDelay = TimeSpan.Zero;

        if (_opt.DelaysSeconds is not null &&
            attempt >= 1 &&
            attempt <= _opt.DelaysSeconds.Length)
        {
            baseDelay = TimeSpan.FromSeconds(_opt.DelaysSeconds[attempt - 1]);
        }

        if (baseDelay == TimeSpan.Zero)
            return TimeSpan.Zero;

        var jitterMs = (_opt.JitterSeconds <= 0)
            ? 0
            : Random.Shared.Next(0, _opt.JitterSeconds * 1000);

        return baseDelay + TimeSpan.FromMilliseconds(jitterMs);
    }
}