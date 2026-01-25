using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Systems;

public sealed class MerchantDialogSystem
{
    private const long SelectCooldownMs = 150;
    private const long ClickNpcCooldownMs = 500;

    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private long _lastSelectMs;
    private int _lastClickNpcRecogId;
    private long _lastClickNpcMs;

    public MerchantDialogSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public void Reset()
    {
        _lastSelectMs = 0;
        _lastClickNpcRecogId = 0;
        _lastClickNpcMs = 0;
    }

    public bool TryPickMerchantNpcAtCell(int mapX, int mapY, out int recogId, out ActorMarker actor)
    {
        recogId = 0;
        actor = default;

        foreach ((int actorId, ActorMarker marker) in _world.Actors)
        {
            if (FeatureCodec.Race(marker.Feature) != Grobal2.RCC_MERCHANT)
                continue;

            int dx = marker.X - mapX;
            int dy = marker.Y - mapY;
            if (dx is < -1 or > 1 || dy is < -1 or > 1)
                continue;

            recogId = actorId;
            actor = marker;
            return true;
        }

        return false;
    }

    public async ValueTask TrySelectAsync(int merchantId, string command, CancellationToken token)
    {
        if (!TryBeginSelect())
            return;

        if (merchantId <= 0)
        {
            _log?.Invoke("[merchant] select ignored: merchant not set");
            return;
        }

        command = (command ?? string.Empty).Trim();
        if (command.Length == 0)
            return;

        try
        {
            await _session.SendClientStringAsync(Grobal2.CM_MERCHANTDLGSELECT, merchantId, 0, 0, 0, command, token);
            _log?.Invoke($"[merchant] CM_MERCHANTDLGSELECT merchant={merchantId} '{command}'");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[merchant] CM_MERCHANTDLGSELECT send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask<bool> TryClickNpcAsync(int recogId, int x, int y, CancellationToken token)
    {
        if (recogId <= 0)
            return false;

        if (!TryBeginNpcClick(recogId))
            return true;

        try
        {
            await _session.SendClientMessageAsync(Grobal2.CM_CLICKNPC, recogId, 0, 0, 0, token);
            _log?.Invoke($"[merchant] CM_CLICKNPC recog={recogId} x={x} y={y}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[merchant] CM_CLICKNPC send failed: {ex.GetType().Name}: {ex.Message}");
        }

        return true;
    }

    private bool TryBeginSelect()
    {
        long nowMs = Environment.TickCount64;
        if (nowMs - _lastSelectMs < SelectCooldownMs)
            return false;

        _lastSelectMs = nowMs;
        return true;
    }

    private bool TryBeginNpcClick(int recogId)
    {
        long nowMs = Environment.TickCount64;
        if (recogId == _lastClickNpcRecogId && nowMs - _lastClickNpcMs < ClickNpcCooldownMs)
            return false;

        _lastClickNpcRecogId = recogId;
        _lastClickNpcMs = nowMs;
        return true;
    }
}
