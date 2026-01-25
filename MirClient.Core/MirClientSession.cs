using System.IO;
using System.Reflection;
using System.Threading.Channels;
using MirClient.Core.Diagnostics;
using MirClient.Core.Hardware;
using MirClient.Core.Messages;
using MirClient.Net.Transport;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;
using MirClient.Protocol.Startup;
using MirClient.Protocol.Text;

namespace MirClient.Core;

public enum MirSessionStage
{
    Idle,
    LoginGate,
    SelectCountry,
    SelectServer,
    SelectGate,
    SelectCharacter,
    RunGate,
    InGame
}

public sealed record MirLoginCredentials(
    string Account,
    string Password,
    string? PreferredCharacterName = null,
    bool CreateIfMissing = false);

public sealed record MirAccountFullEntry(
    string Account,
    string Password,
    string UserName,
    string Quiz1,
    string Answer1,
    string Quiz2,
    string Answer2,
    string BirthDay,
    string SSNo = "",
    string Phone = "",
    string EMail = "",
    string MobilePhone = "");

public sealed record MirCharacterInfo(string Name, byte Job, byte Hair, int Level, byte Sex, bool Selected);

public sealed record MirServerListResult(string ServerListRaw);

public sealed record MirSelectServerResult(
    string SelectedServerName,
    int Certification,
    string SelGateAddress,
    int SelGatePort);

public sealed record MirCharacterListResult(IReadOnlyList<MirCharacterInfo> Characters);

public sealed record MirDeletedCharacterInfo(string Name, byte Job, byte Sex, int Level);

public sealed record MirDeletedCharacterListResult(IReadOnlyList<MirDeletedCharacterInfo> Characters);

public sealed record MirStartPlayResult(string RunGateAddress, int RunGatePort);

public sealed record MirLoginResult(
    string ServerName,
    int Certification,
    string SelGateAddress,
    int SelGatePort,
    IReadOnlyList<MirCharacterInfo> Characters,
    string SelectedCharacterName,
    string RunGateAddress,
    int RunGatePort,
    string ServerListRaw);

public sealed class MirClientSession : IAsyncDisposable, IMirPacketSource
{
    private enum NetTraceMode
    {
        Off = 0,
        LoginOnly = 1,
        All = 2
    }

    private static readonly NetTraceMode s_netTraceMode = ParseNetTraceMode();
    private static readonly Lazy<Dictionary<ushort, string>> s_identNames = new(BuildIdentNameMap);

    private readonly MirConnection _connection = new();
    private readonly Channel<string> _incoming = CreateChannel();
    private readonly Queue<MirServerPacket> _pendingPackets = new();
    private readonly object _pendingPacketsGate = new();
    private TaskCompletionSource<bool> _disconnectSignal = CreateDisconnectSignal();
    private RunGateCredentials? _runGateCredentials;

    private string? _loginGateHost;
    private int _loginGatePort;
    private string? _loginAccount;
    private int _loginCertification;
    private string? _selGateHost;
    private int _selGatePort;

    private string _currentEndpoint = string.Empty;
    private MirSessionStage _stage = MirSessionStage.Idle;

    public MirSessionStage Stage => _stage;
    public bool IsConnected => _connection.IsConnected;
    public bool CanReconnectToSelGate =>
        !string.IsNullOrWhiteSpace(_selGateHost) &&
        _selGatePort is > 0 and <= 65535 &&
        !string.IsNullOrWhiteSpace(_loginAccount) &&
        _loginCertification > 0;

    public event Action<string>? Log;
    public event Action<MirSessionStage>? StageChanged;
    public event Action<bool>? ConnectionStateChanged;

    public void MarkInGame()
    {
        if (_stage == MirSessionStage.RunGate)
            SetStage(MirSessionStage.InGame);
    }

    public MirClientSession()
    {
        _connection.Connected += () =>
        {
            string msg = $"[net] connected {_currentEndpoint}";
            Log?.Invoke(msg);
            MirErrorLog.Write(msg);
            ConnectionStateChanged?.Invoke(true);
        };
        _connection.Disconnected += () =>
        {
            string msg = $"[net] disconnected {_currentEndpoint}";
            Log?.Invoke(msg);
            MirErrorLog.Write(msg);
            _disconnectSignal.TrySetResult(true);
            ConnectionStateChanged?.Invoke(false);
        };
        _connection.Error += ex =>
        {
            string msg = $"[net] error {ex.GetType().Name}: {ex.Message}";
            Log?.Invoke(msg);
            MirErrorLog.WriteException("MirConnection.Error", ex);
            _disconnectSignal.TrySetException(ex);
            ConnectionStateChanged?.Invoke(false);
        };
        _connection.PacketReceived += packet => _incoming.Writer.TryWrite(packet);
    }

