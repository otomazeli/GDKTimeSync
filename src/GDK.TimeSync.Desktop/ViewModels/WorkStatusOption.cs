using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public sealed record WorkStatusOption(WorkStatus Value, string DisplayName)
{
    public static IReadOnlyList<WorkStatusOption> All { get; } =
    [
        new(WorkStatus.CodeReview, "Code review"),
        new(WorkStatus.Analyzing, "Analyzing"),
        new(WorkStatus.Done, "Done"),
        new(WorkStatus.InProgress, "In Progress"),
        new(WorkStatus.Waiting, "Waiting")
    ];
}
