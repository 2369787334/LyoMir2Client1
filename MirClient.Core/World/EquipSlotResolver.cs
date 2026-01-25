using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.World;

public static class EquipSlotResolver
{
    public static bool IsValidTakeOnSlotForStdMode(int stdMode, int slot) => stdMode switch
    {
        2 or 3 or 4 => slot == Grobal2.U_BUJUK,
        5 or 6 => slot == Grobal2.U_WEAPON,
        7 => slot == Grobal2.U_CHARM,
        10 or 11 => slot == Grobal2.U_DRESS,
        15 => slot == Grobal2.U_HELMET,
        19 or 20 or 21 => slot == Grobal2.U_NECKLACE,
        22 or 23 => slot is Grobal2.U_RINGL or Grobal2.U_RINGR,
        24 or 26 => slot is Grobal2.U_ARMRINGL or Grobal2.U_ARMRINGR,
        25 => slot is Grobal2.U_BUJUK or Grobal2.U_ARMRINGL,
        51 => slot == Grobal2.U_BUJUK,
        27 => slot == Grobal2.U_BELT,
        28 or 29 or 30 => slot == Grobal2.U_RIGHTHAND,
        52 or 62 => slot == Grobal2.U_BOOTS,
        53 or 63 => slot == Grobal2.U_CHARM,
        54 or 64 => slot == Grobal2.U_BELT,
        >= 41 and <= 50 => slot == Grobal2.U_BUJUK,
        _ => false
    };

    public static bool TryResolveTakeOnSlot(ClientItem item, IReadOnlyDictionary<int, ClientItem> useItems, ref bool takeOnPosToggle, out int slot)
    {
        slot = -1;

        if (item.MakeIndex == 0 || string.IsNullOrWhiteSpace(item.NameString))
            return false;

        int stdMode = item.S.StdMode;
        switch (stdMode)
        {
            case 2:
            case 3:
            case 4:
                slot = Grobal2.U_BUJUK;
                return true;
            case 5:
            case 6:
                slot = Grobal2.U_WEAPON;
                return true;
            case 7:
                slot = Grobal2.U_CHARM;
                return true;
            case 10:
            case 11:
                slot = Grobal2.U_DRESS;
                return true;
            case 15:
                slot = Grobal2.U_HELMET;
                return true;
            case 19:
            case 20:
            case 21:
                slot = Grobal2.U_NECKLACE;
                return true;
            case 22:
            case 23:
                slot = ResolveToggleSlot(Grobal2.U_RINGL, Grobal2.U_RINGR, ref takeOnPosToggle);
                return true;
            case 24:
            case 26:
                slot = ResolveToggleSlot(Grobal2.U_ARMRINGR, Grobal2.U_ARMRINGL, ref takeOnPosToggle);
                return true;
            case 25:
                slot = IsEmptySlot(useItems, Grobal2.U_BUJUK)
                    ? Grobal2.U_BUJUK
                    : IsEmptySlot(useItems, Grobal2.U_ARMRINGL) ? Grobal2.U_ARMRINGL : Grobal2.U_BUJUK;
                return true;
            case 51:
                slot = Grobal2.U_BUJUK;
                return true;
            case 27:
                slot = Grobal2.U_BELT;
                return true;
            case 28:
            case 29:
            case 30:
                slot = Grobal2.U_RIGHTHAND;
                return true;
            case 52:
            case 62:
                slot = Grobal2.U_BOOTS;
                return true;
            case 53:
            case 63:
                slot = Grobal2.U_CHARM;
                return true;
            case 54:
            case 64:
                slot = Grobal2.U_BELT;
                return true;
            case >= 41 and <= 50:
                if (item.S.Shape is >= 9 and <= 45)
                    return false;
                slot = Grobal2.U_BUJUK;
                return true;
            default:
                slot = -1;
                return false;
        }
    }

    private static int ResolveToggleSlot(int first, int second, ref bool takeOnPosToggle)
    {
        int slot = takeOnPosToggle ? first : second;
        takeOnPosToggle = !takeOnPosToggle;
        return slot;
    }

    private static bool IsEmptySlot(IReadOnlyDictionary<int, ClientItem> useItems, int slot)
    {
        if (!useItems.TryGetValue(slot, out ClientItem item))
            return true;

        return item.MakeIndex == 0 || string.IsNullOrWhiteSpace(item.NameString);
    }
}
