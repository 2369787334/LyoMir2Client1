using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class SpellCastSystem
{
    private readonly MirClientSession _session;
    private readonly CommandThrottleSystem _throttle;
    private readonly AutoMoveSystem _autoMoveSystem;
    private readonly Action<string>? _log;

    public SpellCastSystem(
        MirClientSession session,
        CommandThrottleSystem throttle,
        AutoMoveSystem autoMoveSystem,
        Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
        _autoMoveSystem = autoMoveSystem ?? throw new ArgumentNullException(nameof(autoMoveSystem));
        _log = log;
    }

    public bool TryCastHotbarMagic(
        MirWorldState world,
        TargetingSystem targeting,
        int slot,
        int? mouseMapX,
        int? mouseMapY,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(targeting);

        if ((uint)slot >= 8u)
            return false;

        IReadOnlyList<ClientMagic> magics = world.MyMagics;
        if ((uint)slot >= (uint)magics.Count)
        {
            _log?.Invoke($"[magic] F{slot + 1} ignored: empty slot (myMagics={magics.Count})");
            return false;
        }

        ClientMagic magic = magics[slot];
        return TryCastMagic(world, targeting, slot, magic, mouseMapX, mouseMapY, token);
    }

    public bool TryCastMagic(
        MirWorldState world,
        TargetingSystem targeting,
        int slot,
        ClientMagic magic,
        int? mouseMapX,
        int? mouseMapY,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(targeting);

        if ((uint)slot >= 8u)
            return false;

        ushort magicId = magic.Def.MagicId;
        if (magicId == 0)
            return false;

        int targetId = 0;
        int x;
        int y;

        if (targeting.TryGetSelectedTarget(world, out int selectedId, out ActorMarker target))
        {
            targetId = selectedId;
            x = target.X;
            y = target.Y;
        }
        else if (mouseMapX is int mx && mouseMapY is int my)
        {
            x = mx;
            y = my;
        }
        else if (world.TryGetMyself(out ActorMarker myself))
        {
            x = myself.Dir & 0xFF;
            y = 0;
        }
        else
        {
            x = 0;
            y = 0;
        }

        return TrySendHotbarSpell(slot, magicId, magic.Def.MagicNameString, targetId, x, y, token);
    }

    public bool TrySendHotbarSpell(int slot, ushort magicId, string? magicName, int targetId, int x, int y, CancellationToken token)
    {
        if (_session.Stage != MirSessionStage.InGame)
            return false;

        if (!_throttle.TryCombatSend())
            return false;

        _autoMoveSystem.Cancel();

        int recog = MakeLongU16(x, y);
        ushort targetLo = unchecked((ushort)(targetId & 0xFFFF));
        ushort targetHi = unchecked((ushort)((targetId >> 16) & 0xFFFF));

        try
        {
            _ = _session.SendClientMessageAsync(Grobal2.CM_SPELL, recog, targetLo, magicId, targetHi, token)
                .ContinueWith(
                    t => _log?.Invoke($"[magic] CM_SPELL send failed: {t.Exception?.GetBaseException().Message}"),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[magic] CM_SPELL send failed: {ex.GetType().Name}: {ex.Message}");
        }

        string name = string.IsNullOrWhiteSpace(magicName) ? $"#{magicId}" : magicName!;
        _log?.Invoke($"[magic] CM_SPELL F{slot + 1} magic={magicId} '{name}' target={targetId} x={x} y={y}");
        return true;
    }

    private static int MakeLongU16(int lo, int hi) => (int)unchecked((ushort)lo) | ((int)unchecked((ushort)hi) << 16);
}
