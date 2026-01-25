namespace MirClient.Core.Effects;

public enum MagicType
{
    Ready = 0,
    Fly = 1,
    Explosion = 2,
    FlyAxe = 3,
    FireWind = 4,
    FireGun = 5,
    LightingThunder = 6,
    Thunder = 7,
    ExploBujauk = 8,
    BujaukGroundEffect = 9,
    KyulKai = 10,
    FlyArrow = 11,
    FlyBug = 12,
    GroundEffect = 13,
    ThunderEx = 14,
    FireBall = 15,
    FlyBolt = 16,
    RedThunder = 17,
    RedGroundThunder = 18,
    Lava = 19,
    Spurt = 20,
    FlyStick = 21,
    FlyStick2 = 22
}

public readonly record struct UseMagicInfo(
    int ServerMagicCode,
    int MagicSerial,
    int Target,
    MagicType EffectType,
    int EffectNumber,
    int TargetX,
    int TargetY,
    bool Recusion,
    int AniTime,
    int SpellLevel,
    int MagicFireLevel,
    int Poison);

public readonly record struct MagicEffTimelineInfo(bool HasFlight, int FlightFrames, int ExplosionFrames, int FrameTimeMs);

public static class MagicEffTimeline
{
    public const int FlyBaseOffset = 10;
    public const int ExplosionBaseOffset = 170;
    public const int DefaultFrameTimeMs = 50;

    public static MagicEffTimelineInfo Get(byte effectType)
    {
        MagicType type = (MagicType)effectType;
        return type switch
        {
            MagicType.Fly or MagicType.BujaukGroundEffect or MagicType.ExploBujauk => new MagicEffTimelineInfo(HasFlight: true, FlightFrames: 6, ExplosionFrames: 10, FrameTimeMs: DefaultFrameTimeMs),
            MagicType.FlyBug => new MagicEffTimelineInfo(HasFlight: true, FlightFrames: 6, ExplosionFrames: 1, FrameTimeMs: DefaultFrameTimeMs),
            MagicType.FlyAxe => new MagicEffTimelineInfo(HasFlight: true, FlightFrames: 3, ExplosionFrames: 3, FrameTimeMs: DefaultFrameTimeMs),
            MagicType.FlyArrow => new MagicEffTimelineInfo(HasFlight: true, FlightFrames: 1, ExplosionFrames: 1, FrameTimeMs: DefaultFrameTimeMs),
            MagicType.FireBall => new MagicEffTimelineInfo(HasFlight: true, FlightFrames: 6, ExplosionFrames: 2, FrameTimeMs: DefaultFrameTimeMs),
            MagicType.FlyBolt => new MagicEffTimelineInfo(HasFlight: true, FlightFrames: 1, ExplosionFrames: 1, FrameTimeMs: DefaultFrameTimeMs),
            MagicType.FlyStick or MagicType.FlyStick2 => new MagicEffTimelineInfo(HasFlight: true, FlightFrames: 6, ExplosionFrames: 10, FrameTimeMs: DefaultFrameTimeMs),
            MagicType.GroundEffect => new MagicEffTimelineInfo(HasFlight: false, FlightFrames: 0, ExplosionFrames: 20, FrameTimeMs: DefaultFrameTimeMs),
            _ => new MagicEffTimelineInfo(HasFlight: false, FlightFrames: 0, ExplosionFrames: 10, FrameTimeMs: DefaultFrameTimeMs)
        };
    }
}
