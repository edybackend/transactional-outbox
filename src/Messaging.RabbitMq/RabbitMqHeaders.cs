using System.Text;

namespace Messaging.RabbitMq;

internal static class RabbitMqHeaders
{
    public const string StreamKey = "x-stream-key";

    public static string ReadString(IDictionary<string, object?>? headers, string name) =>
        headers is not null && headers.TryGetValue(name, out var value)
            ? value switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string text => text,
                _ => string.Empty,
            }
            : string.Empty;
}
