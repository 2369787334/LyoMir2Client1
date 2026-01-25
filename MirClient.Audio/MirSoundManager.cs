using Vortice;
using Vortice.DirectSound;
using Vortice.Multimedia;

namespace MirClient.Audio;

public sealed class MirSoundManager : IDisposable
{
    private sealed class CachedSound
    {
        public CachedSound(string filePath, WaveFormat format, byte[] data)
        {
            FilePath = filePath;
            Format = format;
            Data = data;
        }

        public string FilePath { get; }
        public WaveFormat Format { get; }
        public byte[] Data { get; }
    }

    private readonly DirectSoundAudioEngine _engine = new();
    private MirSoundList? _soundList;
    private string? _resourceRoot;
    private bool _disabledDueToInitFailure;
    private string? _disabledReason;
    private IntPtr _windowHandle;

    private readonly object _sync = new();
    private readonly Dictionary<string, CachedSound> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDirectSoundBuffer> _activeSfx = new();

    private IDirectSoundBuffer? _bgmBuffer;
    private int _bgmId = -1;
    private string? _bgmFilePath;
    private float _musicVolume = 0.6f;
    private float _sfxVolume = 0.8f;

    private float _bgmCurrentVolume;
    private long _bgmFadeStartMs;
    private long _bgmFadeEndMs;
    private float _bgmFadeFromVolume;
    private float _bgmFadeToVolume;
    private bool _bgmFadeStopWhenDone;

    public bool Enabled { get; set; } = true;
    public Action<string>? Log { get; set; }
    public int BgmFadeMs { get; set; } = 600;

    public void Initialize(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
            return;

        _windowHandle = windowHandle;

        if (!Enabled || _disabledDueToInitFailure)
            return;

        TryEnsureEngineInitialized();
    }

    private void DisableAudio(string reason)
    {
        if (_disabledDueToInitFailure)
            return;

        _disabledDueToInitFailure = true;
        _disabledReason = reason;
        Enabled = false;

        if (!string.IsNullOrWhiteSpace(reason))
            Log?.Invoke($"[snd] disabled: {reason}");
        else
            Log?.Invoke("[snd] disabled");
    }

    private bool TryEnsureEngineInitialized()
    {
        if (!Enabled)
            return false;

        if (_disabledDueToInitFailure)
            return false;

        if (_windowHandle == IntPtr.Zero)
            return false;

        if (_engine.TryInitialize(_windowHandle))
            return true;

        DisableAudio(_engine.LastError ?? "DirectSound init failed");
        return false;
    }

    public void Tick(long nowMs)
    {
        lock (_sync)
        {
            CleanupActiveSfxNoLock();
            TickBgmFadeNoLock(nowMs);
        }
    }

    public void SetResourceRoot(string resourceRoot)
    {
        if (string.IsNullOrWhiteSpace(resourceRoot))
            throw new ArgumentException("Resource root is required.", nameof(resourceRoot));

        string fullRoot = Path.GetFullPath(resourceRoot);

        bool rootChanged = _resourceRoot != null &&
                           !string.Equals(_resourceRoot, fullRoot, StringComparison.OrdinalIgnoreCase);

        _resourceRoot = fullRoot;

        string? soundListPath = TryResolveSoundListPath(_resourceRoot);
        if (soundListPath != null && MirSoundList.TryLoad(soundListPath, out MirSoundList list))
        {
            _soundList = list;
            Log?.Invoke($"[snd] sound2.lst loaded: {soundListPath}");
        }
        else
        {
            _soundList = null;
        }

        if (rootChanged)
        {
            lock (_sync)
            {
                StopBgmInternalNoLock(immediate: true);
                ClearCacheNoLock();
            }
        }
    }

    public void SetMusicVolume(float volume)
    {
        _musicVolume = Math.Clamp(volume, 0f, 1f);

        lock (_sync)
        {
            if (_bgmBuffer == null || !_engine.IsAvailable)
                return;

            if (IsBgmFadeActiveNoLock() && !_bgmFadeStopWhenDone)
                _bgmFadeToVolume = _musicVolume;
            else
                SetBufferVolumeSafe(_bgmBuffer, _musicVolume);
        }
    }

