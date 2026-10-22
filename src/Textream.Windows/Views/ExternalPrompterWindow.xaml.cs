using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Textream.Windows.Interop;
using Textream.Windows.Models;
using Textream.Windows.Services;

namespace Textream.Windows.Views;

public partial class ExternalPrompterWindow : Window
{
    private readonly ISpeechRecognizerService _speechService;
    private readonly ISettingsService _settingsService;
    private readonly ITextreamCoordinator _coordinator;

    private List<WordItem> _words = new();
    private readonly List<Border> _wordContainers = new();
    private readonly List<TextBlock> _wordTextBlocks = new();

    public ExternalPrompterWindow(
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
        ApplyMirrorTransform();
        PositionOnExternalDisplay();
        LoadContent();

        _speechService.StateChanged += OnSpeechStateChanged;
    }

    private void ApplyMirrorTransform()
    {
        var settings = _settingsService.Settings;
        if (settings.ExternalDisplayMode == ExternalDisplayMode.Mirror)
        {
            switch (settings.MirrorAxis)
            {
                case MirrorAxis.Horizontal:
                    MirrorTransform.ScaleX = -1;
                    MirrorTransform.ScaleY = 1;
                    break;
                case MirrorAxis.Vertical:
                    MirrorTransform.ScaleX = 1;
                    MirrorTransform.ScaleY = -1;
                    break;
                case MirrorAxis.Both:
                    MirrorTransform.ScaleX = -1;
                    MirrorTransform.ScaleY = -1;
                    break;
            }
        }
        else
        {
            MirrorTransform.ScaleX = 1;
            MirrorTransform.ScaleY = 1;
        }
    }

    private void PositionOnExternalDisplay()
    {
        // Target second monitor if present, otherwise primary
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = SystemParameters.VirtualScreenWidth;
        var primaryWidth = SystemParameters.PrimaryScreenWidth;

        if (virtualWidth > primaryWidth)
        {
            // Secondary monitor exists
            Left = primaryWidth;
            Top = 0;
            Width = virtualWidth - primaryWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }
        else
        {
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;
        }
    }

    private void LoadContent()
    {
        var text = _coordinator.CurrentPageText;
        _words = TextProcessor.BuildWordItems(text);

        WordsWrapPanel.Children.Clear();
        _wordContainers.Clear();
        _wordTextBlocks.Clear();

        var settings = _settingsService.Settings;
        Color fontColor = ParseColor(settings.FontColor, Colors.White);
        Color cueColor = ParseColor(settings.CueColor, Color.FromRgb(255, 179, 64));

        for (int i = 0; i < _words.Count; i++)
        {
            var item = _words[i];
            var tb = new TextBlock
            {
                Text = item.Word,
                FontSize = Math.Max(28, settings.FontSize * 1.5),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(8, 4, 8, 4)
            };

            var container = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(4),
                Child = tb
            };

            if (item.IsAnnotation)
            {
                tb.Foreground = new SolidColorBrush(cueColor);
                tb.Opacity = 0.5;
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
            UpdateHighlighting(activeIndex);
            ScrollToActiveWord(activeIndex);
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
                tb.Opacity = 0.35;
                container.Background = Brushes.Transparent;
            }
            else if (i == activeIndex)
            {
                tb.Opacity = 1.0;
                container.Background = new SolidColorBrush(Color.FromArgb(90, 10, 132, 255));
                tb.Foreground = item.IsAnnotation ? new SolidColorBrush(cueColor) : Brushes.White;
            }
            else
            {
                tb.Opacity = item.IsAnnotation ? 0.5 : 0.85;
                container.Background = Brushes.Transparent;
                tb.Foreground = item.IsAnnotation ? new SolidColorBrush(cueColor) : new SolidColorBrush(fontColor);
            }
        }
    }

    private void ScrollToActiveWord(int activeIndex)
    {
        if (activeIndex < 0 || activeIndex >= _wordContainers.Count) return;

        try
        {
            var container = _wordContainers[activeIndex];
            if (!container.IsLoaded || !WordsWrapPanel.IsLoaded) return;
            if (!container.IsDescendantOf(WordsWrapPanel)) return;

            var transform = container.TransformToAncestor(WordsWrapPanel);
            var pt = transform.Transform(new Point(0, 0));

            double targetY = Math.Max(0, pt.Y - (TextScrollViewer.ActualHeight / 2.0));
            TextScrollViewer.ScrollToVerticalOffset(targetY);
        }
        catch
        {
            // Ignore during visual tree layout passes
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _speechService.StateChanged -= OnSpeechStateChanged;
    }

    private static Color ParseColor(string hex, Color fallback)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return fallback; }
    }
}
