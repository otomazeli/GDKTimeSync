using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.Services;

public interface IConfirmedTaskDeliveryService
{
    Task<DeliveryAttempt> DeliverConfirmedAsync(PlannedWorkItem item, CancellationToken cancellationToken = default);
}
