using System.Net;

namespace GDK.TimeSync.Tempo;

public sealed class TempoApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
