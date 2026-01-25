using MirClient.Audio.Bass;

namespace MirClient.Audio;

public sealed class BassAudioEngine : IDisposable
{
    private bool _initialized;
    private bool _disposed;

    public bool IsAvailable => _initialized;
    public string? LastError { get; private set; }

    public bool TryInitialize(int sampleRate = 44100)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BassAudioEngine));

        if (_initialized)
            return true;

        try
        {
            if (!BassNative.BASS_Init(device: -1, freq: (uint)sampleRate, flags: BassNative.BassInitFlags.Default, win: IntPtr.Zero, clsid: IntPtr.Zero))
            {
                int err = BassNative.BASS_ErrorGetCode();
                LastError = $"BASS_Init failed (err={err}).";
                return false;
            }

            _initialized = true;
            LastError = null;
            return true;
        }
        catch (DllNotFoundException ex)
        {
            LastError = ex.Message;
            return false;
        }
        catch (BadImageFormatException ex)
        {
            LastError = ex.Message;
            return false;
        }
        catch (EntryPointNotFoundException ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_initialized)
        {
            try
            {
                BassNative.BASS_Free();
            }
            catch
            {
                
            }
        }
    }
}

