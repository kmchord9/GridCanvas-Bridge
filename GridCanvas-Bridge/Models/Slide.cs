using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GridCanvas_Bridge.Models;

public record Slide
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("elements")]
    public IReadOnlyList<GridElement> Elements { get; init; } = [];

    public Slide UpdateElement(GridElement updated) =>
        this with
        {
            Elements = Elements
                .Select(e => e.Id == updated.Id ? updated : e)
                .ToList()
        };
}