    public void SetSfxVolume(float volume)
    {
        _sfxVolume = Math.Clamp(volume, 0f, 1f);
    }

    public void PlayBgmById(int musicId)
    {
        if (!Enabled)
            return;

        if (_resourceRoot == null)
            return;

        if (musicId < 0)
        {
            StopBgm();
            return;
        }

        lock (_sync)
        {
            if (_bgmBuffer != null && _bgmId == musicId)
                return;

            string? filePath = ResolveSoundFilePath(_resourceRoot, musicId);
            if (filePath == null)
            {
                Log?.Invoke($"[snd] bgm not found: id={musicId}");
                return;
            }

            if (!TryEnsureEngineInitialized())
                return;

            long nowMs = Environment.TickCount64;

            StopBgmInternalNoLock(immediate: true);

            if (!TryCreateBgmBufferNoLock(filePath, out IDirectSoundBuffer? buffer))
                return;

            _bgmBuffer = buffer;
            _bgmId = musicId;
            _bgmFilePath = null;

            StartBgmNoLock(buffer!, nowMs);
            Log?.Invoke($"[snd] bgm playing id={musicId} file='{filePath}'");
        }
    }

    public void PlaySfxById(int soundId, float volume = 1.0f, float pan = 0.0f, bool loop = false)
    {
        if (!Enabled)
            return;

        if (_resourceRoot == null)
            return;

        if (soundId < 0)
            return;

        lock (_sync)
        {
            string? filePath = ResolveSoundFilePath(_resourceRoot, soundId);
            if (filePath == null)
            {
                Log?.Invoke($"[snd] sfx not found: id={soundId}");
                return;
            }

            if (!TryEnsureEngineInitialized())
                return;

            PlaySfxByFilePathNoLock(filePath, volume, pan, loop);
        }
    }

    public void PlaySoundFile(string fileName, bool loop)
    {
        if (!Enabled)
            return;

        if (_resourceRoot == null)
            return;

        if (string.IsNullOrWhiteSpace(fileName))
        {
            if (loop)
                StopBgm();
            return;
        }

        string? filePath = ResolveArbitrarySoundFilePath(_resourceRoot, fileName);
        if (filePath == null)
        {
            Log?.Invoke($"[snd] file not found: '{fileName}'");
            return;
        }

        lock (_sync)
        {
            if (!TryEnsureEngineInitialized())
                return;

            if (loop)
                PlayBgmByFilePathNoLock(filePath);
            else
                PlaySfxByFilePathNoLock(filePath, volume: 1.0f, pan: 0.0f, loop: false);
        }
    }

    public void SilenceSound()
    {
        lock (_sync)
        {
            StopBgmInternalNoLock(immediate: true);

            if (_activeSfx.Count > 0)
            {
                foreach (IDirectSoundBuffer sfx in _activeSfx)
                {
                    try { sfx.Stop(); } catch {  }
                    try { sfx.Dispose(); } catch {  }
                }

                _activeSfx.Clear();
            }
        }
    }

    public void StopBgm()
    {
        lock (_sync)
            StopBgmInternalNoLock(immediate: false);
    }

