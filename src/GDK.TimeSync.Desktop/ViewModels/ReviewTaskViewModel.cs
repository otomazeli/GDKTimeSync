using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.ViewModels;

public enum DeliveryMark { Pending, Delivered, Failed }

public sealed class ReviewTaskViewModel : INotifyPropertyChanged
{
    private DeliveryAttempt? attempt;
    private bool isSelected;

    public ReviewTaskViewModel(PlannedWorkItem item, DeliveryAttempt? attempt = null)
    {
        ArgumentNullException.ThrowIfNull(item);
        Item = item;
        this.attempt = attempt;
        isSelected = true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public PlannedWorkItem Item { get; }
    public Guid Id => Item.Id;
    public string JiraIssueKey => Item.JiraIssueKey;
    public string Description => Item.Comment;
    public TimeSpan Duration => Item.Duration;

    // Whether this row can be *delivered*: an item neither marked for Toggl nor already linked to an
    // entry cannot be, and a delivered one must not be posted twice. This used to gate the checkbox,
    // but the tick now also decides what goes into the Slack update, which is composed after posting
    // -- gating it here would make the delivered work impossible to report. ReviewViewModel applies
    // this guard when it builds the batch instead.
    public bool CanPost =>
        (Item.PostToToggl || Item.TogglEntryId is not null) &&
        attempt?.Status is not DeliveryAttemptStatus.Succeeded;

    // "In scope for today's review" -- both for delivery and for the Slack update. Ticked by
    // default so composing without touching anything reports the whole day.
    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (isSelected == value) return;
            isSelected = value;
            OnPropertyChanged();
        }
    }

    public DeliveryMark Toggl => Mark(attempt?.TogglEntryId is not null, DeliveryFailureCode.TogglFailed);

    // Reaching Tempo at all proves Jira validated, whether or not Tempo then succeeded.
    public DeliveryMark Jira => Mark(
        attempt?.TempoWorklogId is not null || attempt?.FailureCode == DeliveryFailureCode.TempoFailed,
        DeliveryFailureCode.JiraFailed, DeliveryFailureCode.JiraIssueNotFound);

    public DeliveryMark Tempo => Mark(attempt?.TempoWorklogId is not null, DeliveryFailureCode.TempoFailed);

    public string? FailureText
    {
        get
        {
            if (attempt?.FailureCode is not { } code) return null;
            var where = code switch
            {
                DeliveryFailureCode.TogglFailed => "Toggl",
                DeliveryFailureCode.JiraFailed or DeliveryFailureCode.JiraIssueNotFound => "Jira",
                DeliveryFailureCode.TempoFailed => "Tempo",
                _ => "Delivery"
            };
            // PostAllCoordinator.RequiresManualReconciliation builds its attempt with `attempt with { ... }`,
            // so a stale FailureDetail from an earlier Jira/Tempo failure can survive onto an attempt whose
            // FailureCode has since been changed to PersistenceFailed. Only trust FailureDetail for the
            // codes PostAllCoordinator actually pairs a message with -- every other code falls back to the
            // coded reason, even when a (stale) detail is present.
            var usesDetail = code is DeliveryFailureCode.JiraFailed or DeliveryFailureCode.JiraIssueNotFound
                or DeliveryFailureCode.TempoFailed;
            var message = (usesDetail ? attempt.FailureDetail : null) ?? CodedReason(code);
            return $"{where}: {message}";
        }
    }

    public void ApplyAttempt(DeliveryAttempt updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        attempt = updated;
        foreach (var name in new[] { nameof(Toggl), nameof(Jira), nameof(Tempo), nameof(FailureText), nameof(CanPost) })
            OnPropertyChanged(name);
    }

    private DeliveryMark Mark(bool delivered, params DeliveryFailureCode[] failedHere)
    {
        if (delivered) return DeliveryMark.Delivered;
        return attempt?.FailureCode is { } code && failedHere.Contains(code) ? DeliveryMark.Failed : DeliveryMark.Pending;
    }

    private static string CodedReason(DeliveryFailureCode code) => code switch
    {
        DeliveryFailureCode.TogglFailed => "Toggl delivery failed.",
        DeliveryFailureCode.JiraFailed => "Jira delivery failed.",
        DeliveryFailureCode.JiraIssueNotFound => "Jira issue was not found.",
        DeliveryFailureCode.TempoFailed => "Tempo delivery failed.",
        DeliveryFailureCode.PersistenceFailed => "Delivery state could not be saved.",
        DeliveryFailureCode.Cancelled => "Delivery was cancelled.",
        DeliveryFailureCode.RemoteChangedAfterDelivery => "The Toggl entry changed after delivery.",
        _ => "Delivery failed."
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
