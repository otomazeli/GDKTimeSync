using System.Net;
namespace GDK.TimeSync.Slack;

public sealed class SlackApiException(string message, SlackFailureCode failureCode, HttpStatusCode? statusCode = null) : Exception(message)
{
    public SlackFailureCode FailureCode { get; } = failureCode;
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

public enum SlackFailureCode
{
    UnsuccessfulResponse,
    InvalidResponse,
    Transport,
    Cancelled
}
