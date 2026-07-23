using NAudio.Wave;

namespace Lumen.App.Audio;

/// <summary>
/// Mic capture and speaker playback at the Realtime API's native format:
/// PCM16, 24 kHz, mono. Playback buffer is clearable for barge-in (LD-003).
///
/// Echo gate: while the assistant is audibly speaking, quiet mic chunks
/// (speaker bleed) are dropped so the model doesn't hear itself; loud chunks
/// still pass, preserving barge-in. Headphones make this moot.
/// </summary>
public sealed class AudioLoop : IDisposable
{
    private const int OutputSampleRate = 24000; // both providers output 24 kHz

    private readonly int _inputSampleRate;
    private readonly WaveInEvent _mic;
    private readonly WaveOutEvent _speaker;
    private readonly BufferedWaveProvider _playbackBuffer;
    private DateTime _playbackQuietSince = DateTime.MinValue;

    /// <summary>Raw PCM16 chunks from the microphone (~50 ms cadence).</summary>
    public event Action<byte[]>? MicChunk;

    /// <summary>
    /// Fired the instant the user speaks over the assistant (two consecutive
    /// above-gate chunks while playback is live). Local, zero-latency barge-in
    /// signal — the host should clear playback and cancel the response
    /// without waiting for the server VAD (LD-003).
    /// </summary>
    public event Action? UserBargeIn;

    public bool Muted { get; set; }

    /// <summary>
    /// RMS floor a mic chunk must exceed to pass while the assistant is
    /// speaking. Raise if the model still hears itself; lower if barge-in
    /// requires shouting. 0 disables the gate.
    /// </summary>
    public int EchoGateRms { get; set; } = 1000;

    private int _bargeStreak;
    private DateTime _lastBargeFire = DateTime.MinValue;

    /// <summary>Keep gating briefly after playback drains (room reverb tail).</summary>
    public TimeSpan EchoGateHangover { get; set; } = TimeSpan.FromMilliseconds(250);

    public bool AssistantSpeaking =>
        _playbackBuffer.BufferedBytes > 0 ||
        DateTime.UtcNow - _playbackQuietSince < EchoGateHangover;

    public AudioLoop(int inputSampleRate = 24000)
    {
        _inputSampleRate = inputSampleRate;

        var outFormat = new WaveFormat(OutputSampleRate, 16, 1);
        _playbackBuffer = new BufferedWaveProvider(outFormat)
        {
            BufferDuration = TimeSpan.FromMinutes(2),
            DiscardOnBufferOverflow = true
        };
        _speaker = new WaveOutEvent { DesiredLatency = 100 };
        _speaker.Init(_playbackBuffer);

        _mic = new WaveInEvent
        {
            WaveFormat = new WaveFormat(_inputSampleRate, 16, 1),
            BufferMilliseconds = 50
        };
        _mic.DataAvailable += (_, e) =>
        {
            if (Muted || e.BytesRecorded == 0) return;

            // Note the moment playback drains so the hangover window starts.
            if (_playbackBuffer.BufferedBytes == 0 && _playbackQuietSince == DateTime.MaxValue)
                _playbackQuietSince = DateTime.UtcNow;

            if (EchoGateRms > 0 && AssistantSpeaking)
            {
                if (Rms(e.Buffer, e.BytesRecorded) < EchoGateRms)
                {
                    _bargeStreak = 0;
                    return; // speaker bleed, not the user
                }

                // The user is audibly speaking over the assistant — instant barge-in.
                if (++_bargeStreak >= 2 && DateTime.UtcNow - _lastBargeFire > TimeSpan.FromMilliseconds(600))
                {
                    _lastBargeFire = DateTime.UtcNow;
                    UserBargeIn?.Invoke();
                }
            }
            else
            {
                _bargeStreak = 0;
            }

            var chunk = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, chunk, 0, e.BytesRecorded);
            MicChunk?.Invoke(chunk);
        };
    }

    public void Start()
    {
        _mic.StartRecording();
        _speaker.Play();
    }

    public void Stop()
    {
        _mic.StopRecording();
        _speaker.Stop();
        ClearPlayback();
    }

    /// <summary>Queue assistant audio for playback.</summary>
    public void Play(byte[] pcm16)
    {
        _playbackBuffer.AddSamples(pcm16, 0, pcm16.Length);
        _playbackQuietSince = DateTime.MaxValue; // actively speaking
    }

    /// <summary>Drop all unplayed assistant audio immediately (barge-in).</summary>
    public void ClearPlayback()
    {
        _playbackBuffer.ClearBuffer();
        _playbackQuietSince = DateTime.UtcNow;
    }

    private static double Rms(byte[] buffer, int count)
    {
        long sumSquares = 0;
        var samples = count / 2;
        if (samples == 0) return 0;
        for (var i = 0; i < samples; i++)
        {
            int s = BitConverter.ToInt16(buffer, i * 2);
            sumSquares += (long)s * s;
        }
        return Math.Sqrt(sumSquares / (double)samples);
    }

    public void Dispose()
    {
        _mic.Dispose();
        _speaker.Dispose();
    }
}
