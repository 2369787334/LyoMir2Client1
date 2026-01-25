using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirPasswordMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        Func<bool>? togglePasswordMode = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.Register(Grobal2.SM_PASSWORD, _ =>
        {
            if (togglePasswordMode != null)
            {
                bool enabled = togglePasswordMode();
                log?.Invoke($"[pwd] SM_PASSWORD mode={(enabled ? "on" : "off")}");
            }
            else
            {
                log?.Invoke("[pwd] SM_PASSWORD");
            }

            return ValueTask.CompletedTask;
        });
    }
}

