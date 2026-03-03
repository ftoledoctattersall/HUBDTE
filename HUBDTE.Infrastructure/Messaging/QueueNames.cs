namespace HUBDTE.Infrastructure.Messaging;

public static class QueueNames
{
    public static string Dlq(string mainQueue)
    {
        if (string.IsNullOrWhiteSpace(mainQueue))
            throw new ArgumentException("mainQueue vacío", nameof(mainQueue));

        return mainQueue.Replace(".queue", ".dlq");
    }

    public static string Retry(string mainQueue, int attempt)
    {
        if (string.IsNullOrWhiteSpace(mainQueue))
            throw new ArgumentException("mainQueue vacío", nameof(mainQueue));

        if (attempt <= 0)
            throw new ArgumentOutOfRangeException(nameof(attempt), "attempt debe ser >= 1");

        return mainQueue.Replace(".queue", $".retry.{attempt:00}");
    }
}