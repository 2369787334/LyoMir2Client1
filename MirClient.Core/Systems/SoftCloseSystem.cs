using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class SoftCloseSystem
{
    private readonly MirClientSession _session;

    public SoftCloseSystem(MirClientSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public async Task TrySendAsync()
    {
        if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
            return;

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await _session.SendClientMessageAsync(Grobal2.CM_SOFTCLOSE, 0, 0, 0, 0, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            
        }
    }
}

