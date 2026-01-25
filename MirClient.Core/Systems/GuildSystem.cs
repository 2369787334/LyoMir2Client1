using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class GuildSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    public GuildSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public bool DialogVisible { get; private set; }
    public bool MemberListVisible { get; private set; }

    public void Reset()
    {
        DialogVisible = false;
        MemberListVisible = false;
    }

    public void CloseAll(bool logUi)
    {
        if (!DialogVisible && !MemberListVisible)
            return;

        DialogVisible = false;
        MemberListVisible = false;

        if (logUi)
            _log?.Invoke("[ui] guild ui closed");
    }

    public void ToggleDialog(CancellationToken token, bool logUi)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        DialogVisible = !DialogVisible;
        if (DialogVisible)
        {
            _ = _session.SendClientMessageAsync(Grobal2.CM_OPENGUILDDLG, 0, 0, 0, 0, token)
                .ContinueWith(
                    t => _log?.Invoke($"[guild] CM_OPENGUILDDLG send failed: {t.Exception?.GetBaseException().Message}"),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            _log?.Invoke("[guild] CM_OPENGUILDDLG");
        }
        else
        {
            if (logUi)
                _log?.Invoke("[ui] guild dialog closed");
        }
    }

    public void ToggleMemberList(CancellationToken token, bool logUi)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        MemberListVisible = !MemberListVisible;
        if (MemberListVisible)
        {
            _ = _session.SendClientMessageAsync(Grobal2.CM_GUILDMEMBERLIST, 0, 0, 0, 0, token)
                .ContinueWith(
                    t => _log?.Invoke($"[guild] CM_GUILDMEMBERLIST send failed: {t.Exception?.GetBaseException().Message}"),
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted,
                    TaskScheduler.Default);
            _log?.Invoke("[guild] CM_GUILDMEMBERLIST");
        }
        else
        {
            if (logUi)
                _log?.Invoke("[ui] guild members closed");
        }
    }
}

