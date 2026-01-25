using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class BoxSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private long _lastActionMs;
    private int _lastClickIndex = -1;
    private long _lastClickMs;
    private int _lastServerItemIndex;

    public BoxSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public int SelectedIndex { get; private set; } = -1;
    public bool FlashRequested { get; private set; }
    public bool GetRequested { get; private set; }

    public void Reset()
    {
        SelectedIndex = -1;
        _lastClickIndex = -1;
        _lastClickMs = 0;
        FlashRequested = false;
        GetRequested = false;
        _lastServerItemIndex = 0;
        _lastActionMs = 0;
    }

    public void Tick()
    {
        if (!_world.BoxOpen)
        {
            Reset();
            return;
        }

        if (_world.BoxServerItemIndex == 0 && _lastServerItemIndex != 0)
        {
            _lastServerItemIndex = 0;
            FlashRequested = false;
            GetRequested = false;
        }

        if (_world.BoxServerItemIndex != 0 && _world.BoxServerItemIndex != _lastServerItemIndex)
        {
            _lastServerItemIndex = _world.BoxServerItemIndex;
            int idx = _world.BoxServerItemIndex - 1;
            if ((uint)idx < 9u)
                SelectedIndex = idx;
        }

        if (_world.BoxServerItemIndex != 0)
            FlashRequested = false;
    }

    public int GetSelectedIndexForUi()
    {
        int selectedIndex;

        if ((uint)SelectedIndex < 9u)
            selectedIndex = SelectedIndex;
        else if (_world.BoxServerItemIndex is >= 1 and <= 9)
            selectedIndex = _world.BoxServerItemIndex - 1;
        else
            selectedIndex = 0;

        if ((uint)selectedIndex >= 9u)
            selectedIndex = 0;

        SelectedIndex = selectedIndex;
        return selectedIndex;
    }

    public void Close(bool logUi)
    {
        _world.CloseBox();
        Reset();

        if (logUi)
            _log?.Invoke("[box] closed");
    }

    public async Task HandleSlotClickAsync(int slotIndex, long nowMs, CancellationToken token)
    {
        if (!_world.BoxOpen)
            return;

        if ((uint)slotIndex >= 9u)
            return;

        SelectedIndex = slotIndex;

        bool doubleClick = slotIndex == _lastClickIndex && (nowMs - _lastClickMs) <= 350;
        _lastClickIndex = slotIndex;
        _lastClickMs = nowMs;

        int svrIdx = _world.BoxServerItemIndex;
        if (!doubleClick || GetRequested || svrIdx != slotIndex + 1)
            return;

        await TryGetAsync(nowMs, token).ConfigureAwait(false);
    }

    public async Task TryFlashAsync(long nowMs, CancellationToken token)
    {
        if (!_world.BoxOpen)
            return;

        if (FlashRequested || GetRequested || _world.BoxServerItemIndex != 0)
            return;

        if (nowMs - _lastActionMs < 120)
            return;

        _lastActionMs = nowMs;
        FlashRequested = true;

        try
        {
            await _session.SendClientMessageAsync(Grobal2.CM_SELETEBOXFLASH, 0, 0, 0, 0, token).ConfigureAwait(false);
            _log?.Invoke("[box] CM_SELETEBOXFLASH");
        }
        catch (Exception ex)
        {
            FlashRequested = false;
            _log?.Invoke($"[box] CM_SELETEBOXFLASH send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async Task TryGetAsync(long nowMs, CancellationToken token)
    {
        if (!_world.BoxOpen)
            return;

        if (GetRequested || _world.BoxServerItemIndex == 0)
            return;

        if (nowMs - _lastActionMs < 120)
            return;

        _lastActionMs = nowMs;
        GetRequested = true;

        try
        {
            await _session.SendClientMessageAsync(Grobal2.CM_GETBOXITEM, 0, 0, 0, 0, token).ConfigureAwait(false);
            _log?.Invoke("[box] CM_GETBOXITEM");
        }
        catch (Exception ex)
        {
            GetRequested = false;
            _log?.Invoke($"[box] CM_GETBOXITEM send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

