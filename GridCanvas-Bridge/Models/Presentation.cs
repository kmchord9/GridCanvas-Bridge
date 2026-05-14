using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace GridCanvas_Bridge.Models;

public record Presentation
{
    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0";

    [JsonPropertyName("aspectRatio")]
    public string AspectRatio { get; init; } = "16:9";

    [JsonPropertyName("slides")]
    public IReadOnlyList<Slide> Slides { get; init; } = [];

    public Presentation UpdateSlide(Slide updated) =>
        this with
        {
            Slides = Slides
                .Select(s => s.Id == updated.Id ? updated : s)
                .ToList()
        };
}
