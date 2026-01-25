namespace MirClient.Core.Scenes;

public sealed class LoadingScene : IMirScene
{
    private readonly MirSceneContext _ctx;

    public LoadingScene(MirSceneContext ctx) => _ctx = ctx;

    public void OnEnter() => _ctx.Log?.Invoke("[scene] Loading enter");

    public void OnLeave() => _ctx.Log?.Invoke("[scene] Loading leave");

    public void Tick(long nowTimestamp, long nowMs)
    {
    }
}

