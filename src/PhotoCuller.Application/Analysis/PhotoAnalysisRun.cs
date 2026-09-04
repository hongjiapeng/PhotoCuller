using System.Text.Json.Serialization;
using PhotoCuller.Application.Contracts;
using PhotoCuller.Core.Models;

namespace PhotoCuller.Application.Analysis;

/// <summary>
/// One Engine result shared by clients. Report is the stable serialized view;
/// domain objects are retained for interactive clients and persistence.
/// </summary>
public sealed class PhotoAnalysisRun
{
    public required PhotoAnalysisReport Report { get; init; }

    [JsonIgnore]
    public required IReadOnlyList<PhotoItem> Photos { get; init; }

    [JsonIgnore]
    public required IReadOnlyList<PhotoGroup> Groups { get; init; }
}
