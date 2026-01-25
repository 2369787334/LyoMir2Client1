namespace MirClient.Core.Effects;

public static class StruckEffectAtlas
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
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic4);
                startIndex = 170;
                frames = 5;
                frameTimeMs = 120;
                return true;
            case 2:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic4);
                startIndex = 520;
                frames = 16;
                frameTimeMs = 120;
                return true;
            case 3:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic4);
                startIndex = 820;
                frames = 10;
                frameTimeMs = 120;
                return true;
            case 4:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic4);
                startIndex = 600;
                frames = 6;
                frameTimeMs = 120;
                return true;
            case 5:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic4);
                startIndex = 260;
                frames = 8;
                frameTimeMs = 120;
                return true;
            case 6:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic4);
                startIndex = 420;
                frames = 16;
                frameTimeMs = 120;
                return true;
            case 7:
            case 8:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic4);
                startIndex = 180;
                frames = 6;
                frameTimeMs = 120;
                return true;
            case 9:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Prguse2);
                startIndex = 0;
                frames = 25;
                frameTimeMs = 120;
                return true;
            case 10:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Prguse2);
                startIndex = 30;
                frames = 25;
                frameTimeMs = 120;
                return true;
            case 11:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic5);
                startIndex = 790;
                frames = 10;
                frameTimeMs = 60;
                return true;
            case 13:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic6);
                startIndex = 470;
                frames = 5;
                frameTimeMs = 120;
                return true;
            case 14:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Prguse2);
                startIndex = 110;
                frames = 15;
                frameTimeMs = 80;
                return true;
            case 15:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic6);
                startIndex = 480;
                frames = 6;
                frameTimeMs = 120;
                return true;
            case 16:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic6);
                startIndex = 490;
                frames = 8;
                frameTimeMs = 120;
                return true;
            case 210:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic2);
                startIndex = 210;
                frames = 6;
                frameTimeMs = 80;
                return true;
            case 1110:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic2);
                startIndex = 1110;
                frames = 10;
                frameTimeMs = 80;
                return true;
            case 17:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Mon, 24);
                startIndex = 3740;
                frames = 10;
                frameTimeMs = 500;
                return true;
            case 18:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                startIndex = 4060;
                frames = 37;
                frameTimeMs = 50;
                return true;
            case 19:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                startIndex = 4100;
                frames = 33;
                frameTimeMs = 55;
                return true;
            case 20:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                startIndex = 4140;
                frames = 30;
                frameTimeMs = 60;
                return true;
            case 21:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                startIndex = 4180;
                frames = 6;
                frameTimeMs = 120;
                return true;
            case 22:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                startIndex = 4190;
                frames = 4;
                frameTimeMs = 120;
                return true;
            case 23:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Prguse2);
                startIndex = 640;
                frames = 10;
                frameTimeMs = 120;
                return true;
            case 24:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Prguse2);
                startIndex = 650;
                frames = 15;
                frameTimeMs = 95;
                return true;
            case 25:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Prguse2);
                startIndex = 670;
                frames = 18;
                frameTimeMs = 90;
                return true;
            case 26:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Prguse2);
                startIndex = 690;
                frames = 17;
                frameTimeMs = 90;
                return true;
            case 27:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Prguse2);
                startIndex = 710;
                frames = 19;
                frameTimeMs = 88;
                return true;
            case 28:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Prguse2);
                startIndex = 630;
                frames = 6;
                frameTimeMs = 120;
                return true;
            case 30:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic8Images2);
                startIndex = 2460;
                frames = 6;
                frameTimeMs = 100;
                return true;
            case 31:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Ui1);
                startIndex = 1210;
                frames = 12;
                frameTimeMs = 120;
                return true;
            case 32:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Ui1);
                startIndex = 1222;
                frames = 12;
                frameTimeMs = 120;
                return true;
            case >= 33 and <= 40:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Ui1);
                startIndex = 1080 + 10 * (type - 33);
                frames = 6;
                frameTimeMs = 220;
                return true;
            case >= 41 and <= 43:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Ui1);
                startIndex = 1170 + 10 * (type - 41);
                frames = 6;
                frameTimeMs = 220;
                return true;
            default:
                return false;
        }
    }
}
