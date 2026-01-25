namespace MirClient.Core.Scenes;

public sealed class MirSceneManager
{
    private readonly Dictionary<MirSceneId, IMirScene> _scenes = new();
    public MirSceneId CurrentId { get; private set; }
    public IMirScene? Current { get; private set; }

    public event Action<MirSceneId, MirSceneId>? SceneChanged;

    public void Register(MirSceneId id, IMirScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scenes[id] = scene;
    }

    public bool Switch(MirSceneId id)
    {
        if (!_scenes.TryGetValue(id, out IMirScene? next))
            return false;

        MirSceneId from = CurrentId;
        Current?.OnLeave();
        CurrentId = id;
        Current = next;
        Current.OnEnter();
        SceneChanged?.Invoke(from, id);
        return true;
    }

    public void Tick(long nowTimestamp, long nowMs)
    {
        Current?.Tick(nowTimestamp, nowMs);
    }
}
