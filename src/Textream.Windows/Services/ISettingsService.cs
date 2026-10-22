using Textream.Windows.Models;

namespace Textream.Windows.Services;

public interface ISettingsService
{
    NotchSettings Current { get; }
    NotchSettings Settings => Current;
    void Save();
    void SaveSettings() => Save();
    void Reload();
    void ResetToDefaults();
}
