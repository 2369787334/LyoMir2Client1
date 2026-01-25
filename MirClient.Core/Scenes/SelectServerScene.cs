using MirClient.Protocol.Startup;

namespace MirClient.Core.Scenes;

public sealed class SelectServerScene : IMirScene
{
    private readonly MirSceneContext _ctx;
    private MirLoginResult? _lastLoginResult;
    private string _selectedServer = string.Empty;

    public SelectServerScene(MirSceneContext ctx) => _ctx = ctx;

    public void OnEnter() => _ctx.Log?.Invoke("[scene] SelectServer enter");

    public void OnLeave() => _ctx.Log?.Invoke("[scene] SelectServer leave");

    public void Tick(long nowTimestamp, long nowMs)
    {
    }

    public void SetLoginResult(MirLoginResult loginResult)
    {
        _lastLoginResult = loginResult;
        _selectedServer = loginResult.ServerName;
    }

    public string SelectedServer => _selectedServer;

    public void SelectServer(string serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return;
        _selectedServer = serverName.Trim();
    }
}
