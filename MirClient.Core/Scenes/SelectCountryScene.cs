namespace MirClient.Core.Scenes;




public sealed class SelectCountryScene : IMirScene
{
    private readonly MirSceneContext _ctx;

    public SelectCountryScene(MirSceneContext ctx) => _ctx = ctx;

    public void OnEnter() => _ctx.Log?.Invoke("[scene] SelectCountry enter");

    public void OnLeave() => _ctx.Log?.Invoke("[scene] SelectCountry leave");

    public void Tick(long nowTimestamp, long nowMs)
    {
    }
}

