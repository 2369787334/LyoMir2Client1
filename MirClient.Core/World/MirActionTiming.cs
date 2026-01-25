using MirClient.Protocol;

namespace MirClient.Core.World;

public static class MirActionTiming
{
    public readonly record struct HumanActionInfo(int Start, int Frames, int Skip, int FrameTimeMs, bool HoldLast);

    public readonly record struct MoveTiming(int Frames, int FrameTimeMs);

    
    private static readonly HumanActionInfo Stand = new(Start: 0, Frames: 4, Skip: 4, FrameTimeMs: 200, HoldLast: false);
    private static readonly HumanActionInfo Walk = new(Start: 64, Frames: 6, Skip: 2, FrameTimeMs: 90, HoldLast: false);
    private static readonly HumanActionInfo Run = new(Start: 128, Frames: 6, Skip: 2, FrameTimeMs: 115, HoldLast: false);
    private static readonly HumanActionInfo Rush = new(Start: 128, Frames: 3, Skip: 5, FrameTimeMs: 120, HoldLast: false);
    private static readonly HumanActionInfo RushEx = new(Start: 80, Frames: 8, Skip: 2, FrameTimeMs: 77, HoldLast: false);
    private static readonly HumanActionInfo Hit = new(Start: 200, Frames: 6, Skip: 2, FrameTimeMs: 85, HoldLast: false);
    private static readonly HumanActionInfo HeavyHit = new(Start: 264, Frames: 6, Skip: 2, FrameTimeMs: 90, HoldLast: false);
    private static readonly HumanActionInfo BigHit = new(Start: 328, Frames: 8, Skip: 0, FrameTimeMs: 70, HoldLast: false);
    private static readonly HumanActionInfo Spell = new(Start: 392, Frames: 6, Skip: 2, FrameTimeMs: 60, HoldLast: false);
    private static readonly HumanActionInfo SitDown = new(Start: 456, Frames: 2, Skip: 0, FrameTimeMs: 300, HoldLast: true);
    private static readonly HumanActionInfo Struck = new(Start: 472, Frames: 3, Skip: 5, FrameTimeMs: 70, HoldLast: false);
    private static readonly HumanActionInfo Die = new(Start: 536, Frames: 4, Skip: 4, FrameTimeMs: 120, HoldLast: true);

    
    private static readonly HumanActionInfo SmiteHit = new(Start: 160, Frames: 15, Skip: 5, FrameTimeMs: 56, HoldLast: false);
    private static readonly HumanActionInfo SmiteLongHitPhase1 = new(Start: 1920, Frames: 5, Skip: 5, FrameTimeMs: 45, HoldLast: false);
    private static readonly HumanActionInfo SmiteLongHitPhase2 = new(Start: 320, Frames: 6, Skip: 4, FrameTimeMs: 80, HoldLast: false);
    private static readonly HumanActionInfo SmiteLongHit2 = new(Start: 400, Frames: 12, Skip: 8, FrameTimeMs: 70, HoldLast: false);
    private static readonly HumanActionInfo SmiteLongHit3 = new(Start: 320, Frames: 6, Skip: 4, FrameTimeMs: 100, HoldLast: false);
    private static readonly HumanActionInfo SmiteWideHit = new(Start: 560, Frames: 10, Skip: 0, FrameTimeMs: 78, HoldLast: false);
    private static readonly HumanActionInfo SmiteWideHit2 = new(Start: 400, Frames: 12, Skip: 8, FrameTimeMs: 85, HoldLast: false);

    public static HumanActionInfo GetHumanActionInfo(ushort action) =>
        action switch
        {
            Grobal2.SM_WALK or Grobal2.SM_BACKSTEP => Walk,
            Grobal2.SM_RUN or Grobal2.SM_HORSERUN or Grobal2.SM_RUSHKUNG => Run,
            Grobal2.SM_RUSH => Rush,
            Grobal2.SM_RUSHEX => RushEx,
            Grobal2.SM_SITDOWN => SitDown,

            Grobal2.SM_HIT or Grobal2.SM_FIREHIT or Grobal2.SM_POWERHIT or Grobal2.SM_LONGHIT or Grobal2.SM_SQUHIT or Grobal2.SM_CRSHIT or Grobal2.SM_TWNHIT or Grobal2.SM_WIDEHIT or Grobal2.SM_PURSUEHIT or Grobal2.SM_HERO_LONGHIT or Grobal2.SM_HERO_LONGHIT2 or Grobal2.SM_WWJATTACK or Grobal2.SM_WSJATTACK or Grobal2.SM_WTJATTACK => Hit,
            Grobal2.SM_SMITEHIT => SmiteHit,
            Grobal2.SM_SMITELONGHIT => SmiteLongHitPhase1,
            Grobal2.SM_SMITELONGHIT2 => SmiteLongHit2,
            Grobal2.SM_SMITELONGHIT3 => SmiteLongHit3,
            Grobal2.SM_SMITEWIDEHIT => SmiteWideHit,
            Grobal2.SM_SMITEWIDEHIT2 => SmiteWideHit2,

            Grobal2.SM_HEAVYHIT => HeavyHit,
            Grobal2.SM_BIGHIT => BigHit,
            Grobal2.SM_SPELL => Spell,
            Grobal2.SM_STRUCK => Struck,
            Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH => Die,
            _ => Stand
        };

    public static MoveTiming GetMoveTiming(ushort action) =>
        action switch
        {
            Grobal2.SM_WALK or Grobal2.SM_BACKSTEP => new MoveTiming(Frames: Walk.Frames, FrameTimeMs: Walk.FrameTimeMs),
            Grobal2.SM_RUN or Grobal2.SM_HORSERUN or Grobal2.SM_RUSHKUNG => new MoveTiming(Frames: Run.Frames, FrameTimeMs: Run.FrameTimeMs),
            Grobal2.SM_RUSH => new MoveTiming(Frames: Rush.Frames, FrameTimeMs: Rush.FrameTimeMs),
            Grobal2.SM_RUSHEX => new MoveTiming(Frames: RushEx.Frames, FrameTimeMs: RushEx.FrameTimeMs),
            _ => default
        };

    public static int GetActionDurationMs(ushort action)
    {
        if (action == Grobal2.SM_TURN)
            return 0;

        
        if (action == Grobal2.SM_SMITELONGHIT)
            return (SmiteLongHitPhase1.Frames * SmiteLongHitPhase1.FrameTimeMs) + (SmiteLongHitPhase2.Frames * SmiteLongHitPhase2.FrameTimeMs);

        HumanActionInfo info = GetHumanActionInfo(action);
        if (info.Frames <= 0 || info.FrameTimeMs <= 0)
            return 0;

        return info.Frames * info.FrameTimeMs;
    }
}
