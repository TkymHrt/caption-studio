using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace CaptionStudio;

public sealed partial class MainWindow : Window
{
    private const double FollowDistance = 16;
    private readonly CaptionTranscript _transcript = new();
    private CancellationTokenSource? _capture;
    private int _captionWindowHandle;
    private bool _saving;
    private bool _closeConfirmationPending;
    private string _lastSavedTranscript = string.Empty;
    private bool _closed;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(StudioTitleBar);
        Closed += OnClosed;
        AppWindow.Closing += OnAppWindowClosing;
    }

    private void OnLayoutLoaded(object sender, RoutedEventArgs args)
    {
        var layout = (FrameworkElement)sender;
        layout.Loaded -= OnLayoutLoaded;
        var scale = layout.XamlRoot.RasterizationScale;
        AppWindow.Resize(new SizeInt32((int)(560 * scale), (int)(460 * scale)));
    }

    private void OnToolbarSizeChanged(object sender, SizeChangedEventArgs args)
    {
        var narrow = args.NewSize.Width < 328;
        Grid.SetRow(CaptureStatusBadge, narrow ? 1 : 0);
        Grid.SetColumn(CaptureStatusBadge, narrow ? 0 : 1);
        Grid.SetColumnSpan(CaptureStatusBadge, narrow ? 4 : 1);
        CaptureStatusBadge.Margin = narrow ? new Thickness(0, 6, 0, 0) : default;
    }

    private async void OnCaptureClicked(object sender, RoutedEventArgs args)
    {
        if (_capture is not null)
        {
            StopCapture();
            return;
        }

        using var capture = new CancellationTokenSource();
        _capture = capture;
        CaptureButton.Content = "取得停止";
        SetCaptureStatus("待機中");

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
                CaptureButton.Content = TranscriptText.Text.Length == 0 ? "取得開始" : "取得再開";
                CaptureButton.IsEnabled = true;
                SetCaptureStatus("停止中");
            }
        }
    }

    private void StopCapture()
    {
        if (_capture is null || _capture.IsCancellationRequested)
        {
            return;
        }

        CaptureButton.IsEnabled = false;
        _transcript.Pause();
        _capture.Cancel();
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

                if (windowHandle == 0)
                {
                    SetCaptureStatus("待機中", TranscriptText.Text.Length == 0
                        ? null
                        : "Win + Ctrl + L でライブ キャプションを開いてください。");
                }
                else
                {
                    SetCaptureStatus(string.IsNullOrWhiteSpace(caption) ? "字幕待ち" : "取得中");
                }

                if (caption is not null && _transcript.Update(caption))
                {
                    UpdateTranscript();
                }
            }
            catch (ElementNotAvailableException)
            {
                // The caption window or element can disappear during a read. Retry on the next poll.
            }
            catch (Exception exception) when (exception is COMException or UnauthorizedAccessException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetCaptureStatus("再接続中", "字幕を読み取れません。再接続を試みています。");
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private void SetCaptureStatus(string status, string? hint = null)
    {
        StatusText.Text = status;
        LiveStatusDot.Visibility = status == "取得中" ? Visibility.Visible : Visibility.Collapsed;
        CaptureHint.Text = hint ?? string.Empty;
        CaptureHint.Visibility = hint is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateTranscript()
    {
        var previousOffset = TranscriptScroll.VerticalOffset;
        var wasNearLatest = IsNearLatest();
        TranscriptText.Text = _transcript.Text;
        EmptyTranscriptHint.Visibility = Visibility.Collapsed;
        CopyMenuItem.IsEnabled = true;
        SaveButton.IsEnabled = !_saving;

        TranscriptScroll.UpdateLayout();
        var latestOffset = TranscriptScroll.ScrollableHeight;
        var targetOffset = wasNearLatest ? latestOffset : Math.Min(previousOffset, latestOffset);
        TranscriptScroll.ChangeView(null, targetOffset, null, true);
        UpdateLatestButton();
    }

    private bool IsNearLatest() => TranscriptScroll.ScrollableHeight - TranscriptScroll.VerticalOffset <= FollowDistance;

    private void UpdateLatestButton() => LatestButton.Visibility = IsNearLatest() ? Visibility.Collapsed : Visibility.Visible;

    private void OnTranscriptViewChanged(object sender, ScrollViewerViewChangedEventArgs args) => UpdateLatestButton();

    private void OnTranscriptSizeChanged(object sender, SizeChangedEventArgs args) => UpdateLatestButton();

    private void OnLatestClicked(object sender, RoutedEventArgs args)
    {
        TranscriptScroll.Focus(FocusState.Programmatic);
        ScrollToLatest();
    }

    private void ScrollToLatest()
    {
        TranscriptScroll.ChangeView(null, TranscriptScroll.ScrollableHeight, null, true);
        UpdateLatestButton();
    }

    private void OnCopyClicked(object sender, RoutedEventArgs args)
    {
        try
        {
            var data = new DataPackage();
            data.SetText(TranscriptText.Text);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            ShowFeedback(InfoBarSeverity.Success, "字幕をクリップボードにコピーしました。");
        }
        catch (COMException)
        {
            ShowFeedback(InfoBarSeverity.Error, "字幕をコピーできませんでした。");
        }
    }

    private void OnAlwaysOnTopClicked(object sender, RoutedEventArgs args)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = AlwaysOnTopItem.IsChecked;
        }
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_saving || _closeConfirmationPending)
        {
            args.Cancel = true;
            return;
        }

        var text = TranscriptText.Text;
        if (text.Length == 0 || text == _lastSavedTranscript)
        {
            return;
        }

        args.Cancel = true;
        _closeConfirmationPending = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = TranscriptText.XamlRoot,
                Title = "字幕を保存しますか？",
                Content = "保存されていない字幕があります。",
                PrimaryButtonText = "保存して閉じる",
                SecondaryButtonText = "保存せずに閉じる",
                CloseButtonText = "キャンセル",
                DefaultButton = ContentDialogButton.Close,
            };
            var choice = await dialog.ShowAsync();
            if (choice == ContentDialogResult.Secondary ||
                (choice == ContentDialogResult.Primary && await SaveTranscriptAsync(closeAfterSaving: true)))
            {
                Close();
            }
        }
        finally
        {
            _closeConfirmationPending = false;
        }
    }

    private async void OnSaveClicked(object sender, RoutedEventArgs args)
    {
        if (!_closeConfirmationPending)
        {
            await SaveTranscriptAsync(closeAfterSaving: false);
        }
    }

    private async Task<bool> SaveTranscriptAsync(bool closeAfterSaving)
    {
        var text = TranscriptText.Text;
        if (_saving || text.Length == 0)
        {
            return false;
        }

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
            if (result is null)
            {
                return false;
            }

            if (closeAfterSaving)
            {
                // Include captions collected while choosing the path, then freeze the transcript before writing.
                StopCapture();
                text = TranscriptText.Text;
            }

            await File.WriteAllTextAsync(result.Path, text);
            _lastSavedTranscript = text;
            ShowFeedback(InfoBarSeverity.Success, "字幕を保存しました。");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
        {
            ShowFeedback(InfoBarSeverity.Error, "字幕を保存できませんでした。保存先を確認してください。");
            return false;
        }
        finally
        {
            _saving = false;
            if (!_closed)
            {
                SaveButton.IsEnabled = TranscriptText.Text.Length > 0;
            }
        }
    }

    private void ShowFeedback(InfoBarSeverity severity, string message)
    {
        if (_closed)
        {
            return;
        }

        var wasNearLatest = IsNearLatest();
        SaveFeedback.Severity = severity;
        SaveFeedback.Message = message;
        SaveFeedback.IsOpen = true;

        if (wasNearLatest)
        {
            TranscriptScroll.UpdateLayout();
            ScrollToLatest();
        }
    }
}
