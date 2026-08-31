namespace GDK.TimeSync.Core;

public interface IAssistedTextGenerator
{
    Task<DescriptionSuggestionResult> SuggestAsync(
        DescriptionSuggestionRequest request,
        CancellationToken cancellationToken = default);
}
