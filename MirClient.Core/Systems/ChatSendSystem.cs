using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class ChatSendSystem
{
    private readonly MirClientSession _session;
    private readonly Action<string>? _log;

    public ChatSendSystem(MirClientSession session, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _log = log;
    }

    public async Task<bool> TrySendSayAsync(string text, CancellationToken token)
    {
        try
        {
            await _session.SendClientStringAsync(Grobal2.CM_SAY, 0, 0, 0, 0, text, token).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[chat] send failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TrySendPasswordAsync(string text, CancellationToken token)
    {
        try
        {
            await _session.SendClientStringAsync(Grobal2.CM_PASSWORD, 0, 1, 0, 0, text, token).ConfigureAwait(false);
            _log?.Invoke("[pwd] CM_PASSWORD sent.");
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[chat] send failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}

