namespace MirClient.Core.Effects;

public static class NormalEffectAtlas
{
    public static bool TryGetInfo(int type, out MagicEffectArchiveRef archive, out int startIndex, out int frames, out int frameTimeMs)
    {
        archive = default;
        startIndex = 0;
        frames = 0;
        frameTimeMs = 0;

        switch (type)
        {
            case 1:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Mon, 14);
                startIndex = 410;
                frames = 6;
                frameTimeMs = 120;
                return true;
            case 2:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic2);
                startIndex = 670;
                frames = 10;
                frameTimeMs = 150;
                return true;
            case 3:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic2);
                startIndex = 690;
                frames = 10;
                frameTimeMs = 150;
                return true;
            case 4:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                startIndex = 400;
                frames = 5;
                frameTimeMs = 50;
                return true;
            case 5:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                startIndex = 230;
                frames = 5;
                frameTimeMs = 50;
                return true;
            case 6:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                startIndex = 440;
                frames = 20;
                frameTimeMs = 50;
                return true;
            case 7:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                startIndex = 470;
                frames = 10;
                frameTimeMs = 50;
                return true;
            case 8:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Ui1);
                startIndex = 1210;
                frames = 12;
                frameTimeMs = 120;
                return true;
            case >= 41 and <= 43:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Ui1);
                startIndex = 1170 + 10 * (type - 41);
                frames = 6;
                frameTimeMs = 220;
                return true;
            case >= 75 and <= 83:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic3);
                startIndex = (type - 75) * 20;
                frames = 20;
                frameTimeMs = 150;
                return true;
            case 84:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Effect);
                startIndex = 800;
                frames = 10;
                frameTimeMs = 100;
                return true;
            case 85:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Effect);
                startIndex = 810;
                frames = 10;
                frameTimeMs = 100;
                return true;
            default:
                return false;
        }
    }
}

