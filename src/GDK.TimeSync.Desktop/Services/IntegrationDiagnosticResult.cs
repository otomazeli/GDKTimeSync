namespace GDK.TimeSync.Desktop.Services;

public enum IntegrationDiagnosticTarget { Toggl, Jira, Tempo }

public sealed record IntegrationDiagnosticResult(
    IntegrationDiagnosticTarget Target,
    bool IsSuccessful,
    string SafeMessage);
