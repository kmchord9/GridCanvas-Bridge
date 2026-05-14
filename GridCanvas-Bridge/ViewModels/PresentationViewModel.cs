using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GridCanvas_Bridge.Models;

namespace GridCanvas_Bridge.ViewModels;

public partial class PresentationViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [ObservableProperty]
    private Presentation _presentation = new();

    [ObservableProperty]
    private int _currentSlideIndex = 0;

    [ObservableProperty]
    private GridElement? _selectedElement = null;

    public Slide? CurrentSlide =>
        Presentation.Slides.Count > 0
            ? Presentation.Slides[CurrentSlideIndex]
            : null;

    public int SlideCount => Presentation.Slides.Count;

    public void LoadFromHtml(string html)
    {
        var match = Regex.Match(html,
            @"<!-- GCB_MANIFEST_START -->.*?<script[^>]*id=""gcb-manifest""[^>]*>(.*?)</script>.*?<!-- GCB_MANIFEST_END -->",
            RegexOptions.Singleline);
        if (!match.Success) return;

        var json = match.Groups[1].Value.Trim();
        var loaded = JsonSerializer.Deserialize<Presentation>(json, JsonOpts);
        if (loaded is null) return;

        Presentation = loaded;
        CurrentSlideIndex = 0;
        SelectedElement = null;
        OnPropertyChanged(nameof(CurrentSlide));
        OnPropertyChanged(nameof(SlideCount));
    }

    public string BuildHtmlForCurrentSlide(string templateHtml)
    {
        if (CurrentSlide is null) return templateHtml;

        var updatedManifest = JsonSerializer.Serialize(Presentation, JsonOpts);
        var html = Regex.Replace(templateHtml,
            @"<!-- GCB_MANIFEST_START -->.*?<!-- GCB_MANIFEST_END -->",
            $"""
            <!-- GCB_MANIFEST_START -->
            <script type="application/json" id="gcb-manifest">
            {updatedManifest}
            </script>
            <!-- GCB_MANIFEST_END -->
            """,
            RegexOptions.Singleline);
        return html;
    }

    public void MoveElement(string elementId, int gridX, int gridY)
    {
        if (CurrentSlide is null) return;
        var element = CurrentSlide.Elements.FirstOrDefault(e => e.Id == elementId);
        if (element is null) return;

        var updated = element.WithPosition(gridX, gridY);
        var newSlide = CurrentSlide.UpdateElement(updated);
        Presentation = Presentation.UpdateSlide(newSlide);
        OnPropertyChanged(nameof(CurrentSlide));
    }

    public void UpdateElementContent(string elementId, string content)
    {
        if (CurrentSlide is null) return;
        var element = CurrentSlide.Elements.FirstOrDefault(e => e.Id == elementId);
        if (element is null) return;

        var updated = element.WithContent(content);
        var newSlide = CurrentSlide.UpdateElement(updated);
        Presentation = Presentation.UpdateSlide(newSlide);
        OnPropertyChanged(nameof(CurrentSlide));
    }

    public void SelectElement(string? elementId)
    {
        SelectedElement = elementId is null
            ? null
            : CurrentSlide?.Elements.FirstOrDefault(e => e.Id == elementId);
    }

    [RelayCommand]
    private void GoToPreviousSlide()
    {
        if (CurrentSlideIndex > 0)
        {
            CurrentSlideIndex--;
            OnPropertyChanged(nameof(CurrentSlide));
        }
    }

    [RelayCommand]
    private void GoToNextSlide()
    {
        if (CurrentSlideIndex < Presentation.Slides.Count - 1)
        {
            CurrentSlideIndex++;
            OnPropertyChanged(nameof(CurrentSlide));
        }
    }

    public string SerializeManifest() =>
        JsonSerializer.Serialize(Presentation, JsonOpts);
}
