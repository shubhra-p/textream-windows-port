using Textream.Windows.Services;
using Xunit;

namespace Textream.Windows.Tests;

public class VoiceActivityDetectorTests
{
    [Fact]
    public void IsActive_Initially_ReturnsFalse()
    {
        var vad = new VoiceActivityDetector();
        Assert.False(vad.IsActive(0.0));
    }

    [Fact]
    public void Process_ImmediateActivationLevel_ActivatesImmediately()
    {
        var vad = new VoiceActivityDetector(activationLevel: 0.012, immediateActivationLevel: 0.04, hangoverDuration: 0.75);

        // Immediate activation threshold is 0.04
        vad.Process(0.05, 1.0);

        Assert.True(vad.IsActive(1.0));
        Assert.True(vad.IsActive(1.5));
        Assert.False(vad.IsActive(1.8)); // 1.0 + 0.75 = 1.75
    }

    [Fact]
    public void Process_ConsecutiveFramesAboveActivation_ActivatesAfterRequiredFrames()
    {
        var vad = new VoiceActivityDetector(activationLevel: 0.012, immediateActivationLevel: 0.04, requiredActiveFrames: 2, hangoverDuration: 0.5);

        // First frame above activation but below immediate
        vad.Process(0.02, 1.0);
        Assert.False(vad.IsActive(1.0));

        // Second frame above activation
        vad.Process(0.02, 1.05);
        Assert.True(vad.IsActive(1.05));
        Assert.True(vad.IsActive(1.4));
        Assert.False(vad.IsActive(1.6)); // 1.05 + 0.5 = 1.55
    }

    [Fact]
    public void Process_InterruptedFrames_ResetsConsecutiveCounter()
    {
        var vad = new VoiceActivityDetector(activationLevel: 0.012, immediateActivationLevel: 0.04, requiredActiveFrames: 2);

        vad.Process(0.02, 1.0); // 1st active frame
        vad.Process(0.005, 1.05); // Quiet frame -> resets counter
        vad.Process(0.02, 1.10); // 1st active frame again
        Assert.False(vad.IsActive(1.10)); // Still not active because reset occurred
    }

    [Fact]
    public void Reset_ClearsActiveStateImmediately()
    {
        var vad = new VoiceActivityDetector();
        vad.Process(0.1, 1.0);
        Assert.True(vad.IsActive(1.2));

        vad.Reset();
        Assert.False(vad.IsActive(1.2));
    }
}
