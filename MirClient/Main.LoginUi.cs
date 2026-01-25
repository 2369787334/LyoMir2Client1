using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using System.Numerics;
using System.Text;
using System.Windows.Forms;
using MirClient.Assets.PackData;
using MirClient.Assets.Wil;
using MirClient.Core;
using MirClient.Protocol;
using MirClient.Protocol.Startup;
using MirClient.Protocol.Text;
using MirClient.Rendering.D3D11;
using Vortice.Mathematics;

namespace MirClient;

internal partial class Main
{
    private enum LoginUiScreen
    {
        None = 0,
        Login = 1,
        ChangePassword = 2,
        Register = 3,
        UpdateAccount = 4,
        SelectServer = 5,
        SelectCharacter = 6,
        CreateCharacter = 7,
        RestoreDeletedCharacter = 8,
        OpeningDoor = 9
    }

    private enum UiModalButtons
    {
        Ok = 0,
        OkCancel = 1,
        YesNo = 2
    }

    private enum UiModalLayout
    {
        Default = 0,
        Classic0 = 1,
        Classic1 = 2,
        Classic2 = 3
    }

    private enum UiModalResult
    {
        Ok = 0,
        Cancel = 1,
        Yes = 2,
        No = 3
    }

    private enum UiTextPromptButtons
    {
        OkCancel = 0,
        OkCancelAbort = 1
    }

    private enum UiTextPromptResult
    {
        Ok = 0,
        Cancel = 1,
        Abort = 2
    }

    private sealed class UiModal
    {
        public required string Title { get; init; }
        public required string Message { get; init; }
        public required UiModalButtons Buttons { get; init; }
        public UiModalLayout Layout { get; init; } = UiModalLayout.Default;
        public required Action<UiModalResult> OnResult { get; init; }
    }

    private sealed class UiTextPrompt
    {
        public required string Title { get; init; }
        public required string Prompt { get; init; }
        public required UiTextPromptButtons Buttons { get; init; }
        public required UiTextInput Input { get; init; }
        public required Action<UiTextPromptResult, string> OnResult { get; init; }
    }

    private sealed class UiTextInput
    {
        public UiTextInput(string label, bool password = false, bool multiline = false, int maxGbkBytes = 0, bool trimWhitespace = true)
        {
            Label = label;
            IsPassword = password;
            IsMultiline = multiline;
            MaxGbkBytes = maxGbkBytes;
            TrimWhitespace = trimWhitespace;
        }

        public string Label { get; }
        public bool IsPassword { get; set; }
        public bool IsMultiline { get; set; }
        public int MaxGbkBytes { get; set; }
        public bool TrimWhitespace { get; set; }

        public string Text { get; private set; } = string.Empty;

        public void Set(string? value)
        {
            Text = value ?? string.Empty;
            TrimToLimit();
        }

        public void Clear() => Text = string.Empty;

        public void Backspace()
        {
            if (Text.Length == 0)
                return;

            Text = Text[..^1];
        }

        public void Append(string value)
        {
            if (string.IsNullOrEmpty(value))
                return;

            Text += value;
            TrimToLimit();
        }

        public string GetDisplayText()
        {
            if (!IsPassword)
                return Text;

            return Text.Length == 0 ? string.Empty : new string('*', Text.Length);
        }

        public string GetTrimmed() => string.IsNullOrEmpty(Text) ? string.Empty : (TrimWhitespace ? Text.Trim() : Text);

        private void TrimToLimit()
        {
            if (MaxGbkBytes <= 0)
                return;

            string value = GetTrimmed();
            while (value.Length > 0 && GbkEncoding.Instance.GetByteCount(value) > MaxGbkBytes)
                value = value[..^1];
            Text = value;
        }
    }

    private LoginUiScreen _loginUiScreen = LoginUiScreen.None;
    private UiModal? _loginUiModal;
    private UiTextPrompt? _uiTextPrompt;
    private DrawingRectangle? _loginUiModalRect;

    private const string LoginUiBgmIntro = "Log-in-long2.wav"; 
    private const string LoginUiBgmSelect = "main_theme.wav";  

    private string _loginUiResourceRoot = string.Empty;
    private string? _loginUiChrSelPath;
    private string? _loginUiPrgusePath;
    private string? _loginUiPrguse3Path;
    private string? _loginUiOpUiPath;

    private long _loginUiDoorStartMs;
    private bool _loginUiDoorCharactersReady;
    private bool _loginUiDoorPlayedSfx;

    private struct LoginUiCharSlot
    {
        public MirCharacterInfo? Info;
        public bool Selected;
        public bool FreezeState;
        public bool Unfreezing;
        public bool Freezing;
        public int AniIndex;
        public int DarkLevel;
        public int EffIndex;
        public long StartMs;
        public long MoreMs;
        public long StartEffMs;

        public readonly bool Valid => Info != null;
        public readonly byte Job => Info?.Job ?? 0;
        public readonly byte Sex => Info?.Sex ?? 0;
        public readonly int Level => Info?.Level ?? 0;
        public readonly string Name => Info?.Name ?? string.Empty;
    }

    private readonly LoginUiCharSlot[] _loginUiCharSlots = new LoginUiCharSlot[2];
    private int _loginUiCreateSlotIndex;

    private readonly UiTextInput _uiLoginAccount = new("Account", maxGbkBytes: 10);
    private readonly UiTextInput _uiLoginPassword = new("Password", password: true, maxGbkBytes: 10, trimWhitespace: false);

    private readonly UiTextInput _uiChangePwdAccount = new("Account", maxGbkBytes: 10);
    private readonly UiTextInput _uiChangePwdOld = new("Old Password", password: true, maxGbkBytes: 10, trimWhitespace: false);
    private readonly UiTextInput _uiChangePwdNew = new("New Password", password: true, maxGbkBytes: 10, trimWhitespace: false);
    private readonly UiTextInput _uiChangePwdConfirm = new("Confirm New Password", password: true, maxGbkBytes: 10, trimWhitespace: false);

    private readonly UiTextInput _uiAccAccount = new("Account", maxGbkBytes: 10);
    private readonly UiTextInput _uiAccPassword = new("Password", password: true, maxGbkBytes: 10, trimWhitespace: false);
    private readonly UiTextInput _uiAccConfirm = new("Confirm", password: true, maxGbkBytes: 10, trimWhitespace: false);
    private readonly UiTextInput _uiAccUserName = new("UserName", maxGbkBytes: 20);
    private readonly UiTextInput _uiAccQuiz1 = new("Quiz1", maxGbkBytes: 20);
    private readonly UiTextInput _uiAccAnswer1 = new("Answer1", maxGbkBytes: 12);
    private readonly UiTextInput _uiAccQuiz2 = new("Quiz2", maxGbkBytes: 20);
    private readonly UiTextInput _uiAccAnswer2 = new("Answer2", maxGbkBytes: 12);
    private readonly UiTextInput _uiAccBirthDay = new("BirthDay", maxGbkBytes: 10);
    private readonly UiTextInput _uiAccSsNo = new("SSNo", maxGbkBytes: 14);
    private readonly UiTextInput _uiAccPhone = new("Phone", maxGbkBytes: 14);
    private readonly UiTextInput _uiAccEmail = new("EMail", maxGbkBytes: 40);
    private readonly UiTextInput _uiAccMobilePhone = new("MobilePhone", maxGbkBytes: 13);

    private readonly UiTextInput _uiNewCharName = new("Name", maxGbkBytes: Grobal2.ActorNameLen);
    private byte _uiNewCharJob;
    private byte _uiNewCharSex;
    private byte _uiNewCharHair;

    private UiTextInput? _uiFocusedInput;
    private long _uiFocusStartMs;

    private IReadOnlyList<ServerListItem> _uiServers = Array.Empty<ServerListItem>();
    private int _uiServerSelectedIndex;

    private IReadOnlyList<MirCharacterInfo> _uiCharacters = Array.Empty<MirCharacterInfo>();
    private int _uiCharacterSelectedIndex;

    private IReadOnlyList<MirDeletedCharacterInfo> _uiDeletedCharacters = Array.Empty<MirDeletedCharacterInfo>();
    private int _uiDeletedSelectedIndex;

    private bool LoginUiVisible => _loginUiScreen != LoginUiScreen.None || _loginUiModal != null || _uiTextPrompt != null;

    private void ShowLoginUi(LoginUiScreen screen)
    {
        _loginUiScreen = screen;
        _loginUiModal = null;
        _uiTextPrompt = null;
        SyncLoginUiPrefill();

        if (screen == LoginUiScreen.SelectCharacter)
            InitializeLoginUiCharacterSlots();

        _uiFocusStartMs = Environment.TickCount64;
        _uiFocusedInput = GetDefaultFocusForScreen(screen);

        UpdateLoginUiBgm();

        try { _renderControl.Focus(); } catch {  }
    }

    private void HideLoginUi()
    {
        _loginUiModal = null;
        _uiTextPrompt = null;
        _loginUiScreen = LoginUiScreen.None;
        _uiFocusedInput = null;
        _soundManager.StopBgm();
    }

    private void SyncLoginUiPrefill()
    {
        _uiLoginAccount.Set(_txtAccount.Text);
        _uiLoginPassword.Set(_txtPassword.Text);
    }

    private void UpdateLoginUiBgm()
    {
        if (_session.Stage is MirSessionStage.RunGate or MirSessionStage.InGame)
            return;

        string file = _loginUiScreen switch
        {
            LoginUiScreen.SelectCharacter or LoginUiScreen.CreateCharacter or LoginUiScreen.RestoreDeletedCharacter => LoginUiBgmSelect,
            _ => LoginUiBgmIntro
        };

        _soundManager.PlaySoundFile(file, loop: true);
    }

