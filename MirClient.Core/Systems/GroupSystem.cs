using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class GroupSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    public GroupSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public void TryToggleGroupMode(CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        bool enable = !_world.AllowGroup;
        ushort param = enable ? (ushort)1 : (ushort)0;

        _ = _session.SendClientMessageAsync(Grobal2.CM_GROUPMODE, 0, param, 0, 0, token)
            .ContinueWith(
                t => _log?.Invoke($"[group] CM_GROUPMODE send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"[group] CM_GROUPMODE {(enable ? "on" : "off")}");
    }

    public void TryCreateGroup(string who, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        who = (who ?? string.Empty).Trim();
        if (who.Length == 0)
            return;

        _ = _session.SendClientStringAsync(Grobal2.CM_CREATEGROUP, 0, 0, 0, 0, who, token)
            .ContinueWith(
                t => _log?.Invoke($"[group] CM_CREATEGROUP send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"[group] CM_CREATEGROUP '{who}'");
    }

    public void TryAddGroupMember(string who, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        who = (who ?? string.Empty).Trim();
        if (who.Length == 0)
            return;

        _ = _session.SendClientStringAsync(Grobal2.CM_ADDGROUPMEMBER, 0, 0, 0, 0, who, token)
            .ContinueWith(
                t => _log?.Invoke($"[group] CM_ADDGROUPMEMBER send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"[group] CM_ADDGROUPMEMBER '{who}'");
    }

    public void TryDelGroupMember(string who, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        who = (who ?? string.Empty).Trim();
        if (who.Length == 0)
            return;

        _ = _session.SendClientStringAsync(Grobal2.CM_DELGROUPMEMBER, 0, 0, 0, 0, who, token)
            .ContinueWith(
                t => _log?.Invoke($"[group] CM_DELGROUPMEMBER send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"[group] CM_DELGROUPMEMBER '{who}'");
    }
}

