namespace GDK.TimeSync.Core;

public sealed record DescriptionSuggestionResult(
    bool IsAvailable,
    string? SuggestedDescription,
    string SafeMessage);