    private void InitializeLoginUiCharacterSlots()
    {
        _loginUiCharSlots[0] = default;
        _loginUiCharSlots[1] = default;

        int selectedIndex = Math.Clamp(_uiCharacterSelectedIndex, 0, 1);

        for (int i = 0; i < _uiCharacters.Count && i < 2; i++)
        {
            _loginUiCharSlots[i].Info = _uiCharacters[i];
            _loginUiCharSlots[i].FreezeState = i != selectedIndex;
            _loginUiCharSlots[i].Selected = i == selectedIndex && _uiCharacters[i] != null;
            _loginUiCharSlots[i].DarkLevel = 0;
            _loginUiCharSlots[i].StartMs = Environment.TickCount64;
            _loginUiCharSlots[i].MoreMs = _loginUiCharSlots[i].StartMs;
            _loginUiCharSlots[i].StartEffMs = _loginUiCharSlots[i].StartMs;
        }

        if (_loginUiCharSlots[selectedIndex].Valid)
        {
            string name = _loginUiCharSlots[selectedIndex].Name.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                _txtCharacter.Text = name;
                _selectCharacterScene.SelectCharacter(name);
            }
        }
    }

    private void BeginLoginUiDoorOpening()
    {
        _loginUiDoorStartMs = Environment.TickCount64;
        _loginUiDoorCharactersReady = true;
        _loginUiDoorPlayedSfx = false;
        ShowLoginUi(LoginUiScreen.OpeningDoor);
    }

    private void MarkLoginUiDoorCharactersReady()
    {
        _loginUiDoorCharactersReady = true;
    }

    private static bool IsDoorAnimationComplete(long nowMs, long startMs)
    {
        const int frameMs = 28;
        const int frames = 10;
        long elapsed = nowMs - startMs;
        if (elapsed < 0)
            elapsed = 0;

        return elapsed >= (frames - 1) * frameMs;
    }

    private bool TryEnsureLoginUiArchives()
    {
        string resourceRoot = GetResourceRootDir();
        if (string.Equals(resourceRoot, _loginUiResourceRoot, StringComparison.OrdinalIgnoreCase) &&
            _loginUiChrSelPath != null &&
            _loginUiPrgusePath != null &&
            _loginUiPrguse3Path != null)
        {
            return true;
        }

        _loginUiResourceRoot = resourceRoot;
        _loginUiChrSelPath = null;
        _loginUiPrgusePath = null;
        _loginUiPrguse3Path = null;
        _loginUiOpUiPath = null;

        string dataDir = Path.Combine(resourceRoot, "Data");
        if (!Directory.Exists(dataDir))
            return false;

        _loginUiChrSelPath = TryResolveArchiveFilePath(dataDir, "ChrSel");
        _loginUiPrgusePath = TryResolveArchiveFilePath(dataDir, "Prguse");
        _loginUiPrguse3Path = TryResolveArchiveFilePath(dataDir, "Prguse3");
        _loginUiOpUiPath = TryResolveArchiveFilePath(dataDir, "NewopUI");

        return _loginUiChrSelPath != null && _loginUiPrgusePath != null && _loginUiPrguse3Path != null;
    }

    private UiTextInput? GetDefaultFocusForScreen(LoginUiScreen screen) =>
        screen switch
        {
            LoginUiScreen.Login => string.IsNullOrWhiteSpace(_uiLoginAccount.Text) ? _uiLoginAccount : _uiLoginPassword,
            LoginUiScreen.ChangePassword => _uiChangePwdAccount,
            LoginUiScreen.Register => _uiAccAccount,
            LoginUiScreen.UpdateAccount => _uiAccAccount,
            LoginUiScreen.CreateCharacter => _uiNewCharName,
            _ => null
        };

    private UiTextInput[] GetFocusableFieldsForScreen(LoginUiScreen screen) =>
        screen switch
        {
            LoginUiScreen.Login => new[] { _uiLoginAccount, _uiLoginPassword },
            LoginUiScreen.ChangePassword => new[] { _uiChangePwdAccount, _uiChangePwdOld, _uiChangePwdNew, _uiChangePwdConfirm },
            LoginUiScreen.Register => new[]
            {
                _uiAccAccount,
                _uiAccPassword,
                _uiAccConfirm,
                _uiAccUserName,
                _uiAccSsNo,
                _uiAccBirthDay,
                _uiAccQuiz1,
                _uiAccAnswer1,
                _uiAccQuiz2,
                _uiAccAnswer2,
                _uiAccPhone,
                _uiAccMobilePhone,
                _uiAccEmail
            },
            LoginUiScreen.UpdateAccount => new[]
            {
                _uiAccAccount,
                _uiAccPassword,
                _uiAccConfirm,
                _uiAccUserName,
                _uiAccSsNo,
                _uiAccBirthDay,
                _uiAccQuiz1,
                _uiAccAnswer1,
                _uiAccQuiz2,
                _uiAccAnswer2,
                _uiAccPhone,
                _uiAccMobilePhone,
                _uiAccEmail
            },
            LoginUiScreen.CreateCharacter => new[] { _uiNewCharName },
            _ => Array.Empty<UiTextInput>()
        };

    private void FocusNextField(bool backward)
    {
        UiTextInput[] fields = GetFocusableFieldsForScreen(_loginUiScreen);
        if (fields.Length == 0)
            return;

        int idx = Array.IndexOf(fields, _uiFocusedInput);
        if (idx < 0)
            idx = 0;

        idx = backward ? (idx - 1 + fields.Length) % fields.Length : (idx + 1) % fields.Length;
        _uiFocusedInput = fields[idx];
        _uiFocusStartMs = Environment.TickCount64;
    }

    private void ShowModal(string title, string message, UiModalButtons buttons, Action<UiModalResult> onResult, UiModalLayout layout = UiModalLayout.Default)
    {
        _loginUiModal = new UiModal
        {
            Title = title,
            Message = message,
            Buttons = buttons,
            Layout = layout,
            OnResult = onResult
        };
        _uiTextPrompt = null;
        _uiFocusedInput = null;
    }

    private void ResolveModalDefault(bool cancel)
    {
        if (_loginUiModal == null)
            return;

        UiModal modal = _loginUiModal;
        _loginUiModal = null;

        UiModalResult result = modal.Buttons switch
        {
            UiModalButtons.Ok => UiModalResult.Ok,
            UiModalButtons.OkCancel => cancel ? UiModalResult.Cancel : UiModalResult.Ok,
            UiModalButtons.YesNo => cancel ? UiModalResult.No : UiModalResult.Yes,
            _ => UiModalResult.Ok
        };

        try { modal.OnResult(result); } catch {  }

        _uiFocusedInput = GetDefaultFocusForScreen(_loginUiScreen);
        _uiFocusStartMs = Environment.TickCount64;
    }

    private Task<(UiTextPromptResult Result, string Value)> PromptTextAsync(
        string title,
        string prompt,
        UiTextPromptButtons buttons = UiTextPromptButtons.OkCancel,
        string? initialValue = null,
        bool password = false,
        bool multiline = false,
        int maxGbkBytes = 0,
        bool trimWhitespace = true)
    {
        var tcs = new TaskCompletionSource<(UiTextPromptResult, string)>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Show()
        {
            if (IsDisposed || Disposing)
            {
                tcs.TrySetResult((UiTextPromptResult.Cancel, string.Empty));
                return;
            }

            if (_loginUiModal != null)
            {
                tcs.TrySetResult((UiTextPromptResult.Cancel, string.Empty));
                return;
            }

            if (_uiTextPrompt != null)
                ResolveTextPrompt(UiTextPromptResult.Cancel);

            var input = new UiTextInput(label: string.Empty, password: password, multiline: multiline, maxGbkBytes: maxGbkBytes, trimWhitespace: trimWhitespace);
            input.Set(initialValue);

            _uiTextPrompt = new UiTextPrompt
            {
                Title = title,
                Prompt = prompt,
                Buttons = buttons,
                Input = input,
                OnResult = (result, value) => tcs.TrySetResult((result, value))
            };

            _uiFocusedInput = input;
            _uiFocusStartMs = Environment.TickCount64;

            try { _renderControl.Focus(); } catch {  }
        }

        if (InvokeRequired)
            BeginInvoke((Action)Show);
        else
            Show();

        return tcs.Task;
    }

    private void ResolveTextPrompt(UiTextPromptResult result)
    {
        if (_uiTextPrompt == null)
            return;

        UiTextPrompt prompt = _uiTextPrompt;
        _uiTextPrompt = null;

        string value = prompt.Input.GetTrimmed();
        try { prompt.OnResult(result, value); } catch {  }

        _uiFocusedInput = GetDefaultFocusForScreen(_loginUiScreen);
        _uiFocusStartMs = Environment.TickCount64;
    }

    private bool TryHandleLoginUiCmdKey(Keys keyData)
    {
        if (!LoginUiVisible)
            return false;

        Keys keyCode = keyData & Keys.KeyCode;
        bool ctrl = (keyData & Keys.Control) != 0;
        bool alt = (keyData & Keys.Alt) != 0;

        if (_loginUiModal != null)
        {
            if (!ctrl && !alt && keyCode == Keys.Escape)
            {
                ResolveModalDefault(cancel: true);
                return true;
            }

            if (!ctrl && !alt && keyCode == Keys.Enter)
            {
                ResolveModalDefault(cancel: false);
                return true;
            }

            return true;
        }

        if (_uiTextPrompt != null)
        {
            UiTextInput input = _uiTextPrompt.Input;

            if (!ctrl && !alt && keyCode == Keys.Escape)
            {
                ResolveTextPrompt(UiTextPromptResult.Cancel);
                return true;
            }

            if (!ctrl && !alt && keyCode == Keys.Enter)
            {
                ResolveTextPrompt(UiTextPromptResult.Ok);
                return true;
            }

            if (!ctrl && !alt && keyCode == Keys.Back)
            {
                input.Backspace();
                return true;
            }

            if (ctrl && !alt && keyCode == Keys.V)
            {
                try
                {
                    string clip = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(clip))
                        input.Append(clip);
                }
                catch
                {
                    
                }

                return true;
            }

            return true;
        }

        if (!ctrl && !alt && keyCode == Keys.Tab)
        {
            FocusNextField(backward: (keyData & Keys.Shift) != 0);
            return true;
        }

        if (!ctrl && !alt && keyCode == Keys.Back && _uiFocusedInput != null)
        {
            _uiFocusedInput.Backspace();
            return true;
        }

        if (!ctrl && !alt && keyCode == Keys.Enter)
        {
            BeginLoginUiPrimaryAction();
            return true;
        }

        if (!ctrl && !alt && keyCode == Keys.Up)
        {
            if (_loginUiScreen == LoginUiScreen.SelectServer)
                MoveServerSelection(-1);
            else if (_loginUiScreen == LoginUiScreen.SelectCharacter)
                MoveCharacterSelection(-1);
            else if (_loginUiScreen == LoginUiScreen.RestoreDeletedCharacter)
                MoveDeletedSelection(-1);
            else
                return false;

            return true;
        }

        if (!ctrl && !alt && keyCode == Keys.Down)
        {
            if (_loginUiScreen == LoginUiScreen.SelectServer)
                MoveServerSelection(1);
            else if (_loginUiScreen == LoginUiScreen.SelectCharacter)
                MoveCharacterSelection(1);
            else if (_loginUiScreen == LoginUiScreen.RestoreDeletedCharacter)
                MoveDeletedSelection(1);
            else
                return false;

            return true;
        }

        if (ctrl && !alt && keyCode == Keys.V && _uiFocusedInput != null)
        {
            try
            {
                string clip = Clipboard.GetText();
                if (!string.IsNullOrEmpty(clip))
                    _uiFocusedInput.Append(clip);
            }
            catch
            {
                
            }
            return true;
        }

        if (!ctrl && !alt && keyCode == Keys.Escape)
        {
            switch (_loginUiScreen)
            {
                case LoginUiScreen.Login:
                    HideLoginUi();
                    break;
                default:
                    ShowLoginUi(LoginUiScreen.Login);
                    break;
            }
            return true;
        }

        return false;
    }

    private void HandleLoginUiKeyPress(char keyChar)
    {
        if (!LoginUiVisible || _loginUiModal != null)
            return;

        UiTextInput? focus = _uiFocusedInput;
        if (focus == null)
            return;

        if ((keyChar == '\r' || keyChar == '\n') && focus.IsMultiline)
        {
            focus.Append(Environment.NewLine);
            return;
        }

        if (char.IsControl(keyChar))
            return;

        focus.Append(keyChar.ToString());
    }

    private void BeginLoginUiPrimaryAction()
    {
        if (IsDisposed || Disposing)
            return;

        BeginInvoke(async () =>
        {
            try { await HandleLoginUiPrimaryActionAsync().ConfigureAwait(true); } catch {  }
        });
    }

    private void MoveServerSelection(int delta)
    {
        if (_uiServers.Count == 0)
            return;

        _uiServerSelectedIndex = Math.Clamp(_uiServerSelectedIndex + delta, 0, _uiServers.Count - 1);
    }

    private void MoveCharacterSelection(int delta)
    {
        int count = Math.Min(2, _uiCharacters.Count);
        if (count <= 0)
            return;

        _uiCharacterSelectedIndex = Math.Clamp(_uiCharacterSelectedIndex + delta, 0, count - 1);
    }

    private void MoveDeletedSelection(int delta)
    {
        if (_uiDeletedCharacters.Count == 0)
            return;

        _uiDeletedSelectedIndex = Math.Clamp(_uiDeletedSelectedIndex + delta, 0, _uiDeletedCharacters.Count - 1);
    }

    private async Task HandleLoginUiPrimaryActionAsync()
    {
        if (!LoginUiVisible || _loginUiModal != null)
            return;

        switch (_loginUiScreen)
        {
            case LoginUiScreen.Login:
                await LoginUiTryLoginAsync().ConfigureAwait(true);
                break;
            case LoginUiScreen.SelectServer:
                await LoginUiTrySelectServerAsync().ConfigureAwait(true);
                break;
            case LoginUiScreen.SelectCharacter:
                await LoginUiTryEnterGameAsync().ConfigureAwait(true);
                break;
            case LoginUiScreen.CreateCharacter:
                await LoginUiTryCreateCharacterAsync().ConfigureAwait(true);
                break;
            case LoginUiScreen.RestoreDeletedCharacter:
                await LoginUiTryRestoreDeletedCharacterAsync().ConfigureAwait(true);
                break;
            case LoginUiScreen.ChangePassword:
                await LoginUiTryChangePasswordAsync().ConfigureAwait(true);
                break;
            case LoginUiScreen.Register:
                await LoginUiTryRegisterAsync().ConfigureAwait(true);
                break;
            case LoginUiScreen.UpdateAccount:
                await LoginUiTryUpdateAccountAsync().ConfigureAwait(true);
                break;
        }
    }

    private void LoginUiSetServerList(string raw, string? preferredServerName)
    {
        _uiServers = ParseServerListRaw(raw);
        _uiServerSelectedIndex = 0;

        if (!string.IsNullOrWhiteSpace(preferredServerName))
        {
            string preferred = preferredServerName.Trim();
            for (int i = 0; i < _uiServers.Count; i++)
            {
                if (string.Equals(_uiServers[i].Name, preferred, StringComparison.OrdinalIgnoreCase))
                {
                    _uiServerSelectedIndex = i;
                    break;
                }
            }
        }
    }

    private void LoginUiSetCharacterList(IReadOnlyList<MirCharacterInfo> characters)
    {
        _uiCharacters = characters;
        _uiCharacterSelectedIndex = 0;

        for (int i = 0; i < Math.Min(2, _uiCharacters.Count); i++)
        {
            if (_uiCharacters[i].Selected)
            {
                _uiCharacterSelectedIndex = i;
                break;
            }
        }

        if (_uiCharacters.Count > 0)
        {
            string name = _uiCharacters[Math.Clamp(_uiCharacterSelectedIndex, 0, _uiCharacters.Count - 1)].Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                _txtCharacter.Text = name.Trim();
                _selectCharacterScene.SelectCharacter(name);
            }
        }

        if (_loginUiScreen is LoginUiScreen.SelectCharacter or LoginUiScreen.CreateCharacter or LoginUiScreen.OpeningDoor)
            InitializeLoginUiCharacterSlots();

        if (_loginUiScreen == LoginUiScreen.OpeningDoor)
            MarkLoginUiDoorCharactersReady();
    }

    private async Task LoginUiTryLoginAsync()
    {
        if (_loginFlowInProgress)
            return;

        _ = EnsureStartupInfo("login-ui");

        string account = _uiLoginAccount.GetTrimmed();
        string password = _uiLoginPassword.GetTrimmed();
        if (string.IsNullOrWhiteSpace(account))
        {
            ShowModal("Login", "Account is required.", UiModalButtons.Ok, _ => { });
            _uiFocusedInput = _uiLoginAccount;
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            ShowModal("Login", "Password is required.", UiModalButtons.Ok, _ => { });
            _uiFocusedInput = _uiLoginPassword;
            return;
        }

        _txtAccount.Text = account;
        _txtPassword.Text = password;

        await AdvanceLoginFlowAsync().ConfigureAwait(true);
    }

    private async Task LoginUiTrySelectServerAsync()
    {
        if (_loginFlowInProgress)
            return;

        if (_uiServers.Count == 0)
        {
            ShowModal("Select Server", "No servers available.", UiModalButtons.Ok, _ => { });
            return;
        }

        int idx = Math.Clamp(_uiServerSelectedIndex, 0, _uiServers.Count - 1);
        string serverName = _uiServers[idx].Name;
        if (string.IsNullOrWhiteSpace(serverName))
            return;

        MirStartupInfo current = EnsureStartupInfo("select-server");
        _startup = current with { ServerName = serverName };
        _lblStartup.Text = $"LoginGate: {_startup.ServerAddress}:{_startup.ServerPort}  ServerName: {_startup.ServerName}  ResDir: {_startup.ResourceDir}";
        SelectServerInUi(serverName);
        _selectServerScene.SelectServer(serverName);

        await AdvanceLoginFlowAsync().ConfigureAwait(true);
    }

    private async Task LoginUiTryEnterGameAsync()
    {
        if (_loginFlowInProgress)
            return;

        if (_uiCharacters.Count == 0)
        {
            ShowModal("Select Character", "No characters available.", UiModalButtons.Ok, _ => { });
            return;
        }

        string name = _txtCharacter.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            int idx = Math.Clamp(_uiCharacterSelectedIndex, 0, _uiCharacters.Count - 1);
            name = _uiCharacters[idx].Name.Trim();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowModal("Select Character", "Select a character first.", UiModalButtons.Ok, _ => { });
            return;
        }

        _txtCharacter.Text = name;
        _selectCharacterScene.SelectCharacter(name);

        HideLoginUi();

        bool entered = await StartPlayStepAsync().ConfigureAwait(true);
        if (!entered && _session.Stage == MirSessionStage.SelectCharacter)
            ShowLoginUi(LoginUiScreen.SelectCharacter);
    }

    private async Task LoginUiTryCreateCharacterAsync()
    {
        if (_loginFlowInProgress)
            return;

        string name = _uiNewCharName.GetTrimmed();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowModal("Create Character", "Name is required.", UiModalButtons.Ok, _ => { });
            _uiFocusedInput = _uiNewCharName;
            return;
        }

        if (string.IsNullOrWhiteSpace(_loginAccount) || _loginCertification == 0)
        {
            ShowModal("Create Character", "Login context missing. Return to login.", UiModalButtons.Ok, __ => { _ = DisconnectAsync(DisconnectBehavior.PromptLogin); });
            return;
        }

        CancellationToken token = _loginCts?.Token ?? CancellationToken.None;
        try
        {
            
            byte hair = (byte)(1 + Random.Shared.Next(5));
            MirCharacterListResult created = await _session.CreateCharacterAsync(
                _loginAccount,
                _loginCertification,
                name,
                hair: hair,
                job: _uiNewCharJob,
                sex: _uiNewCharSex,
                token).ConfigureAwait(true);

            ApplyCharacterListToUi(created.Characters);
            LoginUiSetCharacterList(created.Characters);
            _txtCharacter.Text = name;
            _selectCharacterScene.SelectCharacter(name);
            ShowLoginUi(LoginUiScreen.SelectCharacter);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            
        }
        catch (Exception ex)
        {
            ShowModal("Create Character Failed", ex.Message, UiModalButtons.Ok, _ => { });
        }
    }

    private async Task LoginUiTryRestoreDeletedCharacterAsync()
    {
        if (_loginFlowInProgress)
            return;

        if (string.IsNullOrWhiteSpace(_loginAccount) || _loginCertification == 0)
        {
            ShowModal("Restore", "Login context missing. Return to login.", UiModalButtons.Ok, __ => { _ = DisconnectAsync(DisconnectBehavior.PromptLogin); });
            return;
        }

        if (_uiDeletedCharacters.Count == 0 || _uiDeletedSelectedIndex < 0 || _uiDeletedSelectedIndex >= _uiDeletedCharacters.Count)
            return;

        string deletedName = _uiDeletedCharacters[_uiDeletedSelectedIndex].Name;
        if (string.IsNullOrWhiteSpace(deletedName))
            return;

        CancellationToken token = _loginCts?.Token ?? CancellationToken.None;
        try
        {
            MirCharacterListResult restored = await _session.RestoreDeletedCharacterAsync(_loginAccount, _loginCertification, deletedName, token).ConfigureAwait(true);
            ApplyCharacterListToUi(restored.Characters);
            LoginUiSetCharacterList(restored.Characters);
            ShowLoginUi(LoginUiScreen.SelectCharacter);
        }
        catch (Exception ex)
        {
            ShowModal("Restore Failed", ex.Message, UiModalButtons.Ok, _ => { });
        }
    }

    private async Task LoginUiTryChangePasswordAsync()
    {
        if (_loginFlowInProgress)
            return;

        MirStartupInfo startup = EnsureStartupInfo("change-password");

        string account = _uiChangePwdAccount.GetTrimmed();
        if (string.IsNullOrWhiteSpace(account))
        {
            ShowModal("Change Password", "Account is required.", UiModalButtons.Ok, _ => { });
            _uiFocusedInput = _uiChangePwdAccount;
            return;
        }

        string oldPwd = _uiChangePwdOld.GetTrimmed();
        if (string.IsNullOrEmpty(oldPwd))
        {
            ShowModal("Change Password", "Old password is required.", UiModalButtons.Ok, _ => { });
            _uiFocusedInput = _uiChangePwdOld;
            return;
        }

        string newPwd = _uiChangePwdNew.GetTrimmed();
        if (string.IsNullOrEmpty(newPwd))
        {
            ShowModal("Change Password", "New password is required.", UiModalButtons.Ok, _ => { });
            _uiFocusedInput = _uiChangePwdNew;
            return;
        }

        if (newPwd != _uiChangePwdConfirm.GetTrimmed())
        {
            ShowModal("Change Password", "New password does not match.", UiModalButtons.Ok, _ => { });
            _uiFocusedInput = _uiChangePwdConfirm;
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _session.ChangePasswordAsync(startup, account, oldPwd, newPwd, cts.Token).ConfigureAwait(true);
            _uiLoginPassword.Set(newPwd);
            _txtPassword.Text = newPwd;
            ShowModal("Change Password", "Password changed successfully.", UiModalButtons.Ok, _ => ShowLoginUi(LoginUiScreen.Login));
        }
        catch (Exception ex)
        {
            ShowModal("Change Password Failed", ex.Message, UiModalButtons.Ok, _ => { });
        }
    }

    private bool TryBuildAccountEntry(bool isRegister, out MirAccountFullEntry entry, out string error)
    {
        entry = default!;
        error = string.Empty;

        string account = _uiAccAccount.GetTrimmed();
        string password = _uiAccPassword.GetTrimmed();
        string confirm = _uiAccConfirm.GetTrimmed();
        string userName = _uiAccUserName.GetTrimmed();
        string ssno = _uiAccSsNo.GetTrimmed();
        string phone = _uiAccPhone.GetTrimmed();
        string email = _uiAccEmail.GetTrimmed();
        string mobilePhone = _uiAccMobilePhone.GetTrimmed();
        string quiz1 = _uiAccQuiz1.GetTrimmed();
        string answer1 = _uiAccAnswer1.GetTrimmed();
        string quiz2 = _uiAccQuiz2.GetTrimmed();
        string answer2 = _uiAccAnswer2.GetTrimmed();
        string birthDay = _uiAccBirthDay.GetTrimmed();

        if (string.IsNullOrWhiteSpace(account))
        {
            error = "Account is required.";
            return false;
        }

        if (account.Trim().Length < 3)
        {
            error = "登录帐号的长度必须大于3位.";
            return false;
        }

        if (string.IsNullOrEmpty(password))
        {
            error = "Password is required.";
            return false;
        }

        if (password.Length < 4)
        {
            error = "密码长度必须大于 4位.";
            return false;
        }

        if (isRegister && password != confirm)
        {
            error = "二次输入的密码不一至！！！";
            return false;
        }

        if (string.IsNullOrWhiteSpace(userName))
        {
            error = "UserName is required.";
            return false;
        }

        if (!TryValidateSsno(ssno, out string ssnoError))
        {
            error = ssnoError;
            return false;
        }

        if (string.IsNullOrWhiteSpace(quiz2) || string.IsNullOrWhiteSpace(answer2))
        {
            error = "Quiz2/Answer2 is required (avoid server asking to update).";
            return false;
        }

        if (string.IsNullOrWhiteSpace(quiz1) || string.IsNullOrWhiteSpace(answer1))
        {
            error = "Quiz1/Answer1 is required.";
            return false;
        }

        if (!TryValidateBirthDay(birthDay, out string birthError))
        {
            error = birthError;
            return false;
        }

        entry = new MirAccountFullEntry(
            Account: account,
            Password: password,
            UserName: userName,
            Quiz1: quiz1,
            Answer1: answer1,
            Quiz2: quiz2,
            Answer2: answer2,
            BirthDay: birthDay,
            SSNo: ssno,
            Phone: phone,
            EMail: email,
            MobilePhone: mobilePhone);
        return true;
    }

    private static bool TryValidateSsno(string value, out string error)
    {
        error = string.Empty;

        
        string trimmed = value.Trim();
        int dash = trimmed.IndexOf('-');
        if (dash <= 0 || dash >= trimmed.Length - 1)
        {
            error = "请输入你的身份证号 (例如：720101-146720)";
            return false;
        }

        string t1 = trimmed[..dash];
        string t2 = trimmed[(dash + 1)..];
        if (t1.Length != 6 || t2.Length != 7)
        {
            error = "请输入你的身份证号 (例如：720101-146720)";
            return false;
        }

        if (!int.TryParse(t1.Substring(2, 2), out int month) || month is <= 0 or > 12)
        {
            error = "请输入你的身份证号 (例如：720101-146720)";
            return false;
        }

        if (!int.TryParse(t1.Substring(4, 2), out int day) || day is <= 0 or > 31)
        {
            error = "请输入你的身份证号 (例如：720101-146720)";
            return false;
        }

        if (!int.TryParse(t2.Substring(0, 1), out int sex) || sex is <= 0 or > 2)
        {
            error = "请输入你的身份证号 (例如：720101-146720)";
            return false;
        }

        return true;
    }

    private static bool TryValidateBirthDay(string value, out string error)
    {
        error = string.Empty;

        
        string trimmed = value.Trim();
        string[] parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], out int year) ||
            !int.TryParse(parts[1], out int month) ||
            !int.TryParse(parts[2], out int day))
        {
            error = "请输入您的生日 (例如：1977/10/15)";
            return false;
        }

        bool ok = year is > 1890 and <= 2101 && month is > 0 and <= 12 && day is > 0 and <= 31;
        if (!ok)
        {
            error = "请输入您的生日 (例如：1977/10/15)";
            return false;
        }

        return true;
    }

    private async Task LoginUiTryRegisterAsync()
    {
        MirStartupInfo startup = EnsureStartupInfo("register");

        if (!TryBuildAccountEntry(isRegister: true, out MirAccountFullEntry entry, out string error))
        {
            ShowModal("Register", error, UiModalButtons.Ok, _ => { });
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _session.CreateAccountAsync(startup, entry, cts.Token).ConfigureAwait(true);
            _uiLoginAccount.Set(entry.Account);
            _uiLoginPassword.Set(entry.Password);
            _txtAccount.Text = entry.Account;
            _txtPassword.Text = entry.Password;
            ShowModal("Register", "Account created successfully.", UiModalButtons.Ok, _ => ShowLoginUi(LoginUiScreen.Login));
        }
        catch (Exception ex)
        {
            ShowModal("Register Failed", ex.Message, UiModalButtons.Ok, _ => { });
        }
    }

    private async Task LoginUiTryUpdateAccountAsync()
    {
        MirStartupInfo startup = EnsureStartupInfo("update-account");

        if (!TryBuildAccountEntry(isRegister: false, out MirAccountFullEntry entry, out string error))
        {
            ShowModal("Update Account", error, UiModalButtons.Ok, _ => { });
            return;
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _session.UpdateAccountAsync(startup, entry, cts.Token).ConfigureAwait(true);
            _uiLoginAccount.Set(entry.Account);
            _uiLoginPassword.Set(entry.Password);
            _txtAccount.Text = entry.Account;
            _txtPassword.Text = entry.Password;
            ShowModal("Update Account", "Account updated successfully.", UiModalButtons.Ok, _ => ShowLoginUi(LoginUiScreen.Login));
        }
        catch (Exception ex)
        {
            ShowModal("Update Account Failed", ex.Message, UiModalButtons.Ok, _ => { });
        }
    }

    private static DrawingRectangle GetLoginUiPanelRect(DrawingSize logicalSize)
    {
        int w = logicalSize.Width;
        int h = logicalSize.Height;

        int panelW = Math.Clamp(w - 32, 520, 860);
        int panelH = Math.Clamp(h - 64, 320, 560);

        int x0 = Math.Max(16, (w - panelW) / 2);
        int y0 = Math.Max(16, (h - panelH) / 2);
        return new DrawingRectangle(x0, y0, panelW, panelH);
    }

    private void DrawRectBorder(DrawingRectangle rect, Color4 color)
    {
        if (_spriteBatch == null || _whiteTexture == null)
            return;

        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left, rect.Top, rect.Width, 1), color: color);
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left, rect.Bottom - 1, rect.Width, 1), color: color);
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left, rect.Top, 1, rect.Height), color: color);
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Right - 1, rect.Top, 1, rect.Height), color: color);
    }

    private static string ClipMultiline(string text, int maxLines, int maxCharsPerLine)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        int take = Math.Min(maxLines, lines.Length);

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < take; i++)
        {
            string line = lines[i];
            if (line.Length > maxCharsPerLine)
                line = line[..maxCharsPerLine] + "...";

            if (i > 0)
                sb.AppendLine();
            sb.Append(line);
        }

        if (lines.Length > take)
            sb.AppendLine("...");

        return sb.ToString();
    }

    private static string GetJobName(byte job) =>
        job switch
        {
            0 => "Warrior",
            1 => "Wizard",
            2 => "Taoist",
            _ => $"Job{job}"
        };

    private static string GetSexName(byte sex) =>
        sex switch
        {
            0 => "Male",
            1 => "Female",
            _ => $"Sex{sex}"
        };

    private bool TryDrawLoginUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;
        _loginUiModalRect = null;

        if (!LoginUiVisible)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        int w = view.LogicalSize.Width;
        int h = view.LogicalSize.Height;
        if (w <= 0 || h <= 0)
            return false;

        if (TryDrawLoginUiClassic(frame, view, out stats))
            return true;

        DrawingRectangle full = new(0, 0, w, h);
        bool hasScreen = _loginUiScreen != LoginUiScreen.None;
        DrawingRectangle panel = hasScreen ? GetLoginUiPanelRect(view.LogicalSize) : full;

        bool caretOn = ((Environment.TickCount64 - _uiFocusStartMs) / 500) % 2 == 0;

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, full, color: new Color4(0, 0, 0, 0.45f));

        if (hasScreen)
        {
            _spriteBatch.Draw(_whiteTexture, panel, color: new Color4(0, 0, 0, 0.78f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panel.X + 1, panel.Y + 1, panel.Width - 2, panel.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.92f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panel.X + 1, panel.Y + 1, panel.Width - 2, 34), color: new Color4(0.18f, 0.18f, 0.23f, 0.92f));
        }

        const int pad = 14;
        const int fieldH = 28;
        const int gap = 8;
        const int btnH = 28;

        int x = panel.X + pad;
        int y = panel.Y + 44;
        int innerW = panel.Width - (pad * 2);

        void DrawField(DrawingRectangle rect, UiTextInput field)
        {
            bool focused = ReferenceEquals(_uiFocusedInput, field);
            _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0.05f, 0.05f, 0.05f, 0.55f));
            DrawRectBorder(rect, focused ? new Color4(0.95f, 0.75f, 0.35f, 1f) : new Color4(0, 0, 0, 0.65f));

            if (focused && caretOn && _textRenderer != null)
            {
                string display = field.GetDisplayText();
                float textWidthBack = string.IsNullOrEmpty(display) ? 0f : _textRenderer.MeasureTextWidth(display);
                float scaleX = Math.Max(0.0001f, view.Scale.X);
                int caretX = rect.X + 6 + (int)MathF.Round(textWidthBack / scaleX) + 1;
                int caretY = rect.Y + 5;
                int caretH = Math.Max(6, rect.Height - 10);
                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(caretX, caretY, 2, caretH), color: new Color4(0.95f, 0.85f, 0.55f, 0.95f));
            }
        }

        void DrawButton(DrawingRectangle rect, bool enabled)
        {
            Color4 bg = enabled ? new Color4(0.22f, 0.22f, 0.28f, 0.95f) : new Color4(0.15f, 0.15f, 0.18f, 0.85f);
            _spriteBatch.Draw(_whiteTexture, rect, color: bg);
            DrawRectBorder(rect, new Color4(0, 0, 0, 0.65f));
        }

        if (hasScreen)
        {
        switch (_loginUiScreen)
        {
            case LoginUiScreen.Login:
            {
                DrawingRectangle infoRect = new(x, y, innerW, 34);
                _spriteBatch.Draw(_whiteTexture, infoRect, color: new Color4(0.08f, 0.08f, 0.11f, 0.55f));
                DrawRectBorder(infoRect, new Color4(0, 0, 0, 0.65f));
                y += infoRect.Height + gap;

                DrawField(new DrawingRectangle(x, y, innerW, fieldH), _uiLoginAccount);
                y += fieldH + gap;
                DrawField(new DrawingRectangle(x, y, innerW, fieldH), _uiLoginPassword);

                int btnW = 120;
                int yBtn2 = panel.Bottom - pad - btnH;
                int yBtn1 = yBtn2 - btnH - gap;
                int rowX3 = panel.Right - pad - ((btnW * 3) + (gap * 2));
                int rowX2 = panel.Right - pad - ((btnW * 2) + gap);

                DrawButton(new DrawingRectangle(rowX3, yBtn1, btnW, btnH), enabled: true); 
                DrawButton(new DrawingRectangle(rowX3 + btnW + gap, yBtn1, btnW, btnH), enabled: true); 
                DrawButton(new DrawingRectangle(rowX3 + (btnW + gap) * 2, yBtn1, btnW, btnH), enabled: true); 

                DrawButton(new DrawingRectangle(rowX2, yBtn2, btnW, btnH), enabled: !_loginFlowInProgress); 
                DrawButton(new DrawingRectangle(rowX2 + btnW + gap, yBtn2, btnW, btnH), enabled: true); 
                break;
            }
            case LoginUiScreen.RestoreDeletedCharacter:
            {
                int listH = panel.Height - 44 - pad - btnH - gap;
                DrawingRectangle listRect = new(x, y, innerW, Math.Max(120, listH));
                _spriteBatch.Draw(_whiteTexture, listRect, color: new Color4(0.05f, 0.05f, 0.05f, 0.55f));
                DrawRectBorder(listRect, new Color4(0, 0, 0, 0.65f));

                int rowH = 22;
                int visible = Math.Max(1, listRect.Height / rowH);
                int start = Math.Clamp(_uiDeletedSelectedIndex - (visible / 2), 0, Math.Max(0, _uiDeletedCharacters.Count - visible));

                for (int i = 0; i < visible && i + start < _uiDeletedCharacters.Count; i++)
                {
                    int idx = start + i;
                    if (idx == _uiDeletedSelectedIndex)
                    {
                        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(listRect.X + 1, listRect.Y + 1 + (i * rowH), listRect.Width - 2, rowH), color: new Color4(0.25f, 0.25f, 0.32f, 0.75f));
                    }
                }

                int btnW = 120;
                int yBtn = panel.Bottom - pad - btnH;
                DrawButton(new DrawingRectangle(panel.X + pad, yBtn, btnW, btnH), enabled: true); 
                DrawButton(new DrawingRectangle(panel.Right - pad - btnW, yBtn, btnW, btnH), enabled: !_loginFlowInProgress && _uiDeletedCharacters.Count > 0); 
                break;
            }
            case LoginUiScreen.SelectServer:
            {
                int listH = panel.Height - 44 - pad - btnH - gap;
                DrawingRectangle listRect = new(x, y, innerW, Math.Max(120, listH));
                _spriteBatch.Draw(_whiteTexture, listRect, color: new Color4(0.05f, 0.05f, 0.05f, 0.55f));
                DrawRectBorder(listRect, new Color4(0, 0, 0, 0.65f));

                int rowH = 22;
                int visible = Math.Max(1, listRect.Height / rowH);
                int start = Math.Clamp(_uiServerSelectedIndex - (visible / 2), 0, Math.Max(0, _uiServers.Count - visible));

                for (int i = 0; i < visible && i + start < _uiServers.Count; i++)
                {
                    int idx = start + i;
                    if (idx == _uiServerSelectedIndex)
                    {
                        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(listRect.X + 1, listRect.Y + 1 + (i * rowH), listRect.Width - 2, rowH), color: new Color4(0.25f, 0.25f, 0.32f, 0.75f));
                    }
                }

                int btnW = 120;
                int yBtn = panel.Bottom - pad - btnH;
                DrawButton(new DrawingRectangle(panel.X + pad, yBtn, btnW, btnH), enabled: true); 
                DrawButton(new DrawingRectangle(panel.Right - pad - btnW, yBtn, btnW, btnH), enabled: !_loginFlowInProgress && _uiServers.Count > 0); 
                break;
            }
            case LoginUiScreen.SelectCharacter:
            {
                int rowH = 28;
                DrawingRectangle listRect = new(x, y, innerW, (rowH * 2) + gap);
                _spriteBatch.Draw(_whiteTexture, listRect, color: new Color4(0.05f, 0.05f, 0.05f, 0.55f));
                DrawRectBorder(listRect, new Color4(0, 0, 0, 0.65f));

                for (int i = 0; i < 2; i++)
                {
                    if (i == _uiCharacterSelectedIndex)
                    {
                        int ry = listRect.Y + 1 + (i * (rowH + gap));
                        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(listRect.X + 1, ry, listRect.Width - 2, rowH), color: new Color4(0.25f, 0.25f, 0.32f, 0.75f));
                    }
                }

                int btnW = 120;
                int yBtn2 = panel.Bottom - pad - btnH;
                int yBtn1 = yBtn2 - btnH - gap;
                int rowX = panel.Right - pad - ((btnW * 3) + (gap * 2));

                DrawButton(new DrawingRectangle(rowX, yBtn1, btnW, btnH), enabled: !_loginFlowInProgress); 
                DrawButton(new DrawingRectangle(rowX + btnW + gap, yBtn1, btnW, btnH), enabled: !_loginFlowInProgress); 
                DrawButton(new DrawingRectangle(rowX + (btnW + gap) * 2, yBtn1, btnW, btnH), enabled: !_loginFlowInProgress); 

                DrawButton(new DrawingRectangle(rowX, yBtn2, btnW, btnH), enabled: true); 
                DrawButton(new DrawingRectangle(rowX + btnW + gap, yBtn2, btnW, btnH), enabled: !_loginFlowInProgress); 
                break;
            }
            default:
            {
                UiTextInput[] fields = GetFocusableFieldsForScreen(_loginUiScreen);
                int yy = y;
                foreach (UiTextInput f in fields)
                {
                    DrawField(new DrawingRectangle(x, yy, innerW, fieldH), f);
                    yy += fieldH + gap;
                }

                int btnW = 120;
                int yBtn = panel.Bottom - pad - btnH;
                DrawButton(new DrawingRectangle(panel.X + pad, yBtn, btnW, btnH), enabled: true); 
                DrawButton(new DrawingRectangle(panel.Right - pad - btnW, yBtn, btnW, btnH), enabled: !_loginFlowInProgress); 
                break;
            }
        }
        }

        if (_loginUiModal != null)
            DrawLoginUiModal(frame, panel);
        else if (_uiTextPrompt != null)
        {
            bool hasAbort = _uiTextPrompt.Buttons == UiTextPromptButtons.OkCancelAbort;
            GetTextPromptLayout(panel, hasAbort, out DrawingRectangle rect, out DrawingRectangle promptRect, out DrawingRectangle inputRect, out DrawingRectangle btnOk, out DrawingRectangle btnCancel, out DrawingRectangle btnAbort);

            _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.88f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, rect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.96f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.X + 1, rect.Y + 1, rect.Width - 2, 34), color: new Color4(0.18f, 0.18f, 0.23f, 0.96f));

            _spriteBatch.Draw(_whiteTexture, promptRect, color: new Color4(0.05f, 0.05f, 0.05f, 0.35f));
            DrawRectBorder(promptRect, new Color4(0, 0, 0, 0.65f));

            DrawField(inputRect, _uiTextPrompt.Input);

            DrawButton(btnOk, enabled: true);
            DrawButton(btnCancel, enabled: true);
            if (hasAbort)
                DrawButton(btnAbort, enabled: true);
        }

        _spriteBatch.End();
        stats = _spriteBatch.Stats;
        return true;
    }

    private void DrawLoginUiModal(D3D11Frame frame, DrawingRectangle panel)
    {
        if (_spriteBatch == null || _whiteTexture == null || _loginUiModal == null)
            return;

        UiModal modal = _loginUiModal;
        UiModalLayout layout = modal.Layout;
        if (layout != UiModalLayout.Default && modal.Buttons != UiModalButtons.Ok)
            layout = UiModalLayout.Default;

        DrawingRectangle rect;
        DrawingRectangle okRect;
        DrawingRectangle? cancelRect = null;

        if (layout == UiModalLayout.Classic2)
        {
            const int bgIndex = 380; 
            const int okButtonIndex = 361; 
            const int fallbackW = 256;
            const int fallbackH = 359;

            int x = panel.X + (panel.Width - fallbackW) / 2;
            int y = panel.Y + (panel.Height - fallbackH) / 2;
            rect = new DrawingRectangle(x, y, fallbackW, fallbackH);
            _loginUiModalRect = rect;

            string resourceRoot = GetResourceRootDir();
            string dataDir = Path.Combine(resourceRoot, "Data");
            string? wMainPath = Directory.Exists(dataDir) ? TryResolveArchiveFilePath(dataDir, "WMain") : null;

            if (!string.IsNullOrWhiteSpace(wMainPath))
            {
                PrefetchArchiveImageCached(wMainPath, bgIndex);
                if (TryGetArchiveTextureCached(frame, wMainPath, bgIndex, out D3D11Texture2D bg))
                    _spriteBatch.Draw(bg, rect);
                else
                    _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0.12f, 0.12f, 0.16f, 0.96f));
            }
            else
            {
                _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0.12f, 0.12f, 0.16f, 0.96f));
            }

            
            okRect = new DrawingRectangle(rect.Left + 90, rect.Top + 305, width: 92, height: 28);
            bool drewOk = false;
            if (!string.IsNullOrWhiteSpace(wMainPath))
            {
                PrefetchArchiveImageCached(wMainPath, okButtonIndex);
                if (TryGetArchiveTextureCached(frame, wMainPath, okButtonIndex, out D3D11Texture2D okButton))
                {
                    _spriteBatch.Draw(okButton, okRect);
                    drewOk = true;
                }
            }

            if (!drewOk)
            {
                _spriteBatch.Draw(_whiteTexture, okRect, color: new Color4(0.22f, 0.22f, 0.28f, 0.96f));
                DrawRectBorder(okRect, new Color4(0, 0, 0, 0.65f));
            }
            return;
        }

        const int pad = 14;
        const int btnH = 28;
        const int gap = 10;

        int w = Math.Clamp(panel.Width - 80, 360, 640);
        int h = 200;
        int x0 = panel.X + (panel.Width - w) / 2;
        int y0 = panel.Y + (panel.Height - h) / 2;

        rect = new DrawingRectangle(x0, y0, w, h);
        _loginUiModalRect = rect;

        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.88f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(x0 + 1, y0 + 1, w - 2, h - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.96f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(x0 + 1, y0 + 1, w - 2, 34), color: new Color4(0.18f, 0.18f, 0.23f, 0.96f));

        int btnW = 120;
        int yBtn = y0 + h - pad - btnH;
        int xBtnRight = x0 + w - pad - btnW;
        okRect = new DrawingRectangle(xBtnRight, yBtn, btnW, btnH);

        _spriteBatch.Draw(_whiteTexture, okRect, color: new Color4(0.22f, 0.22f, 0.28f, 0.96f));
        DrawRectBorder(okRect, new Color4(0, 0, 0, 0.65f));

        if (modal.Buttons is UiModalButtons.OkCancel or UiModalButtons.YesNo)
        {
            int xBtnLeft = xBtnRight - (btnW + gap);
            cancelRect = new DrawingRectangle(xBtnLeft, yBtn, btnW, btnH);
            _spriteBatch.Draw(_whiteTexture, cancelRect.Value, color: new Color4(0.22f, 0.22f, 0.28f, 0.96f));
            DrawRectBorder(cancelRect.Value, new Color4(0, 0, 0, 0.65f));
        }
    }

    private bool TryGetArchiveTextureCached(D3D11Frame frame, string archivePath, int imageIndex, out D3D11Texture2D texture)
    {
        if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".wzl", StringComparison.OrdinalIgnoreCase))
        {
            texture = null!;
            if (_wilTextureCache == null)
                return false;

            var key = new WilImageKey(archivePath, imageIndex);
            if (_wilTextureCache.TryGet(key, out texture!))
                return true;

            if (_wilImageCache.TryGetImage(key, out WilImage image))
            {
                texture = _wilTextureCache.GetOrCreate(
                    key,
                    () => D3D11Texture2D.CreateFromBgra32(frame.Device, image.Bgra32, image.Width, image.Height));
                return true;
            }

            return false;
        }

        texture = null!;
        if (_dataTextureCache == null)
            return false;

        var dataKey = new PackDataImageKey(archivePath, imageIndex);
        if (_dataTextureCache.TryGet(dataKey, out texture!))
            return true;

        if (_packDataImageCache.TryGetImage(dataKey, out PackDataImage dataImage))
        {
            texture = _dataTextureCache.GetOrCreate(
                dataKey,
                () => D3D11Texture2D.CreateFromBgra32(frame.Device, dataImage.Bgra32, dataImage.Width, dataImage.Height));
            return true;
        }

        return false;
    }

    private void PrefetchArchiveImageCached(string archivePath, int imageIndex)
    {
        if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase) ||
            archivePath.EndsWith(".wzl", StringComparison.OrdinalIgnoreCase))
        {
            _ = _wilImageCache.GetImageAsyncFullPath(archivePath, imageIndex);
            return;
        }

        _ = _packDataImageCache.GetImageAsyncFullPath(archivePath, imageIndex);
    }

    private void AppendLoginUiText(D3D11ViewTransform view)
    {
        if (!LoginUiVisible || _textRenderer == null)
            return;

        if (AppendLoginUiTextClassic(view))
            return;

        DrawingRectangle full = new(0, 0, view.LogicalSize.Width, view.LogicalSize.Height);
        DrawingRectangle panel = _loginUiScreen == LoginUiScreen.None ? full : GetLoginUiPanelRect(view.LogicalSize);

        if (_loginUiScreen == LoginUiScreen.None)
        {
            if (_loginUiModal != null)
                AppendLoginUiModalText(panel, view);
            else if (_uiTextPrompt != null)
                AppendTextPromptText(panel, view);
            return;
        }

        const int pad = 14;
        const int fieldH = 28;
        const int gap = 8;
        const int btnH = 28;

        int x = panel.X + pad;
        int y = panel.Y + 12;

        string title = _loginUiScreen switch
        {
            LoginUiScreen.Login => "Login",
            LoginUiScreen.SelectServer => "Select Server",
            LoginUiScreen.SelectCharacter => "Select Character",
            LoginUiScreen.CreateCharacter => "Create Character",
            LoginUiScreen.ChangePassword => "Change Password",
            LoginUiScreen.Register => "Register",
            LoginUiScreen.UpdateAccount => "Update Account",
            LoginUiScreen.RestoreDeletedCharacter => "Restore Deleted",
            _ => "Menu"
        };

        Vector2 pTitle = view.ToBackBuffer(new Vector2(x, y));
        _textRenderer.DrawText(title, pTitle.X, pTitle.Y, new Color4(0.95f, 0.95f, 0.95f, 1));

        x = panel.X + pad;
        y = panel.Y + 44;
        int innerW = panel.Width - (pad * 2);

        void DrawButtonLabel(DrawingRectangle rect, string text, bool enabled)
        {
            Color4 c = enabled ? new Color4(0.92f, 0.92f, 0.92f, 1) : new Color4(0.55f, 0.55f, 0.55f, 1);
            Vector2 p = view.ToBackBuffer(new Vector2(rect.X + 10, rect.Y + 6));
            _textRenderer.DrawText(text, p.X, p.Y, c);
        }

        void DrawFieldText(DrawingRectangle rect, UiTextInput field, bool multilineClip)
        {
            string display = field.GetDisplayText();
            if (multilineClip && field.IsMultiline)
                display = ClipMultiline(display, maxLines: 3, maxCharsPerLine: 200);

            Vector2 p = view.ToBackBuffer(new Vector2(rect.X + 6, rect.Y + 6));
            _textRenderer.DrawText(display, p.X, p.Y, new Color4(0.92f, 0.92f, 0.92f, 1));
        }

        switch (_loginUiScreen)
        {
            case LoginUiScreen.Login:
            {
                DrawingRectangle infoRect = new(x, y, innerW, 34);
                string info = _startup == null
                    ? "LoginGate: (not set)"
                    : $"LoginGate: {_startup.ServerAddress}:{_startup.ServerPort}  ServerName: {_startup.ServerName}  ResDir: {_startup.ResourceDir}";
                Vector2 pInfo = view.ToBackBuffer(new Vector2(infoRect.X + 6, infoRect.Y + 8));
                _textRenderer.DrawText(info, pInfo.X, pInfo.Y, new Color4(0.85f, 0.85f, 0.85f, 1));
                y += infoRect.Height + gap;

                DrawFieldText(new DrawingRectangle(x, y, innerW, fieldH), _uiLoginAccount, multilineClip: false);
                y += fieldH + gap;
                DrawFieldText(new DrawingRectangle(x, y, innerW, fieldH), _uiLoginPassword, multilineClip: false);

                int btnW = 120;
                int yBtn2 = panel.Bottom - pad - btnH;
                int yBtn1 = yBtn2 - btnH - gap;
                int rowX3 = panel.Right - pad - ((btnW * 3) + (gap * 2));
                int rowX2 = panel.Right - pad - ((btnW * 2) + gap);

                DrawButtonLabel(new DrawingRectangle(rowX3, yBtn1, btnW, btnH), "ChangePwd", enabled: true);
                DrawButtonLabel(new DrawingRectangle(rowX3 + btnW + gap, yBtn1, btnW, btnH), "Register", enabled: true);
                DrawButtonLabel(new DrawingRectangle(rowX3 + (btnW + gap) * 2, yBtn1, btnW, btnH), "Update", enabled: true);

                DrawButtonLabel(new DrawingRectangle(rowX2, yBtn2, btnW, btnH), _loginFlowInProgress ? "Working..." : "Login", enabled: !_loginFlowInProgress);
                DrawButtonLabel(new DrawingRectangle(rowX2 + btnW + gap, yBtn2, btnW, btnH), "Cancel", enabled: true);
                break;
            }
            case LoginUiScreen.RestoreDeletedCharacter:
            {
                int listH = panel.Height - 44 - pad - btnH - gap;
                DrawingRectangle listRect = new(x, y, innerW, Math.Max(120, listH));
                int rowH = 22;
                int visible = Math.Max(1, listRect.Height / rowH);
                int start = Math.Clamp(_uiDeletedSelectedIndex - (visible / 2), 0, Math.Max(0, _uiDeletedCharacters.Count - visible));

                for (int i = 0; i < visible && i + start < _uiDeletedCharacters.Count; i++)
                {
                    int idx = start + i;
                    MirDeletedCharacterInfo c = _uiDeletedCharacters[idx];
                    string text = $"{c.Name}  Lv{c.Level}  {GetJobName(c.Job)}  {GetSexName(c.Sex)}";
                    Color4 col = idx == _uiDeletedSelectedIndex ? new Color4(0.95f, 0.85f, 0.55f, 1) : new Color4(0.9f, 0.9f, 0.9f, 1);
                    Vector2 p = view.ToBackBuffer(new Vector2(listRect.X + 8, listRect.Y + 6 + (i * rowH)));
                    _textRenderer.DrawText(text, p.X, p.Y, col);
                }

                int btnW = 120;
                int yBtn = panel.Bottom - pad - btnH;
                DrawButtonLabel(new DrawingRectangle(panel.X + pad, yBtn, btnW, btnH), "Back", enabled: true);
                DrawButtonLabel(new DrawingRectangle(panel.Right - pad - btnW, yBtn, btnW, btnH), "Restore", enabled: !_loginFlowInProgress && _uiDeletedCharacters.Count > 0);
                break;
            }
            case LoginUiScreen.SelectServer:
            {
                int listH = panel.Height - 44 - pad - btnH - gap;
                DrawingRectangle listRect = new(x, y, innerW, Math.Max(120, listH));
                int rowH = 22;
                int visible = Math.Max(1, listRect.Height / rowH);
                int start = Math.Clamp(_uiServerSelectedIndex - (visible / 2), 0, Math.Max(0, _uiServers.Count - visible));

                for (int i = 0; i < visible && i + start < _uiServers.Count; i++)
                {
                    int idx = start + i;
                    ServerListItem s = _uiServers[idx];
                    string text = string.IsNullOrWhiteSpace(s.Status) ? s.Name : $"{s.Name} ({s.Status})";
                    Color4 c = idx == _uiServerSelectedIndex ? new Color4(0.95f, 0.85f, 0.55f, 1) : new Color4(0.9f, 0.9f, 0.9f, 1);
                    Vector2 p = view.ToBackBuffer(new Vector2(listRect.X + 8, listRect.Y + 6 + (i * rowH)));
                    _textRenderer.DrawText(text, p.X, p.Y, c);
                }

                int btnW = 120;
                int yBtn = panel.Bottom - pad - btnH;
                DrawButtonLabel(new DrawingRectangle(panel.X + pad, yBtn, btnW, btnH), "Back", enabled: true);
                DrawButtonLabel(new DrawingRectangle(panel.Right - pad - btnW, yBtn, btnW, btnH), "OK", enabled: !_loginFlowInProgress && _uiServers.Count > 0);
                break;
            }
            case LoginUiScreen.SelectCharacter:
            {
                int rowH = 28;
                    for (int i = 0; i < 2; i++)
                    {
                        MirCharacterInfo? c = i < _uiCharacters.Count ? _uiCharacters[i] : null;
                    string text = c == null || string.IsNullOrWhiteSpace(c.Name)
                        ? $"Slot {i + 1}: (empty)"
                        : $"Slot {i + 1}: {(c.Selected ? "* " : string.Empty)}{c.Name}  Lv{c.Level}  {GetJobName(c.Job)}  {GetSexName(c.Sex)}  Hair{c.Hair}";
                        Color4 col = i == _uiCharacterSelectedIndex ? new Color4(0.95f, 0.85f, 0.55f, 1) : new Color4(0.9f, 0.9f, 0.9f, 1);
                        Vector2 p = view.ToBackBuffer(new Vector2(x + 8, y + 6 + (i * (rowH + gap))));
                        _textRenderer.DrawText(text, p.X, p.Y, col);
                    }

                int btnW = 120;
                int yBtn2 = panel.Bottom - pad - btnH;
                int yBtn1 = yBtn2 - btnH - gap;
                int rowX = panel.Right - pad - ((btnW * 3) + (gap * 2));

                DrawButtonLabel(new DrawingRectangle(rowX, yBtn1, btnW, btnH), "Create", enabled: !_loginFlowInProgress);
                DrawButtonLabel(new DrawingRectangle(rowX + btnW + gap, yBtn1, btnW, btnH), "Delete", enabled: !_loginFlowInProgress);
                DrawButtonLabel(new DrawingRectangle(rowX + (btnW + gap) * 2, yBtn1, btnW, btnH), "Restore", enabled: !_loginFlowInProgress);

                DrawButtonLabel(new DrawingRectangle(rowX, yBtn2, btnW, btnH), "Back", enabled: true);
                DrawButtonLabel(new DrawingRectangle(rowX + btnW + gap, yBtn2, btnW, btnH), "Enter", enabled: !_loginFlowInProgress);
                break;
            }
            default:
            {
                UiTextInput[] fields = GetFocusableFieldsForScreen(_loginUiScreen);
                int yy = y;
                foreach (UiTextInput f in fields)
                {
                    DrawFieldText(new DrawingRectangle(x, yy, innerW, fieldH), f, multilineClip: false);
                    yy += fieldH + gap;
                }

                int btnW = 120;
                int yBtn = panel.Bottom - pad - btnH;
                DrawButtonLabel(new DrawingRectangle(panel.X + pad, yBtn, btnW, btnH), "Back", enabled: true);
                DrawButtonLabel(new DrawingRectangle(panel.Right - pad - btnW, yBtn, btnW, btnH), "OK", enabled: !_loginFlowInProgress);
                break;
            }
        }

        if (_loginUiModal != null)
            AppendLoginUiModalText(panel, view);
        else if (_uiTextPrompt != null)
            AppendTextPromptText(panel, view);
    }

    private void AppendTextPromptText(DrawingRectangle panel, D3D11ViewTransform view)
    {
        if (_uiTextPrompt == null || _textRenderer == null)
            return;

        const int pad = 14;

        bool hasAbort = _uiTextPrompt.Buttons == UiTextPromptButtons.OkCancelAbort;
        GetTextPromptLayout(panel, hasAbort, out DrawingRectangle rect, out DrawingRectangle promptRect, out DrawingRectangle inputRect, out DrawingRectangle btnOk, out DrawingRectangle btnCancel, out DrawingRectangle btnAbort);

        Vector2 pTitle = view.ToBackBuffer(new Vector2(rect.X + pad, rect.Y + 10));
        _textRenderer.DrawText(_uiTextPrompt.Title, pTitle.X, pTitle.Y, new Color4(0.95f, 0.95f, 0.95f, 1));

        string msg = ClipMultiline(_uiTextPrompt.Prompt, maxLines: 6, maxCharsPerLine: 120);
        Vector2 pMsg = view.ToBackBuffer(new Vector2(promptRect.X + 2, promptRect.Y + 2));
        _textRenderer.DrawText(msg, pMsg.X, pMsg.Y, new Color4(0.9f, 0.9f, 0.9f, 1));

        Vector2 pInput = view.ToBackBuffer(new Vector2(inputRect.X + 6, inputRect.Y + 6));
        _textRenderer.DrawText(_uiTextPrompt.Input.GetDisplayText(), pInput.X, pInput.Y, new Color4(0.92f, 0.92f, 0.92f, 1));

        Vector2 pOk = view.ToBackBuffer(new Vector2(btnOk.X + 10, btnOk.Y + 6));
        _textRenderer.DrawText("OK", pOk.X, pOk.Y, new Color4(0.92f, 0.92f, 0.92f, 1));

        Vector2 pCancel = view.ToBackBuffer(new Vector2(btnCancel.X + 10, btnCancel.Y + 6));
        _textRenderer.DrawText("Cancel", pCancel.X, pCancel.Y, new Color4(0.92f, 0.92f, 0.92f, 1));

        if (hasAbort)
        {
            Vector2 pAbort = view.ToBackBuffer(new Vector2(btnAbort.X + 10, btnAbort.Y + 6));
            _textRenderer.DrawText("Abort", pAbort.X, pAbort.Y, new Color4(0.92f, 0.92f, 0.92f, 1));
        }
    }

    private void AppendLoginUiModalText(DrawingRectangle panel, D3D11ViewTransform view)
    {
        if (_loginUiModal == null || _textRenderer == null)
            return;

        UiModal modal = _loginUiModal;
        UiModalLayout layout = modal.Layout;
        if (layout != UiModalLayout.Default && modal.Buttons != UiModalButtons.Ok)
            layout = UiModalLayout.Default;

        if (layout == UiModalLayout.Classic2)
        {
            const int msglx = 23; 
            const int msgly = 20; 
            const int lineH = 14; 
            const int buttonTop = 305; 

            DrawingRectangle rect;
            if (_loginUiModalRect is { } last && last.Width > 0 && last.Height > 0)
            {
                rect = last;
            }
            else
            {
                const int fallbackW = 256;
                const int fallbackH = 359;
                int dlgX = panel.X + (panel.Width - fallbackW) / 2;
                int dlgY = panel.Y + (panel.Height - fallbackH) / 2;
                rect = new DrawingRectangle(dlgX, dlgY, fallbackW, fallbackH);
            }

            int maxLines = Math.Max(1, (buttonTop - msgly) / lineH);
            string msgText = ClipMultiline(modal.Message, maxLines, maxCharsPerLine: 120);
            Vector2 pMsgText = view.ToBackBuffer(new Vector2(rect.Left + msglx, rect.Top + msgly));
            _textRenderer.DrawText(msgText, pMsgText.X, pMsgText.Y, new Color4(0.95f, 0.95f, 0.95f, 1));
            return;
        }

        const int pad = 14;
        const int btnH = 28;
        const int gap = 10;

        int w = Math.Clamp(panel.Width - 80, 360, 640);
        int h = 200;
        int x = panel.X + (panel.Width - w) / 2;
        int y = panel.Y + (panel.Height - h) / 2;

        Vector2 pTitle = view.ToBackBuffer(new Vector2(x + pad, y + 10));
        _textRenderer.DrawText(modal.Title, pTitle.X, pTitle.Y, new Color4(0.95f, 0.95f, 0.95f, 1));

        string msg = ClipMultiline(modal.Message, maxLines: 5, maxCharsPerLine: 120);
        Vector2 pMsg = view.ToBackBuffer(new Vector2(x + pad, y + 46));
        _textRenderer.DrawText(msg, pMsg.X, pMsg.Y, new Color4(0.9f, 0.9f, 0.9f, 1));

        int btnW = 120;
        int yBtn = y + h - pad - btnH;
        int xBtnRight = x + w - pad - btnW;

        string right = modal.Buttons == UiModalButtons.YesNo ? "Yes" : "OK";
        Vector2 pRight = view.ToBackBuffer(new Vector2(xBtnRight + 10, yBtn + 6));
        _textRenderer.DrawText(right, pRight.X, pRight.Y, new Color4(0.92f, 0.92f, 0.92f, 1));

        if (modal.Buttons is UiModalButtons.OkCancel or UiModalButtons.YesNo)
        {
            int xBtnLeft = xBtnRight - (btnW + gap);
            string left = modal.Buttons == UiModalButtons.OkCancel ? "Cancel" : "No";
            Vector2 pLeft = view.ToBackBuffer(new Vector2(xBtnLeft + 10, yBtn + 6));
            _textRenderer.DrawText(left, pLeft.X, pLeft.Y, new Color4(0.92f, 0.92f, 0.92f, 1));
        }
    }

    private static void GetTextPromptLayout(
        DrawingRectangle hostPanel,
        bool hasAbort,
        out DrawingRectangle rect,
        out DrawingRectangle promptRect,
        out DrawingRectangle inputRect,
        out DrawingRectangle btnOk,
        out DrawingRectangle btnCancel,
        out DrawingRectangle btnAbort)
    {
        const int pad = 14;
        const int btnH = 28;
        const int gap = 10;
        const int inputH = 28;

        int w = Math.Clamp(hostPanel.Width - 80, 420, 740);
        int h = 240;
        int x = hostPanel.X + (hostPanel.Width - w) / 2;
        int y = hostPanel.Y + (hostPanel.Height - h) / 2;

        rect = new DrawingRectangle(x, y, w, h);

        int btnW = 120;
        int yBtn = y + h - pad - btnH;
        int xBtnOk = x + w - pad - btnW;

        btnOk = new DrawingRectangle(xBtnOk, yBtn, btnW, btnH);
        btnCancel = new DrawingRectangle(xBtnOk - (btnW + gap), yBtn, btnW, btnH);
        btnAbort = new DrawingRectangle(xBtnOk - ((btnW + gap) * 2), yBtn, btnW, btnH);

        int inputY = yBtn - gap - inputH;
        inputRect = new DrawingRectangle(x + pad, inputY, w - (pad * 2), inputH);

        int promptY = y + 46;
        int promptH = Math.Max(1, inputY - gap - promptY);
        promptRect = new DrawingRectangle(x + pad, promptY, w - (pad * 2), promptH);

        if (!hasAbort)
            btnAbort = default;
    }

    private static bool RectContains(DrawingRectangle rect, Vector2 logical) =>
        logical.X >= rect.Left && logical.X < rect.Right && logical.Y >= rect.Top && logical.Y < rect.Bottom;

    private DrawingSize GetLoginUiLogicalSizeFallback()
    {
        if (_lastLogicalSize.Width > 0 && _lastLogicalSize.Height > 0)
            return _lastLogicalSize;

        DrawingSize s = _renderControl.ClientSize;
        return s.Width > 0 && s.Height > 0 ? s : new DrawingSize(800, 600);
    }

    private void FocusField(UiTextInput field)
    {
        _uiFocusedInput = field;
        _uiFocusStartMs = Environment.TickCount64;
    }

    private void PrefillAndShowChangePassword()
    {
        _uiChangePwdAccount.Set(_uiLoginAccount.GetTrimmed());
        _uiChangePwdOld.Clear();
        _uiChangePwdNew.Clear();
        _uiChangePwdConfirm.Clear();
        ShowLoginUi(LoginUiScreen.ChangePassword);
    }

    private void PrefillAndShowRegister()
    {
        _uiAccAccount.Set(_uiLoginAccount.GetTrimmed());
        _uiAccPassword.Set(_uiLoginPassword.GetTrimmed());
        _uiAccConfirm.Set(_uiLoginPassword.GetTrimmed());
        _uiAccUserName.Clear();
        _uiAccSsNo.Clear();
        _uiAccQuiz1.Clear();
        _uiAccAnswer1.Clear();
        _uiAccQuiz2.Clear();
        _uiAccAnswer2.Clear();
        _uiAccBirthDay.Clear();
        _uiAccPhone.Clear();
        _uiAccMobilePhone.Clear();
        _uiAccEmail.Clear();
        ShowLoginUi(LoginUiScreen.Register);
    }

    private void PrefillAndShowUpdateAccount()
    {
        _uiAccAccount.Set(_uiLoginAccount.GetTrimmed());
        _uiAccPassword.Set(_uiLoginPassword.GetTrimmed());
        _uiAccConfirm.Clear();
        _uiAccUserName.Clear();
        _uiAccSsNo.Clear();
        _uiAccQuiz1.Clear();
        _uiAccAnswer1.Clear();
        _uiAccQuiz2.Clear();
        _uiAccAnswer2.Clear();
        _uiAccBirthDay.Clear();
        _uiAccPhone.Clear();
        _uiAccMobilePhone.Clear();
        _uiAccEmail.Clear();
        ShowLoginUi(LoginUiScreen.UpdateAccount);
    }

    private async Task<bool> TryHandleLoginUiMouseDownAsync(Vector2 logical, bool left, bool right)
    {
        if (!LoginUiVisible)
            return false;

        if (!left && !right)
            return true;

        DrawingSize logicalSize = GetLoginUiLogicalSizeFallback();
        DrawingRectangle full = new(0, 0, logicalSize.Width, logicalSize.Height);

        bool classic = IsClassicLoginUiScreen(_loginUiScreen) && TryEnsureLoginUiArchives();
        DrawingRectangle panel = classic || _loginUiScreen == LoginUiScreen.None ? full : GetLoginUiPanelRect(logicalSize);

        if (_loginUiModal != null)
        {
            HandleLoginUiModalClick(panel, logical, left);
            return true;
        }

        if (_uiTextPrompt != null)
        {
            HandleTextPromptClick(panel, logical, left);
            return true;
        }

        if (classic)
        {
            bool consumed = await TryHandleLoginUiMouseDownClassicAsync(logical, left, right).ConfigureAwait(true);
            if (consumed)
                return true;
        }

        if (!RectContains(panel, logical))
        {
            _uiFocusedInput = null;
            return true;
        }

        const int pad = 14;
        const int fieldH = 28;
        const int gap = 8;
        const int btnH = 28;

        int x = panel.X + pad;
        int y = panel.Y + 44;
        int innerW = panel.Width - (pad * 2);

        switch (_loginUiScreen)
        {
            case LoginUiScreen.Login:
            {
                DrawingRectangle rInfo = new(x, y, innerW, 34);
                y += rInfo.Height + gap;
                DrawingRectangle rAcc = new(x, y, innerW, fieldH);
                y += fieldH + gap;
                DrawingRectangle rPwd = new(x, y, innerW, fieldH);

                int btnW = 120;
                int yBtn2 = panel.Bottom - pad - btnH;
                int yBtn1 = yBtn2 - btnH - gap;
                int rowX3 = panel.Right - pad - ((btnW * 3) + (gap * 2));
                int rowX2 = panel.Right - pad - ((btnW * 2) + gap);

                DrawingRectangle btnChangePwd = new(rowX3, yBtn1, btnW, btnH);
                DrawingRectangle btnRegister = new(rowX3 + btnW + gap, yBtn1, btnW, btnH);
                DrawingRectangle btnUpdate = new(rowX3 + (btnW + gap) * 2, yBtn1, btnW, btnH);

                DrawingRectangle btnLogin = new(rowX2, yBtn2, btnW, btnH);
                DrawingRectangle btnCancel = new(rowX2 + btnW + gap, yBtn2, btnW, btnH);

                if (RectContains(rAcc, logical))
                {
                    FocusField(_uiLoginAccount);
                    return true;
                }

                if (RectContains(rPwd, logical))
                {
                    FocusField(_uiLoginPassword);
                    return true;
                }

                if (RectContains(btnChangePwd, logical) && left)
                {
                    PrefillAndShowChangePassword();
                    return true;
                }

                if (RectContains(btnRegister, logical) && left)
                {
                    PrefillAndShowRegister();
                    return true;
                }

                if (RectContains(btnUpdate, logical) && left)
                {
                    PrefillAndShowUpdateAccount();
                    return true;
                }

                if (RectContains(btnLogin, logical) && left)
                {
                    await LoginUiTryLoginAsync().ConfigureAwait(true);
                    return true;
                }

                if (RectContains(btnCancel, logical) && left)
                {
                    HideLoginUi();
                    return true;
                }

                
                if (RectContains(rInfo, logical))
                {
                    _uiFocusedInput = null;
                    return true;
                }

                return true;
            }
            case LoginUiScreen.SelectServer:
            {
                int listH = panel.Height - 44 - pad - btnH - gap;
                DrawingRectangle listRect = new(x, y, innerW, Math.Max(120, listH));
                int btnW = 120;
                int yBtn = panel.Bottom - pad - btnH;
                DrawingRectangle btnBack = new(panel.X + pad, yBtn, btnW, btnH);
                DrawingRectangle btnOk = new(panel.Right - pad - btnW, yBtn, btnW, btnH);

                if (RectContains(btnBack, logical) && left)
                {
                    _ = DisconnectAsync(DisconnectBehavior.PromptLogin);
                    return true;
                }

                if (RectContains(btnOk, logical) && left)
                {
                    await LoginUiTrySelectServerAsync().ConfigureAwait(true);
                    return true;
                }

                if (RectContains(listRect, logical))
                {
                    int rowH = 22;
                    int visible = Math.Max(1, listRect.Height / rowH);
                    int start = Math.Clamp(_uiServerSelectedIndex - (visible / 2), 0, Math.Max(0, _uiServers.Count - visible));

                    int row = (int)((logical.Y - listRect.Y) / rowH);
                    int idx = start + row;
                    if (idx >= 0 && idx < _uiServers.Count)
                        _uiServerSelectedIndex = idx;

                    return true;
                }

                return true;
            }
            case LoginUiScreen.SelectCharacter:
            {
                int rowH = 28;
                DrawingRectangle listRect = new(x, y, innerW, (rowH * 2) + gap);

                int btnW = 120;
                int yBtn2 = panel.Bottom - pad - btnH;
                int yBtn1 = yBtn2 - btnH - gap;
                int rowX = panel.Right - pad - ((btnW * 3) + (gap * 2));

                DrawingRectangle btnCreate = new(rowX, yBtn1, btnW, btnH);
                DrawingRectangle btnDelete = new(rowX + btnW + gap, yBtn1, btnW, btnH);
                DrawingRectangle btnRestore = new(rowX + (btnW + gap) * 2, yBtn1, btnW, btnH);
                DrawingRectangle btnBack = new(rowX, yBtn2, btnW, btnH);
                DrawingRectangle btnEnter = new(rowX + btnW + gap, yBtn2, btnW, btnH);

                if (RectContains(btnBack, logical) && left)
                {
                    _ = DisconnectAsync(DisconnectBehavior.PromptLogin);
                    return true;
                }

                if (RectContains(btnEnter, logical) && left)
                {
                    await LoginUiTryEnterGameAsync().ConfigureAwait(true);
                    return true;
                }

                if (RectContains(btnCreate, logical) && left)
                {
                    if (_uiCharacters.Count >= 2)
                    {
                        ShowModal("Create Character", "No empty slot available.", UiModalButtons.Ok, _ => { });
                        return true;
                    }

                    _uiNewCharName.Clear();
                    _uiNewCharJob = 0;
                    _uiNewCharSex = 0;
                    _uiNewCharHair = 0;
                    ShowLoginUi(LoginUiScreen.CreateCharacter);
                    return true;
                }

                if (RectContains(btnRestore, logical) && left)
                {
                    await LoginUiOpenDeletedListAsync().ConfigureAwait(true);
                    return true;
                }

                if (RectContains(btnDelete, logical) && left)
                {
                    string? name = GetSelectedCharacterNameForUi();
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        ShowModal("Delete", "Select a character first.", UiModalButtons.Ok, _ => { });
                        return true;
                    }

                    ShowModal(
                        "Confirm Delete",
                        $"Delete character '{name}'?",
                        UiModalButtons.YesNo,
                        result =>
                        {
                            if (result == UiModalResult.Yes)
                                BeginInvoke(async () => { try { await LoginUiDeleteSelectedCharacterAsync().ConfigureAwait(true); } catch {  } });
                        });
                    return true;
                }

                if (RectContains(listRect, logical))
                {
                    DrawingRectangle r0 = new(listRect.X, listRect.Y, listRect.Width, rowH);
                    DrawingRectangle r1 = new(listRect.X, listRect.Y + rowH + gap, listRect.Width, rowH);
                    if (RectContains(r0, logical))
                        _uiCharacterSelectedIndex = 0;
                    else if (RectContains(r1, logical))
                        _uiCharacterSelectedIndex = 1;

                    if (_uiCharacterSelectedIndex >= 0 && _uiCharacterSelectedIndex < _uiCharacters.Count)
                    {
                        string selectedName = _uiCharacters[_uiCharacterSelectedIndex].Name;
                        if (!string.IsNullOrWhiteSpace(selectedName))
                        {
                            _txtCharacter.Text = selectedName.Trim();
                            _selectCharacterScene.SelectCharacter(selectedName);
                        }
                    }

                    return true;
                }

                return true;
            }
            case LoginUiScreen.RestoreDeletedCharacter:
            {
                int listH = panel.Height - 44 - pad - btnH - gap;
                DrawingRectangle listRect = new(x, y, innerW, Math.Max(120, listH));
                int btnW = 120;
                int yBtn = panel.Bottom - pad - btnH;
                DrawingRectangle btnBack = new(panel.X + pad, yBtn, btnW, btnH);
                DrawingRectangle btnRestore = new(panel.Right - pad - btnW, yBtn, btnW, btnH);

                if (RectContains(btnBack, logical) && left)
                {
                    ShowLoginUi(LoginUiScreen.SelectCharacter);
                    return true;
                }

                if (RectContains(btnRestore, logical) && left)
                {
                    await LoginUiTryRestoreDeletedCharacterAsync().ConfigureAwait(true);
                    return true;
                }

                if (RectContains(listRect, logical))
                {
                    int rowH = 22;
                    int visible = Math.Max(1, listRect.Height / rowH);
                    int start = Math.Clamp(_uiDeletedSelectedIndex - (visible / 2), 0, Math.Max(0, _uiDeletedCharacters.Count - visible));

                    int row = (int)((logical.Y - listRect.Y) / rowH);
                    int idx = start + row;
                    if (idx >= 0 && idx < _uiDeletedCharacters.Count)
                        _uiDeletedSelectedIndex = idx;

                    return true;
                }

                return true;
            }
            default:
            {
                UiTextInput[] fields = GetFocusableFieldsForScreen(_loginUiScreen);
                int yy = y;
                for (int i = 0; i < fields.Length; i++)
                {
                    DrawingRectangle r = new(x, yy, innerW, fieldH);
                    if (RectContains(r, logical))
                    {
                        FocusField(fields[i]);
                        return true;
                    }
                    yy += fieldH + gap;
                }

                int btnW = 120;
                int yBtn = panel.Bottom - pad - btnH;
                DrawingRectangle btnBack = new(panel.X + pad, yBtn, btnW, btnH);
                DrawingRectangle btnOk = new(panel.Right - pad - btnW, yBtn, btnW, btnH);

                if (RectContains(btnBack, logical) && left)
                {
                    ShowLoginUi(_loginUiScreen == LoginUiScreen.CreateCharacter ? LoginUiScreen.SelectCharacter : LoginUiScreen.Login);
                    return true;
                }

                if (RectContains(btnOk, logical) && left)
                {
                    BeginLoginUiPrimaryAction();
                    return true;
                }

                return true;
            }
        }
    }

    private void HandleLoginUiModalClick(DrawingRectangle panel, Vector2 logical, bool left)
    {
        if (!left || _loginUiModal == null)
            return;

        UiModal modal = _loginUiModal;
        UiModalLayout layout = modal.Layout;
        if (layout != UiModalLayout.Default && modal.Buttons != UiModalButtons.Ok)
            layout = UiModalLayout.Default;

        if (layout == UiModalLayout.Classic2)
        {
            DrawingRectangle rect;
            if (_loginUiModalRect is { } last && last.Width > 0 && last.Height > 0)
            {
                rect = last;
            }
            else
            {
                const int fallbackW = 256;
                const int fallbackH = 359;
                int x0 = panel.X + (panel.Width - fallbackW) / 2;
                int y0 = panel.Y + (panel.Height - fallbackH) / 2;
                rect = new DrawingRectangle(x0, y0, fallbackW, fallbackH);
            }

            DrawingRectangle okRect = new(rect.Left + 90, rect.Top + 305, width: 92, height: 28);
            if (RectContains(okRect, logical))
                ResolveModalDefault(cancel: false);
            return;
        }

        const int pad = 14;
        const int btnH = 28;
        const int gap = 10;

        int w = Math.Clamp(panel.Width - 80, 360, 640);
        int h = 200;
        int x = panel.X + (panel.Width - w) / 2;
        int y = panel.Y + (panel.Height - h) / 2;

        int btnW = 120;
        int yBtn = y + h - pad - btnH;
        int xBtnRight = x + w - pad - btnW;

        DrawingRectangle rightRect = new(xBtnRight, yBtn, btnW, btnH);
        if (RectContains(rightRect, logical))
        {
            ResolveModalDefault(cancel: false);
            return;
        }

        if (modal.Buttons is UiModalButtons.OkCancel or UiModalButtons.YesNo)
        {
            int xBtnLeft = xBtnRight - (btnW + gap);
            DrawingRectangle leftRect = new(xBtnLeft, yBtn, btnW, btnH);
            if (RectContains(leftRect, logical))
                ResolveModalDefault(cancel: true);
        }
    }

    private void HandleTextPromptClick(DrawingRectangle panel, Vector2 logical, bool left)
    {
        if (!left || _uiTextPrompt == null)
            return;

        bool hasAbort = _uiTextPrompt.Buttons == UiTextPromptButtons.OkCancelAbort;
        GetTextPromptLayout(panel, hasAbort, out DrawingRectangle rect, out _, out DrawingRectangle inputRect, out DrawingRectangle btnOk, out DrawingRectangle btnCancel, out DrawingRectangle btnAbort);

        if (!RectContains(rect, logical))
            return;

        if (RectContains(inputRect, logical))
        {
            FocusField(_uiTextPrompt.Input);
            return;
        }

        if (RectContains(btnOk, logical))
        {
            ResolveTextPrompt(UiTextPromptResult.Ok);
            return;
        }

        if (RectContains(btnCancel, logical))
        {
            ResolveTextPrompt(UiTextPromptResult.Cancel);
            return;
        }

        if (hasAbort && RectContains(btnAbort, logical))
            ResolveTextPrompt(UiTextPromptResult.Abort);
    }

    private string? GetSelectedCharacterNameForUi()
    {
        if (_uiCharacters.Count == 0)
            return null;

        int idx = Math.Clamp(_uiCharacterSelectedIndex, 0, _uiCharacters.Count - 1);
        string name = _uiCharacters[idx].Name;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    private async Task LoginUiOpenDeletedListAsync()
    {
        if (_loginFlowInProgress)
            return;

        CancellationToken token = _loginCts?.Token ?? CancellationToken.None;
        try
        {
            MirDeletedCharacterListResult list = await _session.QueryDeletedCharactersAsync(token).ConfigureAwait(true);
            if (list.Characters.Count == 0)
            {
                ShowModal("Restore", "No deleted characters found.", UiModalButtons.Ok, _ => { });
                return;
            }

            _uiDeletedCharacters = list.Characters;
            _uiDeletedSelectedIndex = 0;
            ShowLoginUi(LoginUiScreen.RestoreDeletedCharacter);
        }
        catch (Exception ex)
        {
            ShowModal("Restore Failed", ex.Message, UiModalButtons.Ok, _ => { });
        }
    }

    private async Task LoginUiDeleteSelectedCharacterAsync()
    {
        if (_loginFlowInProgress)
            return;

        string? name = GetSelectedCharacterNameForUi();
        if (string.IsNullOrWhiteSpace(name))
            return;

        if (string.IsNullOrWhiteSpace(_loginAccount) || _loginCertification == 0)
        {
            ShowModal("Delete", "Login context missing. Return to login.", UiModalButtons.Ok, __ => { _ = DisconnectAsync(DisconnectBehavior.PromptLogin); });
            return;
        }

        CancellationToken token = _loginCts?.Token ?? CancellationToken.None;
        try
        {
            MirCharacterListResult deleted = await _session.DeleteCharacterAsync(_loginAccount, _loginCertification, name, token).ConfigureAwait(true);
            ApplyCharacterListToUi(deleted.Characters);
            LoginUiSetCharacterList(deleted.Characters);
            _txtCharacter.Clear();
            ShowLoginUi(LoginUiScreen.SelectCharacter);
        }
        catch (Exception ex)
        {
            ShowModal("Delete Failed", ex.Message, UiModalButtons.Ok, _ => { });
        }
    }

    
}
