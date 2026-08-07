namespace GDK.TimeSync.Desktop.ViewModels;

public sealed class SettingsSaveException(string message, Exception innerException) : Exception(message, innerException);
