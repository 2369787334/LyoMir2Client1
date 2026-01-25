namespace MirClient.Core.Effects;

public static class MagicEffExplosionAtlas
{
    public static bool TryGetInfo(
        int effectNumber,
        int effectType,
        int magicLevel,
        out MagicEffectArchiveRef archive,
        out int startIndex,
        out int frames,
        out int frameTimeMs)
        => TryGetInfo(effectNumber, effectType, magicLevel, selfX: 0, selfY: 0, out archive, out startIndex, out frames, out frameTimeMs);

    public static bool TryGetInfo(
        int effectNumber,
        int effectType,
        int magicLevel,
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

        MagicType magicType = (MagicType)effectType;
        switch (effectNumber)
        {
            case 75 when magicType == MagicType.ExploBujauk:
            {
                
                
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic5);
                startIndex = 220;
                frames = 3;
                frameTimeMs = 80;
                return true;
            }
            case 63:
            {
                
                if (!MagicEffectAtlas.TryGetEffectBase(effectNumber - 1, mType: 0, selfX, selfY, out archive, out _))
                    return false;

                startIndex = 780;
                frames = 25;
                frameTimeMs = 100;
                return true;
            }
            case 100:
            case 101:
            {
                
                if (!MagicEffectAtlas.TryGetEffectBase(effectNumber - 1, mType: 0, selfX, selfY, out archive, out int baseIndex))
                    return false;

                startIndex = baseIndex + MagicEffTimeline.ExplosionBaseOffset;
                frames = effectNumber == 100 ? 5 : 10;
                frameTimeMs = 100;
                return true;
            }
            case 121:
            {
                
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic8Images2);
                startIndex = 0;
                frames = 8;
                frameTimeMs = 100;
                return true;
            }
            case 122:
            {
                
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic7Images2);
                startIndex = 860;
                frames = 20;
                frameTimeMs = 100;
                return true;
            }
        }

        if (magicType == MagicType.BujaukGroundEffect)
        {
            switch (effectNumber)
            {
                case 74:
            {
                
                if (!MagicEffectAtlas.TryGetEffectBase(effectNumber - 1, mType: 0, selfX, selfY, out archive, out startIndex))
                    return false;

                frames = 10;
                frameTimeMs = 80;
                return true;
                }
                case 46:
            {
                
                if (!MagicEffectAtlas.TryGetEffectBase(effectNumber - 1, mType: 0, selfX, selfY, out archive, out int baseIndex))
                    return false;

                startIndex = baseIndex + MagicEffTimeline.FlyBaseOffset;
                frames = 24;
                frameTimeMs = 50;
                    return true;
                }
                case 11:
                    return TryGetBujauk11Or12ExplosionInfo(
                        magicLevel,
                        fallbackStartIndex: 1160 + 160,
                        level1StartIndex: 2470,
                        level2StartIndex: 2490,
                        level3StartIndex: 2520,
                        out archive,
                        out startIndex,
                        out frames,
                        out frameTimeMs);
                case 12:
                    return TryGetBujauk11Or12ExplosionInfo(
                        magicLevel,
                        fallbackStartIndex: 1160 + 180,
                        level1StartIndex: 2410,
                        level2StartIndex: 2430,
                        level3StartIndex: 2450,
                        out archive,
                        out startIndex,
                        out frames,
                        out frameTimeMs);
            }
        }

        return MapMagicEffectAtlas.TryGetInfo(effectNumber, effectType, selfX, selfY, out archive, out startIndex, out frames, out frameTimeMs);
    }

    private static bool TryGetBujauk11Or12ExplosionInfo(
        int magicLevel,
        int fallbackStartIndex,
        int level1StartIndex,
        int level2StartIndex,
        int level3StartIndex,
        out MagicEffectArchiveRef archive,
        out int startIndex,
        out int frames,
        out int frameTimeMs)
    {
        frames = magicLevel > 3 ? 20 : 16;
        frameTimeMs = 50;

        archive = magicLevel > 3
            ? new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic7)
            : new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic);

        int bucket = magicLevel / 4;
        startIndex = bucket switch
        {
            1 => level1StartIndex,
            2 => level2StartIndex,
            3 => level3StartIndex,
            _ => fallbackStartIndex
        };

        return true;
    }
}
