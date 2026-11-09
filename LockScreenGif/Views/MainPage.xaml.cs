using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using LockscreenGif.Contracts.Services;
using LockscreenGif.Helpers;
using LockscreenGif.Models;
using LockscreenGif.Services;
using LockscreenGif.Services.Analytics;
using LockscreenGif.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Media.Core;
using Windows.Media.Editing;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT;

namespace LockscreenGif.Views;

public sealed partial class MainPage : Page
{
    public MainViewModel ViewModel { get; }

    private readonly ILockscreenService _lockscreenService;
    private readonly IAppNotificationService _notificationService;
    private readonly AnalyticsService _analyticsService;

    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _lockscreenModeTimer;

    private StorageFile? _videoFile;
    private uint _videoWidth;
    private uint _videoHeight;
    private double? _videoFps;
    private MediaPlaybackSession? _session;
    private CancellationTokenSource? _generationCts;
    private LockscreenApplyResult? _lastAppliedResult;
    private string? _lastAppliedSourcePath;

    private void Seek(double sec)
    {
        if (_session != null)
        {
            _session.Position = TimeSpan.FromSeconds(sec);
        }
    }

    public MainPage()
    {
        ViewModel = App.GetService<MainViewModel>();
        _lockscreenService = App.GetService<ILockscreenService>();
        _notificationService = App.GetService<IAppNotificationService>();
        _analyticsService = App.GetService<AnalyticsService>();
        InitializeComponent();
        _flowReady = true;
        ValidateEnvironment();
        LoadRecentFiles();
        RefreshFlowUi();

        NavigationCacheMode = Microsoft.UI.Xaml.Navigation.NavigationCacheMode.Required;
        Loaded += (_, _) =>
        {
            StartLockscreenModePolling();
            LoadRecentFiles();
            RefreshFlowUi();
            FocusStage();
        };

        Unloaded += MainPage_Unloaded;
        App.MainWindow.Closed += MainWindow_Closed;
    }

    private void ValidateEnvironment()
    {
        var validation = EnvironmentValidator.Validate();
        if (!validation.CanOperate)
        {
            EnvironmentStatusInfoBar.Title = "System Compatibility Notice";
            EnvironmentStatusInfoBar.Message = validation.PrimaryWarning ?? "System prerequisites could not be verified.";
            EnvironmentStatusInfoBar.Severity = validation.IsSupportedOS ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
            EnvironmentStatusInfoBar.IsOpen = true;
        }
    }

