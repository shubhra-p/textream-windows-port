using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Textream.Windows.Common;
using Textream.Windows.Interop;
using Textream.Windows.Models;
using Textream.Windows.Services;

namespace Textream.Windows.Views;

public partial class PrompterOverlayWindow : Window
{
    private readonly ISpeechRecognizerService _speechService;
    private readonly ISettingsService _settingsService;
    private readonly ITextreamCoordinator _coordinator;

    private List<WordItem> _words = new();
    private readonly List<Border> _wordContainers = new();
    private readonly List<TextBlock> _wordTextBlocks = new();

    private DispatcherTimer? _monitorTrackingTimer;
    private DispatcherTimer? _cursorTrackingTimer;
    private DispatcherTimer? _autoNextPageTimer;
    private DispatcherTimer? _dismissTimer;
    private int _countdownSeconds;
    private bool _isCompleted;

    private StopButtonWindow? _stopButtonWindow;
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _currentMonitor = IntPtr.Zero;

    private bool _isManualScrolling;
    private DateTime _lastManualScrollTime = DateTime.MinValue;
    private int _lastActiveWordIndex = -1;

    public PrompterOverlayWindow(
        ISpeechRecognizerService speechService,
        ISettingsService settingsService,
        ITextreamCoordinator coordinator)
    {
        _speechService = speechService;
        _settingsService = settingsService;
        _coordinator = coordinator;

        InitializeComponent();

        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyWindowModeAndStyles();
        LoadCurrentPageContent();

        _speechService.StateChanged += OnSpeechStateChanged;
        _speechService.WordJumped += OnWordJumped;

        // Start speech/prompter tracking for current page text
        var settings = _settingsService.Settings;
        var text = _coordinator.CurrentPageText;
        _speechService.Start(text, settings.ListeningMode, settings.ReadingSpeedWpm);

        // Keep PC and display awake during reading session
        if (settings.KeepScreenAwake)
        {
            NativeMethods.SetThreadExecutionState(
                NativeMethods.ExecutionState.EsContinuous |
                NativeMethods.ExecutionState.EsDisplayRequired |
                NativeMethods.ExecutionState.EsSystemRequired);
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var helper = new WindowInteropHelper(this);
        _hwnd = helper.Handle;

        var settings = _settingsService.Settings;
        bool isFollowCursor = settings.OverlayMode == OverlayMode.Floating && settings.FollowCursorWhenUndocked;

        // Set Win32 styles: Non-activating, Topmost, ToolWindow (+ Transparent for follow-cursor)
        uint exStyle = NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_TOOLWINDOW;
        if (isFollowCursor)
        {
            exStyle |= NativeMethods.WS_EX_TRANSPARENT;
        }

        IntPtr curStyle = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(curStyle.ToInt64() | exStyle));

        // Hide from screen recording/captures if configured
        if (settings.HideFromScreenShare)
        {
            NativeMethods.SetWindowDisplayAffinity(_hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
        }
    }

    private void ApplyWindowModeAndStyles()
    {
        var settings = _settingsService.Settings;
        bool isFollowCursor = settings.OverlayMode == OverlayMode.Floating && settings.FollowCursorWhenUndocked;

        Opacity = Math.Clamp(settings.PrompterOpacity, 0.1, 1.0);
        Width = Math.Clamp(settings.NotchWidth, 280, 1400);
        Height = Math.Clamp(settings.TextAreaHeight, 100, 800);

        if (settings.OverlayMode == OverlayMode.Fullscreen)
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            OverlayBorder.CornerRadius = new CornerRadius(0);
            OverlayBorder.Background = new SolidColorBrush(Color.FromRgb(10, 10, 12));
            DragHandlePanel.Visibility = Visibility.Collapsed;

            // Position to target monitor
            PositionFullscreen();
        }
        else if (settings.OverlayMode == OverlayMode.Floating)
        {
            OverlayBorder.CornerRadius = new CornerRadius(18);
            DragHandlePanel.Visibility = isFollowCursor ? Visibility.Collapsed : Visibility.Visible;

            if (isFollowCursor)
            {
                StartCursorTracking();
            }
            else
            {
                PositionFloatingCenter();
            }
        }
        else // Pinned (Top-Pinned Notch)
        {
            OverlayBorder.CornerRadius = new CornerRadius(0, 0, 22, 22);
            DragHandlePanel.Visibility = Visibility.Collapsed;

            PositionTopPinnedNotch();

            if (settings.NotchDisplayMode == NotchDisplayMode.FollowMouse)
            {
                StartMonitorTracking();
            }
        }
    }

