using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GridCanvas_Bridge.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace GridCanvas_Bridge;

public sealed partial class MainWindow : Window
{
    private readonly PresentationViewModel _vm = new();
    private string _templateHtml16x9 = string.Empty;
    private string _templateHtml4x3 = string.Empty;
    private string _currentFilePath = string.Empty;
    private bool _isUpdatingProperties = false;

    public MainWindow()
    {
        InitializeComponent();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await SlideWebView.EnsureCoreWebView2Async();
        SlideWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        SlideWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;

        _templateHtml16x9 = await ReadAssetAsync("Assets/Templates/slide-16x9.html");
        _templateHtml4x3 = await ReadAssetAsync("Assets/Templates/slide-4x3.html");

        // デフォルトで 16:9 テンプレートをロード
        _vm.LoadFromHtml(_templateHtml16x9);
        RefreshSlideList();
        await NavigateToCurrentSlide();
    }

    private static async Task<string> ReadAssetAsync(string relativePath)
    {
        var installPath = Windows.ApplicationModel.Package.Current.InstalledLocation.Path;
        var fullPath = Path.Combine(installPath, relativePath);
        return File.Exists(fullPath) ? await File.ReadAllTextAsync(fullPath) : string.Empty;
    }

    // ── WebView2 ナビゲーション ──────────────────────────

    private async Task NavigateToCurrentSlide()
    {
        var template = _vm.Presentation.AspectRatio == "4:3" ? _templateHtml4x3 : _templateHtml16x9;
        var html = _vm.BuildHtmlForCurrentSlide(template);
        SlideWebView.CoreWebView2.NavigateToString(html);
        await Task.Delay(100); // DOM レンダリング待機
        await SlideWebView.CoreWebView2.ExecuteScriptAsync("if(window.GCB) GCB.enableEditMode();");
    }

    // ── JS → C# メッセージ受信 ───────────────────────────

    private void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "elementMoved":
                    var movedId = root.GetProperty("id").GetString() ?? string.Empty;
                    var gx = root.GetProperty("gridX").GetInt32();
                    var gy = root.GetProperty("gridY").GetInt32();
                    _vm.MoveElement(movedId, gx, gy);
                    break;

                case "elementSelected":
                    var selId = root.GetProperty("id").GetString();
                    _vm.SelectElement(selId);
                    DispatcherQueue.TryEnqueue(UpdatePropertyPanel);
                    break;

                case "elementDeselected":
                    _vm.SelectElement(null);
                    DispatcherQueue.TryEnqueue(UpdatePropertyPanel);
                    break;

