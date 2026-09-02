using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed record WorkStatusOption(WorkStatus Value, string DisplayName)
{
    public static IReadOnlyList<WorkStatusOption> All { get; } =
        Enum.GetValues<WorkStatus>()
            .OrderBy(status => WorkStatusDisplay.Name(status), StringComparer.Ordinal)
            .Select(status => new WorkStatusOption(status, WorkStatusDisplay.Label(status)))
            .ToArray();
}
