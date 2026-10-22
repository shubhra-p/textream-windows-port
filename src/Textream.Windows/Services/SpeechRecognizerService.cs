using System.Diagnostics;
using Textream.Windows.Common;
using Textream.Windows.Models;

namespace Textream.Windows.Services;

public interface ISpeechRecognizerService : IDisposable
{
    int RecognizedCharCount { get; }
    double RecognizedWordProgress { get; }
    bool IsListening { get; }
    bool IsStarting { get; }
    bool IsSpeaking { get; }
    string? ErrorMessage { get; }
    IReadOnlyList<double> AudioLevels { get; }
    string LastSpokenText { get; }
    bool ShouldDismiss { get; }
    bool ShouldAdvancePage { get; }

    event Action? StateChanged;
    event Action<int>? WordJumped;

    void Start(string text, ListeningMode mode, double wordsPerMinute);
    void Stop();
    void JumpTo(int charOffset);
    void JumpToWord(int wordIndex);
    void UpdateText(string text, int preservingCharCount);
    void SetSpeed(double wordsPerMinute);
}

public class SpeechRecognizerService : ISpeechRecognizerService
{
    private readonly IAudioCaptureService _audioCapture;
    private readonly ISettingsService _settingsService;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private string _sourceText = string.Empty;
    private List<string> _words = new();
    private List<(int Start, int End)> _annotationRanges = new();
    private List<int> _wordCharOffsets = new();

    private ListeningMode _mode = ListeningMode.WordTracking;
    private double _wordsPerSecond = 2.5; // ~150 WPM
    private double _currentWordProgress;
    private int _recognizedCharCount;
    private bool _isListening;
    private bool _isStarting;
    private string? _errorMessage;
    private string _lastSpokenText = string.Empty;
    private bool _shouldDismiss;
    private bool _shouldAdvancePage;

    private System.Threading.Timer? _scrollTimer;
    private readonly object _lock = new();

    public int RecognizedCharCount => _recognizedCharCount;
    public double RecognizedWordProgress => _currentWordProgress;
    public bool IsListening => _isListening;
    public bool IsStarting => _isStarting;
    public bool IsSpeaking => _audioCapture.IsSpeaking;
    public string? ErrorMessage => _errorMessage;
    public IReadOnlyList<double> AudioLevels => _audioCapture.AudioLevels;
    public string LastSpokenText => _lastSpokenText;
    public bool ShouldDismiss => _shouldDismiss;
    public bool ShouldAdvancePage => _shouldAdvancePage;

    public event Action? StateChanged;
    public event Action<int>? WordJumped;

    public SpeechRecognizerService(IAudioCaptureService audioCapture, ISettingsService settingsService)
    {
        _audioCapture = audioCapture;
        _settingsService = settingsService;
        _audioCapture.AudioLevelUpdated += OnAudioLevelUpdated;
    }

    private void OnAudioLevelUpdated(double level, IReadOnlyList<double> levels, bool isSpeaking)
    {
        StateChanged?.Invoke();
    }

    public void Start(string text, ListeningMode mode, double wordsPerMinute)
    {
        Stop();

        _mode = mode;
        _wordsPerSecond = Math.Max(0.5, wordsPerMinute / 60.0);
        _sourceText = text;
        _words = TextProcessor.SplitTextIntoWords(text);
        _annotationRanges = SpeechTextAlignment.AnnotationRanges(text);

        _wordCharOffsets.Clear();
        int offset = 0;
        foreach (var w in _words)
        {
            _wordCharOffsets.Add(offset);
            offset += w.Length + 1;
        }

        _recognizedCharCount = SpeechTextAlignment.AdvancePastAnnotations(_sourceText, _annotationRanges, 0);
        _currentWordProgress = CharOffsetToWordProgress(_recognizedCharCount);
        _shouldDismiss = false;
        _shouldAdvancePage = false;
        _lastSpokenText = string.Empty;
        _errorMessage = null;

        _isStarting = true;
        _isListening = true;

        if (_mode != ListeningMode.Classic)
        {
            _audioCapture.Start();
        }

        _isStarting = false;

        // Start high-precision 60Hz ticker for continuous scroll & speech progress
        _scrollTimer = new System.Threading.Timer(OnTick, null, 16, 16);

        AppLogger.Info($"Speech/Prompter service started in mode={_mode}, words={_words.Count}, wpm={wordsPerMinute}");
        StateChanged?.Invoke();
    }

    public void SetSpeed(double wordsPerMinute)
    {
        _wordsPerSecond = Math.Max(0.5, wordsPerMinute / 60.0);
    }

