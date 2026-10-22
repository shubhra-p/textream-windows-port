namespace Textream.Windows.Services;

/// <summary>
/// Voice activity detection based on audio RMS power thresholds and hangover duration.
/// Direct port of Textream macOS VoiceActivityDetector.swift.
/// </summary>
public class VoiceActivityDetector
{
    private readonly double _activationLevel;
    private readonly double _immediateActivationLevel;
    private readonly int _requiredActiveFrames;
    private readonly double _hangoverDuration;

    private int _consecutiveActiveFrames;
    private double _activeUntil = double.NegativeInfinity;

    public VoiceActivityDetector(
        double activationLevel = 0.012,
        double immediateActivationLevel = 0.04,
        int requiredActiveFrames = 2,
        double hangoverDuration = 0.75)
    {
        _activationLevel = activationLevel;
        _immediateActivationLevel = immediateActivationLevel;
        _requiredActiveFrames = requiredActiveFrames;
        _hangoverDuration = hangoverDuration;
    }

    public void Process(double level, double timestampSeconds)
    {
        if (level >= _immediateActivationLevel)
        {
            _consecutiveActiveFrames = _requiredActiveFrames;
            ExtendActivity(timestampSeconds);
        }
        else if (level >= _activationLevel)
        {
            _consecutiveActiveFrames++;
            if (_consecutiveActiveFrames >= _requiredActiveFrames)
            {
                ExtendActivity(timestampSeconds);
            }
        }
        else
        {
            _consecutiveActiveFrames = 0;
        }
    }

    public bool IsActive(double timestampSeconds)
    {
        return timestampSeconds < _activeUntil;
    }

    public void Reset()
    {
        _consecutiveActiveFrames = 0;
        _activeUntil = double.NegativeInfinity;
    }

    private void ExtendActivity(double timestampSeconds)
    {
        _activeUntil = Math.Max(_activeUntil, timestampSeconds + _hangoverDuration);
    }
}
