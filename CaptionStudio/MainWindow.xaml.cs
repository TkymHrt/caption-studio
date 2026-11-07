using System.Runtime.InteropServices;
using System.Windows.Automation;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace CaptionStudio;

public sealed partial class MainWindow : Window
{
    private enum CaptureState
    {
        Stopped,
        Starting,
        WaitingForWindow,
        WaitingForSpeech,
        Capturing,
        Reconnecting,
    }

    private const double FollowDistance = 16;
    private const int LaunchGraceMilliseconds = 5_000;
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
        SetCaptureStatus(CaptureState.Stopped);
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
        var narrow = args.NewSize.Width < 288;
        Grid.SetRow(TranscriptActions, narrow ? 1 : 0);
        Grid.SetColumn(TranscriptActions, narrow ? 0 : 1);
        Grid.SetColumnSpan(TranscriptActions, narrow ? 2 : 1);
        TranscriptActions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        TranscriptActions.Margin = narrow ? new Thickness(0, 8, 0, 0) : default;
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
        ActionFeedback.Text = string.Empty;
        SetCaptureStatus(CaptureState.Starting);

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
                SetCaptureStatus(CaptureState.Stopped);
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
        var firstRead = true;
        var launchDeadline = 0L;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Keep cross-process UI Automation calls off the WinUI thread.
                var (windowHandle, caption) = await Task.Run(LiveCaptionReader.Read, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                if (firstRead)
                {
                    firstRead = false;
                    if (windowHandle == 0 && LiveCaptionReader.TryOpenWindow())
                    {
                        launchDeadline = Environment.TickCount64 + LaunchGraceMilliseconds;
                    }
                }

                if (windowHandle != _captionWindowHandle)
                {
                    _transcript.EndSegment();
                    _captionWindowHandle = windowHandle;
                }

                if (windowHandle == 0)
                {
                    SetCaptureStatus(Environment.TickCount64 < launchDeadline ? CaptureState.Starting : CaptureState.WaitingForWindow);
                }
                else
                {
                    launchDeadline = 0;
                    SetCaptureStatus(string.IsNullOrWhiteSpace(caption) ? CaptureState.WaitingForSpeech : CaptureState.Capturing);
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
                SetCaptureStatus(CaptureState.Reconnecting);
            }

            await Task.Delay(250, cancellationToken);
        }
    }

    private void SetCaptureStatus(CaptureState state)
    {
        var (status, title, detail) = state switch
        {
            CaptureState.Starting => ("起動中", "ライブ キャプションを起動中", "Windows の字幕ウィンドウが開くまでお待ちください。"),
            CaptureState.WaitingForWindow => ("接続待ち", "ライブ キャプションの接続待ち", "Win + Ctrl + L でライブ キャプションを開いてください。"),
            CaptureState.WaitingForSpeech => ("音声待ち", "音声を待っています", "音声を再生すると、ここに字幕が表示されます。"),
            CaptureState.Capturing => ("取得中", string.Empty, string.Empty),
            CaptureState.Reconnecting => ("再接続中", "ライブ キャプションを再接続中", "字幕を読み取れません。接続が戻るまでお待ちください。"),
            _ => ("停止中", "字幕の取得を始める", "［取得開始］を押すと、Windows のライブ キャプションが開きます。"),
        };

        if (StatusText.Text == status)
        {
            return;
        }

        StatusText.Text = status;
        EmptyTranscriptTitle.Text = title;
        EmptyTranscriptDetail.Text = detail;

        var showHint = TranscriptText.Text.Length > 0 && state is CaptureState.WaitingForWindow or CaptureState.Reconnecting;
        CaptureHint.Text = showHint ? detail : string.Empty;
        CaptureHint.Visibility = showHint ? Visibility.Visible : Visibility.Collapsed;
        AnnounceLiveRegion(StatusText);
    }

    private void UpdateTranscript()
    {
        var previousOffset = TranscriptScroll.VerticalOffset;
        var wasNearLatest = IsNearLatest();
        TranscriptText.Text = _transcript.Text;
        EmptyTranscriptState.Visibility = Visibility.Collapsed;
        CopyButton.IsEnabled = true;
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
            ShowSuccess("字幕をクリップボードにコピーしました。");
        }
        catch (COMException)
        {
            ShowError("字幕をコピーできませんでした。もう一度お試しください。");
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
        ActionFeedback.Text = string.Empty;

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
            ShowSuccess("字幕を保存しました。");
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or COMException)
        {
            ShowError("字幕を保存できませんでした。保存先を確認してください。");
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

    private void ShowSuccess(string message)
    {
        if (!_closed)
        {
            SaveFeedback.IsOpen = false;
            ActionFeedback.Text = message;
            AnnounceLiveRegion(ActionFeedback);
        }
    }

    private void ShowError(string message)
    {
        if (_closed)
        {
            return;
        }

        var wasNearLatest = IsNearLatest();
        ActionFeedback.Text = string.Empty;
        SaveFeedback.Message = message;
        SaveFeedback.IsOpen = true;

        if (wasNearLatest)
        {
            TranscriptScroll.UpdateLayout();
            ScrollToLatest();
        }
    }

    private static void AnnounceLiveRegion(TextBlock element) =>
        FrameworkElementAutomationPeer.CreatePeerForElement(element)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
}
