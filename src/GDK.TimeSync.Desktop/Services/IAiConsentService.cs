using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.Services;

public interface IAiConsentService
{
    bool IsEnabled { get; }
    bool CanSubmit(DescriptionSuggestionRequest request);
}
