namespace HUBDTE.Infrastructure.Messaging;

public interface IRetryPolicyService
{
    bool ShouldRetry(int attempt);
    string GetRetryQueueForAttempt(string mainQueueName, int attempt);
    TimeSpan ComputeBackoffWithJitter(int attemptFromHeader);
}