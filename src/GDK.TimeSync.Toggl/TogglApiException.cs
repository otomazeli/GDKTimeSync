using System.Net;

namespace GDK.TimeSync.Toggl;

public sealed class TogglApiException(string message, HttpStatusCode? statusCode = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
