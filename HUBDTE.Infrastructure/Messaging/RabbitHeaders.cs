using RabbitMQ.Client;
using System.Text;

namespace HUBDTE.Infrastructure.Messaging;

public static class RabbitHeaders
{
    public const string Attempt = "x-attempt";
    public const string Invalid = "x-invalid";
    public const string Error = "x-error";
    public const string MessageType = "x-message-type";

    public static int GetAttempt(IBasicProperties? props)
    {
        try
        {
            if (props?.Headers == null) return 0;

            if (!props.Headers.TryGetValue(Attempt, out var raw) || raw is null)
                return 0;

            if (raw is byte[] bytes)
            {
                var str = Encoding.UTF8.GetString(bytes);
                return int.TryParse(str, out var attempt) ? attempt : 0;
            }

            return int.TryParse(raw.ToString(), out var value) ? value : 0;
        }
        catch
        {
            return 0;
        }
    }

    public static void SetAttempt(IBasicProperties props, int attempt)
    {
        props.Headers ??= new Dictionary<string, object>();
        props.Headers[Attempt] = Encoding.UTF8.GetBytes(attempt.ToString());
    }

    public static void SetInvalid(IBasicProperties props, bool invalid)
    {
        props.Headers ??= new Dictionary<string, object>();
        props.Headers[Invalid] = Encoding.UTF8.GetBytes(invalid ? "true" : "false");
    }

    public static void SetError(IBasicProperties props, string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return;
        props.Headers ??= new Dictionary<string, object>();
        props.Headers[Error] = Encoding.UTF8.GetBytes(error);
    }

    public static void EnsureCorrelationId(IBasicProperties props, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(props.CorrelationId)) return;
        props.CorrelationId = fallback;
    }
}