using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MirClient.Core.LocalConfig;

public static class ClientSetIni
{
    private const string SectionBasic = "Basic";

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetPrivateProfileString(
        string section,
        string key,
        string defaultValue,
        StringBuilder retVal,
        int size,
        string filePath);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WritePrivateProfileString(
        string section,
        string key,
        string? value,
        string filePath);

    public readonly record struct BasicSettings(
        bool ShowActorName,
        bool DuraWarning,
        bool AutoAttack,
        bool ShowDropItems,
        bool HideDeathBody)
    {
        public static BasicSettings Defaults => new(
            ShowActorName: true,
            DuraWarning: true,
            AutoAttack: false,
            ShowDropItems: true,
            HideDeathBody: false);
    }

    public static BasicSettings LoadBasic(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return BasicSettings.Defaults;

        BasicSettings d = BasicSettings.Defaults;
        return new BasicSettings(
            ShowActorName: ReadBool(filePath, SectionBasic, "ShowActorName", d.ShowActorName),
            DuraWarning: ReadBool(filePath, SectionBasic, "DuraWarning", d.DuraWarning),
            AutoAttack: ReadBool(filePath, SectionBasic, "AutoAttack", d.AutoAttack),
            ShowDropItems: ReadBool(filePath, SectionBasic, "ShowDropItems", d.ShowDropItems),
            HideDeathBody: ReadBool(filePath, SectionBasic, "HideDeathBody", d.HideDeathBody));
    }

    public static void SaveBasic(string filePath, BasicSettings settings)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return;

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        _ = WriteBool(filePath, SectionBasic, "ShowActorName", settings.ShowActorName);
        _ = WriteBool(filePath, SectionBasic, "DuraWarning", settings.DuraWarning);
        _ = WriteBool(filePath, SectionBasic, "AutoAttack", settings.AutoAttack);
        _ = WriteBool(filePath, SectionBasic, "ShowDropItems", settings.ShowDropItems);
        _ = WriteBool(filePath, SectionBasic, "HideDeathBody", settings.HideDeathBody);
    }

    private static bool ReadBool(string filePath, string section, string key, bool defaultValue)
    {
        var sb = new StringBuilder(128);
        _ = GetPrivateProfileString(section, key, defaultValue ? "1" : "0", sb, sb.Capacity, filePath);
        string value = sb.ToString().Trim();
        if (value.Length == 0)
            return defaultValue;

        if (value == "1")
            return true;
        if (value == "0")
            return false;

        if (bool.TryParse(value, out bool b))
            return b;

        return defaultValue;
    }

    private static bool WriteBool(string filePath, string section, string key, bool value)
    {
        return WritePrivateProfileString(section, key, value ? "1" : "0", filePath);
    }
}
