using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirMagicMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        void HandleMagicsList(string identName, string bodyEncoded, bool hero)
        {
            MirBagItemsUpdate update = world.ApplyMagics(bodyEncoded, hero);
            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[magic] {identName} count={update.Count} sample={update.SampleNames}"
                : $"[magic] {identName} count={update.Count}");
        }

        void HandleAddMagic(string identName, string bodyEncoded, bool hero)
        {
            if (!world.TryApplyAddMagic(bodyEncoded, hero, out ClientMagic magic))
            {
                log?.Invoke($"[magic] {identName} decode failed (len={bodyEncoded.Length}).");
                return;
            }

            log?.Invoke($"[magic] {identName} class={magic.Def.Class} id={magic.Def.MagicId} key='{magic.KeyChar}' lv={magic.Level} name='{magic.Def.MagicNameString}'");
        }

        void HandleDelMagic(string identName, int magicId, int magicClass, bool hero)
        {
            bool removed = world.TryApplyDelMagic(magicId, magicClass, hero);
            if (removed)
                log?.Invoke($"[magic] {identName} class={magicClass} id={magicId}");
        }

        dispatcher.Register(Grobal2.SM_SENDMYMAGIC, packet =>
        {
            HandleMagicsList("SM_SENDMYMAGIC", packet.BodyEncoded, hero: false);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SENDMYMAGIC_SZ, packet =>
        {
            HandleMagicsList("SM_SENDMYMAGIC_SZ", packet.BodyEncoded, hero: false);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROMYMAGICS, packet =>
        {
            HandleMagicsList("SM_HEROMYMAGICS", packet.BodyEncoded, hero: true);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_ADDMAGIC, packet =>
        {
            HandleAddMagic("SM_ADDMAGIC", packet.BodyEncoded, hero: false);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROADDMAGIC, packet =>
        {
            HandleAddMagic("SM_HEROADDMAGIC", packet.BodyEncoded, hero: true);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DELMAGIC, packet =>
        {
            HandleDelMagic("SM_DELMAGIC", packet.Header.Recog, packet.Header.Param, hero: false);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HERODELMAGIC, packet =>
        {
            HandleDelMagic("SM_HERODELMAGIC", packet.Header.Recog, packet.Header.Param, hero: true);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CONVERTMAGIC, packet =>
        {
            if (!world.TryApplyConvertMagic(packet.Header.Recog, packet.Header.Param, packet.Header.Tag, packet.Header.Series, packet.BodyEncoded, hero: false, out ClientMagic magic))
                log?.Invoke($"[magic] SM_CONVERTMAGIC decode failed (len={packet.BodyEncoded.Length}).");
            else
                log?.Invoke($"[magic] SM_CONVERTMAGIC -> class={magic.Def.Class} id={magic.Def.MagicId} lv={magic.Level} name='{magic.Def.MagicNameString}'");

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HCONVERTMAGIC, packet =>
        {
            if (!world.TryApplyConvertMagic(packet.Header.Recog, packet.Header.Param, packet.Header.Tag, packet.Header.Series, packet.BodyEncoded, hero: true, out ClientMagic magic))
                log?.Invoke($"[magic] SM_HCONVERTMAGIC decode failed (len={packet.BodyEncoded.Length}).");
            else
                log?.Invoke($"[magic] SM_HCONVERTMAGIC -> class={magic.Def.Class} id={magic.Def.MagicId} lv={magic.Level} name='{magic.Def.MagicNameString}'");

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_MAGIC_LVEXP, packet =>
        {
            int train = packet.Header.Tag | (packet.Header.Series << 16);
            bool updated = world.TryApplyMagicLvExp(packet.Header.Recog, packet.Header.Param, train, hero: false);
            if (updated)
                log?.Invoke($"[magic] SM_MAGIC_LVEXP magId={packet.Header.Recog} lv={packet.Header.Param} train={train}");

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_MAGIC_MAXLV, packet =>
        {
            bool hero = packet.Header.Series != 0;
            bool updated = world.TryApplyMagicMaxLv(packet.Header.Recog, packet.Header.Param, hero);
            if (updated)
                log?.Invoke($"[magic] SM_MAGIC_MAXLV magId={packet.Header.Recog} maxLv={packet.Header.Param} hero={(hero ? 1 : 0)}");

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROMAGIC_LVEXP, packet =>
        {
            int train = packet.Header.Tag | (packet.Header.Series << 16);
            bool updated = world.TryApplyMagicLvExp(packet.Header.Recog, packet.Header.Param, train, hero: true);
            if (updated)
                log?.Invoke($"[magic] SM_HEROMAGIC_LVEXP magId={packet.Header.Recog} lv={packet.Header.Param} train={train}");

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROPOWERUP, packet =>
        {
            world.ApplyHeroPowerUp(packet.Header.Param, packet.Header.Recog, packet.Header.Tag);
            log?.Invoke($"[hero] SM_HEROPOWERUP type={packet.Header.Param} energy={packet.Header.Recog}/{packet.Header.Tag}");
            return ValueTask.CompletedTask;
        });
    }
}

