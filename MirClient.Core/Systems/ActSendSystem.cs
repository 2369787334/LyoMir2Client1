using System.Diagnostics;
using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class ActSendSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState? _world;
    private readonly Action<string>? _log;

    public ActSendSystem(MirClientSession session, MirWorldState? world = null, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world;
        _log = log;
    }

    public Task SendAsync(ushort ident, int x, int y, byte dir, CancellationToken token)
    {
        TryApplyLocalPrediction(ident, x, y, dir);

        int recog = (y << 16) | (x & 0xFFFF);
        return _session.SendClientMessageAsync(ident, recog, 0, dir, 0, token)
            .ContinueWith(
                t => _log?.Invoke($"[net] {ident} send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
    }

    private void TryApplyLocalPrediction(ushort ident, int x, int y, byte dir)
    {
        if (_world == null)
            return;

        if (_world.MapMoving)
            return;

        if (_session.Stage != MirSessionStage.InGame)
            return;

        if (!_world.MyselfRecogIdSet || _world.MyselfRecogId == 0)
            return;

        ushort smIdent = ident switch
        {
            Grobal2.CM_TURN => Grobal2.SM_TURN,
            Grobal2.CM_WALK => Grobal2.SM_WALK,
            Grobal2.CM_RUN => Grobal2.SM_RUN,
            Grobal2.CM_HORSERUN => Grobal2.SM_HORSERUN,
            Grobal2.CM_SITDOWN => Grobal2.SM_SITDOWN,
            Grobal2.CM_HIT => Grobal2.SM_HIT,
            Grobal2.CM_HEAVYHIT => Grobal2.SM_HEAVYHIT,
            Grobal2.CM_BIGHIT => Grobal2.SM_BIGHIT,
            Grobal2.CM_POWERHIT => Grobal2.SM_POWERHIT,
            Grobal2.CM_LONGHIT => Grobal2.SM_LONGHIT,
            Grobal2.CM_WIDEHIT => Grobal2.SM_WIDEHIT,
            Grobal2.CM_FIREHIT => Grobal2.SM_FIREHIT,
            _ => 0
        };

        if (smIdent == 0)
            return;

        long now = Stopwatch.GetTimestamp();
        long nowMs = Environment.TickCount64;
        _world.TryApplyActorSimpleAction(smIdent, _world.MyselfRecogId, x, y, dir, now, nowMs, out _, out _);
    }
}
