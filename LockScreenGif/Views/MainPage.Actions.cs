using System.Diagnostics;
using LockscreenGif.Helpers;
using LockscreenGif.Models;
using LockscreenGif.Services;
using LockscreenGif.Services.Analytics;
using LockscreenGif.Services.Diagnostics;
using LockscreenGif.Services.Lockscreen;
using LockscreenGif.Views.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace LockscreenGif.Views;

public sealed partial class MainPage
{
    private bool _actionPending => Flow.IsBusy;

    private Task RunActionAsync(Func<Task> action) => RunActionAsync(_ => action());

    private async Task RunActionAsync(
        Func<MainFlowOperationToken, Task> action,
        MainFlowOperation operation = MainFlowOperation.Selecting,
        bool changesLockscreen = false
    )
    {
        if (_actionPending)
        {
            return;
        }

        if (changesLockscreen && App.GetService<WindowsImageFeatureService>().IsBusy)
        {
            OperationStatus.Title = "A Windows feature action is running";
            OperationStatus.Message = "Wait for it to finish before changing the lock screen.";
            OperationStatus.IsOpen = true;
            return;
        }

        if (changesLockscreen && App.GetService<DiagnosticsSessionService>().IsRunning)
        {
            OperationStatus.Title = "A diagnostic test is running";
            OperationStatus.Message = "Finish or stop the test on the Diagnostics page before changing the lock screen.";
            OperationStatus.IsOpen = true;
            return;
        }
        if (changesLockscreen && App.GetService<LockscreenVerificationService>().IsRunning)
        {
            OperationStatus.Title = "Checking the applied lock screen";
            OperationStatus.Message = "Lock and unlock to finish the file-read check before changing the lock screen.";
            OperationStatus.IsOpen = true;
            return;
        }
        if (!Flow.TryBeginOperation(operation, out var token))
        {
            return;
        }
        OperationStatus.IsOpen = false;
        RefreshFlowUi();
        try
        {
            await action(token);
        }
        catch (Exception ex)
        {
            _analyticsService.CaptureException(ex, AnalyticsErrorContext.MainAction);
            Logger.Error($"Main page action failed: {ex.GetType().Name} (0x{ex.HResult:X8})", ex);
            // Feedback after a completed apply can outlive the draft it belonged to.
            if (!Flow.IsCurrentOperation(token))
            {
                return;
            }
            OperationStatus.Title = "The action could not be completed";
            OperationStatus.Message = HumanErrorTranslator.Translate(ex);
            OperationStatus.Severity = InfoBarSeverity.Error;
            OperationStatus.IsOpen = true;
            if (operation == MainFlowOperation.Applying)
            {
                Flow.TryCompleteApply(token, new LockscreenApplyResult { Cancelled = ex is OperationCanceledException });
                ShowApplyOutcome();
            }
        }
        finally
        {
            Flow.TryFinishOperation(token);
            RefreshFlowUi();
        }
    }

    private async void SetLockscreenButton_click(object sender, RoutedEventArgs e)
    {
        if (_preparedGif is null || _actionPending || Flow.Stage != MainFlowStage.Set)
        {
            return;
        }

        var confirmed = await PreElevationDialog.ShowAsync(XamlRoot);
        if (!confirmed)
        {
            return;
        }

        await RunActionAsync(
            async token =>
            {
                Logger.Info("Trying to set lockscreen");
                var source = _preparedGif ?? throw new InvalidOperationException("Prepare an animation before applying.");
                var sourcePath = source.Path;
                // The normal entry point snapshots this source synchronously and preserves normal-usage analytics.
                _lockscreenService.SetCurrentImage(
                    source,
                    Flow.Source == MainFlowSource.Video ? LockscreenSourceKind.Video : LockscreenSourceKind.UserGif
                );
                var result = await _lockscreenService.ApplyGifAsLockscreenAsync();
                // Keep the source available for retries and diagnostics until next startup.
                if (!Flow.TryCompleteApply(token, result))
                {
                    return;
                }
                ShowApplyOutcome();
                RefreshFlowUi();
                if (Flow.LastApplyOutcome != MainFlowApplyOutcome.Succeeded)
                {
                    _notificationService.Show(string.Format("AppNotificationFailure".GetLocalized(), AppContext.BaseDirectory));
                    return;
                }
                _lastAppliedResult = result;
                _lastAppliedSourcePath = sourcePath;
                _notificationService.Show(string.Format("AppNotificationSuccess".GetLocalized(), AppContext.BaseDirectory));
            },
            MainFlowOperation.Applying,
            changesLockscreen: true
        );
    }

