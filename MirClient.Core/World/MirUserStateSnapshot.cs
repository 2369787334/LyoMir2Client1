using MirClient.Protocol.Packets;

namespace MirClient.Core.World;

public sealed class MirUserStateSnapshot
{
    public int Feature { get; init; }
    public string UserName { get; init; } = string.Empty;
    public byte NameColor { get; init; }
    public string GuildName { get; init; } = string.Empty;
    public string GuildRankName { get; init; } = string.Empty;
    public byte Gender { get; init; }
    public byte HumAttr { get; init; }
    public byte ActiveTitle { get; init; }
    public IReadOnlyList<HumTitle> Titles { get; init; } = Array.Empty<HumTitle>();
    public IReadOnlyList<ClientItem> UseItems { get; init; } = Array.Empty<ClientItem>();
}

