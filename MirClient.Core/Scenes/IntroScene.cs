namespace MirClient.Core.Scenes;

public sealed class IntroScene : IMirScene
{
    private readonly MirSceneContext _ctx;

    public IntroScene(MirSceneContext ctx) => _ctx = ctx;

    public void OnEnter() => _ctx.Log?.Invoke("[scene] Intro enter");

    public void OnLeave() => _ctx.Log?.Invoke("[scene] Intro leave");

    public void Tick(long nowTimestamp, long nowMs)
    {
        
    }
}
