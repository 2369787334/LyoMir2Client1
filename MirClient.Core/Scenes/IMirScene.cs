namespace MirClient.Core.Scenes;

public interface IMirScene
{
    void OnEnter();
    void OnLeave();
    void Tick(long nowTimestamp, long nowMs);
}

