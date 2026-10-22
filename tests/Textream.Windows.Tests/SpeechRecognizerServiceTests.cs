using Textream.Windows.Models;
using Textream.Windows.Services;
using Xunit;

namespace Textream.Windows.Tests;

public class MockAudioCaptureService : IAudioCaptureService
{
    public bool IsCapturing { get; private set; }
    public bool IsSpeaking { get; set; }
    public IReadOnlyList<double> AudioLevels { get; set; } = new double[30];
    public event Action<double, IReadOnlyList<double>, bool>? AudioLevelUpdated;

    public void Start() => IsCapturing = true;
    public void Stop() => IsCapturing = false;
    public void Dispose() => Stop();

    public void FireLevel(double level, bool isSpeaking)
    {
        IsSpeaking = isSpeaking;
        AudioLevelUpdated?.Invoke(level, AudioLevels, isSpeaking);
    }
}

public class SpeechRecognizerServiceTests
{
    [Fact]
    public void Start_InitializesProgressAndSkipsLeadingAnnotation()
    {
        var mockAudio = new MockAudioCaptureService();
        var settingsService = new SettingsService();
        using var service = new SpeechRecognizerService(mockAudio, settingsService);

        var text = "[smile] Hello world this is a test script.";
        service.Start(text, ListeningMode.WordTracking, 150);

        Assert.True(service.IsListening);
        // Should skip "[smile] " (8 chars)
        Assert.True(service.RecognizedCharCount >= 8);
        Assert.False(service.ShouldAdvancePage);
    }

    [Fact]
    public void JumpToWord_UpdatesWordProgressAndCharCount()
    {
        var mockAudio = new MockAudioCaptureService();
        var settingsService = new SettingsService();
        using var service = new SpeechRecognizerService(mockAudio, settingsService);

        var text = "First second third fourth fifth sixth";
        service.Start(text, ListeningMode.WordTracking, 150);

        bool jumpedFired = false;
        int jumpedIndex = -1;
        service.WordJumped += idx =>
        {
            jumpedFired = true;
            jumpedIndex = idx;
        };

        service.JumpToWord(3); // "fourth"

        Assert.True(jumpedFired);
        Assert.Equal(3, jumpedIndex);
        Assert.Equal(3, (int)service.RecognizedWordProgress);
        Assert.True(service.RecognizedCharCount >= 19); // "First second third "
    }

    [Fact]
    public void UpdateText_PreservesCharCountForDirectorLiveEditing()
    {
        var mockAudio = new MockAudioCaptureService();
        var settingsService = new SettingsService();
        using var service = new SpeechRecognizerService(mockAudio, settingsService);

        var initialText = "The quick brown fox jumps over the lazy dog";
        service.Start(initialText, ListeningMode.WordTracking, 150);
        service.JumpToWord(4); // jumps to "jumps"

        int readChars = service.RecognizedCharCount;

        // Director updates subsequent text
        var revisedText = "The quick brown fox leaps across the energetic puppy";
        service.UpdateText(revisedText, preservingCharCount: readChars);

        Assert.Equal(readChars, service.RecognizedCharCount);
        Assert.Equal(4, (int)service.RecognizedWordProgress);
    }
}
