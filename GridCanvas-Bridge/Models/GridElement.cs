using System.Text.Json.Serialization;

namespace GridCanvas_Bridge.Models;

public enum ElementType { Text, Image, Shape }

public record GridElement
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ElementType Type { get; init; } = ElementType.Text;

    [JsonPropertyName("gridX")]
    public int GridX { get; init; }

    [JsonPropertyName("gridY")]
    public int GridY { get; init; }

    [JsonPropertyName("gridW")]
    public int GridW { get; init; } = 1;

    [JsonPropertyName("gridH")]
    public int GridH { get; init; } = 1;

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("classes")]
    public string Classes { get; init; } = string.Empty;

    // グリッド座標 → CSS % 変換 (24x24 グリッド)
    public double LeftPercent => GridX / 24.0 * 100;
    public double TopPercent => GridY / 24.0 * 100;
    public double WidthPercent => GridW / 24.0 * 100;
    public double HeightPercent => GridH / 24.0 * 100;

    public GridElement WithPosition(int gridX, int gridY) =>
        this with { GridX = gridX, GridY = gridY };

    public GridElement WithContent(string content) =>
        this with { Content = content };
}
