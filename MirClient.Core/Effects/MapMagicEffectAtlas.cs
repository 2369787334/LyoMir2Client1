namespace MirClient.Core.Effects;

public static class MapMagicEffectAtlas
{
    public static bool TryGetInfo(
        int effectNumber,
        int effectType,
        out MagicEffectArchiveRef archive,
        out int startIndex,
        out int frames,
        out int frameTimeMs)
        => TryGetInfo(effectNumber, effectType, selfX: 0, selfY: 0, out archive, out startIndex, out frames, out frameTimeMs);

    public static bool TryGetInfo(
        int effectNumber,
        int effectType,
        int selfX,
        int selfY,
        out MagicEffectArchiveRef archive,
        out int startIndex,
        out int frames,
        out int frameTimeMs)
    {
        archive = default;
        startIndex = 0;
        frames = 0;
        frameTimeMs = 0;

        if (effectNumber <= 0)
            return false;

        
        switch (effectNumber)
        {
            case 70:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                startIndex = 400;
                frames = 5;
                frameTimeMs = 50;
                return true;
            case 71:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                startIndex = 440;
                frames = 20;
                frameTimeMs = 50;
                return true;
            case 72:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                startIndex = 470;
                frames = 10;
                frameTimeMs = 50;
                return true;
            case 80:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                startIndex = 230;
                frames = 5;
                frameTimeMs = 50;
                return true;
            case 90:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                startIndex = 350;
                frames = 34;
                frameTimeMs = 50;
                return true;
            case 91:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                startIndex = 440;
                frames = 20;
                frameTimeMs = 50;
                return true;
            case 92:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic2);
                startIndex = 1250;
                frames = 14;
                frameTimeMs = 50;
                return true;
            case 93:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic2);
                startIndex = 1280;
                frames = 10;
                frameTimeMs = 50;
                return true;
        }

        if (!MagicEffectAtlas.TryGetEffectBase(effectNumber - 1, mType: 0, selfX, selfY, out archive, out int baseIndex))
            return false;

        if ((uint)effectType > byte.MaxValue)
            effectType = 0;

        MagicEffTimelineInfo timeline = MagicEffTimeline.Get((byte)effectType);

        startIndex = baseIndex + MagicEffTimeline.ExplosionBaseOffset;
        frames = timeline.ExplosionFrames > 0 ? timeline.ExplosionFrames : MapMagicEffect.DefaultFrames;
        frameTimeMs = timeline.FrameTimeMs > 0 ? timeline.FrameTimeMs : MapMagicEffect.DefaultFrameTimeMs;
        return true;
    }
}
