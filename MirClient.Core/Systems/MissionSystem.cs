namespace MirClient.Core.Systems;

public sealed class MissionSystem
{
    public bool Visible { get; private set; }
    public int MissionClass { get; private set; } = 1;
    public int TopIndex { get; private set; }
    public int SelectedIndex { get; private set; } = -1;

    public void Open(int missionClass)
    {
        Visible = true;
        MissionClass = Math.Clamp(missionClass, 1, 4);
        TopIndex = 0;
        SelectedIndex = -1;
    }

    public void Close() => Visible = false;

    public void Toggle()
    {
        if (Visible)
        {
            Close();
            return;
        }

        Open(MissionClass);
    }

    public void SetClass(int missionClass)
    {
        MissionClass = Math.Clamp(missionClass, 1, 4);
        TopIndex = 0;
        SelectedIndex = 0;
    }

    public void Navigate(MissionNavigation navigation, int missionCount, int pageSize)
    {
        missionCount = Math.Max(0, missionCount);
        if (missionCount <= 0)
        {
            SelectedIndex = -1;
            TopIndex = 0;
            return;
        }

        if (SelectedIndex < 0)
            SelectedIndex = 0;

        pageSize = Math.Max(1, pageSize);

        switch (navigation)
        {
            case MissionNavigation.Up:
                SelectedIndex = Math.Max(0, SelectedIndex - 1);
                break;
            case MissionNavigation.Down:
                SelectedIndex = Math.Min(missionCount - 1, SelectedIndex + 1);
                break;
            case MissionNavigation.PageUp:
                SelectedIndex = Math.Max(0, SelectedIndex - pageSize);
                TopIndex = Math.Max(0, TopIndex - pageSize);
                break;
            case MissionNavigation.PageDown:
                SelectedIndex = Math.Min(missionCount - 1, SelectedIndex + pageSize);
                TopIndex = Math.Max(0, TopIndex + pageSize);
                break;
            case MissionNavigation.Home:
                SelectedIndex = 0;
                TopIndex = 0;
                break;
            case MissionNavigation.End:
                SelectedIndex = missionCount - 1;
                TopIndex = Math.Max(0, missionCount - 1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(navigation), navigation, null);
        }
    }

    public void EnsureListWindow(int total, int listLines)
    {
        MissionClass = Math.Clamp(MissionClass, 1, 4);
        listLines = Math.Max(1, listLines);

        if (total <= 0)
        {
            SelectedIndex = -1;
            TopIndex = 0;
            return;
        }

        SelectedIndex = Math.Clamp(SelectedIndex, 0, total - 1);

        int maxTop = Math.Max(0, total - listLines);
        TopIndex = Math.Clamp(TopIndex, 0, maxTop);

        if (SelectedIndex < TopIndex)
            TopIndex = SelectedIndex;
        else if (SelectedIndex >= TopIndex + listLines)
            TopIndex = SelectedIndex - listLines + 1;

        TopIndex = Math.Clamp(TopIndex, 0, maxTop);
    }

    public enum MissionNavigation
    {
        Up = 1,
        Down = 2,
        PageUp = 3,
        PageDown = 4,
        Home = 5,
        End = 6
    }
}