    private void ShowApplyOutcome()
    {
        if (Flow.LastApplyOutcome == MainFlowApplyOutcome.Succeeded)
        {
            OperationStatus.IsOpen = false;
            return;
        }
        OperationStatus.Title = Flow.LastApplyOutcome switch
        {
            MainFlowApplyOutcome.Cancelled => "Administrator permission was cancelled",
            MainFlowApplyOutcome.Partial => "Partial lock-screen update",
            _ => "Couldn’t apply lock-screen animation",
        };
        OperationStatus.Message = Flow.LastApplyOutcome switch
        {
            MainFlowApplyOutcome.Cancelled when Flow.LastApplyChangedFiles =>
                "Some lock-screen files were updated before elevation was cancelled. Your animation is still ready to retry, or you can restore default settings.",
            MainFlowApplyOutcome.Cancelled =>
                "The administrator elevation prompt was cancelled. No files or system settings were modified. Your animation is preserved and ready whenever you want to try again.",
            MainFlowApplyOutcome.Partial when Flow.LastApplyError is not null =>
                $"Some lock-screen files were updated, but the operation did not finish. {Flow.LastApplyError}",
            MainFlowApplyOutcome.Partial =>
                "Some lock-screen files were updated, but the operation did not finish. Try again or restore default lock screen.",
            _ => Flow.LastApplyError ?? "Windows could not update the lock screen. Check Diagnostics or try again.",
        };
        OperationStatus.Severity =
            Flow.LastApplyOutcome == MainFlowApplyOutcome.Cancelled
                ? (Flow.LastApplyChangedFiles ? InfoBarSeverity.Warning : InfoBarSeverity.Informational)
                : InfoBarSeverity.Error;
        OperationStatus.IsOpen = true;
    }

