using System.Net.Sockets;
using System.Text;
using MirClient.Net.Framing;

namespace MirClient.Net.Transport;

public sealed class MirConnection : IAsyncDisposable
{
    private static readonly Encoding Ascii = Encoding.ASCII;
    private readonly PacketFramer _framer = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private TcpClient? _client;
    private NetworkStream? _stream;
    private Task? _readerTask;
    private CancellationTokenSource? _connectionCts;
    private int _cleanupOnce;
    private byte _sendCode;

    public bool IsConnected => _client?.Connected == true;

    public event Action? Connected;
    public event Action? Disconnected;
    public event Action<string>? RawChunkReceived;
    public event Action<string>? PacketReceived;
    public event Action<Exception>? Error;

    public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (_client != null)
            throw new InvalidOperationException("Already connected.");

        _cleanupOnce = 0;
        _framer.Clear();
        _sendCode = 0;

        _client = new TcpClient
        {
            NoDelay = true
        };

        await _client.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        _stream = _client.GetStream();
        Connected?.Invoke();

        _connectionCts = new CancellationTokenSource();
        _readerTask = Task.Run(() => ReadLoopAsync(_connectionCts.Token));
    }

    public async Task DisconnectAsync()
    {
        _connectionCts?.Cancel();
        try
        {
            _stream?.Close();
            _client?.Close();
        }
        finally
        {
            Cleanup();
        }

        if (_readerTask != null)
        {
            try { await _readerTask.ConfigureAwait(false); }
            catch {  }
            _readerTask = null;
        }

        _connectionCts?.Dispose();
        _connectionCts = null;
    }

    public async Task SendPayloadAsync(string payload, CancellationToken cancellationToken)
    {
        NetworkStream? stream = _stream;
        if (stream == null)
            return;

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            
            byte code = _sendCode;
            _sendCode++;
            if (_sendCode >= 10)
                _sendCode = 1;

            string framed = $"#{code}{payload}!";
            await stream.WriteAsync(Ascii.GetBytes(framed), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task SendRawAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        NetworkStream? stream = _stream;
        if (stream == null)
            return;

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                NetworkStream? stream = _stream;
                if (stream == null)
                    return;

                int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                    break;

                string chunk = Ascii.GetString(buffer, 0, read);
                RawChunkReceived?.Invoke(chunk);

                int heartbeatIndex = chunk.IndexOf('*');
                if (heartbeatIndex >= 0)
                {
                    
                    chunk = chunk.Remove(heartbeatIndex, 1);
                    await SendRawAsync(new[] { (byte)'*' }, cancellationToken).ConfigureAwait(false);
                }

                foreach (string packet in _framer.Push(chunk))
                {
                    PacketReceived?.Invoke(packet);
                }
            }
        }
        catch (OperationCanceledException)
        {
            
        }
        catch (Exception ex)
        {
            Error?.Invoke(ex);
        }
        finally
        {
            Cleanup();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _sendLock.Dispose();
    }

    private void Cleanup()
    {
        if (Interlocked.Exchange(ref _cleanupOnce, 1) != 0)
            return;

        try
        {
            _stream?.Close();
            _client?.Close();
        }
        catch
        {
            
        }

        _stream = null;
        _client = null;
        _framer.Clear();
        Disconnected?.Invoke();
    }
}