    public async Task<MirLoginResult> LoginAsync(
        MirStartupInfo startup,
        MirLoginCredentials credentials,
        CancellationToken cancellationToken)
    {
        if (startup == null) throw new ArgumentNullException(nameof(startup));
        if (credentials == null) throw new ArgumentNullException(nameof(credentials));

        SetStage(MirSessionStage.LoginGate);
        await ReconnectAsync(startup.ServerAddress, startup.ServerPort, cancellationToken).ConfigureAwait(false);

        await TrySendProtocolAsync(cancellationToken).ConfigureAwait(false);

        await SendLoginAsync(credentials.Account, credentials.Password, cancellationToken).ConfigureAwait(false);
        MirServerPacket passOk;
        try
        {
            passOk = await WaitForPacketAsync(
                p => p.Header.Ident is Grobal2.SM_PASSOK_SELECTSERVER or Grobal2.SM_PASSWD_FAIL or Grobal2.SM_ID_NOTFOUND,
                TimeSpan.FromSeconds(10),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Login timeout: no response to CM_IDPASSWORD.");
        }

        if (passOk.Header.Ident == Grobal2.SM_ID_NOTFOUND)
            throw new InvalidOperationException("Login failed: account not found (SM_ID_NOTFOUND).");

        if (passOk.Header.Ident == Grobal2.SM_PASSWD_FAIL)
        {
            int code = passOk.Header.Recog;
            throw new InvalidOperationException($"Login failed. Code={code} ({DescribeLoginFailCode(code)}).");
        }

        string decodedServerList = EdCode.DecodeString(passOk.BodyEncoded);
        string selectedServer = SelectServerName(decodedServerList, startup.ServerName);
        if (string.IsNullOrWhiteSpace(selectedServer))
            throw new InvalidOperationException("Server list is empty.");

        Log?.Invoke($"[login] select server: {selectedServer}");
        SetStage(MirSessionStage.SelectServer);
        await SendSelectServerAsync(selectedServer, cancellationToken).ConfigureAwait(false);

        MirServerPacket selOk;
        try
        {
            selOk = await WaitForPacketAsync(
                p => p.Header.Ident is Grobal2.SM_SELECTSERVER_OK or Grobal2.SM_STARTFAIL,
                TimeSpan.FromSeconds(10),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Login timeout: no response to CM_SELECTSERVER.");
        }

        if (selOk.Header.Ident == Grobal2.SM_STARTFAIL)
            throw new InvalidOperationException("Select server failed (SM_STARTFAIL).");

        (string selGateIp, int selGatePort, int cert) = ParseSelectServerOk(selOk.BodyEncoded);
        Log?.Invoke($"[login] SelGate: {selGateIp}:{selGatePort} cert={cert}");

        SetStage(MirSessionStage.SelectGate);
        await ReconnectAsync(selGateIp, selGatePort, cancellationToken).ConfigureAwait(false);

        await SendQueryCharactersAsync(credentials.Account, cert, cancellationToken).ConfigureAwait(false);
        MirServerPacket chrPacket = await WaitForPacketAsync(
            p => p.Header.Ident is Grobal2.SM_QUERYCHR or Grobal2.SM_QUERYCHR_FAIL,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (chrPacket.Header.Ident == Grobal2.SM_QUERYCHR_FAIL)
            throw new InvalidOperationException("Query character list failed.");

        IReadOnlyList<MirCharacterInfo> characters = ParseCharacterList(chrPacket.BodyEncoded);
        string? preferredName = string.IsNullOrWhiteSpace(credentials.PreferredCharacterName) ? null : credentials.PreferredCharacterName.Trim();
        bool shouldCreate = false;
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            bool hasPreferred = false;
            foreach (MirCharacterInfo c in characters)
            {
                if (string.Equals(c.Name, preferredName, StringComparison.OrdinalIgnoreCase))
                {
                    hasPreferred = true;
                    break;
                }
            }

            shouldCreate = characters.Count == 0 || (credentials.CreateIfMissing && !hasPreferred);
        }

        if (shouldCreate)
        {
            string newName = preferredName!;
            Log?.Invoke($"[selchr] creating: {newName}");

            
            await Task.Delay(TimeSpan.FromMilliseconds(1100), cancellationToken).ConfigureAwait(false);
            await SendCreateCharacterAsync(credentials.Account, newName, hair: 0, job: 0, sex: 0, cancellationToken).ConfigureAwait(false);

            MirServerPacket newChrResp = await WaitForPacketAsync(
                p => p.Header.Ident is Grobal2.SM_NEWCHR_SUCCESS or Grobal2.SM_NEWCHR_FAIL,
                TimeSpan.FromSeconds(10),
                cancellationToken).ConfigureAwait(false);

            if (newChrResp.Header.Ident == Grobal2.SM_NEWCHR_FAIL)
            {
                int code = newChrResp.Header.Recog;
                throw new InvalidOperationException($"Create character failed. Code={code} ({DescribeNewCharacterFailCode(code)}).");
            }

            await SendQueryCharactersAsync(credentials.Account, cert, cancellationToken).ConfigureAwait(false);
            chrPacket = await WaitForPacketAsync(
                p => p.Header.Ident is Grobal2.SM_QUERYCHR or Grobal2.SM_QUERYCHR_FAIL,
                TimeSpan.FromSeconds(10),
                cancellationToken).ConfigureAwait(false);

            if (chrPacket.Header.Ident == Grobal2.SM_QUERYCHR_FAIL)
                throw new InvalidOperationException("Query character list failed (after create).");

            characters = ParseCharacterList(chrPacket.BodyEncoded);
        }

        SetStage(MirSessionStage.SelectCharacter);

        string selectedCharacter = SelectCharacterName(characters, credentials.PreferredCharacterName);
        if (string.IsNullOrWhiteSpace(selectedCharacter))
            throw new InvalidOperationException("No character available.");

        Log?.Invoke($"[login] select character: {selectedCharacter}");
        await SendSelectCharacterAsync(credentials.Account, selectedCharacter, cancellationToken).ConfigureAwait(false);

        MirServerPacket startPlay = await WaitForPacketAsync(
            p => p.Header.Ident is Grobal2.SM_STARTPLAY or Grobal2.SM_STARTFAIL,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (startPlay.Header.Ident == Grobal2.SM_STARTFAIL)
            throw new InvalidOperationException("Start play failed (server full or invalid character).");

        (string runGateIp, int runGatePort) = ParseStartPlay(startPlay.BodyEncoded);
        Log?.Invoke($"[login] RunGate: {runGateIp}:{runGatePort}");

        SetStage(MirSessionStage.RunGate);
        await ReconnectAsync(runGateIp, runGatePort, cancellationToken).ConfigureAwait(false);

        _runGateCredentials = new RunGateCredentials(credentials.Account, selectedCharacter, cert);
        await SendRunGateLoginAsync(credentials.Account, selectedCharacter, cert, cancellationToken).ConfigureAwait(false);

        return new MirLoginResult(
            ServerName: selectedServer,
            Certification: cert,
            SelGateAddress: selGateIp,
            SelGatePort: selGatePort,
            Characters: characters,
            SelectedCharacterName: selectedCharacter,
            RunGateAddress: runGateIp,
            RunGatePort: runGatePort,
            ServerListRaw: decodedServerList);
    }

    public async Task<MirServerListResult> LoginGateAsync(
        MirStartupInfo startup,
        string account,
        string password,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startup);

        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required.", nameof(account));

        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password is required.", nameof(password));

        _loginGateHost = startup.ServerAddress;
        _loginGatePort = startup.ServerPort;
        _loginAccount = account.Trim();
        _loginCertification = 0;
        _selGateHost = null;
        _selGatePort = 0;
        _runGateCredentials = null;

        SetStage(MirSessionStage.LoginGate);
        await ReconnectAsync(startup.ServerAddress, startup.ServerPort, cancellationToken).ConfigureAwait(false);

        await TrySendProtocolAsync(cancellationToken).ConfigureAwait(false);
        await SendLoginAsync(account, password, cancellationToken).ConfigureAwait(false);

        MirServerPacket passOk = await WaitForPacketAsync(
            p => p.Header.Ident is Grobal2.SM_PASSOK_SELECTSERVER or Grobal2.SM_PASSWD_FAIL or Grobal2.SM_ID_NOTFOUND,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (passOk.Header.Ident == Grobal2.SM_ID_NOTFOUND)
            throw new InvalidOperationException("Login failed: account not found (SM_ID_NOTFOUND).");

        if (passOk.Header.Ident == Grobal2.SM_PASSWD_FAIL)
        {
            int code = passOk.Header.Recog;
            throw new InvalidOperationException($"Login failed. Code={code} ({DescribeLoginFailCode(code)}).");
        }

        string decodedServerList = EdCode.DecodeString(passOk.BodyEncoded);
        SetStage(MirSessionStage.SelectServer);
        return new MirServerListResult(decodedServerList);
    }

    public async Task ChangePasswordAsync(
        MirStartupInfo startup,
        string account,
        string oldPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startup);

        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required.", nameof(account));

        if (string.IsNullOrEmpty(oldPassword))
            throw new ArgumentException("Old password is required.", nameof(oldPassword));

        if (string.IsNullOrEmpty(newPassword))
            throw new ArgumentException("New password is required.", nameof(newPassword));

        try
        {
            SetStage(MirSessionStage.LoginGate);
            await ReconnectAsync(startup.ServerAddress, startup.ServerPort, cancellationToken).ConfigureAwait(false);

            await TrySendProtocolAsync(cancellationToken).ConfigureAwait(false);
            await SendChangePasswordAsync(account.Trim(), oldPassword, newPassword, cancellationToken).ConfigureAwait(false);

            MirServerPacket resp = await WaitForPacketAsync(
                p => p.Header.Ident is Grobal2.SM_CHGPASSWD_SUCCESS or Grobal2.SM_CHGPASSWD_FAIL,
                TimeSpan.FromSeconds(10),
                cancellationToken).ConfigureAwait(false);

            if (resp.Header.Ident == Grobal2.SM_CHGPASSWD_FAIL)
            {
                int code = resp.Header.Recog;
                throw new InvalidOperationException($"Change password failed. Code={code} ({DescribeChangePasswordFailCode(code)}).");
            }
        }
        finally
        {
            try { await DisconnectAsync().ConfigureAwait(false); } catch {  }
        }
    }

    public async Task CreateAccountAsync(
        MirStartupInfo startup,
        MirAccountFullEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(entry);

        string account = entry.Account?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required.", nameof(entry));

        if (string.IsNullOrEmpty(entry.Password))
            throw new ArgumentException("Password is required.", nameof(entry));

        try
        {
            SetStage(MirSessionStage.LoginGate);
            await ReconnectAsync(startup.ServerAddress, startup.ServerPort, cancellationToken).ConfigureAwait(false);

            await TrySendProtocolAsync(cancellationToken).ConfigureAwait(false);
            await SendAddNewUserAsync(entry, cancellationToken).ConfigureAwait(false);

            MirServerPacket resp = await WaitForPacketAsync(
                p => p.Header.Ident is Grobal2.SM_NEWID_SUCCESS or Grobal2.SM_NEWID_FAIL,
                TimeSpan.FromSeconds(10),
                cancellationToken).ConfigureAwait(false);

            if (resp.Header.Ident == Grobal2.SM_NEWID_FAIL)
            {
                int code = resp.Header.Recog;
                throw new InvalidOperationException($"Create account failed. Code={code} ({DescribeNewIdFailCode(code)}).");
            }
        }
        finally
        {
            try { await DisconnectAsync().ConfigureAwait(false); } catch {  }
        }
    }

    public async Task UpdateAccountAsync(
        MirStartupInfo startup,
        MirAccountFullEntry entry,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(entry);

        string account = entry.Account?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required.", nameof(entry));

        if (string.IsNullOrEmpty(entry.Password))
            throw new ArgumentException("Password is required.", nameof(entry));

        try
        {
            _ = await LoginGateAsync(startup, account, entry.Password, cancellationToken).ConfigureAwait(false);
            await SendUpdateUserAsync(entry, cancellationToken).ConfigureAwait(false);

            MirServerPacket resp = await WaitForPacketAsync(
                p => p.Header.Ident is Grobal2.SM_UPDATEID_SUCCESS or Grobal2.SM_UPDATEID_FAIL,
                TimeSpan.FromSeconds(10),
                cancellationToken).ConfigureAwait(false);

            if (resp.Header.Ident == Grobal2.SM_UPDATEID_FAIL)
            {
                int code = resp.Header.Recog;
                throw new InvalidOperationException($"Update account failed. Code={code} ({DescribeUpdateIdFailCode(code)}).");
            }
        }
        finally
        {
            try { await DisconnectAsync().ConfigureAwait(false); } catch {  }
        }
    }

    public async Task<MirSelectServerResult> SelectServerAsync(string serverName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            throw new ArgumentException("Server name is required.", nameof(serverName));

        string trimmed = serverName.Trim();

        await SendSelectServerAsync(trimmed, cancellationToken).ConfigureAwait(false);

        MirServerPacket selOk = await WaitForPacketAsync(
            p => p.Header.Ident is Grobal2.SM_SELECTSERVER_OK or Grobal2.SM_STARTFAIL,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (selOk.Header.Ident == Grobal2.SM_STARTFAIL)
            throw new InvalidOperationException("Select server failed (SM_STARTFAIL).");

        (string selGateIp, int selGatePort, int cert) = ParseSelectServerOk(selOk.BodyEncoded);
        _loginCertification = cert;
        _selGateHost = selGateIp;
        _selGatePort = selGatePort;
        SetStage(MirSessionStage.SelectGate);
        return new MirSelectServerResult(trimmed, cert, selGateIp, selGatePort);
    }

    public async Task<MirCharacterListResult> QueryCharactersAsync(
        string selGateAddress,
        int selGatePort,
        string account,
        int certification,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selGateAddress))
            throw new ArgumentException("SelGate address is required.", nameof(selGateAddress));

        if (selGatePort is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(selGatePort), "SelGate port is invalid.");

        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required.", nameof(account));

        _loginAccount = account.Trim();
        _loginCertification = certification;
        _selGateHost = selGateAddress.Trim();
        _selGatePort = selGatePort;

        SetStage(MirSessionStage.SelectGate);
        await ReconnectAsync(selGateAddress, selGatePort, cancellationToken).ConfigureAwait(false);

        await SendQueryCharactersAsync(account, certification, cancellationToken).ConfigureAwait(false);
        MirServerPacket chrPacket = await WaitForPacketAsync(
            p => p.Header.Ident is Grobal2.SM_QUERYCHR or Grobal2.SM_QUERYCHR_FAIL,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (chrPacket.Header.Ident == Grobal2.SM_QUERYCHR_FAIL)
            throw new InvalidOperationException("Query character list failed (SM_QUERYCHR_FAIL).");

        IReadOnlyList<MirCharacterInfo> characters = ParseCharacterList(chrPacket.BodyEncoded);
        SetStage(MirSessionStage.SelectCharacter);
        return new MirCharacterListResult(characters);
    }

    public async Task<MirCharacterListResult> ReconnectToSelGateAndQueryCharactersAsync(CancellationToken cancellationToken)
    {
        if (!CanReconnectToSelGate)
            throw new InvalidOperationException("Reconnect requested but SelGate endpoint/credentials are not available yet.");

        SetStage(MirSessionStage.SelectGate);
        await ReconnectAsync(_selGateHost!, _selGatePort, cancellationToken).ConfigureAwait(false);

        await SendQueryCharactersAsync(_loginAccount!, _loginCertification, cancellationToken).ConfigureAwait(false);
        MirServerPacket chrPacket = await WaitForPacketAsync(
            p => p.Header.Ident is Grobal2.SM_QUERYCHR or Grobal2.SM_QUERYCHR_FAIL,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (chrPacket.Header.Ident == Grobal2.SM_QUERYCHR_FAIL)
            throw new InvalidOperationException("Query character list failed (SM_QUERYCHR_FAIL).");

        IReadOnlyList<MirCharacterInfo> characters = ParseCharacterList(chrPacket.BodyEncoded);
        SetStage(MirSessionStage.SelectCharacter);
        return new MirCharacterListResult(characters);
    }

    public async Task<MirCharacterListResult> CreateCharacterAsync(
        string account,
        int certification,
        string characterName,
        byte hair,
        byte job,
        byte sex,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required.", nameof(account));

        if (certification <= 0)
            throw new ArgumentOutOfRangeException(nameof(certification), "Certification is required.");

        if (string.IsNullOrWhiteSpace(characterName))
            throw new ArgumentException("Character name is required.", nameof(characterName));

        string accountTrimmed = account.Trim();
        string nameTrimmed = characterName.Trim();

        
        await Task.Delay(TimeSpan.FromMilliseconds(1100), cancellationToken).ConfigureAwait(false);

        await SendCreateCharacterAsync(accountTrimmed, nameTrimmed, hair, job, sex, cancellationToken).ConfigureAwait(false);

        MirServerPacket newChrResp = await WaitForPacketAsync(
            p => p.Header.Ident is Grobal2.SM_NEWCHR_SUCCESS or Grobal2.SM_NEWCHR_FAIL,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (newChrResp.Header.Ident == Grobal2.SM_NEWCHR_FAIL)
        {
            int code = newChrResp.Header.Recog;
            throw new InvalidOperationException($"Create character failed. Code={code} ({DescribeNewCharacterFailCode(code)}).");
        }

        await SendQueryCharactersAsync(accountTrimmed, certification, cancellationToken).ConfigureAwait(false);
        MirServerPacket chrPacket = await WaitForPacketAsync(
            p => p.Header.Ident is Grobal2.SM_QUERYCHR or Grobal2.SM_QUERYCHR_FAIL,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (chrPacket.Header.Ident == Grobal2.SM_QUERYCHR_FAIL)
            throw new InvalidOperationException("Query character list failed (after create).");

        IReadOnlyList<MirCharacterInfo> characters = ParseCharacterList(chrPacket.BodyEncoded);
        SetStage(MirSessionStage.SelectCharacter);
        return new MirCharacterListResult(characters);
    }

    public async Task<MirCharacterListResult> DeleteCharacterAsync(
        string account,
        int certification,
        string characterName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required.", nameof(account));

        if (certification <= 0)
            throw new ArgumentOutOfRangeException(nameof(certification), "Certification is required.");

        if (string.IsNullOrWhiteSpace(characterName))
            throw new ArgumentException("Character name is required.", nameof(characterName));

        string accountTrimmed = account.Trim();
        string nameTrimmed = characterName.Trim();

        await SendDeleteCharacterAsync(nameTrimmed, cancellationToken).ConfigureAwait(false);

        MirServerPacket resp = await WaitForPacketAsync(
            p => p.Header.Ident is Grobal2.SM_DELCHR_SUCCESS or Grobal2.SM_DELCHR_FAIL,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (resp.Header.Ident == Grobal2.SM_DELCHR_FAIL)
            throw new InvalidOperationException("Delete character failed (SM_DELCHR_FAIL).");

        await SendQueryCharactersAsync(accountTrimmed, certification, cancellationToken).ConfigureAwait(false);
        MirServerPacket chrPacket = await WaitForPacketAsync(
            p => p.Header.Ident is Grobal2.SM_QUERYCHR or Grobal2.SM_QUERYCHR_FAIL,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (chrPacket.Header.Ident == Grobal2.SM_QUERYCHR_FAIL)
            throw new InvalidOperationException("Query character list failed (after delete).");

        IReadOnlyList<MirCharacterInfo> characters = ParseCharacterList(chrPacket.BodyEncoded);
        SetStage(MirSessionStage.SelectCharacter);
        return new MirCharacterListResult(characters);
    }

    public async Task<MirDeletedCharacterListResult> QueryDeletedCharactersAsync(CancellationToken cancellationToken)
    {
        await SendQueryDeletedCharactersAsync(cancellationToken).ConfigureAwait(false);

        MirServerPacket resp = await WaitForPacketAsync(
            p => p.Header.Ident == Grobal2.SM_QUERYDELCHR,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        int code = resp.Header.Recog;
        if (code != 1)
            throw new InvalidOperationException($"Query deleted characters failed. Code={code}.");

        IReadOnlyList<MirDeletedCharacterInfo> characters = ParseDeletedCharacterList(resp.BodyEncoded);
        return new MirDeletedCharacterListResult(characters);
    }

    public async Task<MirCharacterListResult> RestoreDeletedCharacterAsync(
        string account,
        int certification,
        string deletedCharacterName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required.", nameof(account));

        if (certification <= 0)
            throw new ArgumentOutOfRangeException(nameof(certification), "Certification is required.");

        if (string.IsNullOrWhiteSpace(deletedCharacterName))
            throw new ArgumentException("Character name is required.", nameof(deletedCharacterName));

        string accountTrimmed = account.Trim();
        string nameTrimmed = deletedCharacterName.Trim();

        await SendRestoreDeletedCharacterAsync(nameTrimmed, cancellationToken).ConfigureAwait(false);

        MirServerPacket resp = await WaitForPacketAsync(
            p => p.Header.Ident == Grobal2.SM_GETBACKDELCHR,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        int code = resp.Header.Recog;
        if (code != 1)
            throw new InvalidOperationException($"Restore deleted character failed. Code={code} ({DescribeGetBackDeletedCharacterCode(code)}).");

        await SendQueryCharactersAsync(accountTrimmed, certification, cancellationToken).ConfigureAwait(false);
        MirServerPacket chrPacket = await WaitForPacketAsync(
            p => p.Header.Ident is Grobal2.SM_QUERYCHR or Grobal2.SM_QUERYCHR_FAIL,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (chrPacket.Header.Ident == Grobal2.SM_QUERYCHR_FAIL)
            throw new InvalidOperationException("Query character list failed (after restore).");

        IReadOnlyList<MirCharacterInfo> characters = ParseCharacterList(chrPacket.BodyEncoded);
        SetStage(MirSessionStage.SelectCharacter);
        return new MirCharacterListResult(characters);
    }

    public async Task<MirStartPlayResult> StartPlayAsync(string account, string characterName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required.", nameof(account));

        if (string.IsNullOrWhiteSpace(characterName))
            throw new ArgumentException("Character name is required.", nameof(characterName));

        await SendSelectCharacterAsync(account.Trim(), characterName.Trim(), cancellationToken).ConfigureAwait(false);

        MirServerPacket startPlay = await WaitForPacketAsync(
            p => p.Header.Ident is Grobal2.SM_STARTPLAY or Grobal2.SM_STARTFAIL,
            TimeSpan.FromSeconds(10),
            cancellationToken).ConfigureAwait(false);

        if (startPlay.Header.Ident == Grobal2.SM_STARTFAIL)
            throw new InvalidOperationException("Start play failed (SM_STARTFAIL).");

        (string runGateIp, int runGatePort) = ParseStartPlay(startPlay.BodyEncoded);
        return new MirStartPlayResult(runGateIp, runGatePort);
    }

    public async Task EnterRunGateAsync(
        string runGateAddress,
        int runGatePort,
        string account,
        string characterName,
        int certification,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runGateAddress))
            throw new ArgumentException("RunGate address is required.", nameof(runGateAddress));

        if (runGatePort is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(runGatePort), "RunGate port is invalid.");

        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException("Account is required.", nameof(account));

        if (string.IsNullOrWhiteSpace(characterName))
            throw new ArgumentException("Character name is required.", nameof(characterName));

        SetStage(MirSessionStage.RunGate);
        await ReconnectAsync(runGateAddress, runGatePort, cancellationToken).ConfigureAwait(false);

        _runGateCredentials = new RunGateCredentials(account.Trim(), characterName.Trim(), certification);
        _loginAccount ??= account.Trim();
        _loginCertification = certification;
        await SendRunGateLoginAsync(account.Trim(), characterName.Trim(), certification, cancellationToken).ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        await _connection.DisconnectAsync().ConfigureAwait(false);
        _loginGateHost = null;
        _loginGatePort = 0;
        _loginAccount = null;
        _loginCertification = 0;
        _selGateHost = null;
        _selGatePort = 0;
        _runGateCredentials = null;
        SetStage(MirSessionStage.Idle);
    }

    public bool TryDequeuePacket(out MirServerPacket packet)
    {
        if (TryDequeuePendingAny(out packet))
            return true;

        while (_incoming.Reader.TryRead(out string? raw))
        {
            if (!MirPacketDecoder.TryDecode(raw, out packet))
                continue;

            TraceReceive(packet);
            return true;
        }

        packet = default;
        return false;
    }

    public Task SendClientMessageAsync(ushort ident, int recog, ushort param, ushort tag, ushort series, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(ident, recog, param, tag, series);
        string payload = EdCode.EncodeMessage(msg);
        TraceSend(msg, bodyPlain: null, payload);
        return _connection.SendPayloadAsync(payload, cancellationToken);
    }

    public Task SendClientStringAsync(ushort ident, int recog, ushort param, ushort tag, ushort series, string body, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(ident, recog, param, tag, series);
        string payload = EdCode.EncodeMessage(msg) + EdCode.EncodeString(body);
        TraceSend(msg, bodyPlain: body, payload);
        return _connection.SendPayloadAsync(payload, cancellationToken);
    }

    public Task SendClientBufferAsync(ushort ident, int recog, ushort param, ushort tag, ushort series, ReadOnlySpan<byte> body, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(ident, recog, param, tag, series);
        string payload = EdCode.EncodeMessage(msg) + EdCode.EncodeBuffer(body);
        TraceSendBuffer(msg, body, payload);
        return _connection.SendPayloadAsync(payload, cancellationToken);
    }

    public async Task RunMessageLoopAsync(MirMessageDispatcher dispatcher, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        while (TryDequeuePendingAny(out MirServerPacket pending))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await dispatcher.DispatchAsync(pending).ConfigureAwait(false);
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            bool ready;
            try
            {
                ready = await WaitForDataOrDisconnectAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Log?.Invoke($"[net] message loop disconnected: {ex.GetType().Name}: {ex.Message}");
                ResetDisconnectSignal();
                continue;
            }

            if (!ready)
            {
                Log?.Invoke("[net] message loop disconnected");
                ResetDisconnectSignal();
                continue;
            }

            while (_incoming.Reader.TryRead(out string? raw))
            {
                if (!MirPacketDecoder.TryDecode(raw, out MirServerPacket packet))
                    continue;

                TraceReceive(packet);
                await dispatcher.DispatchAsync(packet).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _incoming.Writer.TryComplete();
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private void SetStage(MirSessionStage stage)
    {
        if (_stage == stage)
            return;
        _stage = stage;
        StageChanged?.Invoke(stage);
    }

    private async Task ReconnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        await _connection.DisconnectAsync().ConfigureAwait(false);
        ResetDisconnectSignal();
        DrainIncomingPackets();
        _currentEndpoint = $"{host}:{port}";
        lock (_pendingPacketsGate)
            _pendingPackets.Clear();
        await _connection.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
    }

    public async Task ReconnectToRunGateAsync(string host, int port, CancellationToken cancellationToken)
    {
        if (_runGateCredentials == null)
            throw new InvalidOperationException("Reconnect requested but run-gate credentials are not available yet.");

        Log?.Invoke($"[net] reconnect -> {host}:{port}");
        SetStage(MirSessionStage.RunGate);
        await ReconnectAsync(host, port, cancellationToken).ConfigureAwait(false);
        await SendRunGateLoginAsync(_runGateCredentials.Account, _runGateCredentials.CharacterName, _runGateCredentials.Certification, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SendLoginAsync(string account, string password, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(Grobal2.CM_IDPASSWORD, 0, 0, 0, 0);
        string body = $"{account}/{password}";
        string payload = EdCode.EncodeMessage(msg) + EdCode.EncodeString(body);
        TraceSend(msg, bodyPlain: $"{account}/<redacted>", payload);
        Log?.Invoke("[login] -> CM_IDPASSWORD");
        await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task TrySendProtocolAsync(CancellationToken cancellationToken)
    {
        
        if (!IsProtocolProbeEnabled())
            return;

        
        try
        {
            var msg = CmdPack.MakeDefaultMsg(Grobal2.CM_PROTOCOL, Grobal2.VERSION_NUMBER, 0, 0, 0);
            string payload = EdCode.EncodeMessage(msg);
            TraceSend(msg, bodyPlain: null, payload);
            Log?.Invoke($"[login] -> CM_PROTOCOL ({Grobal2.VERSION_NUMBER})");
            await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);

            MirServerPacket resp = await WaitForPacketAsync(
                p => p.Header.Ident is Grobal2.SM_CERTIFICATION_SUCCESS or Grobal2.SM_CERTIFICATION_FAIL,
                TimeSpan.FromSeconds(2),
                cancellationToken).ConfigureAwait(false);

            if (resp.Header.Ident == Grobal2.SM_CERTIFICATION_SUCCESS)
                Log?.Invoke("[login] CM_PROTOCOL ok");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Log?.Invoke("[login] CM_PROTOCOL timeout (ignored)");
        }
    }

    private static bool IsProtocolProbeEnabled()
    {
        string? v = Environment.GetEnvironmentVariable("MIRCLIENT_SEND_CM_PROTOCOL");
        if (string.IsNullOrWhiteSpace(v))
            return false;

        v = v.Trim();
        return v != "0" &&
               !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(v, "off", StringComparison.OrdinalIgnoreCase);
    }

    private async Task SendSelectServerAsync(string serverName, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(Grobal2.CM_SELECTSERVER, 0, 0, 0, 0);
        string payload = EdCode.EncodeMessage(msg) + EdCode.EncodeString(serverName);
        TraceSend(msg, bodyPlain: serverName, payload);
        Log?.Invoke("[login] -> CM_SELECTSERVER");
        await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendChangePasswordAsync(string account, string oldPassword, string newPassword, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(Grobal2.CM_CHANGEPASSWORD, 0, 0, 0, 0);
        string body = $"{account}\t{oldPassword}\t{newPassword}";
        string payload = EdCode.EncodeMessage(msg) + EdCode.EncodeString(body);
        TraceSend(msg, bodyPlain: $"{account}\t<redacted>\t<redacted>", payload);
        Log?.Invoke("[login] -> CM_CHANGEPASSWORD");
        await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendAddNewUserAsync(MirAccountFullEntry entry, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(Grobal2.CM_ADDNEWUSER, 0, 0, 0, 0);
        byte[] buffer = BuildUserFullEntryBuffer(entry);
        string payload = EdCode.EncodeMessage(msg) + EdCode.EncodeBuffer(buffer);
        TraceSendBuffer(msg, buffer, payload);
        Log?.Invoke("[login] -> CM_ADDNEWUSER");
        await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendUpdateUserAsync(MirAccountFullEntry entry, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(Grobal2.CM_UPDATEUSER, 0, 0, 0, 0);
        byte[] buffer = BuildUserFullEntryBuffer(entry);
        string payload = EdCode.EncodeMessage(msg) + EdCode.EncodeBuffer(buffer);
        TraceSendBuffer(msg, buffer, payload);
        Log?.Invoke("[login] -> CM_UPDATEUSER");
        await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendQueryCharactersAsync(string account, int certification, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(Grobal2.CM_QUERYCHR, 0, 0, 0, 0);
        string body = $"{account}/{certification}";
        string payload = EdCode.EncodeMessage(msg) + EdCode.EncodeString(body);
        TraceSend(msg, bodyPlain: body, payload);
        Log?.Invoke("[selchr] -> CM_QUERYCHR");
        await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendSelectCharacterAsync(string account, string characterName, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(Grobal2.CM_SELCHR, 0, 0, 0, 0);
        string body = $"{account}/{characterName}";
        string payload = EdCode.EncodeMessage(msg) + EdCode.EncodeString(body);
        TraceSend(msg, bodyPlain: body, payload);
        Log?.Invoke("[selchr] -> CM_SELCHR");
        await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendRunGateLoginAsync(string account, string characterName, int certification, CancellationToken cancellationToken)
    {
        
        string hwidToken = OperatingSystem.IsWindows() ? MirHardwareId.CreateLoginToken() : string.Empty;
        string msg = string.IsNullOrEmpty(hwidToken)
            ? $"**{account}/{characterName}/{certification}/{Grobal2.CLIENT_VERSION_NUMBER}/{Grobal2.RUNLOGINCODE}"
            : $"**{account}/{characterName}/{certification}/{Grobal2.CLIENT_VERSION_NUMBER}/{Grobal2.RUNLOGINCODE}/{hwidToken}";
        string payload = EdCode.EncodeString(msg);
        TraceSendRaw($"RunGateLogin **{account}/{characterName}/{certification}/{Grobal2.CLIENT_VERSION_NUMBER}/{Grobal2.RUNLOGINCODE}/<hwid>", payload);
        Log?.Invoke("[rungate] -> **login");
        await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendCreateCharacterAsync(string account, string characterName, byte hair, byte job, byte sex, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(Grobal2.CM_NEWCHR, 0, 0, 0, 0);
        string body = $"{account}/{characterName}/{hair}/{job}/{sex}";
        string payload = EdCode.EncodeMessage(msg) + EdCode.EncodeString(body);
        TraceSend(msg, bodyPlain: body, payload);
        Log?.Invoke("[selchr] -> CM_NEWCHR");
        await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendDeleteCharacterAsync(string characterName, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(Grobal2.CM_DELCHR, 0, 0, 0, 0);
        string payload = EdCode.EncodeMessage(msg) + EdCode.EncodeString(characterName);
        TraceSend(msg, bodyPlain: characterName, payload);
        Log?.Invoke("[selchr] -> CM_DELCHR");
        await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendQueryDeletedCharactersAsync(CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(Grobal2.CM_QUERYDELCHR, 0, 0, 0, 0);
        string payload = EdCode.EncodeMessage(msg);
        TraceSend(msg, bodyPlain: null, payload);
        Log?.Invoke("[selchr] -> CM_QUERYDELCHR");
        await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendRestoreDeletedCharacterAsync(string characterName, CancellationToken cancellationToken)
    {
        var msg = CmdPack.MakeDefaultMsg(Grobal2.CM_GETBACKDELCHR, 0, 0, 0, 0);
        string payload = EdCode.EncodeMessage(msg) + EdCode.EncodeString(characterName);
        TraceSend(msg, bodyPlain: characterName, payload);
        Log?.Invoke("[selchr] -> CM_GETBACKDELCHR");
        await _connection.SendPayloadAsync(payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MirServerPacket> WaitForPacketAsync(
        Func<MirServerPacket, bool> predicate,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (TryDequeuePending(predicate, out MirServerPacket queued))
            return queued;

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);
        CancellationToken ct = linked.Token;

        while (await WaitForDataOrDisconnectAsync(ct).ConfigureAwait(false))
        {
            while (_incoming.Reader.TryRead(out string? raw))
            {
                if (!MirPacketDecoder.TryDecode(raw, out MirServerPacket packet))
                    continue;

                TraceReceive(packet);
                Log?.Invoke($"[net] <- Ident={packet.Header.Ident} Recog={packet.Header.Recog} Param={packet.Header.Param} Tag={packet.Header.Tag} Series={packet.Header.Series} BodyLen={packet.BodyEncoded.Length}");

                if (TryGetFatalLoginError(packet, out string fatal))
                    throw new InvalidOperationException(fatal);

                if (predicate(packet))
                    return packet;

                lock (_pendingPacketsGate)
                    _pendingPackets.Enqueue(packet);
            }
        }

        throw new InvalidOperationException("Disconnected while waiting for server response.");
    }

    private async Task<bool> WaitForDataOrDisconnectAsync(CancellationToken cancellationToken)
    {
        Task<bool> waitReadTask = _incoming.Reader.WaitToReadAsync(cancellationToken).AsTask();
        Task<bool> disconnectTask = _disconnectSignal.Task;

        Task completed = await Task.WhenAny(waitReadTask, disconnectTask).ConfigureAwait(false);
        if (completed == disconnectTask)
        {
            await disconnectTask.ConfigureAwait(false);
            return false;
        }

        return await waitReadTask.ConfigureAwait(false);
    }

    private bool TryDequeuePending(Func<MirServerPacket, bool> predicate, out MirServerPacket packet)
    {
        lock (_pendingPacketsGate)
        {
            packet = default;
            bool found = false;

            int count = _pendingPackets.Count;
            for (int i = 0; i < count; i++)
            {
                MirServerPacket p = _pendingPackets.Dequeue();
                if (!found && predicate(p))
                {
                    packet = p;
                    found = true;
                    continue;
                }

                _pendingPackets.Enqueue(p);
            }

            return found;
        }
    }

    private bool TryDequeuePendingAny(out MirServerPacket packet)
    {
        lock (_pendingPacketsGate)
            return _pendingPackets.TryDequeue(out packet);
    }

    private static string SelectServerName(string decodedServerList, string preferredServerName)
    {
        if (string.IsNullOrWhiteSpace(decodedServerList))
            return string.Empty;

        string? first = null;
        string? preferred = null;

        string[] parts = decodedServerList.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            string name = parts[i].Trim();
            if (name.Length == 0)
                continue;
            first ??= name;
            if (!string.IsNullOrWhiteSpace(preferredServerName) &&
                string.Equals(name, preferredServerName, StringComparison.OrdinalIgnoreCase))
            {
                preferred = name;
                break;
            }
        }

        return preferred ?? first ?? string.Empty;
    }

    private static (string ip, int port, int cert) ParseSelectServerOk(string encodedBody)
    {
        string decoded = EdCode.DecodeString(encodedBody);
        string[] parts = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            throw new FormatException($"SM_SELECTSERVER_OK body invalid: '{decoded}'.");

        string ip = parts[0].Trim();
        if (!int.TryParse(parts[1], out int port))
            throw new FormatException($"SM_SELECTSERVER_OK port invalid: '{parts[1]}'.");
        if (!int.TryParse(parts[2], out int cert))
            throw new FormatException($"SM_SELECTSERVER_OK cert invalid: '{parts[2]}'.");

        return (ip, port, cert);
    }

    private static (string ip, int port) ParseStartPlay(string encodedBody)
    {
        string decoded = EdCode.DecodeString(encodedBody);
        string[] parts = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            throw new FormatException($"SM_STARTPLAY body invalid: '{decoded}'.");

        string ip = parts[0].Trim();
        if (!int.TryParse(parts[1], out int port))
            throw new FormatException($"SM_STARTPLAY port invalid: '{parts[1]}'.");

        return (ip, port);
    }

    private static IReadOnlyList<MirCharacterInfo> ParseCharacterList(string encodedBody)
    {
        string decoded = EdCode.DecodeString(encodedBody);
        if (string.IsNullOrWhiteSpace(decoded))
            return Array.Empty<MirCharacterInfo>();

        string[] parts = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var list = new List<MirCharacterInfo>();

        for (int i = 0; i + 4 < parts.Length; i += 5)
        {
            string nameRaw = parts[i].Trim();
            bool selected = nameRaw.StartsWith('*');
            string name = selected ? nameRaw[1..] : nameRaw;

            if (!byte.TryParse(parts[i + 1], out byte job))
                continue;
            if (!byte.TryParse(parts[i + 2], out byte hair))
                continue;
            if (!int.TryParse(parts[i + 3], out int level))
                continue;
            if (!byte.TryParse(parts[i + 4], out byte sex))
                continue;

            if (string.IsNullOrWhiteSpace(name))
                continue;

            list.Add(new MirCharacterInfo(name, job, hair, level, sex, selected));
        }

        return list;
    }

    private static IReadOnlyList<MirDeletedCharacterInfo> ParseDeletedCharacterList(string encodedBody)
    {
        string decoded = EdCode.DecodeString(encodedBody);
        if (string.IsNullOrWhiteSpace(decoded))
            return Array.Empty<MirDeletedCharacterInfo>();

        string[] parts = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var list = new List<MirDeletedCharacterInfo>();

        for (int i = 0; i + 3 < parts.Length; i += 5)
        {
            string name = parts[i].Trim();
            if (name.Length == 0)
                continue;

            if (!byte.TryParse(parts[i + 1], out byte job))
                continue;

            if (!byte.TryParse(parts[i + 2], out byte sex))
                continue;

            if (!int.TryParse(parts[i + 3], out int level))
                continue;

            list.Add(new MirDeletedCharacterInfo(name, job, sex, level));
        }

        return list;
    }

    private static string SelectCharacterName(IReadOnlyList<MirCharacterInfo> characters, string? preferredName)
    {
        if (characters.Count == 0)
            return string.Empty;

        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            MirCharacterInfo? match = characters.FirstOrDefault(c =>
                string.Equals(c.Name, preferredName, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                return match.Name;
        }

        MirCharacterInfo? selected = characters.FirstOrDefault(c => c.Selected);
        if (selected != null)
            return selected.Name;

        return characters[0].Name;
    }

    private static TaskCompletionSource<bool> CreateDisconnectSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void ResetDisconnectSignal() =>
        _disconnectSignal = CreateDisconnectSignal();

    private void DrainIncomingPackets()
    {
        while (_incoming.Reader.TryRead(out _))
        {
        }
    }

    private sealed record RunGateCredentials(string Account, string CharacterName, int Certification);

    private static bool TryGetFatalLoginError(MirServerPacket packet, out string message)
    {
        message = string.Empty;

        switch (packet.Header.Ident)
        {
            case Grobal2.SM_CERTIFICATION_FAIL:
                message = "Login refused: certification failed (SM_CERTIFICATION_FAIL).";
                return true;
            case Grobal2.SM_VERSION_FAIL:
            {
                int crc1 = packet.Header.Recog;
                int crc2 = (packet.Header.Tag << 16) | packet.Header.Param;
                message = $"Login refused: client version mismatch (SM_VERSION_FAIL). crc1={crc1} crc2={crc2}";
                return true;
            }
            case Grobal2.SM_OVERCLIENTCOUNT:
            {
                string detail = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded).Trim() : string.Empty;
                message = string.IsNullOrWhiteSpace(detail)
                    ? "Login refused: too many clients (SM_OVERCLIENTCOUNT)."
                    : detail;
                return true;
            }
            case Grobal2.SM_CDVERSION_FAIL:
            {
                string detail = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded).Trim() : string.Empty;
                message = string.IsNullOrWhiteSpace(detail)
                    ? "Login refused: CD version check failed (SM_CDVERSION_FAIL)."
                    : detail;
                return true;
            }
            case Grobal2.SM_OUTOFCONNECTION:
            {
                string reason = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded).Trim() : string.Empty;
                message = string.IsNullOrWhiteSpace(reason)
                    ? "Disconnected by server (SM_OUTOFCONNECTION)."
                    : reason;
                return true;
            }
        }

        return false;
    }

    private static NetTraceMode ParseNetTraceMode()
    {
        string? v = Environment.GetEnvironmentVariable("MIRCLIENT_NET_TRACE");
        if (string.IsNullOrWhiteSpace(v))
            return NetTraceMode.LoginOnly;

        v = v.Trim();
        if (v == "0" ||
            string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "none", StringComparison.OrdinalIgnoreCase))
        {
            return NetTraceMode.Off;
        }

        if (v == "2" ||
            string.Equals(v, "all", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(v, "full", StringComparison.OrdinalIgnoreCase))
        {
            return NetTraceMode.All;
        }

        return NetTraceMode.LoginOnly;
    }

    private bool ShouldTraceNet()
    {
        if (s_netTraceMode == NetTraceMode.Off)
            return false;

        if (s_netTraceMode == NetTraceMode.All)
            return true;

        return _stage != MirSessionStage.InGame;
    }

    private static Dictionary<ushort, string> BuildIdentNameMap()
    {
        var map = new Dictionary<ushort, string>();

        try
        {
            foreach (FieldInfo field in typeof(Grobal2).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || field.IsInitOnly)
                    continue;

                string name = field.Name;
                if (!name.StartsWith("CM_", StringComparison.Ordinal) &&
                    !name.StartsWith("SM_", StringComparison.Ordinal) &&
                    !name.StartsWith("RM_", StringComparison.Ordinal) &&
                    !name.StartsWith("SS_", StringComparison.Ordinal))
                {
                    continue;
                }

                object? value = field.GetValue(null);
                if (value is ushort us)
                {
                    map[us] = name;
                    continue;
                }

                if (value is int i && i is >= ushort.MinValue and <= ushort.MaxValue)
                    map[(ushort)i] = name;
            }
        }
        catch
        {
            
        }

        return map;
    }

    private static string GetIdentDisplay(ushort ident)
    {
        if (s_identNames.Value.TryGetValue(ident, out string? name) && !string.IsNullOrWhiteSpace(name))
            return $"{ident}({name})";
        return $"{ident}";
    }

    private static string TruncateSingleLine(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || maxChars <= 0)
            return string.Empty;

        string normalized = value.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        if (normalized.Length <= maxChars)
            return normalized;
        return normalized[..maxChars] + "...";
    }

    private static string DescribeDefaultMsg(CmdPack msg) =>
        $"Ident={GetIdentDisplay(msg.Ident)} Recog={msg.Recog} Param={msg.Param} Tag={msg.Tag} Series={msg.Series}";

    private void TraceSend(CmdPack msg, string? bodyPlain, string payload)
    {
        if (!ShouldTraceNet())
            return;

        const int maxBodyChars = 800;
        const int maxPayloadChars = 800;
        string bodyDesc = string.IsNullOrEmpty(bodyPlain) ? "\"\"" : $"\"{TruncateSingleLine(bodyPlain, maxBodyChars)}\"";

        MirErrorLog.Write($"[net->] {DescribeDefaultMsg(msg)} Body={bodyDesc} PayloadLen={payload.Length}");
        MirErrorLog.Write($"[net->payload] {TruncateSingleLine(payload, maxPayloadChars)}");
    }

    private void TraceSendBuffer(CmdPack msg, ReadOnlySpan<byte> bodyBytes, string payload)
    {
        if (!ShouldTraceNet())
            return;

        const int maxPayloadChars = 800;
        const int previewBytes = 64;
        int take = Math.Min(previewBytes, bodyBytes.Length);
        string hex = take > 0 ? Convert.ToHexString(bodyBytes[..take]) : string.Empty;
        string preview = bodyBytes.Length > take ? $"{hex}..." : hex;

        MirErrorLog.Write($"[net->] {DescribeDefaultMsg(msg)} Body=bytes[{bodyBytes.Length}] Hex={preview} PayloadLen={payload.Length}");
        MirErrorLog.Write($"[net->payload] {TruncateSingleLine(payload, maxPayloadChars)}");
    }

    private void TraceSendRaw(string description, string payload)
    {
        if (!ShouldTraceNet())
            return;

        const int maxPayloadChars = 1200;
        MirErrorLog.Write($"[net->] {description} PayloadLen={payload.Length}");
        MirErrorLog.Write($"[net->payload] {TruncateSingleLine(payload, maxPayloadChars)}");
    }

    private void TraceReceive(MirServerPacket packet)
    {
        if (!ShouldTraceNet())
            return;

        const int maxRawChars = 1200;
        const int maxBodyChars = 800;

        string raw = packet.RawPayload ?? string.Empty;
        string rawTrim = TruncateSingleLine(raw, maxRawChars);

        ReadOnlySpan<char> span = raw.AsSpan();
        if (!span.IsEmpty && span[0] is >= '0' and <= '9')
            span = span[1..];

        string bodyEncoded = packet.BodyEncoded ?? string.Empty;
        string bodyPreview = TruncateSingleLine(bodyEncoded, maxBodyChars);

        string decodedPreview = string.Empty;
        if (!span.IsEmpty && span[0] == '+')
        {
            decodedPreview = bodyPreview;
        }
        else if (!string.IsNullOrEmpty(bodyEncoded))
        {
            decodedPreview = TruncateSingleLine(EdCode.DecodeString(bodyEncoded), maxBodyChars);
        }

        MirErrorLog.Write($"[net<-] Ident={GetIdentDisplay(packet.Header.Ident)} Recog={packet.Header.Recog} Param={packet.Header.Param} Tag={packet.Header.Tag} Series={packet.Header.Series} RawLen={raw.Length} BodyLen={bodyEncoded.Length}");
        MirErrorLog.Write($"[net<-raw] {rawTrim}");
        MirErrorLog.Write($"[net<-body] {bodyPreview}");
        if (!string.IsNullOrWhiteSpace(decodedPreview))
            MirErrorLog.Write($"[net<-decoded] {decodedPreview}");
    }

    private static Channel<string> CreateChannel() =>
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = true
        });

    private static string DescribeNewCharacterFailCode(int code) =>
        code switch
        {
            2 => "Name already exists",
            3 => "Character limit reached",
            4 => "Character record failed",
            _ => "Unknown"
        };

    private static string DescribeNewIdFailCode(int code) =>
        code switch
        {
            0 => "Account already exists",
            -1 => "Invalid request",
            _ => "Unknown"
        };

    private static string DescribeLoginFailCode(int code) =>
        code switch
        {
            -1 => "Password incorrect",
            -2 => "Too many failures; account temporarily locked",
            -3 => "Account already logged in or locked",
            -4 => "Account access denied",
            -5 => "Account locked",
            -6 => "Dedicated launcher required",
            _ => "Account not found or unknown error"
        };

    private static string DescribeUpdateIdFailCode(int code) =>
        code switch
        {
            0 => "Account not found",
            -1 => "Invalid request",
            _ => "Unknown"
        };

    private static byte[] BuildUserFullEntryBuffer(MirAccountFullEntry entry)
    {
        using var ms = new MemoryStream(256);
        using var writer = new BinaryWriter(ms);

        WritePascalString(writer, TrimToMaxGbkBytes(entry.Account, 10), 10);
        WritePascalString(writer, TrimToMaxGbkBytes(entry.Password, 10), 10);
        WritePascalString(writer, TrimToMaxGbkBytes(entry.UserName, 20), 20);
        WritePascalString(writer, TrimToMaxGbkBytes(entry.SSNo, 14), 14);
        WritePascalString(writer, TrimToMaxGbkBytes(entry.Phone, 14), 14);
        WritePascalString(writer, TrimToMaxGbkBytes(entry.Quiz1, 20), 20);
        WritePascalString(writer, TrimToMaxGbkBytes(entry.Answer1, 12), 12);
        WritePascalString(writer, TrimToMaxGbkBytes(entry.EMail, 40), 40);
        WritePascalString(writer, TrimToMaxGbkBytes(entry.Quiz2, 20), 20);
        WritePascalString(writer, TrimToMaxGbkBytes(entry.Answer2, 12), 12);
        WritePascalString(writer, TrimToMaxGbkBytes(entry.BirthDay, 10), 10);
        WritePascalString(writer, TrimToMaxGbkBytes(entry.MobilePhone, 13), 13);
        WritePascalString(writer, string.Empty, 20); 
        WritePascalString(writer, string.Empty, 20); 

        return ms.ToArray();
    }

    private static void WritePascalString(BinaryWriter writer, string value, int size)
    {
        byte[] bytes = string.IsNullOrEmpty(value) ? Array.Empty<byte>() : GbkEncoding.Instance.GetBytes(value);
        int len = Math.Min(bytes.Length, size);

        writer.Write(unchecked((byte)len));
        if (len > 0)
            writer.Write(bytes, 0, len);

        int pad = size - len;
        if (pad > 0)
            writer.Write(new byte[pad]);
    }

    private static string TrimToMaxGbkBytes(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        string trimmed = value.Trim();
        while (trimmed.Length > 0 && GbkEncoding.Instance.GetByteCount(trimmed) > maxBytes)
            trimmed = trimmed[..^1];

        return trimmed;
    }

    private static string DescribeChangePasswordFailCode(int code) =>
        code switch
        {
            0 => "Password incorrect",
            -1 => "Account invalid",
            -2 => "Server busy",
            _ => "Unknown"
        };

    private static string DescribeGetBackDeletedCharacterCode(int code) =>
        code switch
        {
            2 => "Character is not deleted",
            3 => "Account character limit reached",
            4 => "Deleted character not found",
            5 => "Character already deleted",
            _ => "Unknown"
        };
}
