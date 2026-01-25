using SharpGen.Runtime;
using Vortice.DirectSound;
using Vortice.Multimedia;

namespace MirClient.Audio;

public sealed class DirectSoundAudioEngine : IDisposable
{
    private bool _initialized;
    private bool _disposed;
    private IDirectSound8? _device;
    private IDirectSoundBuffer? _primaryBuffer;

    public bool IsAvailable => _initialized;
    public string? LastError { get; private set; }

    public IDirectSound8 Device => _device ?? throw new InvalidOperationException("DirectSound is not initialized.");

    public bool TryInitialize(IntPtr windowHandle, int sampleRate = 44100, int channels = 2, int bitsPerSample = 16)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DirectSoundAudioEngine));

        if (_initialized)
            return true;

        if (windowHandle == IntPtr.Zero)
        {
            LastError = "DirectSound init failed: window handle is required.";
            return false;
        }

        try
        {
            Result createResult = DSound.DirectSoundCreate8(out IDirectSound8? device);
            if (createResult.Failure || device == null)
            {
                LastError = $"DirectSoundCreate8 failed (hr=0x{createResult.Code:X8}).";
                return false;
            }

            IDirectSound8 directSound = device;

            Result coop = directSound.SetCooperativeLevel(windowHandle, CooperativeLevel.Priority);
            if (coop.Failure)
                coop = directSound.SetCooperativeLevel(windowHandle, CooperativeLevel.Normal);

            if (coop.Failure)
            {
                LastError = $"SetCooperativeLevel failed (hr=0x{coop.Code:X8}).";
                directSound.Dispose();
                return false;
            }

            IDirectSoundBuffer? primary = null;
            try
            {
                var desc = new SoundBufferDescription
                {
                    Flags = BufferFlags.PrimaryBuffer,
                    BufferBytes = 0,
                    Format = default,
                    AlgorithmFor3D = Guid.Empty
                };

                primary = directSound.CreateSoundBuffer(desc, null);
                primary.Format = new WaveFormat(sampleRate, bitsPerSample, channels);
            }
            catch
            {
                try { primary?.Dispose(); } catch {  }
                primary = null;
            }

            _device = directSound;
            _primaryBuffer = primary;
            _initialized = true;
            LastError = null;
            return true;
        }
        catch (Exception ex)
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

        try { _primaryBuffer?.Dispose(); } catch {  }
        _primaryBuffer = null;

        try { _device?.Dispose(); } catch {  }
        _device = null;

        _initialized = false;
    }
}