    public async Task RestoreDefaultLockscreenAsync()
    {
        if (_actionPending)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = (XamlRoot.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
            Title = "Restore Default Lock Screen?",
            Content = new TextBlock
            {
                Text =
                    "This will remove the animated loop from the Windows lock-screen cache and revert your lock screen to standard Windows static imagery.",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "Restore Defaults",
            CloseButtonText = "Keep Animation",
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        OperationStatus.Title = "Restoring default lock screen…";
        OperationStatus.Message = "Removing animated files from the Windows lock-screen cache.";
        OperationStatus.Severity = InfoBarSeverity.Informational;
        OperationStatus.IsOpen = true;

        try
        {
            var result = await _lockscreenService.RemoveAppliedGif();
            if (result is null || result.FailedDeletions > 0)
            {
                OperationStatus.Title = "Could not remove all animated files";
                OperationStatus.Message = result is null
                    ? "Failed to remove the animation. Check Diagnostics or try again with administrator permissions."
                    : $"{result.SuccessfulDeletions} variants removed, but {result.FailedDeletions} could not be deleted.";
                OperationStatus.Severity = InfoBarSeverity.Warning;
            }
            else
            {
                OperationStatus.Title = "Lock screen restored";
                OperationStatus.Message = "The animated lock screen was removed. Windows will now show its default picture.";
                OperationStatus.Severity = InfoBarSeverity.Success;
                _notificationService.Show("Animated lock screen removed. Windows default lock screen is restored.");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to restore default lock screen", ex);
            OperationStatus.Title = "Error restoring lock screen";
            OperationStatus.Message = HumanErrorTranslator.Translate(ex);
            OperationStatus.Severity = InfoBarSeverity.Error;
        }
        finally
        {
            OperationStatus.IsOpen = true;
        }
    }

    public async Task RunLiveTestCountdownAsync()
    {
        if (_actionPending)
        {
            return;
        }

        var countdown = new DiagnosticLockDialog(XamlRoot, DispatcherQueue);
        if (!await countdown.ShowAsync())
        {
            return;
        }

        try
        {
            var sessionMonitor = App.GetService<WindowsSessionMonitor>();
            if (!sessionMonitor.TryLock(out var lockError))
            {
                OperationStatus.Title = "Could not lock screen automatically";
                OperationStatus.Message = lockError ?? "Press Win + L on your keyboard to lock manually.";
                OperationStatus.Severity = InfoBarSeverity.Warning;
                OperationStatus.IsOpen = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to trigger live test lock", ex);
        }
    }

    private static async Task<StorageFile?> PickFileAsync(PickerLocationId location, params string[] extensions)
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.Thumbnail, SuggestedStartLocation = location };
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(App.MainWindow));
        foreach (var extension in extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        return await picker.PickSingleFileAsync();
    }

    public async Task ProcessFileAsync(StorageFile file)
    {
        var ext = file.FileType?.ToLowerInvariant() ?? "";
        if (ext == ".gif")
        {
            await RunActionAsync(token => LoadGifDirectAsync(file, token));
        }
        else if (ext is ".mp4" or ".webm" or ".mov" or ".mkv" or ".avi")
        {
            await RunActionAsync(token => LoadVideoDirectAsync(file, token));
        }
        else
        {
            OperationStatus.Title = "Unsupported format";
            OperationStatus.Message =
                $"'{file.Name}' has an unsupported extension. Supported formats: .mp4, .webm, .mov, .mkv, .avi, .gif.";
            OperationStatus.Severity = InfoBarSeverity.Error;
            OperationStatus.IsOpen = true;
        }
    }

    private async void OpenGifButton_click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async token =>
        {
            var file = await PickFileAsync(PickerLocationId.PicturesLibrary, ".gif");
            if (file is null)
            {
                _analyticsService.Track(AnalyticsEvent.GifSelected, new AnalyticsProperties { Outcome = AnalyticsOutcome.Cancelled });
                return;
            }
            await LoadGifDirectAsync(file, token);
        });

    private async Task LoadGifDirectAsync(StorageFile file, MainFlowOperationToken token)
    {
        var stage = AnalyticsMediaLoadStage.PickingFile;
        var operation = new AnalyticsProperties { OperationId = Guid.NewGuid() };
        var timer = Stopwatch.StartNew();
        try
        {
            // Early validation: check if file is readable and non-empty
            var basicProps = await file.GetBasicPropertiesAsync();
            if (basicProps.Size == 0)
            {
                throw new InvalidDataException("The selected GIF file is empty (0 bytes).");
            }

            // Decode before committing so a failed replacement cannot discard the previous draft.
            var bitmap = await PrepareGifPreviewAsync(file, value => stage = value);
            if (!Flow.IsCurrentOperation(token))
            {
                return;
            }
            stage = AnalyticsMediaLoadStage.Completing;
            ReleaseVideoDraft();
            _sourceFile = _preparedGif = file;
            _editSignature = null;
            ClearGeneratedGif();
            _lockscreenService.SetCurrentImage(file, LockscreenSourceKind.UserGif);
            currentImage.Source = bitmap;
            Flow.TryCommitSelection(token, MainFlowSource.Gif);

            App.GetService<RecentFilesService>().AddRecentFile(file.Path, isVideo: false);
            LoadRecentFiles();

            _analyticsService.Track(
                AnalyticsEvent.GifSelected,
                operation with
                {
                    Outcome = AnalyticsOutcome.Succeeded,
                    DurationMs = timer.Elapsed.TotalMilliseconds,
                }
            );
        }
        catch (Exception ex)
        {
            _analyticsService.TrackFailure(
                AnalyticsEvent.GifSelected,
                ex,
                operation with
                {
                    MediaLoadStage = stage,
                    DurationMs = timer.Elapsed.TotalMilliseconds,
                }
            );
            Logger.Error($"GIF selection failed at {stage}", ex);
            ShowMediaFailure("GIF could not be opened", ex, "The GIF could not be opened. Try a local copy or another GIF.");
        }
    }

    private void ShowMediaFailure(string title, Exception exception, string fallback)
    {
        OperationStatus.Title = title;
        OperationStatus.Message = MediaFailureGuidance.Message(exception, fallback);
        OperationStatus.Severity = InfoBarSeverity.Error;
        OperationStatus.IsOpen = true;
    }

    private static async Task<BitmapImage> PrepareGifPreviewAsync(StorageFile file, Action<AnalyticsMediaLoadStage>? onStage = null)
    {
        onStage?.Invoke(AnalyticsMediaLoadStage.OpeningFile);
        using var stream = await file.OpenReadAsync();
        onStage?.Invoke(AnalyticsMediaLoadStage.DecodingImage);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private async void OpenVideoButton_click(object sender, RoutedEventArgs e) =>
        await RunActionAsync(async flowToken =>
        {
            StorageFile? file;
            try
            {
                file = await PickFileAsync(PickerLocationId.VideosLibrary, ".mp4", ".webm", ".mov", ".mkv", ".avi");
            }
            catch (Exception ex)
            {
                _analyticsService.TrackFailure(
                    AnalyticsEvent.VideoLoadCompleted,
                    ex,
                    new AnalyticsProperties { MediaLoadStage = AnalyticsMediaLoadStage.PickingFile }
                );
                Logger.Error("Video picker failed", ex);
                ShowMediaFailure("Video could not be selected", ex, "The file picker could not open the video. Try a readable local copy.");
                return;
            }
            if (file is null)
            {
                _analyticsService.Track(
                    AnalyticsEvent.VideoLoadCompleted,
                    new AnalyticsProperties { Outcome = AnalyticsOutcome.Cancelled }
                );
                return;
            }

            await LoadVideoDirectAsync(file, flowToken);
        });

    private async Task LoadVideoDirectAsync(StorageFile file, MainFlowOperationToken flowToken)
    {
        // Early validation on file size
        try
        {
            var basicProps = await file.GetBasicPropertiesAsync();
            if (basicProps.Size == 0)
            {
                throw new InvalidDataException("The selected video file is empty (0 bytes).");
            }
            if (basicProps.Size > 1024L * 1024L * 1024L)
            {
                OperationStatus.Title = "Large video file";
                OperationStatus.Message =
                    $"'{file.Name}' is {basicProps.Size / (1024.0 * 1024.0):F0} MB. Reading frames may take some time.";
                OperationStatus.Severity = InfoBarSeverity.Warning;
                OperationStatus.IsOpen = true;
            }
        }
        catch (Exception ex) when (ex is not InvalidDataException) { }

        var operationId = Guid.NewGuid();
        var timer = Stopwatch.StartNew();
        var operation = new AnalyticsProperties { OperationId = operationId, MetadataFallbackUsed = false };
        var stage = AnalyticsMediaLoadStage.ReadingMetadata;
        _analyticsService.Track(AnalyticsEvent.VideoLoadStarted, operation);
        MediaPlayer? preparedPlayer = null;
        Exception? playbackError = null;
        var scanning = false;
        try
        {
            PausePreview();
            GenerateButton.IsEnabled = false;
            _videoLoadCts?.Dispose();
            var loadSource = _videoLoadCts = new CancellationTokenSource();
            var token = loadSource.Token;
            BeginVideoLoading(file.Name);
            double fps;
            uint width,
                height;
            try
            {
                (fps, width, height) = await Task.Run(() => GetVideoInfoAsync(file), token).WaitAsync(token);
            }
            catch (Exception ex) when (!token.IsCancellationRequested && MediaFailureGuidance.CanUseVideoFallback(ex))
            {
                operation = operation with { MetadataFallbackUsed = true };
                _analyticsService.CaptureException(ex, AnalyticsErrorContext.VideoLoad, operation with { MediaLoadStage = stage });
                Logger.Error("Windows video metadata failed; trying the bundled decoder", ex);
                stage = AnalyticsMediaLoadStage.ReadingFallbackMetadata;
                var metadata = await VideoFrameService.ReadMetadataAsync(file.Path, token);
                (fps, width, height) = (metadata.Fps, metadata.Width, metadata.Height);
            }

            token.ThrowIfCancellationRequested();
            stage = AnalyticsMediaLoadStage.IndexingFrames;
            scanning = true;
            VideoLoadStatus.Text = "Reading video frames…";
            var progress = new Progress<int>(count =>
            {
                if (_isVideoLoading && scanning && ReferenceEquals(_videoLoadCts, loadSource) && !token.IsCancellationRequested)
                {
                    VideoLoadStatus.Text = $"Reading video frames… {count:N0} found";
                }
            });
            var frames = await VideoFrameService.IndexAsync(file.Path, fps, progress, token);
            scanning = false;
            token.ThrowIfCancellationRequested();
            VideoLoadStatus.Text = $"Read {frames.Count:N0} frames. Opening preview…";
            stage = AnalyticsMediaLoadStage.OpeningPreview;
            try
            {
                preparedPlayer = await OpenPreviewPlayerAsync(file, token);
            }
            catch (Exception ex) when (!token.IsCancellationRequested && MediaFailureGuidance.CanUseVideoFallback(ex))
            {
                playbackError = ex;
                _analyticsService.CaptureException(
                    ex,
                    AnalyticsErrorContext.VideoPreview,
                    operation with
                    {
                        MediaLoadStage = stage,
                        PlaybackAvailable = false,
                    }
                );
                Logger.Error("Windows video playback unavailable; keeping frame editing and conversion", ex);
            }
            operation = operation with { PlaybackAvailable = preparedPlayer is not null };
            token.ThrowIfCancellationRequested();
            if (!Flow.IsCurrentOperation(flowToken))
            {
                return;
            }

            stage = AnalyticsMediaLoadStage.Completing;
            // Commit only after decoding/indexing succeeds. Windows playback is optional.
            _previewCts?.Cancel();
            _editorCts?.Cancel();
            _editorCts?.Dispose();
            _editorCts = new CancellationTokenSource();
            _previewErrorReported = false;
            _thumbnailErrorReported = false;
            _previewCache.Clear();
            PreviewStill.Source = null;
            PreviewStill.Visibility = Visibility.Collapsed;
            ClearGeneratedGif();
            _videoFile = file;
            _sourceFile = file;
            _preparedGif = null;
            currentImage.Source = null;
            _editSignature = null;
            _frames = frames;
            _startFrame = _previewFrame = 0;
            _endFrame = frames.Count;
            _videoFps = fps;
            _videoWidth = width;
            _videoHeight = height;
            PopulateResolutionList();
            PopulateFpsList();
            TrimTimeline.Configure(frames);
            GenerateLoading.Visibility = Visibility.Collapsed;
            ReleasePreviewPlayer();
            if (preparedPlayer is not null)
            {
                VideoPreview.SetMediaPlayer(preparedPlayer);
                preparedPlayer = null; // The page now owns this player.
                VideoPreview.MediaPlayer.MediaEnded += VideoPreview_MediaEnded;
                VideoPreview.MediaPlayer.MediaFailed += VideoPreview_MediaFailed;
                _session = VideoPreview.MediaPlayer.PlaybackSession;
                _session.SeekCompleted += Session_SeekCompleted;
            }
            _mediaReady = true;
            UpdatePlaybackButtons();
            ShowVideoUi();
            UpdateEditor();
            Flow.TryCommitSelection(flowToken, MainFlowSource.Video);

            App.GetService<RecentFilesService>().AddRecentFile(file.Path, isVideo: true);
            LoadRecentFiles();

            SyncDraftState();
            ShowFrame(0);
            _ = LoadThumbnailsAsync(file.Path, frames, _editorCts.Token);
            if (playbackError is not null)
            {
                ShowPlaybackUnavailable(playbackError);
            }
            _analyticsService.Track(
                AnalyticsEvent.VideoLoadCompleted,
                operation with
                {
                    Outcome = AnalyticsOutcome.Succeeded,
                    DurationMs = timer.Elapsed.TotalMilliseconds,
                }
            );
        }
        catch (OperationCanceledException ex)
        {
            _analyticsService.TrackFailure(
                AnalyticsEvent.VideoLoadCompleted,
                ex,
                operation with
                {
                    MediaLoadStage = stage,
                    DurationMs = timer.Elapsed.TotalMilliseconds,
                }
            );
            OperationStatus.Title = "Video loading cancelled";
            OperationStatus.Message = _videoFile is null ? "Choose a video when you're ready." : "Your previous selection has been kept.";
            OperationStatus.IsOpen = true;
        }
        catch (Exception ex)
        {
            _analyticsService.TrackFailure(
                AnalyticsEvent.VideoLoadCompleted,
                ex,
                operation with
                {
                    MediaLoadStage = stage,
                    DurationMs = timer.Elapsed.TotalMilliseconds,
                }
            );
            Logger.Error($"Video loading failed at {stage}", ex);
            ShowMediaFailure("Video could not be opened", ex, "The video could not be read or decoded. Try a local copy or another video.");
        }
        finally
        {
            scanning = false;
            preparedPlayer?.Dispose();
            EndVideoLoading();
        }
    }
}
