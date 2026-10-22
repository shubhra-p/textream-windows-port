using System.Windows;
using System.Windows.Controls;
using Textream.Windows.Models;
using Textream.Windows.Services;

namespace Textream.Windows.Views;

public partial class SettingsWindow : Window
{
    private readonly ISettingsService _settingsService;

    public SettingsWindow(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = _settingsService.Settings;

        // General
        KeepAwakeCheck.IsChecked = s.KeepScreenAwake;
        HideScreenShareCheck.IsChecked = s.HideFromScreenShare;
        ShowReadBadgeCheck.IsChecked = s.ShowReadPageBadge;
        SelectComboByTag(AutoNextDelayCombo, s.AutoNextPageDelay.ToString());

        // Prompter
        SelectComboByTag(OverlayModeCombo, s.OverlayMode.ToString());
        FollowCursorCheck.IsChecked = s.FollowCursorWhenUndocked;
        WidthSlider.Value = s.NotchWidth;
        HeightSlider.Value = s.TextAreaHeight;
        OpacitySlider.Value = s.PrompterOpacity;
        FontSizeSlider.Value = s.FontSize;
        SelectComboByTag(ReadingPosCombo, s.ReadingPosition.ToString());

        // Speech & Audio
        SelectComboByTag(ListeningModeCombo, s.ListeningMode.ToString());
        SpeedSlider.Value = s.ReadingSpeedWpm;

        // Remote
        BrowserServerCheck.IsChecked = s.BrowserServerEnabled;
        DirectorServerCheck.IsChecked = s.DirectorServerEnabled;

        // Displays
        SelectComboByTag(ExternalModeCombo, s.ExternalDisplayMode.ToString());
        SelectComboByTag(MirrorAxisCombo, s.MirrorAxis.ToString());
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var s = _settingsService.Settings;

        // General
        s.KeepScreenAwake = KeepAwakeCheck.IsChecked ?? true;
        s.HideFromScreenShare = HideScreenShareCheck.IsChecked ?? true;
        s.ShowReadPageBadge = ShowReadBadgeCheck.IsChecked ?? true;
        if (int.TryParse(GetComboSelectedTag(AutoNextDelayCombo), out int delay))
        {
            s.AutoNextPageDelay = delay;
        }

        // Prompter
        if (Enum.TryParse<OverlayMode>(GetComboSelectedTag(OverlayModeCombo), out var mode))
        {
            s.OverlayMode = mode;
        }
        s.FollowCursorWhenUndocked = FollowCursorCheck.IsChecked ?? false;
        s.NotchWidth = WidthSlider.Value;
        s.TextAreaHeight = HeightSlider.Value;
        s.PrompterOpacity = OpacitySlider.Value;
        s.FontSize = FontSizeSlider.Value;
        if (Enum.TryParse<ReadingPosition>(GetComboSelectedTag(ReadingPosCombo), out var pos))
        {
            s.ReadingPosition = pos;
        }

        // Speech & Audio
        if (Enum.TryParse<ListeningMode>(GetComboSelectedTag(ListeningModeCombo), out var lMode))
        {
            s.ListeningMode = lMode;
        }
        s.ReadingSpeedWpm = SpeedSlider.Value;

        // Remote
        s.BrowserServerEnabled = BrowserServerCheck.IsChecked ?? false;
        s.DirectorServerEnabled = DirectorServerCheck.IsChecked ?? false;

        // Displays
        if (Enum.TryParse<ExternalDisplayMode>(GetComboSelectedTag(ExternalModeCombo), out var extMode))
        {
            s.ExternalDisplayMode = extMode;
        }
        if (Enum.TryParse<MirrorAxis>(GetComboSelectedTag(MirrorAxisCombo), out var mAxis))
        {
            s.MirrorAxis = mAxis;
        }

        _settingsService.SaveSettings();
        Close();
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                combo.SelectedItem = item;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }

    private static string GetComboSelectedTag(ComboBox combo)
    {
        if (combo.SelectedItem is ComboBoxItem item && item.Tag != null)
        {
            return item.Tag.ToString() ?? string.Empty;
        }
        return string.Empty;
    }
}
