using Textream.Windows.Services;
using Xunit;

namespace Textream.Windows.Tests;

public class AudioCaptureServiceTests
{
    [Fact]
    public void InitialState_NotCapturing_AndAudioLevelsInitialized()
    {
        using var service = new AudioCaptureService();
        Assert.False(service.IsCapturing);
        Assert.Equal(30, service.AudioLevels.Count);
        Assert.All(service.AudioLevels, level => Assert.Equal(0.0, level));
    }

    [Fact]
    public void StartAndStop_CyclesWithoutThrowing()
    {
        using var service = new AudioCaptureService();
        service.Start();
        // May or may not capture depending on whether audio hardware is connected in environment
        service.Stop();
        Assert.False(service.IsCapturing);
    }

    [Fact]
    public void Stop_WhenNotStarted_DoesNotThrow()
    {
        using var service = new AudioCaptureService();
        service.Stop();
        service.Stop();
        Assert.False(service.IsCapturing);
    }
}
