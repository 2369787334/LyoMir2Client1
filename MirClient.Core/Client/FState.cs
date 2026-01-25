namespace MirClient.Core.Client;

public static class FState
{
    public static int BottomBoard800 { get; set; } = 371;
    public static int BottomBoard1024 { get; set; } = 371;

    public const int ViewChatLine = 9;
    public const int MaxStatePage = 4;
    public const int ListLineHeight = 13;
    public const int MaketLineHeight = 19;
    public const int MaxMenu = 10;
    public const int FriendMaxMenu = 13;

    public enum SpotDlgMode
    {
        Sell,
        Repair,
        Storage,
        MaketSell,
        ItemDlg,
        BindItem,
        UnBindItem,
        ExchangeBook,
    }

    public enum MenuDlgMode
    {
        Buy,
        MakeDrug,
        DetailMenu,
        GetSave,
    }
}

