using System.Net;

namespace GDK.TimeSync.Jira;

public sealed class JiraApiException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
    : Exception(message, innerException)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}