    private void StopBgmInternalNoLock(bool immediate)
    {
        if (_bgmBuffer == null)
            return;

        IDirectSoundBuffer buffer = _bgmBuffer;

        if (!_engine.IsAvailable)
        {
            _bgmBuffer = null;
            _bgmId = -1;
            _bgmFilePath = null;
            ClearBgmFadeNoLock();
            try { buffer.Dispose(); } catch {  }
            return;
        }

        if (!immediate)
        {
            int fadeMs = Math.Max(0, BgmFadeMs);
            if (fadeMs > 0)
            {
                long nowMs = Environment.TickCount64;
                BeginBgmFadeNoLock(nowMs, _bgmCurrentVolume, toVolume: 0f, fadeMs, stopWhenDone: true);
                return;
            }
        }

        _bgmBuffer = null;
        _bgmId = -1;
        _bgmFilePath = null;
        ClearBgmFadeNoLock();

        try { buffer.Stop(); } catch {  }
        try { buffer.Dispose(); } catch {  }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            StopBgmInternalNoLock(immediate: true);

            if (_activeSfx.Count > 0)
            {
                foreach (IDirectSoundBuffer sfx in _activeSfx)
                {
                    try { sfx.Stop(); } catch {  }
                    try { sfx.Dispose(); } catch {  }
                }

                _activeSfx.Clear();
            }

            ClearCacheNoLock();
            _engine.Dispose();
        }
    }

    private void ClearCacheNoLock()
    {
        if (_cache.Count == 0)
            return;

        _cache.Clear();
    }

    private void CleanupActiveSfxNoLock()
    {
        if (_activeSfx.Count == 0)
            return;

        for (int i = _activeSfx.Count - 1; i >= 0; i--)
        {
            IDirectSoundBuffer sfx = _activeSfx[i];
            BufferStatus status;
            try
            {
                status = (BufferStatus)sfx.Status;
            }
            catch
            {
                _activeSfx.RemoveAt(i);
                try { sfx.Dispose(); } catch {  }
                continue;
            }

            if ((status & BufferStatus.Playing) != 0)
                continue;

            _activeSfx.RemoveAt(i);
            try { sfx.Dispose(); } catch {  }
        }
    }

    private void StartBgmNoLock(IDirectSoundBuffer buffer, long nowMs)
    {
        int fadeMs = Math.Max(0, BgmFadeMs);
        float initial = fadeMs > 0 ? 0f : _musicVolume;
        _bgmCurrentVolume = initial;
        SetBufferVolumeSafe(buffer, initial);
        SetBufferPanSafe(buffer, pan: 0f);

        try
        {
            buffer.SetCurrentPosition(0);
            buffer.Play(0, PlayFlags.Looping);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[snd] bgm play failed: {ex.Message}");
            StopBgmInternalNoLock(immediate: true);
            return;
        }

        if (fadeMs > 0)
            BeginBgmFadeNoLock(nowMs, fromVolume: 0f, toVolume: _musicVolume, fadeMs, stopWhenDone: false);
        else
            ClearBgmFadeNoLock();
    }

    private void TickBgmFadeNoLock(long nowMs)
    {
        if (_bgmBuffer == null)
            return;

        if (!IsBgmFadeActiveNoLock())
            return;

        if (nowMs >= _bgmFadeEndMs)
        {
            _bgmCurrentVolume = _bgmFadeToVolume;
            SetBufferVolumeSafe(_bgmBuffer, _bgmCurrentVolume);

            bool stop = _bgmFadeStopWhenDone;
            ClearBgmFadeNoLock();

            if (stop)
                StopBgmInternalNoLock(immediate: true);

            return;
        }

        float t = (nowMs - _bgmFadeStartMs) / (float)Math.Max(1, _bgmFadeEndMs - _bgmFadeStartMs);
        float v = _bgmFadeFromVolume + ((_bgmFadeToVolume - _bgmFadeFromVolume) * t);
        _bgmCurrentVolume = v;
        SetBufferVolumeSafe(_bgmBuffer, v);
    }

    private void BeginBgmFadeNoLock(long nowMs, float fromVolume, float toVolume, int fadeMs, bool stopWhenDone)
    {
        _bgmFadeStartMs = nowMs;
        _bgmFadeEndMs = nowMs + Math.Max(1, fadeMs);
        _bgmFadeFromVolume = Math.Clamp(fromVolume, 0f, 1f);
        _bgmFadeToVolume = Math.Clamp(toVolume, 0f, 1f);
        _bgmFadeStopWhenDone = stopWhenDone;
    }

    private bool IsBgmFadeActiveNoLock() => _bgmFadeEndMs > _bgmFadeStartMs;

    private void ClearBgmFadeNoLock()
    {
        _bgmFadeStartMs = 0;
        _bgmFadeEndMs = 0;
        _bgmFadeFromVolume = 0;
        _bgmFadeToVolume = 0;
        _bgmFadeStopWhenDone = false;
    }

    private bool TryCreateBgmBufferNoLock(string filePath, out IDirectSoundBuffer? buffer)
    {
        buffer = null;

        if (!TryGetCachedSoundNoLock(filePath, out CachedSound? cached) || cached == null)
            return false;

        try
        {
            return TryCreateSoundBufferNoLock(cached, out buffer);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[snd] create bgm buffer failed: {ex.Message}");
            return false;
        }
    }

    private void PlayBgmByFilePathNoLock(string filePath)
    {
        if (!Enabled)
            return;

        if (_resourceRoot == null)
            return;

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            Log?.Invoke($"[snd] bgm file not found: '{filePath}'");
            return;
        }

        if (_bgmBuffer != null && string.Equals(_bgmFilePath, fullPath, StringComparison.OrdinalIgnoreCase))
            return;

        if (!TryEnsureEngineInitialized())
            return;

        long nowMs = Environment.TickCount64;
        StopBgmInternalNoLock(immediate: true);

        if (!TryCreateBgmBufferNoLock(fullPath, out IDirectSoundBuffer? buffer))
            return;

        _bgmBuffer = buffer;
        _bgmId = -1;
        _bgmFilePath = fullPath;

        StartBgmNoLock(buffer!, nowMs);
        Log?.Invoke($"[snd] bgm playing file='{fullPath}'");
    }

    private void PlaySfxByFilePathNoLock(string filePath, float volume, float pan, bool loop)
    {
        if (!Enabled)
            return;

        if (_resourceRoot == null)
            return;

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            Log?.Invoke($"[snd] sfx file not found: '{filePath}'");
            return;
        }

        if (!TryEnsureEngineInitialized())
            return;

        if (!TryGetCachedSoundNoLock(fullPath, out CachedSound? cached) || cached == null)
            return;

        float finalVolume = Math.Clamp(_sfxVolume * Math.Clamp(volume, 0f, 1f), 0f, 1f);
        float finalPan = Math.Clamp(pan, -1f, 1f);

        if (!TryCreateSoundBufferNoLock(cached, out IDirectSoundBuffer? created) || created == null)
            return;

        IDirectSoundBuffer buffer = created;

        try
        {
            buffer.SetCurrentPosition(0);
            SetBufferVolumeSafe(buffer, finalVolume);
            SetBufferPanSafe(buffer, finalPan);
            buffer.Play(0, loop ? PlayFlags.Looping : PlayFlags.None);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[snd] sfx play failed: {ex.Message}");
            try { buffer.Dispose(); } catch {  }
            return;
        }

        _activeSfx.Add(buffer);
    }

    private bool TryCreateSoundBufferNoLock(CachedSound cached, out IDirectSoundBuffer? buffer)
    {
        buffer = null;

        if (!_engine.IsAvailable)
            return false;

        try
        {
            var desc = new SoundBufferDescription
            {
                Flags = BufferFlags.ControlVolume | BufferFlags.ControlPan | BufferFlags.GlobalFocus | BufferFlags.Static,
                BufferBytes = cached.Data.Length,
                Format = cached.Format,
                AlgorithmFor3D = Guid.Empty
            };

            IDirectSoundBuffer created = _engine.Device.CreateSoundBuffer(desc, null);
            created.Write<byte>(cached.Data, bufferOffset: 0, LockFlags.None);
            created.SetCurrentPosition(0);
            buffer = created;
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[snd] CreateSoundBuffer failed: {ex.Message}");
            try { buffer?.Dispose(); } catch {  }
            buffer = null;
            return false;
        }
    }

    private bool TryGetCachedSoundNoLock(string fullPath, out CachedSound? cached)
    {
        cached = null;

        if (!_engine.IsAvailable)
            return false;

        if (_cache.TryGetValue(fullPath, out CachedSound? existing))
        {
            cached = existing;
            return true;
        }

        try
        {
            using FileStream fs = File.OpenRead(fullPath);
            using var soundStream = new SoundStream(fs);
            WaveFormat? formatCandidate = soundStream.Format;
            if (formatCandidate == null)
            {
                Log?.Invoke($"[snd] unsupported format: '{fullPath}'");
                return false;
            }

            WaveFormat format = formatCandidate;
            using DataStream dataStream = soundStream.ToDataStream();

            int len = checked((int)dataStream.Length);
            if (len <= 0)
            {
                Log?.Invoke($"[snd] empty sound data: '{fullPath}'");
                return false;
            }

            var data = new byte[len];
            int read = dataStream.Read(data, 0, len);
            if (read <= 0)
            {
                Log?.Invoke($"[snd] sound data read failed: '{fullPath}'");
                return false;
            }

            if (read != len)
                Array.Resize(ref data, read);

            cached = new CachedSound(fullPath, format, data);
            _cache[fullPath] = cached;
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"[snd] load failed: '{fullPath}': {ex.Message}");
            return false;
        }
    }

    private static int ToDirectSoundVolume(float linear)
    {
        if (linear <= 0f)
            return -10000;

        float v = Math.Clamp(linear, 0.0001f, 1f);
        float db = 20f * MathF.Log10(v);
        int ds = (int)MathF.Round(db * 100f);
        return Math.Clamp(ds, -10000, 0);
    }

    private static int ToDirectSoundPan(float pan)
    {
        float p = Math.Clamp(pan, -1f, 1f);
        return (int)MathF.Round(p * 10000f);
    }

    private static void SetBufferVolumeSafe(IDirectSoundBuffer buffer, float linearVolume)
    {
        try { buffer.SetVolume(ToDirectSoundVolume(linearVolume)); } catch {  }
    }

    private static void SetBufferPanSafe(IDirectSoundBuffer buffer, float pan)
    {
        try { buffer.SetPan(ToDirectSoundPan(pan)); } catch {  }
    }

    private string? ResolveSoundFilePath(string resourceRoot, int id)
    {
        if (_soundList != null && _soundList.TryGetRelativePath(id, out string relFromList))
        {
            string candidate = Path.GetFullPath(Path.Combine(resourceRoot, relFromList));
            if (File.Exists(candidate))
                return candidate;
        }

        string wavDir = Path.Combine(resourceRoot, "Wav");
        string direct = Path.Combine(wavDir, $"{id}.wav");
        if (File.Exists(direct))
            return Path.GetFullPath(direct);

        string direct3 = Path.Combine(wavDir, id.ToString("D3") + ".wav");
        if (File.Exists(direct3))
            return Path.GetFullPath(direct3);

        int baseId = (id / 10) * 10;
        int suffix = id - baseId;
        if (suffix is >= 0 and <= 9)
        {
            string dashed = Path.Combine(wavDir, $"{baseId}-{suffix}.wav");
            if (File.Exists(dashed))
                return Path.GetFullPath(dashed);
        }

        try
        {
            if (Directory.Exists(wavDir))
            {
                string prefix = id.ToString();
                string? match = Directory.EnumerateFiles(wavDir, "*.wav", SearchOption.TopDirectoryOnly)
                    .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(p => Path.GetFileName(p).StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(match))
                    return Path.GetFullPath(match);
            }
        }
        catch
        {
            
        }

        return null;
    }

    private static string? ResolveArbitrarySoundFilePath(string resourceRoot, string fileName)
    {
        string trimmed = fileName.Trim().Trim('"');
        if (string.IsNullOrEmpty(trimmed))
            return null;

        string normalized = trimmed.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalized))
        {
            string full = Path.GetFullPath(normalized);
            return File.Exists(full) ? full : null;
        }

        string direct = Path.GetFullPath(Path.Combine(resourceRoot, normalized.TrimStart('.', Path.DirectorySeparatorChar)));
        if (File.Exists(direct))
            return direct;

        string inWav = Path.GetFullPath(Path.Combine(resourceRoot, "Wav", normalized.TrimStart('.', Path.DirectorySeparatorChar)));
        if (File.Exists(inWav))
            return inWav;

        string inWavLower = Path.GetFullPath(Path.Combine(resourceRoot, "wav", normalized.TrimStart('.', Path.DirectorySeparatorChar)));
        if (File.Exists(inWavLower))
            return inWavLower;

        return null;
    }

    private static string? TryResolveSoundListPath(string resourceRoot)
    {
        string direct = Path.Combine(resourceRoot, "sound2.lst");
        if (File.Exists(direct))
            return Path.GetFullPath(direct);

        string inWav = Path.Combine(resourceRoot, "Wav", "sound2.lst");
        if (File.Exists(inWav))
            return Path.GetFullPath(inWav);

        string inWavLower = Path.Combine(resourceRoot, "wav", "sound2.lst");
        if (File.Exists(inWavLower))
            return Path.GetFullPath(inWavLower);

        return null;
    }
}