    private void OnTick(object? state)
    {
        if (!_isListening || _words.Count == 0) return;

        bool shouldAdvance = false;

        lock (_lock)
        {
            double dt = 0.016; // ~60Hz
            if (_mode == ListeningMode.Classic)
            {
                shouldAdvance = true;
            }
            else if (_mode == ListeningMode.SilencePaused)
            {
                shouldAdvance = _audioCapture.IsSpeaking;
            }
            else if (_mode == ListeningMode.WordTracking)
            {
                // In word tracking, speech drives the advance
                shouldAdvance = _audioCapture.IsSpeaking;
            }

            if (shouldAdvance)
            {
                _currentWordProgress += _wordsPerSecond * dt;
                if (_currentWordProgress >= _words.Count)
                {
                    _currentWordProgress = _words.Count;
                    if (!_shouldAdvancePage)
                    {
                        _shouldAdvancePage = true;
                        AppLogger.Info("Prompter reached end of page.");
                    }
                }

                int currentWordIndex = (int)Math.Floor(_currentWordProgress);
                if (currentWordIndex < _wordCharOffsets.Count)
                {
                    int targetChar = _wordCharOffsets[currentWordIndex];
                    _recognizedCharCount = SpeechTextAlignment.AdvancePastAnnotations(_sourceText, _annotationRanges, targetChar);
                }
                else
                {
                    _recognizedCharCount = _sourceText.Length;
                }
            }
        }

        StateChanged?.Invoke();
    }

    public void JumpTo(int charOffset)
    {
        lock (_lock)
        {
            int clamped = Math.Max(0, Math.Min(charOffset, _sourceText.Length));
            _recognizedCharCount = SpeechTextAlignment.AdvancePastAnnotations(_sourceText, _annotationRanges, clamped);
            _currentWordProgress = CharOffsetToWordProgress(_recognizedCharCount);
            _shouldAdvancePage = false;
        }

        AppLogger.Info($"Jumped to char offset {_recognizedCharCount} (word progress {_currentWordProgress:F1})");
        WordJumped?.Invoke((int)_currentWordProgress);
        StateChanged?.Invoke();
    }

    public void JumpToWord(int wordIndex)
    {
        if (wordIndex < 0 || wordIndex >= _words.Count) return;

        lock (_lock)
        {
            _currentWordProgress = wordIndex;
            int charOffset = _wordCharOffsets[wordIndex];
            _recognizedCharCount = SpeechTextAlignment.AdvancePastAnnotations(_sourceText, _annotationRanges, charOffset);
            _shouldAdvancePage = false;
        }

        AppLogger.Info($"Jumped to word index {wordIndex}");
        WordJumped?.Invoke(wordIndex);
        StateChanged?.Invoke();
    }

    public void UpdateText(string text, int preservingCharCount)
    {
        lock (_lock)
        {
            _sourceText = text;
            _words = TextProcessor.SplitTextIntoWords(text);
            _annotationRanges = SpeechTextAlignment.AnnotationRanges(text);

            _wordCharOffsets.Clear();
            int offset = 0;
            foreach (var w in _words)
            {
                _wordCharOffsets.Add(offset);
                offset += w.Length + 1;
            }

            _recognizedCharCount = Math.Min(preservingCharCount, _sourceText.Length);
            _recognizedCharCount = SpeechTextAlignment.AdvancePastAnnotations(_sourceText, _annotationRanges, _recognizedCharCount);
            _currentWordProgress = CharOffsetToWordProgress(_recognizedCharCount);
        }

        AppLogger.Info($"Updated prompter text (preserved char count: {_recognizedCharCount})");
        StateChanged?.Invoke();
    }

    private double CharOffsetToWordProgress(int charOffset)
    {
        if (_wordCharOffsets.Count == 0) return 0;
        for (int i = 0; i < _wordCharOffsets.Count; i++)
        {
            if (_wordCharOffsets[i] >= charOffset)
            {
                return i;
            }
        }
        return _wordCharOffsets.Count;
    }

    public void Stop()
    {
        _scrollTimer?.Dispose();
        _scrollTimer = null;

        _audioCapture.Stop();
        _isListening = false;
        _isStarting = false;
        _shouldAdvancePage = false;
        _shouldDismiss = true;

        AppLogger.Info("Speech/Prompter service stopped.");
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        Stop();
        _audioCapture.AudioLevelUpdated -= OnAudioLevelUpdated;
        GC.SuppressFinalize(this);
    }
}
