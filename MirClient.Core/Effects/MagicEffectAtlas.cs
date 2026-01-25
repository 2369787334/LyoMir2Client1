namespace MirClient.Core.Effects;

public enum MagicEffectArchiveKind
{
    Magic = 0,
    Magic2 = 1,
    Magic4 = 2,
    Magic5 = 3,
    Magic6 = 4,
    Magic7 = 5,
    Magic8 = 6,
    Magic10 = 7,
    Ui1 = 8,
    CboEffect = 9,
    Mon = 10,
    Magic3 = 11,
    Prguse2 = 12,
    Dragon = 13,
    Effect = 14,
    Magic9 = 15,
    Magic7Images2 = 16,
    Magic8Images2 = 17
}

public readonly record struct MagicEffectArchiveRef(MagicEffectArchiveKind Kind, int Value = 0);

public static class MagicEffectAtlas
{
    private static readonly int[] EffectBaseTable =
    [
        0, 
        200, 
        400, 
        600, 
        0, 
        900, 
        920, 
        940, 
        20, 
        940, 
        940, 
        940, 
        0, 
        1380, 
        1500, 
        1520, 
        940, 
        1560, 
        1590, 
        1620, 
        1650, 
        1680, 
        0, 
        0, 
        0, 
        3960, 
        1790, 
        0, 
        3880, 
        3920, 
        3840, 
        0, 
        40, 
        130, 
        160, 
        190, 
        0, 
        210, 
        400, 
        600, 
        1500, 
        650, 
        710, 
        740, 
        910, 
        940, 
        990, 
        1040, 
        1110, 
        0, 
        940, 
        0, 
        0, 
        0, 
        0, 
        0, 
        1040, 
        940, 
        0, 
        0, 
        440, 
        270, 
        610, 
        190, 
        540, 
        210, 
        840, 
        0, 
        0 
    ];

    private static readonly int[] HitEffectBaseTable =
    [
        800, 
        1410, 
        1700, 
        3480, 
        40, 
        470, 
        310, 
        630, 
        0, 
        120, 
        0, 
        0, 
        0, 
        0, 
        510 
    ];

    public static bool TryGetEffectBase(int mag, int mType, out MagicEffectArchiveRef archive, out int baseIndex)
    {
        return TryGetEffectBase(mag, mType, selfX: 0, selfY: 0, out archive, out baseIndex);
    }

    public static bool TryGetEffectBase(int mag, int mType, int selfX, int selfY, out MagicEffectArchiveRef archive, out int baseIndex)
    {
        archive = default;
        baseIndex = 0;

        
        switch (mType)
        {
            case 1:
                if (mag > 100)
                {
                    int level = mag / 100;
                    int effect = mag % 100;

                    archive = effect == 4
                        ? new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic8)
                        : new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic7);

                    baseIndex = effect switch
                    {
                        1 => level switch { 1 => 1600, 2 => 1690, 3 => 1780, _ => 0 },
                        2 => level switch { 1 => 2140, 2 => 2230, 3 => 2320, _ => 0 },
                        3 => level switch { 1 => 1870, 2 => 1960, 3 => 2050, _ => 0 },
                        4 => level switch { 1 => 1660, 2 => 1750, 3 => 1840, _ => 0 },
                        _ => 0
                    };

                    return true;
                }

