using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirSeriesSkillMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, MirColor4>? addChatLine = null,
        Action<string, bool>? playSoundFile = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_BUILDACUS, packet =>
        {
            int code = packet.Header.Recog;
            string? message = code switch
            {
                0 => "[锻造] 开始",
                -1 => "[锻造] 失败：交易期间不能进行锻造",
                -2 => "[锻造] 失败：没有锻造所需物品",
                -3 => "[锻造] 失败：包裹中没有锻造所需物品",
                -4 => "[锻造] 失败：锻造材料不一致",
                -5 => "[锻造] 失败：锻造失败",
                -6 => "[锻造] 失败：存在非法锻造材料",
                _ => null
            };

            if (code == 0)
                playSoundFile?.Invoke(@"Wav\warpower-up.wav", false);
            else if (code == -5)
                playSoundFile?.Invoke(@"Wav\UnionHitShield.wav", false);

            if (!string.IsNullOrWhiteSpace(message))
                addChatLine?.Invoke(message, new MirColor4(0.92f, 0.92f, 0.92f, 1f));

            log?.Invoke($"[series] SM_BUILDACUS code={code}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_TRAINVENATION, packet =>
        {
            bool hero = packet.Header.Series != 0;
            int code = packet.Header.Recog;

            if (!world.TryApplyTrainVenation(hero, code, packet.BodyEncoded))
            {
                log?.Invoke($"[series] SM_TRAINVENATION decode failed (hero={hero} bodyLen={packet.BodyEncoded.Length})");
                return ValueTask.CompletedTask;
            }

            string? message = code switch
            {
                0 => hero ? "[经络] (英雄) 数据已更新" : "[经络] 数据已更新",
                -1 => hero ? "[经络] (英雄) 失败：脉络选择有错误" : "[经络] 失败：脉络选择有错误",
                -2 => hero ? "[经络] (英雄) 失败：该脉络未打通，不能修炼" : "[经络] 失败：该脉络未打通，不能修炼",
                -3 => hero ? "[经络] (英雄) 失败：该脉络已修炼到最高级" : "[经络] 失败：该脉络已修炼到最高级",
                -4 => hero ? "[经络] (英雄) 失败：内功等级不足" : "[经络] 失败：内功等级不足",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(message) && code != 0)
                addChatLine?.Invoke(message, new MirColor4(1.0f, 0.35f, 0.35f, 1f));

            log?.Invoke($"[series] SM_TRAINVENATION hero={hero} code={code} param={packet.Header.Param} tag={packet.Header.Tag}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_BREAKPOINT, packet =>
        {
            bool hero = packet.Header.Series != 0;
            int code = packet.Header.Recog;

            if (!world.TryApplyBreakPoint(hero, code, packet.BodyEncoded))
            {
                log?.Invoke($"[series] SM_BREAKPOINT decode failed (hero={hero} bodyLen={packet.BodyEncoded.Length})");
                return ValueTask.CompletedTask;
            }

            string? message = code switch
            {
                0 => hero ? "[经络] (英雄) 穴位已打通" : "[经络] 穴位已打通",
                -1 => hero ? "[经络] (英雄) 失败：脉络选择有错误" : "[经络] 失败：脉络选择有错误",
                -2 => hero ? "[经络] (英雄) 失败：穴位选择有错误" : "[经络] 失败：穴位选择有错误",
                -3 => hero ? "[经络] (英雄) 失败：内功等级不足" : "[经络] 失败：内功等级不足",
                -4 => hero ? "[经络] (英雄) 失败：此穴位已打通" : "[经络] 失败：此穴位已打通",
                -5 => hero ? "[经络] (英雄) 失败：此穴位目前不可打通" : "[经络] 失败：此穴位目前不可打通",
                -6 => hero ? "[经络] (英雄) 失败：缺少舒经活络丸" : "[经络] 失败：缺少舒经活络丸",
                -7 => hero ? "[经络] (英雄) 失败：使用舒经活络丸但未打通" : "[经络] 失败：使用舒经活络丸但未打通",
                -8 => hero ? "[经络] (英雄) 失败：缺少舒经活络丸" : "[经络] 失败：缺少舒经活络丸",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(message))
                addChatLine?.Invoke(message, code == 0
                    ? new MirColor4(0.85f, 0.98f, 0.85f, 1f)
                    : new MirColor4(1.0f, 0.35f, 0.35f, 1f));

            log?.Invoke($"[series] SM_BREAKPOINT hero={hero} code={code} param={packet.Header.Param} tag={packet.Header.Tag}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SERIESSKILLREADY, packet =>
        {
            bool hero = packet.Header.Series != 0;
            if (!hero)
            {
                world.ApplySeriesSkillReady(hero: false, packet.Header.Recog, packet.Header.Param, packet.Header.Tag);
                addChatLine?.Invoke("连续技能已准备好，可再次使用", new MirColor4(0.92f, 0.92f, 0.92f, 1f));
            }
            else
            {
                addChatLine?.Invoke("(英雄) 连续技能已准备好，可再次使用", new MirColor4(0.92f, 0.92f, 0.92f, 1f));
            }

            playSoundFile?.Invoke(@"Wav\warpower-up.wav", false);
            log?.Invoke($"[series] SM_SERIESSKILLREADY hero={hero} step={packet.Header.Tag}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_FIRESERIESSKILL, packet =>
        {
            bool hero = packet.Header.Series != 0;
            int code = packet.Header.Recog;

            if (!hero)
                world.ApplyFireSeriesSkillResult(code);

            string? message = code switch
            {
                0 => hero ? "(英雄) 连续技能启动" : "连续技能启动",
                1 => hero ? "(英雄) 连续技能失败" : "连续技能失败",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(message))
                addChatLine?.Invoke(message, code == 0
                    ? new MirColor4(0.85f, 0.98f, 0.85f, 1f)
                    : new MirColor4(1.0f, 0.35f, 0.35f, 1f));

            log?.Invoke($"[series] SM_FIRESERIESSKILL hero={hero} code={code}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SETSERIESSKILL, packet =>
        {
            bool hero = packet.Header.Series != 0;
            ushort slotIndex = packet.Header.Param;
            int slotValue = packet.Header.Recog;

            if (!world.TryApplySetSeriesSkillSlot(hero, slotIndex, slotValue, out byte applied))
            {
                log?.Invoke($"[series] SM_SETSERIESSKILL invalid slot={slotIndex} hero={hero} value={slotValue}");
                return ValueTask.CompletedTask;
            }

            if (slotValue < 0)
            {
                addChatLine?.Invoke(hero ? "(英雄) 连续技能配置失败：重复/无效技能" : "连续技能配置失败：重复/无效技能",
                    new MirColor4(1.0f, 0.35f, 0.35f, 1f));
            }

            log?.Invoke($"[series] SM_SETSERIESSKILL hero={hero} slot={slotIndex} value={slotValue} applied={applied}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SERIESSKILLARR, packet =>
        {
            bool hero = packet.Header.Series != 0;
            if (world.TryApplySeriesSkillArr(hero, packet.Header.Recog, packet.Header.Param, packet.Header.Tag, packet.BodyEncoded))
            {
                log?.Invoke($"[series] SM_SERIESSKILLARR hero={hero}");
            }
            else
            {
                log?.Invoke($"[series] SM_SERIESSKILLARR decode failed hero={hero} bodyLen={packet.BodyEncoded.Length}");
            }

            return ValueTask.CompletedTask;
        });
    }
}

