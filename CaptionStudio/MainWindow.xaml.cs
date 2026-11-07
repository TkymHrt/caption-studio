using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.Graphics;

namespace CaptionStudio;

public sealed partial class MainWindow : Window
{
    private readonly CaptionTranscript _transcript = new();
    private CancellationTokenSource? _capture;
    private int _captionWindowHandle;
    private bool _saving;
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private void OnLayoutLoaded(object sender, RoutedEventArgs args)
    {
        var layout = (FrameworkElement)sender;
        layout.Loaded -= OnLayoutLoaded;
        var scale = layout.XamlRoot.RasterizationScale;
        AppWindow.Resize(new SizeInt32((int)(560 * scale), (int)(460 * scale)));
    }

    private async void OnStartClicked(object sender, RoutedEventArgs args)
    {
        using var capture = new CancellationTokenSource();
        _capture = capture;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        StatusText.Text = "ライブ キャプションを探しています…";

        try
        {
            await CaptureAsync(capture.Token);
        }
        catch (OperationCanceledException) when (capture.IsCancellationRequested)
        {
        }
        finally
        {
            _capture = null;
            if (!_closed)
            {
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                StatusText.Text = "取得を停止しました。";
            }
        }
    }

    private void OnStopClicked(object sender, RoutedEventArgs args)
    {
        StopButton.IsEnabled = false;
        _transcript.Pause();
        _capture?.Cancel();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _capture?.Cancel();
    }

    private async Task CaptureAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Keep cross-process UI Automation calls off the WinUI thread.
                var (windowHandle, caption) = await Task.Run(LiveCaptionReader.Read, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (windowHandle != _captionWindowHandle)
                {
                    _transcript.EndSegment();
                    _captionWindowHandle = windowHandle;
                }

                StatusText.Text = windowHandle == 0
                    ? "ライブ キャプションが見つかりません。Win + Ctrl + L で開いてください。"
                    : string.IsNullOrWhiteSpace(caption)
                        ? "ライブ キャプションを検出しました。字幕を待っています。"
                        : "取得中";

                if (caption is not null && _transcript.Update(caption))
                {
                    TranscriptText.Text = _transcript.Text;
                    TranscriptScroll.UpdateLayout();
                    TranscriptScroll.ChangeView(null, TranscriptScroll.ScrollableHeight, null, true);
                    SaveButton.IsEnabled = !_saving;
                }
            }
            catch (ElementNotAvailableException)
            {
                // The caption window or element can disappear during a read. Retry on the next poll.
            }
            catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                StatusText.Text = "字幕を読み取れません。再接続を試みています。";
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs args)
    {
        var text = _transcript.Text;
        _saving = true;
        SaveButton.IsEnabled = false;
        SaveFeedback.IsOpen = false;

        try
        {
            var picker = new FileSavePicker(AppWindow.Id)
            {
                DefaultFileExtension = ".txt",
                SuggestedFileName = $"captions-{DateTime.Now:yyyyMMdd-HHmmss}",
            };
            picker.FileTypeChoices.Add("テキスト ファイル", [".txt"]);
            var result = await picker.PickSaveFileAsync();
            if (result is not null)
            {
                await File.WriteAllTextAsync(result.Path, text);
                ShowSaveFeedback(InfoBarSeverity.Success, "字幕を保存しました。");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
        {
            ShowSaveFeedback(InfoBarSeverity.Error, "字幕を保存できませんでした。保存先を確認してください。");
        }
        finally
        {
            _saving = false;
            if (!_closed)
            {
                SaveButton.IsEnabled = true;
            }
        }
    }

    private void ShowSaveFeedback(InfoBarSeverity severity, string message)
    {
        if (!_closed)
        {
            SaveFeedback.Severity = severity;
            SaveFeedback.Message = message;
            SaveFeedback.IsOpen = true;
        }
    }
}
