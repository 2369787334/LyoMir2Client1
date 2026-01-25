using MirClient.Protocol;
using MirClient.Protocol.Startup;

namespace MirClient.Core.Scenes;

public sealed class SelectCharacterScene : IMirScene
{
    private readonly MirSceneContext _ctx;
    private IReadOnlyList<MirCharacterInfo> _characters = Array.Empty<MirCharacterInfo>();
    private string _selectedName = string.Empty;

    public SelectCharacterScene(MirSceneContext ctx) => _ctx = ctx;

    public void OnEnter() => _ctx.Log?.Invoke("[scene] SelectCharacter enter");

    public void OnLeave() => _ctx.Log?.Invoke("[scene] SelectCharacter leave");

    public void Tick(long nowTimestamp, long nowMs)
    {
    }

    public void ApplyLoginResult(MirLoginResult loginResult)
    {
        _characters = loginResult.Characters;
        _selectedName = loginResult.SelectedCharacterName;
    }

    public IReadOnlyList<MirCharacterInfo> Characters => _characters;
    public string SelectedName => _selectedName;

    public void SelectCharacter(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        string trimmed = name.Trim();
        if (_characters.Any(c => string.Equals(c.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            _selectedName = trimmed;
    }
}
