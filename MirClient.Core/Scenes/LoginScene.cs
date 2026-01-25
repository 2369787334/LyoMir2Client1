using MirClient.Protocol.Startup;

namespace MirClient.Core.Scenes;

public sealed class LoginScene : IMirScene
{
    private readonly MirSceneContext _ctx;
    private bool _loginInProgress;

    public LoginScene(MirSceneContext ctx) => _ctx = ctx;

    public void OnEnter() => _ctx.Log?.Invoke("[scene] Login enter");

    public void OnLeave() => _ctx.Log?.Invoke("[scene] Login leave");

    public void Tick(long nowTimestamp, long nowMs)
    {
    }

    public async Task<MirLoginResult> RunLoginAsync(
        MirStartupInfo startup,
        MirLoginCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (_loginInProgress)
            throw new InvalidOperationException("Login already in progress.");

        _loginInProgress = true;
        try
        {
            MirLoginResult result = await _ctx.Session.LoginAsync(startup, credentials, cancellationToken).ConfigureAwait(false);
            _ctx.SetLoginResult(credentials, result);
            return result;
        }
        finally
        {
            _loginInProgress = false;
        }
    }
}