    public void LoadRecentFiles()
    {
        try
        {
            var recent = App.GetService<RecentFilesService>().GetRecentFiles();
            if (RecentFilesItemsControl != null && RecentFilesSection != null)
            {
                if (recent.Count > 0)
                {
                    RecentFilesItemsControl.ItemsSource = recent;
                    RecentFilesSection.Visibility = Visibility.Visible;
                }
                else
                {
                    RecentFilesSection.Visibility = Visibility.Collapsed;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to load recent files list", ex);
        }
    }

    private async void RecentFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path } && File.Exists(path))
        {
            try
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                await ProcessFileAsync(file);
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to open recent file from {path}", ex);
                ShowMediaFailure("Recent file unavailable", ex, "Could not open the selected recent file.");
            }
        }
    }

    private void DropTarget_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Drop video or GIF to load";
        e.DragUIOverride.IsCaptionVisible = true;
        e.DragUIOverride.IsContentVisible = true;
        e.DragUIOverride.IsGlyphVisible = true;
        if (DropTargetCard != null && Application.Current.Resources.TryGetValue("InstrumentAccentBrassBrush", out var brush))
        {
            DropTargetCard.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)brush;
        }
    }

    private void DropTarget_DragLeave(object sender, DragEventArgs e)
    {
        if (DropTargetCard != null && Application.Current.Resources.TryGetValue("InstrumentBorderBrush", out var brush))
        {
            DropTargetCard.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)brush;
        }
    }

    private async void DropTarget_Drop(object sender, DragEventArgs e)
    {
        if (DropTargetCard != null && Application.Current.Resources.TryGetValue("InstrumentBorderBrush", out var brush))
        {
            DropTargetCard.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)brush;
        }

        if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
        {
            try
            {
                var items = await e.DataView.GetStorageItemsAsync();
                if (items.Count > 0 && items[0] is StorageFile file)
                {
                    await ProcessFileAsync(file);
                }
            }
            catch (Exception ex)
            {
                Logger.Error("Drop failed", ex);
                ShowMediaFailure("Drop failed", ex, "The dropped file could not be opened.");
            }
        }
    }

    static double RoundToSigFigs(double value, int digits = 2)
    {
        if (value == 0)
        {
            return 0;
        }

        var abs = Math.Abs(value);
        var exponent = (int)Math.Floor(Math.Log10(abs)); // 5329 → 3
        var scale = Math.Pow(10, exponent - digits + 1); // 10^(3-2+1)=100
        return Math.Round(value / scale) * scale; // → 5300
    }

    private void UpdateFileSizeWarning()
    {
        if (ComboResolution.SelectedItem is not ComboBoxItem || ComboFps.SelectedItem is not ComboBoxItem)
        {
            return;
        }

        var width = (int)((ComboBoxItem)ComboResolution.SelectedItem).Tag; // e.g. 480, 720, 1080
        var fps = (double?)((ComboBoxItem)ComboFps.SelectedItem).Tag ?? 0;

        //   * preserve the source aspect ratio
        //   * assume 24-bit RGB                    → 3 bytes / pixel
        //   * assume PNG compresses to ~35 %       → factor 0.35 (empirical)
        var height = (int)Math.Round(width * (double)_videoHeight / Math.Max(1, _videoWidth));
        var bytesPerFrame =
            width
            * height
            * 3 /*RGB*/
            * 0.35;
        var kbPerFrame = bytesPerFrame / 1024.0;

        var durationSec = _endSec - _startSec;
        var frameCount = fps == 0 ? _endFrame - _startFrame : Math.Min(_endFrame - _startFrame, Math.Ceiling(durationSec * fps) + 1);
        var totalMB = frameCount * kbPerFrame / 1024.0;

        var rounded = RoundToSigFigs(totalMB, 2);

        FileSizeWarning.Message =
            $"Extracted frames are estimated at about {rounded} MiB. "
            + "The GIF needs additional space while these frames are retained. "
            + "Actual usage can be higher or lower depending on the video.";
        FileSizeWarning.IsOpen = true;
        if (totalMB > 5000)
        {
            FileSizeWarning.Severity = InfoBarSeverity.Error;
        }
        else if (totalMB > 1000)
        {
            FileSizeWarning.Severity = InfoBarSeverity.Warning;
        }
        else
        {
            FileSizeWarning.Severity = InfoBarSeverity.Informational;
        }
    }

    private void HideVideoUi()
    {
        VideoPreviewPanel.Visibility = Visibility.Collapsed;
        TrimControlsPanel.Visibility = Visibility.Collapsed;
        ComboSettingsStack.Visibility = Visibility.Collapsed;
        ComboFps.Visibility = Visibility.Collapsed;
        FileSizeWarning.IsOpen = false;
    }

    private void ShowVideoUi()
    {
        VideoPreviewPanel.Visibility = Visibility.Visible;
        TrimControlsPanel.Visibility = Visibility.Visible;
        ComboSettingsStack.Visibility = Visibility.Visible;
        ComboFps.Visibility = Visibility.Visible;
    }

    private void PopulateResolutionList()
    {
        ComboResolution.Items.Clear();
        ComboResolution.Items.Add(new ComboBoxItem { Content = $"Original ({_videoHeight}p)", Tag = (int)_videoWidth });
        if (_videoWidth > 2560)
        {
            ComboResolution.Items.Add(new ComboBoxItem { Content = "1440p", Tag = 2560 });
        }
        if (_videoWidth > 1920)
        {
            ComboResolution.Items.Add(new ComboBoxItem { Content = "1080p", Tag = 1920 });
        }
        if (_videoWidth > 1280)
        {
            ComboResolution.Items.Add(new ComboBoxItem { Content = "720p", Tag = 1280 });
        }
        if (_videoWidth > 854)
        {
            ComboResolution.Items.Add(new ComboBoxItem { Content = "480p", Tag = 854 });
        }
        ComboResolution.SelectedIndex = 0;
    }

    private void PopulateFpsList()
    {
        ComboFps.Items.Clear();
        ComboFps.Items.Add(new ComboBoxItem { Content = "All source frames", Tag = 0.0 });
        if (_videoFps > 30)
        {
            ComboFps.Items.Add(new ComboBoxItem { Content = "30 fps target", Tag = 30.0 });
        }
        if (_videoFps > 15)
        {
            ComboFps.Items.Add(new ComboBoxItem { Content = "15 fps target", Tag = 15.0 });
        }
        if (_videoFps > 10)
        {
            ComboFps.Items.Add(new ComboBoxItem { Content = "10 fps target", Tag = 10.0 });
        }
        if (_videoFps > 5)
        {
            ComboFps.Items.Add(new ComboBoxItem { Content = "5 fps target", Tag = 5.0 });
        }
        ComboFps.SelectedIndex = 0;
    }

    private void CancelGenerate_Click(object sender, RoutedEventArgs e)
    {
        _generationCts?.Cancel();
    }

    private void QualitySlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (QualityValueText != null)
        {
            var q = (int)e.NewValue;
            var label = q >= 95 ? "Maximum" : q >= 80 ? "High" : "Balanced";
            QualityValueText.Text = $"{label} ({q})";
        }
        UpdateTelemetry();
        SyncDraftState();
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_videoFile is null || _frames is null || !_mediaReady || _actionPending || Flow.Stage != MainFlowStage.Edit)
        {
            return;
        }
        var valid = CommitTime(StartTimeTextBox) & CommitTime(EndTimeTextBox);
        if (!valid)
        {
            return;
        }
        if (Flow.TryContinue())
        {
            OperationStatus.IsOpen = false;
            RefreshFlowUi();
            return;
        }
        if (!Flow.TryBeginOperation(MainFlowOperation.Generating, out var flowToken))
        {
            return;
        }

        var operation = new AnalyticsProperties { OperationId = Guid.NewGuid() };
        var timer = Stopwatch.StartNew();
        var stage = AnalyticsGenerationStage.Preparing;
        var stageTimer = Stopwatch.StartNew();
        var source = _videoFile;
        var frames = _frames;
        var startFrame = _startFrame;
        var endFrame = _endFrame;
        var duration = _endSec - _startSec;

        _generationCts?.Dispose();
        _generationCts = new CancellationTokenSource();
        var cancelToken = _generationCts.Token;

        try
        {
            var chosenWidth = (int)((ComboBoxItem)ComboResolution.SelectedItem).Tag;
            var chosenFps = (double)((ComboBoxItem)ComboFps.SelectedItem).Tag;
            var chosenQuality = (uint)(QualitySlider?.Value ?? 85.0);
            operation = operation with
            {
                OutputWidth = chosenWidth,
                TargetFps = chosenFps,
                SourceFps = _videoFps,
                ClipDurationSeconds = duration,
                SelectedFrameCount = endFrame - startFrame,
            };
            _analyticsService.Track(AnalyticsEvent.GifGenerationStarted, operation);
            SuspendEditorPlayback();
            ClearGeneratedGif();
            OperationStatus.IsOpen = false;

            if (ConversionProgressCard != null)
            {
                ConversionProgressCard.Visibility = Visibility.Visible;
                ConversionPhaseTitle.Text = "Step 1 of 2: Extracting video frames (FFmpeg)…";
                ConversionProgressDetail.Text = "Preparing extraction…";
            }
            GenerateLoading.Value = 0;
            GenerateLoading.IsIndeterminate = false;
            RefreshFlowUi();

            var ExtractFramesProgress = (double percent) =>
            {
                var display = percent * 0.4;
                DispatcherQueue.TryEnqueue(() =>
                {
                    GenerateLoading.Value = display;
                    if (ConversionPhaseTitle != null)
                    {
                        ConversionPhaseTitle.Text = "Step 1 of 2: Extracting video frames (FFmpeg)…";
                    }
                    if (ConversionProgressDetail != null)
                    {
                        ConversionProgressDetail.Text = $"{percent:F0}% ({Math.Round(display)}% total)";
                    }
                });
            };

            var CreateGifProgress = (double percent) =>
            {
                var display = 40 + (percent * 0.6);
                DispatcherQueue.TryEnqueue(() =>
                {
                    GenerateLoading.Value = display;
                    if (ConversionPhaseTitle != null)
                    {
                        ConversionPhaseTitle.Text = "Step 2 of 2: Quantizing color palette (Gifski)…";
                    }
                    if (ConversionProgressDetail != null)
                    {
                        ConversionProgressDetail.Text = $"{percent:F0}% ({Math.Round(display)}% total)";
                    }
                });
            };

            stage = AnalyticsGenerationStage.ExtractingFrames;
            stageTimer.Restart();
            var extracted = await VideoFrameService.ExportAsync(
                source.Path,
                frames,
                startFrame,
                endFrame,
                chosenWidth,
                chosenFps,
                ExtractFramesProgress,
                cancelToken
            );
            operation = operation with
            {
                ExtractionDurationMs = stageTimer.Elapsed.TotalMilliseconds,
                ExtractedFrameCount = extracted.Timestamps.Length,
            };

            cancelToken.ThrowIfCancellationRequested();

            stage = AnalyticsGenerationStage.EncodingGif;
            stageTimer.Restart();
            var gifLocation = await GifSkiService.CreateGif(
                extracted.Directory,
                CreateGifProgress,
                extracted.Timestamps,
                duration,
                chosenQuality,
                cancelToken
            );
            operation = operation with { EncodingDurationMs = stageTimer.Elapsed.TotalMilliseconds };

            cancelToken.ThrowIfCancellationRequested();

            stage = AnalyticsGenerationStage.OpeningOutput;
            stageTimer.Restart();
            var generated = await StorageFile.GetFileFromPathAsync(gifLocation);
            stage = AnalyticsGenerationStage.LoadingPreview;
            stageTimer.Restart();
            var bitmap = await PrepareGifPreviewAsync(generated, value => operation = operation with { MediaLoadStage = value });
            operation = operation with { PreviewDurationMs = stageTimer.Elapsed.TotalMilliseconds, MediaLoadStage = null };
            if (!Flow.IsCurrentOperation(flowToken) || cancelToken.IsCancellationRequested)
            {
                _analyticsService.Track(
                    AnalyticsEvent.GifGenerationCompleted,
                    operation with
                    {
                        Outcome = AnalyticsOutcome.Cancelled,
                        DurationMs = timer.Elapsed.TotalMilliseconds,
                    }
                );
                return;
            }
            stage = AnalyticsGenerationStage.Completing;
            stageTimer.Restart();
            _preparedGif = _generatedGif = generated;
            _lockscreenService.SetCurrentImage(generated, LockscreenSourceKind.Video);
            currentImage.Source = bitmap;
            _generatedGifName = Path.GetFileNameWithoutExtension(source.Name);
            Flow.TryCompleteGeneration(flowToken, true);
            GenerateLoading.Value = 100;
            _analyticsService.Track(
                AnalyticsEvent.GifGenerationCompleted,
                operation with
                {
                    Outcome = AnalyticsOutcome.Succeeded,
                    DurationMs = timer.Elapsed.TotalMilliseconds,
                }
            );
        }
        catch (OperationCanceledException)
        {
            Logger.Info("GIF generation cancelled by user");
            ClearGeneratedGif();
            Flow.TryCompleteGeneration(flowToken, false);
            OperationStatus.Title = "Conversion cancelled";
            OperationStatus.Message = "GIF generation was cancelled. Your clip settings were preserved.";
            OperationStatus.Severity = InfoBarSeverity.Informational;
            OperationStatus.IsOpen = true;
        }
        catch (Exception ex)
        {
            _analyticsService.TrackFailure(
                AnalyticsEvent.GifGenerationCompleted,
                ex,
                operation with
                {
                    DurationMs = timer.Elapsed.TotalMilliseconds,
                    FailureStage = stage,
                    FailureStageDurationMs = stageTimer.Elapsed.TotalMilliseconds,
                }
            );
            if (ConversionProgressCard != null)
            {
                ConversionProgressCard.Visibility = Visibility.Collapsed;
            }
            OperationStatus.Title = "GIF generation failed";
            OperationStatus.Message = MediaFailureGuidance.Message(
                ex,
                "The selected clip could not be converted. Try another selection or video."
            );
            OperationStatus.Severity = InfoBarSeverity.Error;
            OperationStatus.IsOpen = true;
            Flow.TryCompleteGeneration(flowToken, false);
            Logger.Error("Failed to create gif from video", ex);
        }
        finally
        {
            Flow.TryFinishOperation(flowToken);
            if (ConversionProgressCard != null)
            {
                ConversionProgressCard.Visibility = Visibility.Collapsed;
            }
            FfmpegService.CleanupTempDirectories();
            GifSkiService.CleanupTempDirectories();
            RefreshFlowUi();
            FocusStage();
        }

        return;
    }

    public static async Task<(double Fps, uint Width, uint Height)> GetVideoInfoAsync(StorageFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        try
        {
            var clip = await MediaClip.CreateFromFileAsync(file);
            var props = clip.GetVideoEncodingProperties();

            double? fps = props.FrameRate.Denominator == 0 ? null : (double)props.FrameRate.Numerator / props.FrameRate.Denominator;
            if (fps is null || !double.IsFinite(fps.Value) || fps <= 0 || props.Width == 0 || props.Height == 0)
            {
                throw new InvalidDataException("This video has no usable video stream metadata.");
            }

            return (fps.Value, props.Width, props.Height);
        }
        catch (Exception ex)
        {
            Logger.Error("failed to get video info", ex);
            throw;
        }
    }

    private void ComboResolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboFps.SelectedItem != null && ComboResolution.SelectedItem != null)
        {
            UpdateFileSizeWarning();
            UpdateTelemetry();
            SyncDraftState();
        }
    }

    private void UpdateTelemetry()
    {
        if (TelemetryDurationText is null || TelemetryFramesText is null || TelemetryFpsText is null || TelemetrySizeText is null || TelemetryImpactText is null)
        {
            return;
        }

        var durationSec = Math.Max(0, _endSec - _startSec);
        TelemetryDurationText.Text = VideoFrameIndex.FormatTime(durationSec);

        var fpsTag = (ComboFps?.SelectedItem as ComboBoxItem)?.Tag as double?;
        var fps = fpsTag ?? _videoFps ?? 0;
        var frameCount = fps <= 0
            ? Math.Max(0, _endFrame - _startFrame)
            : (int)Math.Min(_endFrame - _startFrame, Math.Ceiling(durationSec * fps) + 1);

        TelemetryFramesText.Text = $"{frameCount:N0} frames";

        if (fpsTag is double chosenFps && chosenFps > 0)
        {
            TelemetryFpsText.Text = $"{chosenFps:0.##} fps";
        }
        else if (_videoFps.HasValue && _videoFps.Value > 0)
        {
            TelemetryFpsText.Text = $"{_videoFps.Value:0.##} fps (orig)";
        }
        else
        {
            TelemetryFpsText.Text = "Original";
        }

        var targetWidth = (int)((ComboResolution?.SelectedItem as ComboBoxItem)?.Tag as int? ?? (int)_videoWidth);
        if (targetWidth <= 0)
        {
            targetWidth = 1280;
        }
        var targetHeight = _videoWidth > 0
            ? (int)Math.Round(targetWidth * (double)_videoHeight / _videoWidth)
            : 720;

        var quality = QualitySlider != null ? (uint)QualitySlider.Value : 85;

        var estimate = SizeEstimator.EstimateGifSize(frameCount, targetWidth, targetHeight, quality);
        TelemetrySizeText.Text = $"~{estimate.FormattedSize}";
        TelemetryImpactText.Text = $"• Cache: {estimate.ImpactDescription}";

        var brushKey = estimate.ImpactLevel switch
        {
            CacheImpactLevel.Safe => "InstrumentGaugeGreenBrush",
            CacheImpactLevel.Moderate => "InstrumentAccentBrassBrush",
            _ => "InstrumentAlertRedBrush"
        };

        if (Application.Current.Resources.TryGetValue(brushKey, out var brushObj) && brushObj is Brush brush)
        {
            TelemetryImpactText.Foreground = brush;
        }
    }

    private async void RunLiveTest_Click(object sender, RoutedEventArgs e)
    {
        await RunLiveTestCountdownAsync();
    }

    private async void RestoreDefaultLockscreen_Click(object sender, RoutedEventArgs e)
    {
        await RestoreDefaultLockscreenAsync();
    }
}