                if (mag == 6)
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic4);
                else if (mag is 4 or 9)
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic2);
                else if (mag is 5 or 7)
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic5);
                else if (mag == 8 || (mag >= 10 && mag <= 19))
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic6);
                else
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic);

                if ((uint)mag < (uint)HitEffectBaseTable.Length)
                    baseIndex = HitEffectBaseTable[mag];

                return true;

            case 2:
                if (mag == 10 - 1)
                {
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic6);
                    if ((uint)mag < (uint)HitEffectBaseTable.Length)
                        baseIndex = HitEffectBaseTable[mag];
                    return true;
                }

                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic);
                baseIndex = 0;
                return true;

            case 0:
                break;

            default:
                return false;
        }

        if (mag > 1000)
        {
            int level = mag / 1000;
            int magic = mag % 1000;

            switch (magic)
            {
                case 4:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic7);
                    baseIndex = level switch { 1 => 480, 2 => 540, 3 => 600, _ => 0 };
                    return true;
                case 9:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic7);
                    baseIndex = level switch { 1 => 180, 2 => 190, 3 => 200, _ => 0 };
                    return true;
                case 10:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic8);
                    baseIndex = level switch { 1 => 560, 2 => 570, 3 => 580, _ => 0 };
                    return true;
                case 11:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic8);
                    baseIndex = level switch { 1 => 500, 2 => 510, 3 => 520, _ => 0 };
                    return true;
                case 12:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic8);
                    baseIndex = level switch { 1 => 530, 2 => 540, 3 => 550, _ => 0 };
                    return true;
                case 15:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic7);
                    baseIndex = level switch { 1 => 900, 2 => 960, 3 => 1020, _ => 0 };
                    return true;
                case 20:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic7);
                    baseIndex = level switch { 1 => 60, 2 => 70, 3 => 80, _ => 0 };
                    return true;
                case 21:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic7);
                    baseIndex = level switch { 1 => 260, 2 => 310, 3 => 340, _ => 0 };
                    return true;
                case 28:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic8);
                    baseIndex = level switch { 1 => 160, 2 => 180, 3 => 200, _ => 0 };
                    return true;
                case 31:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic8);
                    baseIndex = level switch { 1 => 20, 2 => 50, 3 => 80, _ => 0 };
                    return true;
                case 34:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic9);
                    baseIndex = level switch { 1 => 340, 2 => 350, 3 => 360, _ => 0 };
                    return true;
                case 48:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic9);
                    baseIndex = level switch { 1 => 670, 2 => 820, 3 => 970, _ => 0 };
                    return true;
                case 51:
                    archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic9);
                    baseIndex = level switch { 1 => 490, 2 => 500, 3 => 510, _ => 0 };
                    return true;
                default:
                    return false;
            }
        }

        switch (mag)
        {
            case 10 - 1 + 500:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic6);
                baseIndex = 120;
                return true;
            case 29 - 1 + 500:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic6);
                baseIndex = 690;
                return true;
            case 34 - 1 + 500:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic6);
                baseIndex = 80;
                return true;
            case 48 - 1 + 500:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic2);
                baseIndex = 1130;
                return true;
            case >= 60 and <= 68:
                if ((uint)mag >= (uint)EffectBaseTable.Length) return false;
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic4);
                baseIndex = EffectBaseTable[mag];
                return true;
            case 51 - 1:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic6);
                baseIndex = 630;
                return true;
            case 8 or 27 or 33 or 35 or 37 or 38 or 39 or 41 or 43 or 44 or 45 or 46 or 47 or 48:
                if ((uint)mag >= (uint)EffectBaseTable.Length) return false;
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic2);
                baseIndex = EffectBaseTable[mag];
                return true;
            case 35 - 1:
            case 43 - 1:
            case 9:
                return false;
            case 31:
                if ((uint)mag >= (uint)EffectBaseTable.Length) return false;
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Mon, 21);
                baseIndex = EffectBaseTable[mag];
                return true;
            case 36:
                if ((uint)mag >= (uint)EffectBaseTable.Length) return false;
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Mon, 22);
                baseIndex = EffectBaseTable[mag];
                return true;

            case >= 80 and <= 82:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                baseIndex = mag switch
                {
                    80 => selfX >= 84 ? 130 : 140,
                    81 => selfX >= 78 && selfY >= 48 ? 150 : 160,
                    82 => 180,
                    _ => 0
                };
                return true;

            case 69:
            case 70:
            case 71:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                baseIndex = 400;
                return true;
            case 73:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic5);
                baseIndex = 90;
                return true;
            case 89:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                baseIndex = 350;
                return true;
            case 90:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                baseIndex = 440;
                return true;
            case 91:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Dragon);
                baseIndex = 470;
                return true;
            case 92:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic2);
                baseIndex = 1250;
                return true;
            case 99:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic5);
                baseIndex = 100;
                return true;
            case 100:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic5);
                baseIndex = 280;
                return true;

            case 103:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                baseIndex = 640;
                return true;
            case 111:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                baseIndex = 720;
                return true;
            case 105:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                baseIndex = 800;
                return true;
            case 106:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                baseIndex = 1040;
                return true;
            case 107:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                baseIndex = 1200;
                return true;
            case 108:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                baseIndex = 1440;
                return true;
            case 109:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                baseIndex = 1600;
                return true;
            case 110:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                baseIndex = 1760;
                return true;
            case 104:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.CboEffect);
                baseIndex = 4210;
                return true;
            case 115:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic8Images2);
                baseIndex = 2040;
                return true;
            case 116:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic8Images2);
                baseIndex = 2180;
                return true;
            case 120:
                
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic8Images2);
                baseIndex = 0;
                return true;
            case 121:
                
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic7Images2);
                baseIndex = 0;
                return true;
            case 125 - 1:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic10);
                baseIndex = 60;
                return true;
            case 126 - 1:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic10);
                baseIndex = 200;
                return true;
            case 128 - 1:
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic10);
                baseIndex = 330;
                return true;

            default:
                if (mag == 25) return false;
                if ((uint)mag >= (uint)EffectBaseTable.Length) return false;
                archive = new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic);
                baseIndex = EffectBaseTable[mag];
                return true;
        }
    }
}