    private void PositionTopPinnedNotch()
    {
        NativeMethods.Point pt;
        NativeMethods.GetCursorPos(out pt);
        IntPtr hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        _currentMonitor = hMonitor;

        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
        if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
        {
            double monLeft = info.rcMonitor.Left;
            double monTop = info.rcMonitor.Top;
            double monWidth = info.rcMonitor.Width;

            Left = monLeft + (monWidth - Width) / 2.0;
            Top = monTop;
        }
        else
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2.0;
            Top = 0;
        }
    }

    private void PositionFloatingCenter()
    {
        NativeMethods.Point pt;
        NativeMethods.GetCursorPos(out pt);
        IntPtr hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);

        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
        if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
        {
            Left = info.rcMonitor.Left + (info.rcMonitor.Width - Width) / 2.0;
            Top = info.rcMonitor.Top + (info.rcMonitor.Height - Height) / 3.0;
        }
        else
        {
            Left = (SystemParameters.PrimaryScreenWidth - Width) / 2.0;
            Top = (SystemParameters.PrimaryScreenHeight - Height) / 3.0;
        }
    }

    private void PositionFullscreen()
    {
        NativeMethods.Point pt;
        NativeMethods.GetCursorPos(out pt);
        IntPtr hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);

        var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MONITORINFO)) };
        if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
        {
            Left = info.rcMonitor.Left;
            Top = info.rcMonitor.Top;
            Width = info.rcMonitor.Width;
            Height = info.rcMonitor.Height;
        }
        else
        {
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }
    }

    private void StartMonitorTracking()
    {
        _monitorTrackingTimer?.Stop();
        _monitorTrackingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _monitorTrackingTimer.Tick += (s, e) =>
        {
            NativeMethods.Point pt;
            NativeMethods.GetCursorPos(out pt);
            IntPtr hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (hMonitor != _currentMonitor && hMonitor != IntPtr.Zero)
            {
                _currentMonitor = hMonitor;
                PositionTopPinnedNotch();
            }
        };
        _monitorTrackingTimer.Start();
    }

    private void StartCursorTracking()
    {
        _cursorTrackingTimer?.Stop();
        _cursorTrackingTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16) // ~60Hz
        };
        _cursorTrackingTimer.Tick += (s, e) =>
        {
            NativeMethods.Point pt;
            NativeMethods.GetCursorPos(out pt);
            Left = pt.X + 10;
            Top = pt.Y + 10;
        };
        _cursorTrackingTimer.Start();

        // Launch companion stop button
        _stopButtonWindow = new StopButtonWindow();
        _stopButtonWindow.StopRequested += Close;
        _stopButtonWindow.Left = SystemParameters.PrimaryScreenWidth - 160;
        _stopButtonWindow.Top = SystemParameters.PrimaryScreenHeight - 80;
        _stopButtonWindow.Show();
    }

    private void LoadCurrentPageContent()
    {
        _isCompleted = false;
        var text = _coordinator.CurrentPageText;

        PageBadgeText.Text = $"{_coordinator.CurrentPageIndex + 1} / {_coordinator.TotalPages}";
        PrevPageButton.IsEnabled = _coordinator.HasPreviousPage;
        NextPageButton.IsEnabled = _coordinator.HasNextPage;

        WordsWrapPanel.Children.Clear();
        _wordContainers.Clear();
        _wordTextBlocks.Clear();

        _words = TextProcessor.BuildWordItems(text);
        var paragraphBreaks = TextProcessor.ParagraphBreakWordIndices(text);
        var settings = _settingsService.Settings;

        Color fontColor = ParseColor(settings.FontColor, Colors.White);
        Color cueColor = ParseColor(settings.CueColor, Color.FromRgb(255, 179, 64));

        for (int i = 0; i < _words.Count; i++)
        {
            var item = _words[i];

            // If this word starts a new paragraph, insert a paragraph break indicator
            if (paragraphBreaks.Contains(i) && i > 0)
            {
                var divider = new Border
                {
                    Width = Math.Max(200, Width - 60),
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    Margin = new Thickness(0, 10, 0, 10)
                };
                WordsWrapPanel.Children.Add(divider);
            }

            var tb = new TextBlock
            {
                Text = item.Word,
                FontSize = settings.FontSize,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 2, 4, 2)
            };

            var container = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(2, 1, 2, 1),
                Margin = new Thickness(2),
                Cursor = Cursors.Hand,
                Tag = i,
                Child = tb
            };

            container.MouseLeftButtonDown += (s, e) =>
            {
                if (s is Border b && b.Tag is int idx)
                {
                    _speechService.JumpToWord(idx);
                }
            };

            if (item.IsAnnotation)
            {
                tb.Foreground = new SolidColorBrush(cueColor);
                tb.Opacity = 0.45;
            }
            else
            {
                tb.Foreground = new SolidColorBrush(fontColor);
                tb.Opacity = 0.85;
            }

            _wordContainers.Add(container);
            _wordTextBlocks.Add(tb);
            WordsWrapPanel.Children.Add(container);
        }

        UpdateHighlighting(0);
    }

    private void OnSpeechStateChanged()
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded) return;

            int activeIndex = (int)Math.Floor(_speechService.RecognizedWordProgress);
            if (activeIndex != _lastActiveWordIndex)
            {
                _lastActiveWordIndex = activeIndex;
                UpdateHighlighting(activeIndex);
                ScrollToActiveWord(activeIndex);
            }

            RenderVisualizer(_speechService.AudioLevels);

            // Update status badge
            if (_speechService.IsSpeaking)
            {
                StatusBadge.Text = "VOICE ACTIVE";
                StatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(76, 217, 100));
            }
            else
            {
                StatusBadge.Text = "LISTENING";
                StatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(140, 140, 145));
            }

            // Check auto next page
            if (!_isCompleted && _speechService.ShouldAdvancePage && _autoNextPageTimer == null)
            {
                TriggerAutoNextPageCountdown();
            }
        });
    }

    private void OnWordJumped(int wordIndex)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _lastActiveWordIndex = wordIndex;
            UpdateHighlighting(wordIndex);
            ScrollToActiveWord(wordIndex);
        });
    }

    private void UpdateHighlighting(int activeIndex)
    {
        var settings = _settingsService.Settings;
        Color fontColor = ParseColor(settings.FontColor, Colors.White);
        Color cueColor = ParseColor(settings.CueColor, Color.FromRgb(255, 179, 64));

        for (int i = 0; i < _wordContainers.Count; i++)
        {
            var container = _wordContainers[i];
            var tb = _wordTextBlocks[i];
            var item = _words[i];

            if (i < activeIndex)
            {
                // Read word
                tb.Opacity = item.IsAnnotation ? 0.3 : 0.4;
                container.Background = Brushes.Transparent;
            }
            else if (i == activeIndex)
            {
                // Active word
                tb.Opacity = 1.0;
                container.Background = new SolidColorBrush(Color.FromArgb(90, 10, 132, 255));
                tb.Foreground = item.IsAnnotation ? new SolidColorBrush(cueColor) : Brushes.White;
            }
            else
            {
                // Unread upcoming word
                tb.Opacity = item.IsAnnotation ? 0.5 : 0.85;
                container.Background = Brushes.Transparent;
                tb.Foreground = item.IsAnnotation ? new SolidColorBrush(cueColor) : new SolidColorBrush(fontColor);
            }
        }
    }

    private void ScrollToActiveWord(int activeIndex)
    {
        if (_isManualScrolling && (DateTime.UtcNow - _lastManualScrollTime).TotalSeconds < 1.8)
        {
            return; // Honor user's manual mouse wheel scroll
        }
        _isManualScrolling = false;

        if (activeIndex < 0 || activeIndex >= _wordContainers.Count) return;

        try
        {
            var container = _wordContainers[activeIndex];
            if (!container.IsLoaded || !WordsWrapPanel.IsLoaded) return;
            if (!container.IsDescendantOf(WordsWrapPanel)) return;

            var transform = container.TransformToAncestor(WordsWrapPanel);
            var pt = transform.Transform(new Point(0, 0));

            double targetY;
            if (_settingsService.Settings.ReadingPosition == ReadingPosition.NearTop)
            {
                targetY = Math.Max(0, pt.Y - 24);
            }
            else
            {
                targetY = Math.Max(0, pt.Y - (TextScrollViewer.ActualHeight / 2.0) + (container.ActualHeight / 2.0));
            }

            TextScrollViewer.ScrollToVerticalOffset(targetY);
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"Failed to scroll to active word {activeIndex}: {ex.Message}");
        }
    }

    private void TriggerAutoNextPageCountdown()
    {
        if (_isCompleted) return;

        var settings = _settingsService.Settings;
        int delay = settings.AutoNextPageDelay;

        _coordinator.MarkCurrentPageRead();

        if (!_coordinator.HasNextPage)
        {
            // Last page completed - mark complete and gracefully dismiss
            _isCompleted = true;
            _speechService.Stop();

            NextPageBanner.Visibility = Visibility.Visible;
            NextPageBannerText.Text = "Script complete! Closing... (or press Esc)";

            _dismissTimer?.Stop();
            _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
            _dismissTimer.Tick += (s, e) =>
            {
                _dismissTimer?.Stop();
                _dismissTimer = null;
                Close();
            };
            _dismissTimer.Start();
            return;
        }

        if (delay <= 0)
        {
            AdvancePage();
            return;
        }

        _countdownSeconds = delay;
        NextPageBanner.Visibility = Visibility.Visible;
        NextPageBannerText.Text = $"Next page in {_countdownSeconds}s...";

        _autoNextPageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoNextPageTimer.Tick += (s, e) =>
        {
            _countdownSeconds--;
            if (_countdownSeconds <= 0)
            {
                _autoNextPageTimer?.Stop();
                _autoNextPageTimer = null;
                NextPageBanner.Visibility = Visibility.Collapsed;
                AdvancePage();
            }
            else
            {
                NextPageBannerText.Text = $"Next page in {_countdownSeconds}s...";
            }
        };
        _autoNextPageTimer.Start();
    }

    private void AdvancePage()
    {
        if (_coordinator.NextPage())
        {
            LoadCurrentPageContent();
            var text = _coordinator.CurrentPageText;
            _speechService.Start(text, _settingsService.Settings.ListeningMode, _settingsService.Settings.ReadingSpeedWpm);
        }
    }

    private void OnPrevPageClick(object sender, RoutedEventArgs e)
    {
        _autoNextPageTimer?.Stop();
        _autoNextPageTimer = null;
        NextPageBanner.Visibility = Visibility.Collapsed;

        if (_coordinator.PreviousPage())
        {
            LoadCurrentPageContent();
            var text = _coordinator.CurrentPageText;
            _speechService.Start(text, _settingsService.Settings.ListeningMode, _settingsService.Settings.ReadingSpeedWpm);
        }
    }

    private void OnNextPageClick(object sender, RoutedEventArgs e)
    {
        _autoNextPageTimer?.Stop();
        _autoNextPageTimer = null;
        NextPageBanner.Visibility = Visibility.Collapsed;

        if (_coordinator.NextPage())
        {
            LoadCurrentPageContent();
            var text = _coordinator.CurrentPageText;
            _speechService.Start(text, _settingsService.Settings.ListeningMode, _settingsService.Settings.ReadingSpeedWpm);
        }
    }

    private void RenderVisualizer(IReadOnlyList<double> levels)
    {
        VisualizerCanvas.Children.Clear();
        double w = VisualizerCanvas.ActualWidth;
        double h = VisualizerCanvas.ActualHeight;
        if (w <= 0 || h <= 0 || levels.Count == 0) return;

        int barCount = levels.Count;
        double barWidth = Math.Max(2, (w / barCount) - 2);

        for (int i = 0; i < barCount; i++)
        {
            double barHeight = Math.Max(2, levels[i] * h);
            var rect = new Rectangle
            {
                Width = barWidth,
                Height = barHeight,
                Fill = new SolidColorBrush(Color.FromArgb(180, 10, 132, 255)),
                RadiusX = 1,
                RadiusY = 1
            };

            Canvas.SetLeft(rect, i * (barWidth + 2));
            Canvas.SetTop(rect, h - barHeight);
            VisualizerCanvas.Children.Add(rect);
        }
    }

    private void OnScrollViewerPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _isManualScrolling = true;
        _lastManualScrollTime = DateTime.UtcNow;
        TextScrollViewer.ScrollToVerticalOffset(TextScrollViewer.VerticalOffset - (e.Delta / 3.0));
        e.Handled = true;
    }

    private void OnDragHandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            if (_speechService.IsListening)
            {
                _speechService.Stop();
            }
            else
            {
                var text = _coordinator.CurrentPageText;
                _speechService.Start(text, _settingsService.Settings.ListeningMode, _settingsService.Settings.ReadingSpeedWpm);
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Left)
        {
            OnPrevPageClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            OnNextPageClick(sender, e);
            e.Handled = true;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _speechService.StateChanged -= OnSpeechStateChanged;
        _speechService.WordJumped -= OnWordJumped;
        _speechService.Stop();

        _monitorTrackingTimer?.Stop();
        _cursorTrackingTimer?.Stop();
        _autoNextPageTimer?.Stop();
        _dismissTimer?.Stop();
        _dismissTimer = null;

        _stopButtonWindow?.Close();

        // Release awake inhibitor
        NativeMethods.SetThreadExecutionState(NativeMethods.ExecutionState.EsContinuous);
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return fallback;
        }
    }
}
