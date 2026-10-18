namespace ClipNest.Core.Models;

/// <summary>List metadata only; <see cref="PreviewText"/> is never used for copying.</summary>
public sealed record ClipboardEntrySummary(Guid Id, DateTimeOffset CreatedAt, ClipboardKind Kind,
    string? PreviewText, string? BlobPath, string? SourceApp, bool IsFavorite);

/// <summary>A consistent slice of matching history and its total count.</summary>
public sealed record ClipboardHistoryPage(IReadOnlyList<ClipboardEntrySummary> Items, int TotalCount, long Revision);
