namespace MirClient.Core.Scenes;

public sealed class LoginNoticeScene : IMirScene
{
    private readonly MirSceneContext _ctx;

    public LoginNoticeScene(MirSceneContext ctx) => _ctx = ctx;

    public void OnEnter() => _ctx.Log?.Invoke("[scene] LoginNotice enter");

    public void OnLeave() => _ctx.Log?.Invoke("[scene] LoginNotice leave");

    public void Tick(long nowTimestamp, long nowMs)
    {
    }
}

