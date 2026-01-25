using MirClient.Core.World;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class UserStallSystem
{
    private const long ActionCooldownMs = 120;

    private readonly MaketSystem _maketSystem;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private long _lastActionMs;

    public bool Visible { get; private set; }
    public int SelectedIndex { get; private set; } = -1;

    public UserStallSystem(MaketSystem maketSystem, MirWorldState world, Action<string>? log = null)
    {
        _maketSystem = maketSystem ?? throw new ArgumentNullException(nameof(maketSystem));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public void HandleServerUserStall(int itemCount)
    {
        bool open = itemCount > 0;
        Visible = open;
        SelectedIndex = -1;
    }

    public void Close(bool logUi)
    {
        Visible = false;
        SelectedIndex = -1;

        if (logUi)
            _log?.Invoke("[stall] user stall closed");
    }

    public void SelectSlot(int index, ClientItem item, bool logUi)
    {
        SelectedIndex = index;

        if (logUi && item.MakeIndex != 0)
            _log?.Invoke($"[stall] user select idx={index} name='{item.NameString}' price={item.S.Price} type={item.S.NeedIdentify}");
    }

    public async ValueTask TryBuyAsync(CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < ActionCooldownMs)
            return;

        _lastActionMs = nowMs;

        ClientItem[] items = _world.UserStallItems.ToArray();
        if ((uint)SelectedIndex >= (uint)items.Length)
            return;

        ClientItem selected = items[SelectedIndex];
        if (selected.MakeIndex == 0)
            return;

        int merchant = _world.UserStallActorId;
        if (merchant <= 0)
        {
            _log?.Invoke("[stall] buy ignored: actor not set");
            return;
        }

        await _maketSystem.TrySendUserBuyItemAsync(merchant, selected, token);
    }

    public bool TryCloseWithThrottle(bool logUi)
    {
        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < ActionCooldownMs)
            return false;

        _lastActionMs = nowMs;
        Close(logUi);
        return true;
    }
}