                case "contentChanged":
                    var contentId = root.GetProperty("id").GetString() ?? string.Empty;
                    var content = root.GetProperty("content").GetString() ?? string.Empty;
                    _vm.UpdateElementContent(contentId, content);
                    break;
            }
        }
        catch (JsonException) { /* 不正なメッセージは無視 */ }
    }

    // ── プロパティパネル更新 ────────────────────────────

    private void UpdatePropertyPanel()
    {
        var el = _vm.SelectedElement;
        if (el is null)
        {
            ElementPropertyPanel.Visibility = Visibility.Collapsed;
            NoSelectionText.Visibility = Visibility.Visible;
            return;
        }

        NoSelectionText.Visibility = Visibility.Collapsed;
        ElementPropertyPanel.Visibility = Visibility.Visible;
        SelectedElementId.Text = el.Id;

        _isUpdatingProperties = true;
        GridXBox.Value = el.GridX;
        GridYBox.Value = el.GridY;
        GridWBox.Value = el.GridW;
        GridHBox.Value = el.GridH;
        _isUpdatingProperties = false;
    }

    private async void OnGridPositionChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_isUpdatingProperties || _vm.SelectedElement is null) return;
        var gx = (int)GridXBox.Value;
        var gy = (int)GridYBox.Value;
        _vm.MoveElement(_vm.SelectedElement.Id, gx, gy);
        await SlideWebView.CoreWebView2.ExecuteScriptAsync(
            $"if(window.GCB) GCB.moveElement('{_vm.SelectedElement.Id}', {gx}, {gy});");
    }

    private async void OnGridSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_isUpdatingProperties || _vm.SelectedElement is null) return;
        var gw = (int)GridWBox.Value;
        var gh = (int)GridHBox.Value;
        await SlideWebView.CoreWebView2.ExecuteScriptAsync(
            $"if(window.GCB) GCB.resizeElement('{_vm.SelectedElement.Id}', {gw}, {gh});");
    }

    // ── スライドリスト ──────────────────────────────────

    private void RefreshSlideList()
    {
        SlideListView.ItemsSource = _vm.Presentation.Slides
            .Select((s, i) => $"スライド {i + 1}")
            .ToList();
        SlideListView.SelectedIndex = _vm.CurrentSlideIndex;
    }

    private async void OnSlideSelected(object sender, SelectionChangedEventArgs e)
    {
        if (SlideListView.SelectedIndex < 0) return;
        _vm.CurrentSlideIndex = SlideListView.SelectedIndex;
        await NavigateToCurrentSlide();
    }

    private async void OnPrevSlide(object sender, RoutedEventArgs e)
    {
        _vm.GoToPreviousSlideCommand.Execute(null);
        SlideListView.SelectedIndex = _vm.CurrentSlideIndex;
        await NavigateToCurrentSlide();
    }

    private async void OnNextSlide(object sender, RoutedEventArgs e)
    {
        _vm.GoToNextSlideCommand.Execute(null);
        SlideListView.SelectedIndex = _vm.CurrentSlideIndex;
        await NavigateToCurrentSlide();
    }

    // ── ファイル操作 ────────────────────────────────────

    private async void OnOpenFile(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        picker.FileTypeFilter.Add(".html");

        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

        var html = await FileIO.ReadTextAsync(file);
        _currentFilePath = file.Path;
        _vm.LoadFromHtml(html);
        RefreshSlideList();
        await NavigateToCurrentSlide();
    }

    private async void OnSaveFile(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentFilePath))
        {
            var picker = new FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("HTML ファイル", [".html"]);
            picker.SuggestedFileName = "presentation";

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            _currentFilePath = file.Path;
        }

        var template = _vm.Presentation.AspectRatio == "4:3" ? _templateHtml4x3 : _templateHtml16x9;
        var html = BuildPortableHtml(_vm.BuildHtmlForCurrentSlide(template));
        await File.WriteAllTextAsync(_currentFilePath, html);
    }

    // ── クリップボード貼り付け ─────────────────────────

    private async void OnPasteFromClipboard(object sender, RoutedEventArgs e)
    {
        var dataPackage = Clipboard.GetContent();
        if (!dataPackage.Contains(StandardDataFormats.Text)) return;

        var html = await dataPackage.GetTextAsync();
        if (!html.Contains("GCB_MANIFEST_START"))
        {
            await ShowInfoDialog("貼り付けエラー", "GCB マニフェストが見つかりません。\nGCB 対応 HTML を貼り付けてください。");
            return;
        }

        _vm.LoadFromHtml(html);
        _currentFilePath = string.Empty;
        RefreshSlideList();
        await NavigateToCurrentSlide();
    }

    // ── 縦横比変更 ──────────────────────────────────────

    private async void OnAspectRatioChanged(object sender, SelectionChangedEventArgs e)
    {
        var ratio = AspectRatioCombo.SelectedIndex == 0 ? "16:9" : "4:3";
        if (_vm.Presentation.AspectRatio == ratio) return;

        _vm.Presentation = _vm.Presentation with { AspectRatio = ratio };
        await NavigateToCurrentSlide();
    }

    // ── 全画面 / 印刷 ────────────────────────────────────

    private async void OnFullScreen(object sender, RoutedEventArgs e)
    {
        await SlideWebView.CoreWebView2.ExecuteScriptAsync(
            "document.getElementById('slide-root').requestFullscreen?.();");
    }

    private async void OnPrint(object sender, RoutedEventArgs e)
    {
        await SlideWebView.CoreWebView2.ExecuteScriptAsync("window.print();");
    }

    // ── ヘルパー ─────────────────────────────────────────

    // gcb-bridge.js を除去してポータブル HTML に変換
    private static string BuildPortableHtml(string html) =>
        System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<script src=""[^""]*gcb-bridge\.js""></script>",
            string.Empty);

    private async Task ShowInfoDialog(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
