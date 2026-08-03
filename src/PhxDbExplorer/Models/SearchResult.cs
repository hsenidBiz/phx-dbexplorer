namespace PhxDbExplorer.Models;

public record SearchMatch(
    string ObjectType,
    string SchemaName,
    string ObjectName,
    string? Detail
);

public record SearchResult(
    string Keyword,
    IReadOnlyList<SearchMatch> Matches
);
