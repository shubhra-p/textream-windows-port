using Textream.Windows.Services;
using Textream.Windows.ViewModels;
using Xunit;

namespace Textream.Windows.Tests;

public class MainViewModelTests
{
    private readonly TextreamCoordinator _coordinator = new();
    private readonly SettingsService _settingsService;

    public MainViewModelTests()
    {
        var tempSettingsPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"textream_vm_test_{System.Guid.NewGuid():N}.json");
        _settingsService = new SettingsService(tempSettingsPath);
    }

    [Fact]
    public void PrompterButtonProperties_ReflectIdleAndActiveStates()
    {
        var vm = new MainViewModel(_coordinator, _settingsService);

        // Initially Idle
        Assert.False(vm.IsPrompterActive);
        Assert.Contains("▶", vm.PrompterButtonLabel);
        Assert.Contains("Start", vm.PrompterButtonLabel);
        Assert.Equal("#2563EB", vm.PrompterButtonBackground);

        // Toggle to Active
        vm.TogglePrompterCommand.Execute(null);
        Assert.True(vm.IsPrompterActive);
        Assert.Contains("⏹", vm.PrompterButtonLabel);
        Assert.Contains("Stop", vm.PrompterButtonLabel);
        Assert.Equal("#DC2626", vm.PrompterButtonBackground);

        // Toggle back to Idle
        vm.TogglePrompterCommand.Execute(null);
        Assert.False(vm.IsPrompterActive);
        Assert.Contains("▶", vm.PrompterButtonLabel);
        Assert.Equal("#2563EB", vm.PrompterButtonBackground);
    }

    [Fact]
    public void DictationButtonProperties_ReflectIdleAndActiveStates()
    {
        var vm = new MainViewModel(_coordinator, _settingsService);

        // Initially Idle
        Assert.False(vm.IsDictating);
        Assert.Contains("Dictate", vm.DictationButtonLabel);
        Assert.Equal("#27272A", vm.DictationButtonBackground);

        // Toggle to Active
        vm.ToggleDictationCommand.Execute(null);
        Assert.True(vm.IsDictating);
        Assert.Contains("Stop", vm.DictationButtonLabel);
        Assert.Equal("#D97706", vm.DictationButtonBackground);

        // Toggle back to Idle
        vm.ToggleDictationCommand.Execute(null);
        Assert.False(vm.IsDictating);
        Assert.Contains("Dictate", vm.DictationButtonLabel);
        Assert.Equal("#27272A", vm.DictationButtonBackground);
    }
}
