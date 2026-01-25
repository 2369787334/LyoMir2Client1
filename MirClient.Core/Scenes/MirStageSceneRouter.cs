namespace MirClient.Core.Scenes;

public sealed class MirStageSceneRouter : IDisposable
{
    private readonly MirClientSession _session;
    private readonly MirSceneManager _sceneManager;
    private readonly Action<Action>? _dispatch;
    private readonly Action<string>? _log;

    public MirStageSceneRouter(
        MirClientSession session,
        MirSceneManager sceneManager,
        Action<Action>? dispatch = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(sceneManager);

        _session = session;
        _sceneManager = sceneManager;
        _dispatch = dispatch;
        _log = log;

        _session.StageChanged += OnStageChanged;
        _session.ConnectionStateChanged += OnConnectionStateChanged;
    }

    public void Dispose()
    {
        _session.StageChanged -= OnStageChanged;
        _session.ConnectionStateChanged -= OnConnectionStateChanged;
    }

    private void OnStageChanged(MirSessionStage stage)
    {
        if (_dispatch != null)
        {
            _dispatch(() => Apply(stage));
            return;
        }

        Apply(stage);
    }

    private void OnConnectionStateChanged(bool _)
    {
        MirSessionStage stage = _session.Stage;
        if (_dispatch != null)
        {
            _dispatch(() => Apply(stage));
            return;
        }

        Apply(stage);
    }

    private void Apply(MirSessionStage stage)
    {
        _log?.Invoke($"[stage] {stage} (connected={_session.IsConnected})");

        MirSceneId sceneId = stage switch
        {
            MirSessionStage.Idle => MirSceneId.Login,
            MirSessionStage.LoginGate => MirSceneId.Login,
            MirSessionStage.SelectCountry => MirSceneId.SelectCountry,
            MirSessionStage.SelectServer => MirSceneId.SelectServer,
            MirSessionStage.SelectGate => MirSceneId.Loading,
            MirSessionStage.SelectCharacter => MirSceneId.SelectCharacter,
            MirSessionStage.RunGate => MirSceneId.LoginNotice,
            MirSessionStage.InGame => _session.IsConnected ? MirSceneId.Play : MirSceneId.LoginNotice,
            _ => MirSceneId.Intro
        };
        _sceneManager.Switch(sceneId);
    }
}
