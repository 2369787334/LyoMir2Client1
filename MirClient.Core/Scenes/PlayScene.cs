using MirClient.Core.World;

namespace MirClient.Core.Scenes;

public sealed class PlayScene : IMirScene
{
    private readonly MirSceneContext _ctx;
    public MirWorldState World => _ctx.World;

    public PlayScene(MirSceneContext ctx) => _ctx = ctx;

    public void OnEnter() => _ctx.Log?.Invoke("[scene] Play enter");

    public void OnLeave() => _ctx.Log?.Invoke("[scene] Play leave");

    public void Tick(long nowTimestamp, long nowMs)
    {
        World.Tick(nowTimestamp, nowMs);
    }
}
