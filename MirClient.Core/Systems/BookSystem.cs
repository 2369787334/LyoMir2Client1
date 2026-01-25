using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class BookSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    public BookSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public void Close(bool logUi)
    {
        if (!_world.BookOpen)
            return;

        _world.CloseBook();
        if (logUi)
            _log?.Invoke("[book] closed");
    }

    public bool TryPrevPage(bool logUi) => TrySetPage(_world.BookPage - 1, logUi);

    public bool TryNextPage(bool logUi) => TrySetPage(_world.BookPage + 1, logUi);

    public bool TrySetPage(int page, bool logUi)
    {
        if (!_world.BookOpen || _world.BookPath != 1)
            return false;

        if (!_world.TrySetBookPage(page))
            return false;

        if (logUi)
            _log?.Invoke($"[book] page={_world.BookPage}");

        return true;
    }

    public async Task ConfirmAsync(CancellationToken token)
    {
        if (!_world.BookOpen || _world.BookPath != 1 || _world.BookPage != 4)
            return;

        int merchantId = _world.BookMerchantId;
        string label = _world.BookLabel;
        _world.CloseBook();

        if (merchantId <= 0 || string.IsNullOrWhiteSpace(label))
        {
            _log?.Invoke("[book] confirm ignored: missing merchant/label");
            return;
        }

        try
        {
            await _session.SendClientStringAsync(Grobal2.CM_MERCHANTDLGSELECT, merchantId, 0, 0, 0, label, token).ConfigureAwait(false);
            _log?.Invoke($"[book] CM_MERCHANTDLGSELECT merchant={merchantId} '{label}'");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[book] CM_MERCHANTDLGSELECT send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

