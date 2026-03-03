namespace HUBDTE.Infrastructure.Messaging.Options;

public sealed class RetryPolicyOptions
{
    public int MaxAttempts { get; set; } = 5;
    public int[] DelaysSeconds { get; set; } = Array.Empty<int>();
    public int JitterSeconds { get; set; } = 0;
}