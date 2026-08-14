using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.Services;

public enum LiveValidationStep { Toggl, Jira, Tempo }

public sealed record LiveValidationResult(LiveValidationStep Step, DeliveryAttempt Attempt, string SafeMessage);
