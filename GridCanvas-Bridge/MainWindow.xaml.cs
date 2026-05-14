using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private string _bridgeJs = string.Empty;
    private string _currentFilePath = string.Empty;
    private string _loadedHtml = string.Empty;   // 外部ファイルまたはクリップボードから読み込んだ生 HTML
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
        _bridgeJs = await ReadAssetAsync("Assets/Scripts/gcb-bridge.js");

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

    /// <summary>
    /// 外部 HTML が読み込まれている場合は GCBPresent.goTo() でスライドを切り替えるだけ。
    /// テンプレートモードの場合はテンプレートを再レンダリングする。
    /// </summary>
    private async Task NavigateToCurrentSlide()
    {
        if (!string.IsNullOrEmpty(_loadedHtml))
        {
            // 既に WebView2 に正しい HTML が表示されているので JS だけ呼ぶ
            await SlideWebView.CoreWebView2.ExecuteScriptAsync(
                $"if(window.GCBPresent) GCBPresent.goTo({_vm.CurrentSlideIndex});" +
                 "if(window.GCB) GCB.enableEditMode();");
            return;
        }

        var template = _vm.Presentation.AspectRatio == "4:3" ? _templateHtml4x3 : _templateHtml16x9;
        await NavigateToHtml(_vm.BuildHtmlForCurrentSlide(template));
    }

    /// <summary>
    /// HTML 文字列を WebView2 に表示する。bridge JS をインライン展開し、
    /// NavigationCompleted 後に enableEditMode() を呼ぶ。
    /// </summary>
    private async Task NavigateToHtml(string html)
    {
        // NavigateToString は相対パスを解決できないため bridge JS をインライン展開
        html = html.Replace(
            "<script src=\"../Scripts/gcb-bridge.js\"></script>",
            $"<script id=\"gcb-bridge\">\n{_bridgeJs}\n</script>");

        var tcs = new TaskCompletionSource<bool>();
        void OnCompleted(CoreWebView2 s, CoreWebView2NavigationCompletedEventArgs e)
        {
            SlideWebView.CoreWebView2.NavigationCompleted -= OnCompleted;
            tcs.TrySetResult(true);
        }
        SlideWebView.CoreWebView2.NavigationCompleted += OnCompleted;
        SlideWebView.CoreWebView2.NavigateToString(html);
        await tcs.Task;

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
        await LoadExternalHtml(html);
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

        // 外部 HTML を開いていれば、そのマニフェストを更新して保存
        // テンプレートモードならテンプレートから生成
        var source = string.IsNullOrEmpty(_loadedHtml)
            ? _vm.BuildHtmlForCurrentSlide(
                _vm.Presentation.AspectRatio == "4:3" ? _templateHtml4x3 : _templateHtml16x9)
            : _vm.BuildHtmlForCurrentSlide(_loadedHtml);

        await File.WriteAllTextAsync(_currentFilePath, BuildPortableHtml(source));
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

        _currentFilePath = string.Empty;
        await LoadExternalHtml(html);
    }

    // ── 縦横比変更 ──────────────────────────────────────

    private async void OnAspectRatioChanged(object sender, SelectionChangedEventArgs e)
    {
        var ratio = AspectRatioCombo.SelectedIndex == 0 ? "16:9" : "4:3";
        if (_vm.Presentation.AspectRatio == ratio) return;

        // テンプレートモードに戻す
        _loadedHtml = string.Empty;
        _currentFilePath = string.Empty;
        _vm.Presentation = _vm.Presentation with { AspectRatio = ratio };
        await NavigateToCurrentSlide();
    }

    // ── 全画面 / 印刷 ────────────────────────────────────

    private async void OnFullScreen(object sender, RoutedEventArgs e)
    {
        // 外部 HTML は #gcb-presentation、テンプレートは #slide-root を全画面化
        await SlideWebView.CoreWebView2.ExecuteScriptAsync(
            "(document.getElementById('gcb-presentation') ?? document.getElementById('slide-root'))?.requestFullscreen?.();");
    }

    private async void OnPrint(object sender, RoutedEventArgs e)
    {
        await SlideWebView.CoreWebView2.ExecuteScriptAsync("window.print();");
    }

    // ── ヘルパー ─────────────────────────────────────────

    /// <summary>
    /// 外部 HTML (ファイル / クリップボード) を読み込み、WebView2 に表示する。
    /// </summary>
    private async Task LoadExternalHtml(string html)
    {
        _loadedHtml = html;
        _vm.LoadFromHtml(html);
        _vm.CurrentSlideIndex = 0;
        RefreshSlideList();

        // 実際の HTML をそのまま表示（テンプレートは使わない）
        await NavigateToHtml(html);

        // 読み込み後、スライド 0 をアクティブにする
        await SlideWebView.CoreWebView2.ExecuteScriptAsync(
            "if(window.GCBPresent) GCBPresent.goTo(0);");
    }

    /// <summary>
    /// bridge JS インライン script とポータブル出力に不要な記述を除去する。
    /// </summary>
    private static string BuildPortableHtml(string html)
    {
        // インライン展開された bridge script を除去
        html = Regex.Replace(html,
            @"<script id=""gcb-bridge"">.*?</script>",
            string.Empty, RegexOptions.Singleline);
        // src 参照の bridge script タグも念のため除去
        html = Regex.Replace(html,
            @"<script src=""[^""]*gcb-bridge\.js""></script>",
            string.Empty);
        return html;
    }

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
