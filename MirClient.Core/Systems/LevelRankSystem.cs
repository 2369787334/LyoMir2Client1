using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class LevelRankSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly CommandThrottleSystem _throttle;
    private readonly Action<string>? _log;

    private int _lastRequestPage = int.MinValue;
    private int _lastRequestType = int.MinValue;

    public LevelRankSystem(
        MirClientSession session,
        MirWorldState world,
        CommandThrottleSystem throttle,
        Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
        _log = log;
    }

    public bool Visible { get; private set; }
    public int Page { get; private set; }
    public int Type { get; private set; }

    public void Reset()
    {
        Visible = false;
        Page = 0;
        Type = 0;
        _lastRequestPage = int.MinValue;
        _lastRequestType = int.MinValue;
    }

    public void Close(bool logUi)
    {
        if (!Visible)
            return;

        Visible = false;
        if (logUi)
            _log?.Invoke("[ui] level rank closed");
    }

    public void Toggle(CancellationToken token, bool logUi)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        if (Visible)
        {
            Close(logUi);
            return;
        }

        Visible = true;
        Type = Math.Clamp(Type, 0, 7);
        Page = Math.Max(0, Page);

        TrySendQuery(Page, Type, token);

        if (logUi)
            _log?.Invoke($"[ui] level rank opened (type={Type} page={Page})");
    }

    public void SetPage(int page) => Page = Math.Max(0, page);

    public void SetType(int type) => Type = Math.Clamp(type, 0, 7);

    public void TrySendQuery(CancellationToken token) => TrySendQuery(Page, Type, token);

    public void TrySendQuery(int page, int type, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        page = Math.Max(0, page);
        type = Math.Clamp(type, 0, 7);

        Page = page;
        Type = type;

        if (page == _lastRequestPage && type == _lastRequestType)
            return;

        if (!_throttle.TryLevelRankSend())
            return;

        _lastRequestPage = page;
        _lastRequestType = type;
        _world.PrepareLevelRankRequest(page, type);

        _ = _session.SendClientMessageAsync(Grobal2.CM_LEVELRANK, page, unchecked((ushort)type), 0, 0, token)
            .ContinueWith(
                t => _log?.Invoke($"[rank] CM_LEVELRANK send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"[rank] CM_LEVELRANK page={page} type={type}");
    }
}

