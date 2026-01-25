using MirClient.Core.World;

namespace MirClient.Core.Scenes;




public sealed class MirSceneContext
{
    public MirSceneContext(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        World = world ?? throw new ArgumentNullException(nameof(world));
        Log = log;
    }

    public MirClientSession Session { get; }
    public MirWorldState World { get; }
    public Action<string>? Log { get; }

    public MirLoginCredentials? Credentials { get; private set; }
    public MirLoginResult? LoginResult { get; private set; }

    public void SetLoginResult(MirLoginCredentials credentials, MirLoginResult result)
    {
        Credentials = credentials;
        LoginResult = result;
    }
}
