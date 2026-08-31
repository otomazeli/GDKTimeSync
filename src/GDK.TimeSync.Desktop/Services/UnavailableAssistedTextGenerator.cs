using GDK.TimeSync.Core;

namespace GDK.TimeSync.Desktop.Services;

public sealed class UnavailableAssistedTextGenerator : IAssistedTextGenerator
{
    public Task<DescriptionSuggestionResult> SuggestAsync(
        DescriptionSuggestionRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new DescriptionSuggestionResult(false, null, "AI provider is not configured."));
}
