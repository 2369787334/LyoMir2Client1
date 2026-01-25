using MirClient.Core.Scenes;

namespace MirClient.Core.Client;

public sealed class ClientMain
{
    public IMirScene? CurrentScene { get; private set; }

    public void SetScene(IMirScene? scene)
    {
        CurrentScene?.OnLeave();
        CurrentScene = scene;
        CurrentScene?.OnEnter();
    }
}

