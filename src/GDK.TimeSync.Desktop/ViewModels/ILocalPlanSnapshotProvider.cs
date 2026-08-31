using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public interface ILocalPlanSnapshotProvider
{
    DailyPlan GetSnapshot();
}
