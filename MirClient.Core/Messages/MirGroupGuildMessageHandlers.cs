using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirGroupGuildMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        
        dispatcher.Register(Grobal2.SM_GROUPMODECHANGED, packet =>
        {
            bool allow = packet.Header.Param > 0;
            world.ApplyGroupModeChanged(allow);
            log?.Invoke($"[group] SM_GROUPMODECHANGED allow={(allow ? 1 : 0)}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CREATEGROUP_OK, _ =>
        {
            log?.Invoke("[group] SM_CREATEGROUP_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CREATEGROUP_FAIL, packet =>
        {
            log?.Invoke($"[group] SM_CREATEGROUP_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GROUPADDMEM_OK, _ =>
        {
            log?.Invoke("[group] SM_GROUPADDMEM_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GROUPADDMEM_FAIL, packet =>
        {
            log?.Invoke($"[group] SM_GROUPADDMEM_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GROUPDELMEM_OK, _ =>
        {
            log?.Invoke("[group] SM_GROUPDELMEM_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GROUPDELMEM_FAIL, packet =>
        {
            log?.Invoke($"[group] SM_GROUPDELMEM_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GROUPCANCEL, _ =>
        {
            world.ClearGroupMembers();
            log?.Invoke("[group] SM_GROUPCANCEL");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GROUPMEMBERS, packet =>
        {
            int count = world.ApplyGroupMembers(EdCode.DecodeString(packet.BodyEncoded));
            log?.Invoke($"[group] SM_GROUPMEMBERS count={count}");
            return ValueTask.CompletedTask;
        });

        
        dispatcher.Register(Grobal2.SM_OPENGUILDDLG, packet =>
        {
            world.ApplyOpenGuildDialog(EdCode.DecodeString(packet.BodyEncoded));
            log?.Invoke($"[guild] SM_OPENGUILDDLG name='{world.GuildDialogName}' commander={(world.GuildCommanderMode ? 1 : 0)} noticeLines={world.GuildNoticeLines.Count} lines={world.GuildDialogLines.Count}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SENDGUILDMEMBERLIST, packet =>
        {
            int count = world.ApplyGuildMemberList(EdCode.DecodeString(packet.BodyEncoded));
            log?.Invoke($"[guild] SM_SENDGUILDMEMBERLIST count={count}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_OPENGUILDDLG_FAIL, _ =>
        {
            log?.Invoke("[guild] SM_OPENGUILDDLG_FAIL");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CHANGEGUILDNAME, packet =>
        {
            world.ApplyChangeGuildName(EdCode.DecodeString(packet.BodyEncoded));
            log?.Invoke($"[guild] SM_CHANGEGUILDNAME name='{world.MyGuildName}' rank='{world.MyGuildRankName}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GUILDADDMEMBER_OK, _ =>
        {
            log?.Invoke("[guild] SM_GUILDADDMEMBER_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GUILDADDMEMBER_FAIL, packet =>
        {
            log?.Invoke($"[guild] SM_GUILDADDMEMBER_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GUILDDELMEMBER_OK, _ =>
        {
            log?.Invoke("[guild] SM_GUILDDELMEMBER_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GUILDDELMEMBER_FAIL, packet =>
        {
            log?.Invoke($"[guild] SM_GUILDDELMEMBER_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GUILDRANKUPDATE_FAIL, packet =>
        {
            log?.Invoke($"[guild] SM_GUILDRANKUPDATE_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GUILDMAKEALLY_OK, packet =>
        {
            log?.Invoke($"[guild] SM_GUILDMAKEALLY_OK code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GUILDMAKEALLY_FAIL, packet =>
        {
            log?.Invoke($"[guild] SM_GUILDMAKEALLY_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GUILDBREAKALLY_OK, packet =>
        {
            log?.Invoke($"[guild] SM_GUILDBREAKALLY_OK code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GUILDBREAKALLY_FAIL, packet =>
        {
            log?.Invoke($"[guild] SM_GUILDBREAKALLY_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_BUILDGUILD_OK, _ =>
        {
            log?.Invoke("[guild] SM_BUILDGUILD_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_BUILDGUILD_FAIL, packet =>
        {
            log?.Invoke($"[guild] SM_BUILDGUILD_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SENDUSERSTATE, packet =>
        {
            if (world.TryApplyUserState(packet.BodyEncoded))
            {
                MirUserStateSnapshot? state = world.LastUserState;
                log?.Invoke(state is null
                    ? "[user] SM_SENDUSERSTATE ok"
                    : $"[user] SM_SENDUSERSTATE name='{state.UserName}' guild='{state.GuildName}' rank='{state.GuildRankName}' items={state.UseItems.Count}");
            }
            else
            {
                log?.Invoke($"[user] SM_SENDUSERSTATE decode failed (len={packet.BodyEncoded.Length}).");
            }

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DONATE_OK, _ =>
        {
            log?.Invoke("[guild] SM_DONATE_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DONATE_FAIL, _ =>
        {
            log?.Invoke("[guild] SM_DONATE_FAIL");
            return ValueTask.CompletedTask;
        });
    }
}
