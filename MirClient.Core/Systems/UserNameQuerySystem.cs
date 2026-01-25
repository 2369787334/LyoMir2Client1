using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class UserNameQuerySystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;

    public UserNameQuerySystem(MirClientSession session, MirWorldState world)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public void Pump(long nowMs, int left, int right, int top, int bottom, CancellationToken token)
    {
        if (_session.Stage != MirSessionStage.InGame)
            return;

        if (token.IsCancellationRequested)
            return;

        const long IntervalMs = 15_000;
        const int MaxPerPump = 2;

        int sent = 0;
        List<int>? updates = null;

        foreach ((int recogId, ActorMarker actor) in _world.Actors)
        {
            if (sent >= MaxPerPump)
                break;

            if (actor.IsMyself)
                continue;

            if (!string.IsNullOrEmpty(actor.UserName))
                continue;

            if (actor.X < left - 4 || actor.X > right + 4 || actor.Y < top - 4 || actor.Y > bottom + 40)
                continue;

            if (nowMs - actor.LastQueryUserNameMs <= IntervalMs)
                continue;

            ushort x = ClampToU16(actor.X);
            ushort y = ClampToU16(actor.Y);
            _ = _session.SendClientMessageAsync(Grobal2.CM_QUERYUSERNAME, recogId, x, y, 0, token);

            updates ??= new List<int>(MaxPerPump);
            updates.Add(recogId);
            sent++;
        }

        if (updates == null)
            return;

        foreach (int recogId in updates)
            _world.TryApplyActorUserNameQuerySent(recogId, nowMs);
    }

    private static ushort ClampToU16(int value)
    {
        if (value <= 0)
            return 0;
        if (value >= ushort.MaxValue)
            return ushort.MaxValue;
        return (ushort)value;
    }
}

