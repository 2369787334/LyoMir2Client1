using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using System.Numerics;
using MirClient.Assets.PackData;
using MirClient.Assets.Wil;
using MirClient.Core;
using MirClient.Rendering.D3D11;
using Vortice.Mathematics;

namespace MirClient;

internal partial class Main
{
    private enum LoginUiClassicClickKind
    {
        None = 0,

        FocusAccount = 1,
        FocusPassword = 2,
        LoginOk = 3,
        LoginNewAccount = 4,
        LoginChangePassword = 5,
        LoginClose = 6,

        ChangePwdFocusField = 50, 
        ChangePwdOk = 51,
        ChangePwdCancel = 52,

        NewAccountFocusField = 60, 
        NewAccountOk = 61,
        NewAccountCancel = 62,
        NewAccountClose = 63,

        ServerClose = 10,
        ServerSelect = 11, 

        CharSelect0 = 20,
        CharSelect1 = 21,
        CharStart = 22,
        CharNew = 23,
        CharErase = 24,
        CharRestoreDeleted = 25,
        CharExit = 26,

        RestoreClose = 30,
        RestoreConfirm = 31,
        RestoreSelectRow = 32, 

        CreateFocusName = 40,
        CreateClose = 41,
        CreateOk = 42,
        CreateJob = 43, 
        CreateSex = 44, 
        CreateHairPrev = 45,
        CreateHairNext = 46
    }

    private readonly struct LoginUiClassicClickPoint(LoginUiClassicClickKind kind, DrawingRectangle rect, int index = 0)
    {
        public LoginUiClassicClickKind Kind { get; } = kind;
        public DrawingRectangle Rect { get; } = rect;
        public int Index { get; } = index;
    }

    private const int DoorFrameMs = 28;
    private const int DoorFrames = 10;
    private const int DoorBaseIndex = 23; 

    private const int FreezeFrameCount = 13;   
    private const int SelectedFrameCount = 16; 

    private const int PressFlashMs = 120;

    private readonly List<LoginUiClassicClickPoint> _loginUiClassicClickPoints = new(96);
    private LoginUiClassicClickKind _loginUiClassicPressedKind;
    private int _loginUiClassicPressedIndex;
    private long _loginUiClassicPressedMs;

    private static bool IsClassicLoginUiScreen(LoginUiScreen screen) =>
        screen is LoginUiScreen.Login or
        LoginUiScreen.ChangePassword or
        LoginUiScreen.Register or
        LoginUiScreen.UpdateAccount or
        LoginUiScreen.SelectServer or
        LoginUiScreen.OpeningDoor or
        LoginUiScreen.SelectCharacter or
        LoginUiScreen.CreateCharacter or
        LoginUiScreen.RestoreDeletedCharacter;

    private static (int X, int Y) GetClassicUiBaseOrigin(DrawingSize logicalSize)
    {
        const int baseWidth = 800;
        const int baseHeight = 600;
        int x = (logicalSize.Width - baseWidth) / 2;
        int y = (logicalSize.Height - baseHeight) / 2;
        return (x, y);
    }

    private void SetClassicPressed(LoginUiClassicClickKind kind, int index = 0)
    {
        _loginUiClassicPressedKind = kind;
        _loginUiClassicPressedIndex = index;
        _loginUiClassicPressedMs = Environment.TickCount64;
    }

    private bool IsClassicPressed(LoginUiClassicClickKind kind, int index = 0)
    {
        if (_loginUiClassicPressedKind != kind)
            return false;
        if (_loginUiClassicPressedIndex != index)
            return false;

        long age = Environment.TickCount64 - _loginUiClassicPressedMs;
        if (age < 0)
            age = 0;

        return age <= PressFlashMs;
    }

    private bool TryGetLoginUiTexture(D3D11Frame frame, string archivePath, int imageIndex, out D3D11Texture2D texture)
    {
        texture = null!;

        if (_wilTextureCache == null || _dataTextureCache == null)
            return false;

        static bool IsWilLike(string path) =>
            path.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".wis", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".wix", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".wzl", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".wzx", StringComparison.OrdinalIgnoreCase);

        if (IsWilLike(archivePath))
        {
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

            _ = _wilImageCache.GetImageAsyncFullPath(key.WilPath, key.ImageIndex);
            return false;
        }

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

        _ = _packDataImageCache.GetImageAsyncFullPath(dataKey.DataPath, dataKey.ImageIndex);
        return false;
    }

    private bool TryDrawLoginUiClassic(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!LoginUiVisible)
            return false;

        if (_uiTextPrompt != null)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        if (!IsClassicLoginUiScreen(_loginUiScreen))
            return false;

        if (!TryEnsureLoginUiArchives())
            return false;

        string? chrSelPath = _loginUiChrSelPath;
        string? prgusePath = _loginUiPrgusePath;
        string? prguse3Path = _loginUiPrguse3Path;
        if (chrSelPath == null || prgusePath == null || prguse3Path == null)
            return false;

        int w = view.LogicalSize.Width;
        int h = view.LogicalSize.Height;
        if (w <= 0 || h <= 0)
            return false;

        (int baseX, int baseY) = GetClassicUiBaseOrigin(view.LogicalSize);
        DrawingRectangle full = new(0, 0, w, h);

        long nowMs = Environment.TickCount64;
        if (_loginUiClassicPressedKind != LoginUiClassicClickKind.None &&
            !IsClassicPressed(_loginUiClassicPressedKind, _loginUiClassicPressedIndex))
        {
            _loginUiClassicPressedKind = LoginUiClassicClickKind.None;
        }

        _loginUiClassicClickPoints.Clear();

        bool switchToSelectCharacter = false;

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, full, color: new Color4(0, 0, 0, 1f));

        switch (_loginUiScreen)
        {
            case LoginUiScreen.Login:
                DrawLoginBackground(frame, chrSelPath, baseX, baseY);
                DrawLoginDialog(frame, view, prgusePath, prguse3Path);
                break;
            case LoginUiScreen.ChangePassword:
                DrawLoginBackground(frame, chrSelPath, baseX, baseY);
                DrawChangePasswordDialog(frame, view, prgusePath);
                break;
            case LoginUiScreen.Register:
            case LoginUiScreen.UpdateAccount:
                DrawLoginBackground(frame, chrSelPath, baseX, baseY);
                DrawNewAccountDialog(frame, view, prgusePath);
                break;
            case LoginUiScreen.SelectServer:
                DrawLoginBackground(frame, chrSelPath, baseX, baseY);
                DrawSelectServerDialog(frame, view, prgusePath, prguse3Path);
                break;
            case LoginUiScreen.OpeningDoor:
            {
                DrawLoginBackground(frame, chrSelPath, baseX, baseY);

                if (!_loginUiDoorPlayedSfx)
                {
                    _loginUiDoorPlayedSfx = true;
                    _soundManager.PlaySfxById(soundId: 100, volume: 0.8f);
                }

                int frameIndex = (int)((nowMs - _loginUiDoorStartMs) / DoorFrameMs);
                if (frameIndex < 0)
                    frameIndex = 0;
                if (frameIndex > DoorFrames - 1)
                    frameIndex = DoorFrames - 1;

                int doorIndex = DoorBaseIndex + frameIndex;
                if (TryGetLoginUiTexture(frame, chrSelPath, doorIndex, out D3D11Texture2D door))
                    _spriteBatch.Draw(door, new DrawingRectangle(baseX + 152, baseY + 96, door.Width, door.Height));

                if (_loginUiDoorCharactersReady && IsDoorAnimationComplete(nowMs, _loginUiDoorStartMs))
                    switchToSelectCharacter = true;

                break;
            }
            case LoginUiScreen.SelectCharacter:
            case LoginUiScreen.CreateCharacter:
            case LoginUiScreen.RestoreDeletedCharacter:
                DrawSelectCharacterScene(frame, view, baseX, baseY, nowMs);
                break;
        }

        if (_loginUiModal != null)
            DrawLoginUiModal(frame, full);

        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        if (switchToSelectCharacter && _loginUiScreen == LoginUiScreen.OpeningDoor)
            ShowLoginUi(LoginUiScreen.SelectCharacter);

        return true;
    }

    private void DrawLoginBackground(D3D11Frame frame, string chrSelPath, int baseX, int baseY)
    {
        if (_spriteBatch == null)
            return;

        if (TryGetLoginUiTexture(frame, chrSelPath, imageIndex: 22, out D3D11Texture2D bg))
            _spriteBatch.Draw(bg, new DrawingRectangle(baseX, baseY, bg.Width, bg.Height));
    }

    private void AddClassicClickWithTexture(D3D11Frame frame, string archivePath, int imageIndex, int x, int y, LoginUiClassicClickKind kind, int index = 0)
    {
        if (!TryGetLoginUiTexture(frame, archivePath, imageIndex, out D3D11Texture2D tex))
            return;

        _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(kind, new DrawingRectangle(x, y, tex.Width, tex.Height), index));
    }

    private void DrawClassicOverlay(D3D11Frame frame, string archivePath, int imageIndex, int x, int y)
    {
        if (_spriteBatch == null)
            return;

        if (!TryGetLoginUiTexture(frame, archivePath, imageIndex, out D3D11Texture2D tex))
            return;

        _spriteBatch.Draw(tex, new DrawingRectangle(x, y, tex.Width, tex.Height));
    }

    
    private void DrawLoginDialog(D3D11Frame frame, D3D11ViewTransform view, string prgusePath, string prguse3Path)
    {
        if (_spriteBatch == null)
            return;

        if (!TryGetLoginUiTexture(frame, prguse3Path, imageIndex: 18, out D3D11Texture2D loginDlg))
            return;

        int w = view.LogicalSize.Width;
        int h = view.LogicalSize.Height;

        int dlgX = (w - loginDlg.Width) / 2;
        int dlgY = (h - loginDlg.Height) / 2;
        _spriteBatch.Draw(loginDlg, new DrawingRectangle(dlgX, dlgY, loginDlg.Width, loginDlg.Height));

        DrawingRectangle rAcc = new(dlgX + 125, dlgY + 75, 136, 16);
        DrawingRectangle rPwd = new(dlgX + 125, dlgY + 107, 136, 16);
        _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.FocusAccount, rAcc));
        _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.FocusPassword, rPwd));

        AddClassicClickWithTexture(frame, prgusePath, 61, dlgX + 32, dlgY + 172, LoginUiClassicClickKind.LoginNewAccount);
        AddClassicClickWithTexture(frame, prguse3Path, 10, dlgX + 164, dlgY + 172, LoginUiClassicClickKind.LoginOk);
        AddClassicClickWithTexture(frame, prguse3Path, 28, dlgX + 164, dlgY + 215, LoginUiClassicClickKind.LoginChangePassword);
        AddClassicClickWithTexture(frame, prgusePath, 64, dlgX + 258, dlgY + 24, LoginUiClassicClickKind.LoginClose);

        if (IsClassicPressed(LoginUiClassicClickKind.LoginNewAccount))
            DrawClassicOverlay(frame, prgusePath, 61, dlgX + 32, dlgY + 172);
        if (IsClassicPressed(LoginUiClassicClickKind.LoginOk))
            DrawClassicOverlay(frame, prguse3Path, 10, dlgX + 164, dlgY + 172);
        if (IsClassicPressed(LoginUiClassicClickKind.LoginChangePassword))
            DrawClassicOverlay(frame, prguse3Path, 28, dlgX + 164, dlgY + 215);
        if (IsClassicPressed(LoginUiClassicClickKind.LoginClose))
            DrawClassicOverlay(frame, prgusePath, 64, dlgX + 258, dlgY + 24);
    }

    private void DrawChangePasswordDialog(D3D11Frame frame, D3D11ViewTransform view, string prgusePath)
    {
        if (_spriteBatch == null || _whiteTexture == null)
            return;

        
        if (!TryGetLoginUiTexture(frame, prgusePath, imageIndex: 50, out D3D11Texture2D dlg))
            return;

        int w = view.LogicalSize.Width;
        int h = view.LogicalSize.Height;
        int dlgX = (w - dlg.Width) / 2;
        int dlgY = (h - dlg.Height) / 2;
        _spriteBatch.Draw(dlg, new DrawingRectangle(dlgX, dlgY, dlg.Width, dlg.Height));

        DrawingRectangle rAcc = new(dlgX + 239, dlgY + 117, 137, 16);
        DrawingRectangle rOld = new(dlgX + 239, dlgY + 149, 137, 16);
        DrawingRectangle rNew = new(dlgX + 239, dlgY + 176, 137, 16);
        DrawingRectangle rRep = new(dlgX + 239, dlgY + 208, 137, 16);

        _spriteBatch.Draw(_whiteTexture, rAcc, color: new Color4(0, 0, 0, 1f));
        _spriteBatch.Draw(_whiteTexture, rOld, color: new Color4(0, 0, 0, 1f));
        _spriteBatch.Draw(_whiteTexture, rNew, color: new Color4(0, 0, 0, 1f));
        _spriteBatch.Draw(_whiteTexture, rRep, color: new Color4(0, 0, 0, 1f));

        _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.ChangePwdFocusField, rAcc, index: 0));
        _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.ChangePwdFocusField, rOld, index: 1));
        _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.ChangePwdFocusField, rNew, index: 2));
        _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.ChangePwdFocusField, rRep, index: 3));

        AddClassicClickWithTexture(frame, prgusePath, 81, dlgX + 181, dlgY + 253, LoginUiClassicClickKind.ChangePwdOk);
        AddClassicClickWithTexture(frame, prgusePath, 52, dlgX + 276, dlgY + 252, LoginUiClassicClickKind.ChangePwdCancel);

        if (IsClassicPressed(LoginUiClassicClickKind.ChangePwdOk))
            DrawClassicOverlay(frame, prgusePath, 81, dlgX + 181, dlgY + 253);
        if (IsClassicPressed(LoginUiClassicClickKind.ChangePwdCancel))
            DrawClassicOverlay(frame, prgusePath, 52, dlgX + 276, dlgY + 252);
    }

    private void DrawNewAccountDialog(D3D11Frame frame, D3D11ViewTransform view, string prgusePath)
    {
        if (_spriteBatch == null || _whiteTexture == null)
            return;

        
        if (!TryGetLoginUiTexture(frame, prgusePath, imageIndex: 63, out D3D11Texture2D dlg))
            return;

        int w = view.LogicalSize.Width;
        int h = view.LogicalSize.Height;
        int dlgX = (w - dlg.Width) / 2;
        int dlgY = (h - dlg.Height) / 2;
        _spriteBatch.Draw(dlg, new DrawingRectangle(dlgX, dlgY, dlg.Width, dlg.Height));

        DrawingRectangle[] fields =
        [
            new DrawingRectangle(dlgX + 161, dlgY + 116, 116, 16), 
            new DrawingRectangle(dlgX + 161, dlgY + 137, 116, 16), 
            new DrawingRectangle(dlgX + 161, dlgY + 158, 116, 16), 
            new DrawingRectangle(dlgX + 161, dlgY + 187, 116, 16), 
            new DrawingRectangle(dlgX + 161, dlgY + 207, 116, 16), 
            new DrawingRectangle(dlgX + 161, dlgY + 227, 116, 16), 
            new DrawingRectangle(dlgX + 161, dlgY + 256, 163, 16), 
            new DrawingRectangle(dlgX + 161, dlgY + 276, 163, 16), 
            new DrawingRectangle(dlgX + 161, dlgY + 297, 163, 16), 
            new DrawingRectangle(dlgX + 161, dlgY + 317, 163, 16), 
            new DrawingRectangle(dlgX + 161, dlgY + 347, 116, 16), 
            new DrawingRectangle(dlgX + 161, dlgY + 368, 116, 16), 
            new DrawingRectangle(dlgX + 161, dlgY + 388, 116, 16)  
        ];

        for (int i = 0; i < fields.Length; i++)
        {
            _spriteBatch.Draw(_whiteTexture, fields[i], color: new Color4(0, 0, 0, 1f));
            _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.NewAccountFocusField, fields[i], index: i));
        }

        AddClassicClickWithTexture(frame, prgusePath, 62, dlgX + 158, dlgY + 416, LoginUiClassicClickKind.NewAccountOk);
        AddClassicClickWithTexture(frame, prgusePath, 52, dlgX + 446, dlgY + 419, LoginUiClassicClickKind.NewAccountCancel);
        AddClassicClickWithTexture(frame, prgusePath, 64, dlgX + 587, dlgY + 33, LoginUiClassicClickKind.NewAccountClose);

        if (IsClassicPressed(LoginUiClassicClickKind.NewAccountOk))
            DrawClassicOverlay(frame, prgusePath, 62, dlgX + 158, dlgY + 416);
        if (IsClassicPressed(LoginUiClassicClickKind.NewAccountCancel))
            DrawClassicOverlay(frame, prgusePath, 52, dlgX + 446, dlgY + 419);
        if (IsClassicPressed(LoginUiClassicClickKind.NewAccountClose))
            DrawClassicOverlay(frame, prgusePath, 64, dlgX + 587, dlgY + 33);
    }
    private void DrawSelectServerDialog(D3D11Frame frame, D3D11ViewTransform view, string prgusePath, string prguse3Path)
    {
        if (_spriteBatch == null)
            return;

        if (!TryGetLoginUiTexture(frame, prgusePath, imageIndex: 256, out D3D11Texture2D dlg))
            return;

        int w = view.LogicalSize.Width;
        int h = view.LogicalSize.Height;

        int dlgX = (w - dlg.Width) / 2;
        int dlgY = (h - dlg.Height) / 2;
        _spriteBatch.Draw(dlg, new DrawingRectangle(dlgX, dlgY, dlg.Width, dlg.Height));

        
        if (TryGetLoginUiTexture(frame, prgusePath, imageIndex: 64, out D3D11Texture2D close))
        {
            int cx = dlgX + 245;
            int cy = dlgY + 31;
            _spriteBatch.Draw(close, new DrawingRectangle(cx, cy, close.Width, close.Height));
            _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.ServerClose, new DrawingRectangle(cx, cy, close.Width, close.Height)));
            if (IsClassicPressed(LoginUiClassicClickKind.ServerClose))
                DrawClassicOverlay(frame, prgusePath, 64, cx, cy);
        }

        int visible = Math.Min(6, _uiServers.Count);
        int[] tops = [100, 145, 190, 235, 280, 325];
        if (_uiServers.Count == 1)
        {
            visible = 1;
            tops[0] = 204;
        }
        else if (_uiServers.Count == 2)
        {
            visible = 2;
            tops[0] = 190;
            tops[1] = 235;
        }

        for (int i = 0; i < visible; i++)
        {
            bool pressed = IsClassicPressed(LoginUiClassicClickKind.ServerSelect, i);
            int faceIndex = pressed ? 3 : 2;

            int xBtn = dlgX + 65;
            int yBtn = dlgY + tops[i];
            if (TryGetLoginUiTexture(frame, prguse3Path, faceIndex, out D3D11Texture2D btn))
            {
                _spriteBatch.Draw(btn, new DrawingRectangle(xBtn, yBtn, btn.Width, btn.Height));
                _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.ServerSelect, new DrawingRectangle(xBtn, yBtn, btn.Width, btn.Height), index: i));
            }
        }
    }
    private void DrawSelectCharacterScene(D3D11Frame frame, D3D11ViewTransform view, int baseX, int baseY, long nowMs)
    {
        if (_spriteBatch == null)
            return;

        string? prgusePath = _loginUiPrgusePath;
        string? prguse3Path = _loginUiPrguse3Path;
        string? chrSelPath = _loginUiChrSelPath;
        if (prgusePath == null || prguse3Path == null || chrSelPath == null)
            return;

        if (TryGetLoginUiTexture(frame, prguse3Path, imageIndex: 400, out D3D11Texture2D bg))
        {
            int x = (view.LogicalSize.Width - bg.Width) / 2;
            int y = (view.LogicalSize.Height - bg.Height) / 2;
            _spriteBatch.Draw(bg, new DrawingRectangle(x, y, bg.Width, bg.Height));
        }

        DrawCharacterSlots(frame, chrSelPath, baseX, baseY, nowMs);

        
        AddClassicClickWithTexture(frame, prgusePath, 66, baseX + 134, baseY + 454, LoginUiClassicClickKind.CharSelect0);
        AddClassicClickWithTexture(frame, prgusePath, 67, baseX + 685, baseY + 454, LoginUiClassicClickKind.CharSelect1);
        if (IsClassicPressed(LoginUiClassicClickKind.CharSelect0))
            DrawClassicOverlay(frame, prgusePath, 66, baseX + 134, baseY + 454);
        if (IsClassicPressed(LoginUiClassicClickKind.CharSelect1))
            DrawClassicOverlay(frame, prgusePath, 67, baseX + 685, baseY + 454);

        
        
        
        bool hasStartButton = false;
        DrawingRectangle startRect = default;

        if (_loginUiOpUiPath != null && TryGetLoginUiTexture(frame, _loginUiOpUiPath, imageIndex: 60, out D3D11Texture2D start))
        {
            int sx = (view.LogicalSize.Width - 68) / 2 + 5;
            int sy = baseY + 455;
            startRect = new DrawingRectangle(sx, sy, start.Width, start.Height);
            _spriteBatch.Draw(start, startRect);
            hasStartButton = true;
        }
        else if (TryGetLoginUiTexture(frame, prgusePath, imageIndex: 68, out D3D11Texture2D startLegacy))
        {
            int sx = (view.LogicalSize.Width - 68) / 2 + 19;
            int sy = baseY + 456;
            startRect = new DrawingRectangle(sx, sy, startLegacy.Width, startLegacy.Height);
            _spriteBatch.Draw(startLegacy, startRect);
            hasStartButton = true;
        }

        if (hasStartButton)
            _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.CharStart, startRect));

        
        DrawClassicButton(frame, prgusePath, 69, baseX + 348, baseY + 486, LoginUiClassicClickKind.CharNew);
        DrawClassicButton(frame, prgusePath, 70, baseX + 347, baseY + 506, LoginUiClassicClickKind.CharErase);
        DrawClassicButton(frame, prgusePath, 405, baseX + 346, baseY + 527, LoginUiClassicClickKind.CharRestoreDeleted);
        DrawClassicButton(frame, prgusePath, 72, baseX + 379, baseY + 547, LoginUiClassicClickKind.CharExit);

        if (_loginUiScreen == LoginUiScreen.CreateCharacter)
            DrawCreateCharacterDialog(frame, prgusePath, baseX, baseY);

        if (_loginUiScreen == LoginUiScreen.RestoreDeletedCharacter)
            DrawRestoreDeletedDialog(frame, baseX, baseY);
    }

    private void DrawClassicButton(D3D11Frame frame, string archivePath, int imageIndex, int x, int y, LoginUiClassicClickKind kind)
    {
        if (_spriteBatch == null)
            return;

        int drawIndex = imageIndex;
        if (IsClassicPressed(kind) && TryGetLoginUiTexture(frame, archivePath, imageIndex + 1, out _))
            drawIndex = imageIndex + 1;

        if (!TryGetLoginUiTexture(frame, archivePath, drawIndex, out D3D11Texture2D tex))
            return;

        _spriteBatch.Draw(tex, new DrawingRectangle(x, y, tex.Width, tex.Height));
        _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(kind, new DrawingRectangle(x, y, tex.Width, tex.Height)));
    }

    private void DrawCreateCharacterDialog(D3D11Frame frame, string prgusePath, int baseX, int baseY)
    {
        if (_spriteBatch == null)
            return;

        if (!TryGetLoginUiTexture(frame, prgusePath, imageIndex: 73, out D3D11Texture2D dlg))
            return;

        int dlgX = _loginUiCreateSlotIndex == 0 ? baseX + 415 : baseX + 75;
        int dlgY = baseY + 15;
        _spriteBatch.Draw(dlg, new DrawingRectangle(dlgX, dlgY, dlg.Width, dlg.Height));

        DrawingRectangle name = new(dlgX + 73, dlgY + 109, 135, 15);
        _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.CreateFocusName, name));

        AddClassicClickWithTexture(frame, prgusePath, 64, dlgX + 248, dlgY + 31, LoginUiClassicClickKind.CreateClose);
        AddClassicClickWithTexture(frame, prgusePath, 62, dlgX + 103, dlgY + 360, LoginUiClassicClickKind.CreateOk);

        AddClassicClickWithTexture(frame, prgusePath, 74, dlgX + 48, dlgY + 157, LoginUiClassicClickKind.CreateJob, index: 0);
        AddClassicClickWithTexture(frame, prgusePath, 75, dlgX + 93, dlgY + 157, LoginUiClassicClickKind.CreateJob, index: 1);
        AddClassicClickWithTexture(frame, prgusePath, 76, dlgX + 138, dlgY + 157, LoginUiClassicClickKind.CreateJob, index: 2);

        AddClassicClickWithTexture(frame, prgusePath, 77, dlgX + 93, dlgY + 231, LoginUiClassicClickKind.CreateSex, index: 0);
        AddClassicClickWithTexture(frame, prgusePath, 78, dlgX + 138, dlgY + 231, LoginUiClassicClickKind.CreateSex, index: 1);

        AddClassicClickWithTexture(frame, prgusePath, 79, dlgX + 76, dlgY + 308, LoginUiClassicClickKind.CreateHairPrev);
        AddClassicClickWithTexture(frame, prgusePath, 80, dlgX + 170, dlgY + 308, LoginUiClassicClickKind.CreateHairNext);

        int jobSel = _uiNewCharJob;
        int sexSel = _uiNewCharSex;

        int jobHighlightIndex = jobSel switch { 0 => 55, 1 => 56, 2 => 57, _ => 55 };
        int sexHighlightIndex = sexSel switch { 0 => 58, 1 => 59, _ => 58 };

        
        int jobX = dlgX + (jobSel switch { 0 => 48, 1 => 93, _ => 138 });
        int sexX = dlgX + (sexSel == 0 ? 93 : 138);
        DrawClassicOverlay(frame, prgusePath, jobHighlightIndex, jobX, dlgY + 157);
        DrawClassicOverlay(frame, prgusePath, sexHighlightIndex, sexX, dlgY + 231);

        
        if (IsClassicPressed(LoginUiClassicClickKind.CreateClose))
            DrawClassicOverlay(frame, prgusePath, 64, dlgX + 248, dlgY + 31);
        if (IsClassicPressed(LoginUiClassicClickKind.CreateOk))
            DrawClassicOverlay(frame, prgusePath, 62, dlgX + 103, dlgY + 360);
    }
    private void DrawRestoreDeletedDialog(D3D11Frame frame, int baseX, int baseY)
    {
        if (_spriteBatch == null)
            return;

        string? prgusePath = _loginUiPrgusePath;
        string? prguse3Path = _loginUiPrguse3Path;
        if (prgusePath == null || prguse3Path == null)
            return;

        if (!TryGetLoginUiTexture(frame, prguse3Path, imageIndex: 406, out D3D11Texture2D dlg))
            return;

        int dlgX = baseX + 120;
        int dlgY = baseY + 88;
        _spriteBatch.Draw(dlg, new DrawingRectangle(dlgX, dlgY, dlg.Width, dlg.Height));

        
        if (TryGetLoginUiTexture(frame, prgusePath, imageIndex: 64, out D3D11Texture2D close))
        {
            int cx = dlgX + 247;
            int cy = dlgY + 30;
            _spriteBatch.Draw(close, new DrawingRectangle(cx, cy, close.Width, close.Height));
            _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.RestoreClose, new DrawingRectangle(cx, cy, close.Width, close.Height)));
        }

        
        if (TryGetLoginUiTexture(frame, prguse3Path, imageIndex: 407, out D3D11Texture2D restore))
        {
            int rx = dlgX + (300 / 2) - 50;
            int ry = dlgY + 417 - 55;
            _spriteBatch.Draw(restore, new DrawingRectangle(rx, ry, restore.Width, restore.Height));
            _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.RestoreConfirm, new DrawingRectangle(rx, ry, restore.Width, restore.Height)));
        }

        
        int visible = Math.Min(20, _uiDeletedCharacters.Count);
        for (int i = 0; i < visible; i++)
        {
            DrawingRectangle row = new(dlgX + 35, dlgY + 125 + (i * 13), 218 - 35, 13);
            _loginUiClassicClickPoints.Add(new LoginUiClassicClickPoint(LoginUiClassicClickKind.RestoreSelectRow, row, index: i));
        }
    }
    private void DrawCharacterSlots(D3D11Frame frame, string chrSelPath, int baseX, int baseY, long nowMs)
    {
        if (_spriteBatch == null)
            return;

        for (int n = 0; n < 2; n++)
        {
            ref LoginUiCharSlot slot = ref _loginUiCharSlots[n];
            if (!slot.Valid)
                continue;

            byte job = slot.Job;
            byte sex = slot.Sex;

            int baseSelected = 40 + (job * 40) + (sex * 120);
            int baseFreeze = 60 + (job * 40) + (sex * 120);

            GetSelectCharacterPositions(job, sex, n, baseX, baseY, out int bx, out int by, out int fx, out int fy, out int ex, out int ey);

            if (slot.Unfreezing)
            {
                int idx = baseFreeze + slot.AniIndex;
                if (TryGetLoginUiTexture(frame, chrSelPath, idx, out D3D11Texture2D tex))
                    _spriteBatch.Draw(tex, new DrawingRectangle(bx, by, tex.Width, tex.Height));

                int effIdx = 4 + slot.EffIndex;
                if (TryGetLoginUiTexture(frame, chrSelPath, effIdx, out D3D11Texture2D eff))
                    _spriteBatch.Draw(eff, new DrawingRectangle(ex, ey, eff.Width, eff.Height));

                if (nowMs - slot.StartMs > 110)
                {
                    slot.StartMs = nowMs;
                    slot.AniIndex++;
                }

                if (nowMs - slot.StartEffMs > 110)
                {
                    slot.StartEffMs = nowMs;
                    slot.EffIndex++;
                }

                if (slot.AniIndex > FreezeFrameCount - 1)
                {
                    slot.Unfreezing = false;
                    slot.FreezeState = false;
                    slot.AniIndex = 0;
                }

                continue;
            }

            if (!slot.Selected && !slot.FreezeState && !slot.Freezing)
            {
                slot.Freezing = true;
                slot.AniIndex = 0;
                slot.StartMs = nowMs;
            }

            if (slot.Freezing)
            {
                int idx = baseFreeze + (FreezeFrameCount - slot.AniIndex - 1);
                if (TryGetLoginUiTexture(frame, chrSelPath, idx, out D3D11Texture2D tex))
                    _spriteBatch.Draw(tex, new DrawingRectangle(bx, by, tex.Width, tex.Height));

                if (nowMs - slot.StartMs > 110)
                {
                    slot.StartMs = nowMs;
                    slot.AniIndex++;
                }

                if (slot.AniIndex > FreezeFrameCount - 1)
                {
                    slot.Freezing = false;
                    slot.FreezeState = true;
                    slot.AniIndex = 0;
                }

                continue;
            }

            if (!slot.FreezeState)
            {
                int idx = baseSelected + slot.AniIndex;
                if (TryGetLoginUiTexture(frame, chrSelPath, idx, out D3D11Texture2D tex))
                    _spriteBatch.Draw(tex, new DrawingRectangle(fx, fy, tex.Width, tex.Height));
            }
            else
            {
                if (TryGetLoginUiTexture(frame, chrSelPath, baseFreeze, out D3D11Texture2D tex))
                    _spriteBatch.Draw(tex, new DrawingRectangle(bx, by, tex.Width, tex.Height));
            }

            if (slot.Selected)
            {
                if (nowMs - slot.StartMs > 230)
                {
                    slot.StartMs = nowMs;
                    slot.AniIndex++;
                    if (slot.AniIndex > SelectedFrameCount - 1)
                        slot.AniIndex = 0;
                }

                if (nowMs - slot.MoreMs > 25)
                {
                    slot.MoreMs = nowMs;
                    if (slot.DarkLevel > 0)
                        slot.DarkLevel--;
                }
            }
        }
    }

    private static void GetSelectCharacterPositions(byte job, byte sex, int slotIndex, int baseX, int baseY, out int bx, out int by, out int fx, out int fy, out int ex, out int ey)
    {
        
        ex = baseX + 90;
        ey = baseY + 58;

        bx = baseX + 71;
        by = baseY + 52;
        fx = bx;
        fy = by;

        switch (job)
        {
            case 0:
            {
                if (sex == 0)
                {
                    bx = baseX + 71;
                    by = baseY + 52;
                    fx = bx;
                    fy = by;
                }
                else
                {
                    bx = baseX + 65;
                    by = baseY + 55;
                    fx = bx;
                    fy = by;
                }

                break;
            }
            case 1:
            {
                if (sex == 0)
                {
                    bx = baseX + 77;
                    by = baseY + 46;
                    fx = bx;
                    fy = by;
                }
                else
                {
                    bx = baseX + 171;
                    by = baseY + 97;
                    fx = bx - 30;
                    fy = by - 14;
                }

                break;
            }
            case 2:
            {
                if (sex == 0)
                {
                    bx = baseX + 85;
                    by = baseY + 63;
                    fx = bx;
                    fy = by;
                }
                else
                {
                    bx = baseX + 164;
                    by = baseY + 103;
                    fx = bx - 23;
                    fy = by - 20;
                }

                break;
            }
        }

        if (slotIndex == 1)
        {
            ex = baseX + 430;
            ey = baseY + 60;
            bx += 340;
            by += 2;
            fx += 340;
            fy += 2;
        }
    }

    private IReadOnlyList<string> GetClassicNewAccountHelpLines(UiTextInput? focus)
    {
        if (focus == null)
            return Array.Empty<string>();

        var lines = new List<string>(20);

        if (ReferenceEquals(focus, _uiAccAccount))
        {
            lines.Add("您的帐号名称可以包括：");
            lines.Add("字符、数字的组合。");
            lines.Add("帐号名称长度必须为4或以上。");
            lines.Add("登陆帐号并游戏中的人物名称。");
            lines.Add("请仔细输入创建帐号所需信息。");
            lines.Add("您的登陆帐号可以登陆游戏");
            lines.Add("及我们网站，以取得一些相关信息。");
            lines.Add(string.Empty);
            lines.Add("建议您的登陆帐号不要与游戏中的角");
            lines.Add("色名相同，");
            lines.Add("以确保你的密码不会被爆力破解。");
        }

        if (ReferenceEquals(focus, _uiAccPassword))
        {
            lines.Add("您的密码可以是字符及数字的组合，");
            lines.Add("但密码长度必须至少4位。");
            lines.Add("建议您的密码内容不要过于简单，");
            lines.Add("以防被人猜到。");
            lines.Add("请记住您输入的密码，如果丢失密码");
            lines.Add("将无法登录游戏。");
            lines.Add(string.Empty);
            lines.Add(string.Empty);
            lines.Add(string.Empty);
            lines.Add(string.Empty);
            lines.Add(string.Empty);
        }

        if (ReferenceEquals(focus, _uiAccConfirm))
        {
            lines.Add("再次输入密码");
            lines.Add("以确认。");
            lines.Add(string.Empty);
        }

        if (ReferenceEquals(focus, _uiAccUserName))
        {
            lines.Add("请输入您的全名.");
            lines.Add(string.Empty);
        }

        if (ReferenceEquals(focus, _uiAccSsNo))
        {
            lines.Add("请输入你的身份证号");
            lines.Add("例如： 720101-146720");
            lines.Add(string.Empty);
        }

        if (ReferenceEquals(focus, _uiAccBirthDay))
        {
            lines.Add("请输入您的生日");
            lines.Add("例如：1977/10/15");
            lines.Add(string.Empty);
        }

        if (ReferenceEquals(focus, _uiAccQuiz1))
        {
            lines.Add("请输入第一个密码提示问题");
            lines.Add("这个提示将用于密码丢失后找");
            lines.Add("回密码用。");
            lines.Add(string.Empty);
        }

        if (ReferenceEquals(focus, _uiAccAnswer1))
        {
            lines.Add("请输入上面问题的");
            lines.Add("答案。");
            lines.Add(string.Empty);
        }

        if (ReferenceEquals(focus, _uiAccQuiz2))
        {
            lines.Add("请输入第二个密码提示问题");
            lines.Add("这个提示将用于密码丢失后找");
            lines.Add("回密码用。");
            lines.Add(string.Empty);
        }

        if (ReferenceEquals(focus, _uiAccAnswer2))
        {
            lines.Add("请输入上面问题的");
            lines.Add("答案。");
            lines.Add(string.Empty);
        }

        if (ReferenceEquals(focus, _uiAccUserName) ||
            ReferenceEquals(focus, _uiAccSsNo) ||
            ReferenceEquals(focus, _uiAccQuiz1) ||
            ReferenceEquals(focus, _uiAccQuiz2) ||
            ReferenceEquals(focus, _uiAccAnswer1) ||
            ReferenceEquals(focus, _uiAccAnswer2))
        {
            lines.Add("您输入的信息必须真实正确的信息");
            lines.Add("如果使用了虚假的注册信息");
            lines.Add("您的帐号将被取消。");
            lines.Add(string.Empty);
        }

        if (ReferenceEquals(focus, _uiAccPhone))
        {
            lines.Add("请输入您的电话");
            lines.Add("号码。");
            lines.Add(string.Empty);
        }

        if (ReferenceEquals(focus, _uiAccMobilePhone))
        {
            lines.Add("请输入您的手机号码。");
            lines.Add(string.Empty);
        }

        if (ReferenceEquals(focus, _uiAccEmail))
        {
            lines.Add("请输入您的邮件地址。您的邮件将被");
            lines.Add("接收最近更新的一些信息");
            lines.Add(string.Empty);
        }

        return lines;
    }

    private bool AppendLoginUiTextClassic(D3D11ViewTransform view)
    {
        if (!LoginUiVisible || _textRenderer == null)
            return false;

        if (!IsClassicLoginUiScreen(_loginUiScreen))
            return false;

        DrawingRectangle full = new(0, 0, view.LogicalSize.Width, view.LogicalSize.Height);

        if (_loginUiModal != null)
        {
            AppendLoginUiModalText(full, view);
            return true;
        }

        if (_uiTextPrompt != null)
        {
            
            return false;
        }

        (int baseX, int baseY) = GetClassicUiBaseOrigin(view.LogicalSize);

        bool caretOn = ((Environment.TickCount64 - _uiFocusStartMs) / 500) % 2 == 0;
        float scale = Math.Max(1f, view.Scale.X);
        float fontSmall = 12f * scale;

        void DrawShadowText(string text, int logicalX, int logicalY, Color4 color, bool bold)
        {
            if (string.IsNullOrEmpty(text))
                return;

            Vector2 p = view.ToBackBuffer(new Vector2(logicalX, logicalY));
            _textRenderer.DrawText(text, p.X + 1, p.Y + 1, new Color4(0, 0, 0, 0.75f), fontSmall, bold);
            _textRenderer.DrawText(text, p.X, p.Y, color, fontSmall, bold);
        }

        void DrawShadowTextInRect(string text, DrawingRectangle logicalRect, Color4 color, bool bold, int offsetX = 0, int offsetY = 0)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var back = view.ToBackBuffer(logicalRect);
            (float tw, float th) = _textRenderer.MeasureText(text, fontSmall, bold);
            float x = back.Left + ((back.Width - tw) * 0.5f) + offsetX;
            float y = back.Top + ((back.Height - th) * 0.5f) + offsetY;
            _textRenderer.DrawText(text, x + 1, y + 1, new Color4(0, 0, 0, 0.75f), fontSmall, bold);
            _textRenderer.DrawText(text, x, y, color, fontSmall, bold);
        }

        bool TryGetClassicRect(LoginUiClassicClickKind kind, int index, out DrawingRectangle rect)
        {
            foreach (LoginUiClassicClickPoint p in _loginUiClassicClickPoints)
            {
                if (p.Kind == kind && p.Index == index)
                {
                    rect = p.Rect;
                    return true;
                }
            }

            rect = default;
            return false;
        }

        void DrawClassicField(LoginUiClassicClickKind kind, int index, UiTextInput field)
        {
            if (!TryGetClassicRect(kind, index, out DrawingRectangle rect))
                return;

            string text = field.GetDisplayText();
            if (caretOn && ReferenceEquals(_uiFocusedInput, field))
                text += "_";

            DrawShadowText(text, rect.X, rect.Y, new Color4(1, 1, 1, 1), bold: false);
        }

        switch (_loginUiScreen)
        {
            case LoginUiScreen.Login:
            {
                
                int dlgX = (view.LogicalSize.Width - 296) / 2;
                int dlgY = (view.LogicalSize.Height - 314) / 2;

                string acc = _uiLoginAccount.GetTrimmed();
                string pwd = _uiLoginPassword.GetDisplayText();

                if (caretOn && ReferenceEquals(_uiFocusedInput, _uiLoginAccount))
                    acc += "_";
                if (caretOn && ReferenceEquals(_uiFocusedInput, _uiLoginPassword))
                    pwd += "_";

                DrawShadowText(acc, dlgX + 125, dlgY + 75, new Color4(1, 1, 1, 1), bold: false);
                DrawShadowText(pwd, dlgX + 125, dlgY + 107, new Color4(1, 1, 1, 1), bold: false);
                return true;
            }
            case LoginUiScreen.ChangePassword:
            {
                DrawClassicField(LoginUiClassicClickKind.ChangePwdFocusField, 0, _uiChangePwdAccount);
                DrawClassicField(LoginUiClassicClickKind.ChangePwdFocusField, 1, _uiChangePwdOld);
                DrawClassicField(LoginUiClassicClickKind.ChangePwdFocusField, 2, _uiChangePwdNew);
                DrawClassicField(LoginUiClassicClickKind.ChangePwdFocusField, 3, _uiChangePwdConfirm);
                return true;
            }
            case LoginUiScreen.Register:
            case LoginUiScreen.UpdateAccount:
            {
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 0, _uiAccAccount);
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 1, _uiAccPassword);
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 2, _uiAccConfirm);
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 3, _uiAccUserName);
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 4, _uiAccSsNo);
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 5, _uiAccBirthDay);
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 6, _uiAccQuiz1);
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 7, _uiAccAnswer1);
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 8, _uiAccQuiz2);
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 9, _uiAccAnswer2);
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 10, _uiAccPhone);
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 11, _uiAccMobilePhone);
                DrawClassicField(LoginUiClassicClickKind.NewAccountFocusField, 12, _uiAccEmail);

                if (TryGetClassicRect(LoginUiClassicClickKind.NewAccountFocusField, 0, out DrawingRectangle r0))
                {
                    int dlgX = r0.X - 161;
                    int dlgY = r0.Y - 116;

                    string title = _loginUiScreen == LoginUiScreen.UpdateAccount ? "(请填写帐号相关信息)" : string.Empty;
                    if (!string.IsNullOrWhiteSpace(title))
                        DrawShadowText(title, dlgX + 283, dlgY + 57, new Color4(1, 1, 1, 1), bold: true);

                    IReadOnlyList<string> helps = GetClassicNewAccountHelpLines(_uiFocusedInput);
                    for (int i = 0; i < helps.Count; i++)
                        DrawShadowText(helps[i], dlgX + 396, dlgY + 124 + (i * 14), new Color4(0.75f, 0.75f, 0.75f, 1f), bold: false);
                }

                return true;
            }
            case LoginUiScreen.SelectServer:
            {
                Color4 nameColor = new(242f / 255f, 244f / 255f, 147f / 255f, 1f); 
                foreach (LoginUiClassicClickPoint p in _loginUiClassicClickPoints)
                {
                    if (p.Kind != LoginUiClassicClickKind.ServerSelect)
                        continue;

                    int idx = p.Index;
                    if (idx < 0 || idx >= _uiServers.Count)
                        continue;

                    string name = _uiServers[idx].Name;
                    bool pressed = IsClassicPressed(LoginUiClassicClickKind.ServerSelect, idx);
                    DrawShadowTextInRect(name, p.Rect, nameColor, bold: true, offsetX: pressed ? 2 : 0, offsetY: pressed ? 2 : 0);
                }

                return true;
            }
            case LoginUiScreen.SelectCharacter:
            case LoginUiScreen.CreateCharacter:
            case LoginUiScreen.RestoreDeletedCharacter:
            {
                if (_loginUiCharSlots[0].Info != null)
                {
                    DrawShadowText(_loginUiCharSlots[0].Name, baseX + 117, baseY + 494, new Color4(1, 1, 1, 1), bold: true);
                    DrawShadowText(_loginUiCharSlots[0].Level.ToString(), baseX + 117, baseY + 523, new Color4(1, 1, 1, 1), bold: true);
                    DrawShadowText(GetJobName(_loginUiCharSlots[0].Job), baseX + 117, baseY + 553, new Color4(1, 1, 1, 1), bold: true);
                }

                if (_loginUiCharSlots[1].Info != null)
                {
                    DrawShadowText(_loginUiCharSlots[1].Name, baseX + 671, baseY + 496, new Color4(1, 1, 1, 1), bold: true);
                    DrawShadowText(_loginUiCharSlots[1].Level.ToString(), baseX + 671, baseY + 525, new Color4(1, 1, 1, 1), bold: true);
                    DrawShadowText(GetJobName(_loginUiCharSlots[1].Job), baseX + 671, baseY + 555, new Color4(1, 1, 1, 1), bold: true);
                }

                string serverName = (_startup?.ServerName ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(serverName))
                {
                    (float tw, _) = _textRenderer.MeasureText(serverName, fontSmall, bold: true);
                    Vector2 p = view.ToBackBuffer(new Vector2((view.LogicalSize.Width / 2f) - (tw / 2f), baseY + 8));
                    _textRenderer.DrawText(serverName, p.X + 1, p.Y + 1, new Color4(0, 0, 0, 0.75f), fontSmall, bold: true);
                    _textRenderer.DrawText(serverName, p.X, p.Y, new Color4(1, 1, 1, 1), fontSmall, bold: true);
                }

                if (_loginUiScreen == LoginUiScreen.CreateCharacter)
                {
                    int dlgX = _loginUiCreateSlotIndex == 0 ? baseX + 415 : baseX + 75;
                    int dlgY = baseY + 15;
                    string name = _uiNewCharName.GetTrimmed();
                    if (caretOn && ReferenceEquals(_uiFocusedInput, _uiNewCharName))
                        name += "_";
                    DrawShadowText(name, dlgX + 73, dlgY + 109, new Color4(1, 1, 1, 1), bold: false);
                }

                if (_loginUiScreen == LoginUiScreen.RestoreDeletedCharacter)
                {
                    int dlgX = baseX + 120;
                    int dlgY = baseY + 88;
                    int visible = Math.Min(20, _uiDeletedCharacters.Count);
                    for (int i = 0; i < visible; i++)
                    {
                        MirDeletedCharacterInfo info = _uiDeletedCharacters[i];
                        Color4 c = i == _uiDeletedSelectedIndex ? new Color4(1.0f, 0.35f, 0.35f, 1f) : new Color4(1, 1, 1, 1);
                        int y = dlgY + 125 + (i * 13);
                        DrawShadowText(info.Name, dlgX + 35, y, c, bold: false);
                        DrawShadowText(info.Level.ToString(), dlgX + 120, y, c, bold: false);
                        DrawShadowText(GetJobName(info.Job), dlgX + 164, y, c, bold: false);
                        DrawShadowText(GetSexName(info.Sex), dlgX + 208, y, c, bold: false);
                    }
                }

                return true;
            }
            default:
                return true;
        }
    }
    private async Task<bool> TryHandleLoginUiMouseDownClassicAsync(Vector2 logical, bool left, bool right)
    {
        if (!LoginUiVisible)
            return false;

        if (!IsClassicLoginUiScreen(_loginUiScreen))
            return false;

        if (!TryEnsureLoginUiArchives())
            return false;

        if (_loginUiModal != null || _uiTextPrompt != null)
            return false;

        if (!left && !right)
            return true;

        if (right)
            return true;

        for (int i = _loginUiClassicClickPoints.Count - 1; i >= 0; i--)
        {
            LoginUiClassicClickPoint point = _loginUiClassicClickPoints[i];
            if (!RectContains(point.Rect, logical))
                continue;

            switch (point.Kind)
            {
                case LoginUiClassicClickKind.FocusAccount:
                    FocusField(_uiLoginAccount);
                    SetClassicPressed(point.Kind, point.Index);
                    return true;
                case LoginUiClassicClickKind.FocusPassword:
                    FocusField(_uiLoginPassword);
                    SetClassicPressed(point.Kind, point.Index);
                    return true;
                case LoginUiClassicClickKind.LoginOk:
                    SetClassicPressed(point.Kind, point.Index);
                    BeginLoginUiPrimaryAction();
                    return true;
                case LoginUiClassicClickKind.LoginNewAccount:
                    SetClassicPressed(point.Kind, point.Index);
                    PrefillAndShowRegister();
                    return true;
                case LoginUiClassicClickKind.LoginChangePassword:
                    SetClassicPressed(point.Kind, point.Index);
                    PrefillAndShowChangePassword();
                    return true;
                case LoginUiClassicClickKind.LoginClose:
                    SetClassicPressed(point.Kind, point.Index);
                    Close();
                    return true;
                case LoginUiClassicClickKind.ChangePwdFocusField:
                {
                    UiTextInput? field = GetClassicChangePasswordField(point.Index);
                    if (field != null)
                        FocusField(field);
                    return true;
                }
                case LoginUiClassicClickKind.ChangePwdOk:
                    SetClassicPressed(point.Kind, point.Index);
                    BeginLoginUiPrimaryAction();
                    return true;
                case LoginUiClassicClickKind.ChangePwdCancel:
                    SetClassicPressed(point.Kind, point.Index);
                    ShowLoginUi(LoginUiScreen.Login);
                    return true;
                case LoginUiClassicClickKind.NewAccountFocusField:
                {
                    UiTextInput? field = GetClassicNewAccountField(point.Index);
                    if (field != null)
                        FocusField(field);
                    return true;
                }
                case LoginUiClassicClickKind.NewAccountOk:
                    SetClassicPressed(point.Kind, point.Index);
                    BeginLoginUiPrimaryAction();
                    return true;
                case LoginUiClassicClickKind.NewAccountCancel:
                case LoginUiClassicClickKind.NewAccountClose:
                    SetClassicPressed(point.Kind, point.Index);
                    ShowLoginUi(LoginUiScreen.Login);
                    return true;
                case LoginUiClassicClickKind.ServerClose:
                    SetClassicPressed(point.Kind, point.Index);
                    Close();
                    return true;
                case LoginUiClassicClickKind.ServerSelect:
                {
                    int idx = Math.Clamp(point.Index, 0, Math.Max(0, _uiServers.Count - 1));
                    _uiServerSelectedIndex = idx;
                    SetClassicPressed(point.Kind, point.Index);
                    BeginLoginUiPrimaryAction();
                    return true;
                }
                case LoginUiClassicClickKind.CharSelect0:
                    LoginUiClassicSelectCharacter(0);
                    SetClassicPressed(point.Kind, point.Index);
                    return true;
                case LoginUiClassicClickKind.CharSelect1:
                    LoginUiClassicSelectCharacter(1);
                    SetClassicPressed(point.Kind, point.Index);
                    return true;
                case LoginUiClassicClickKind.CharStart:
                    SetClassicPressed(point.Kind, point.Index);
                    BeginLoginUiPrimaryAction();
                    return true;
                case LoginUiClassicClickKind.CharNew:
                    SetClassicPressed(point.Kind, point.Index);
                    LoginUiClassicBeginCreateCharacter();
                    return true;
                case LoginUiClassicClickKind.CharErase:
                {
                    SetClassicPressed(point.Kind, point.Index);
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
                case LoginUiClassicClickKind.CharRestoreDeleted:
                    SetClassicPressed(point.Kind, point.Index);
                    await LoginUiOpenDeletedListAsync().ConfigureAwait(true);
                    return true;
                case LoginUiClassicClickKind.CharExit:
                    SetClassicPressed(point.Kind, point.Index);
                    Close();
                    return true;
                case LoginUiClassicClickKind.RestoreClose:
                    SetClassicPressed(point.Kind, point.Index);
                    ShowLoginUi(LoginUiScreen.SelectCharacter);
                    return true;
                case LoginUiClassicClickKind.RestoreConfirm:
                    SetClassicPressed(point.Kind, point.Index);
                    BeginLoginUiPrimaryAction();
                    return true;
                case LoginUiClassicClickKind.RestoreSelectRow:
                {
                    int idx = Math.Clamp(point.Index, 0, Math.Max(0, _uiDeletedCharacters.Count - 1));
                    _uiDeletedSelectedIndex = idx;
                    _soundManager.PlaySfxById(soundId: 105, volume: 0.7f);
                    return true;
                }
                case LoginUiClassicClickKind.CreateFocusName:
                    FocusField(_uiNewCharName);
                    return true;
                case LoginUiClassicClickKind.CreateClose:
                    SetClassicPressed(point.Kind, point.Index);
                    LoginUiClassicCancelCreateCharacter();
                    return true;
                case LoginUiClassicClickKind.CreateOk:
                    SetClassicPressed(point.Kind, point.Index);
                    BeginLoginUiPrimaryAction();
                    return true;
                case LoginUiClassicClickKind.CreateJob:
                    SetClassicPressed(point.Kind, point.Index);
                    _uiNewCharJob = (byte)Math.Clamp(point.Index, 0, 2);
                    LoginUiClassicUpdateCreatePreview();
                    return true;
                case LoginUiClassicClickKind.CreateSex:
                    SetClassicPressed(point.Kind, point.Index);
                    _uiNewCharSex = (byte)Math.Clamp(point.Index, 0, 1);
                    LoginUiClassicUpdateCreatePreview();
                    return true;
                case LoginUiClassicClickKind.CreateHairPrev:
                case LoginUiClassicClickKind.CreateHairNext:
                    SetClassicPressed(point.Kind, point.Index);
                    
                    return true;
            }

            return true;
        }

        return true;
    }

    private UiTextInput? GetClassicChangePasswordField(int index) =>
        index switch
        {
            0 => _uiChangePwdAccount,
            1 => _uiChangePwdOld,
            2 => _uiChangePwdNew,
            3 => _uiChangePwdConfirm,
            _ => null
        };

    private UiTextInput? GetClassicNewAccountField(int index) =>
        index switch
        {
            0 => _uiAccAccount,
            1 => _uiAccPassword,
            2 => _uiAccConfirm,
            3 => _uiAccUserName,
            4 => _uiAccSsNo,
            5 => _uiAccBirthDay,
            6 => _uiAccQuiz1,
            7 => _uiAccAnswer1,
            8 => _uiAccQuiz2,
            9 => _uiAccAnswer2,
            10 => _uiAccPhone,
            11 => _uiAccMobilePhone,
            12 => _uiAccEmail,
            _ => null
        };

    private void LoginUiClassicSelectCharacter(int slotIndex)
    {
        if ((uint)slotIndex > 1u)
            return;

        ref LoginUiCharSlot slot = ref _loginUiCharSlots[slotIndex];
        if (slot.Selected || !slot.Valid || !slot.FreezeState)
            return;

        _uiCharacterSelectedIndex = slotIndex;

        string name = slot.Name.Trim();
        if (!string.IsNullOrWhiteSpace(name))
        {
            _txtCharacter.Text = name;
            _selectCharacterScene.SelectCharacter(name);
        }

        int otherIndex = 1 - slotIndex;
        ref LoginUiCharSlot other = ref _loginUiCharSlots[otherIndex];
        other.Selected = false;

        slot.Selected = true;
        slot.Unfreezing = true;
        slot.AniIndex = 0;
        slot.DarkLevel = 0;
        slot.EffIndex = 0;
        slot.StartMs = Environment.TickCount64;
        slot.MoreMs = slot.StartMs;
        slot.StartEffMs = slot.StartMs;

        _soundManager.PlaySfxById(soundId: 101, volume: 0.8f);
    }

    private void LoginUiClassicBeginCreateCharacter()
    {
        int slotIndex = _loginUiCharSlots[0].Info == null ? 0 : (_loginUiCharSlots[1].Info == null ? 1 : -1);
        if (slotIndex < 0)
        {
            ShowModal("Create Character", "No empty slot available.", UiModalButtons.Ok, _ => { });
            return;
        }

        _loginUiCreateSlotIndex = slotIndex;
        _uiNewCharName.Clear();
        _uiNewCharJob = 0;
        _uiNewCharSex = 0;
        _uiNewCharHair = 0;

        LoginUiClassicUpdateCreatePreview();
        ShowLoginUi(LoginUiScreen.CreateCharacter);
    }

    private void LoginUiClassicCancelCreateCharacter()
    {
        ShowLoginUi(LoginUiScreen.SelectCharacter);
        InitializeLoginUiCharacterSlots();
    }

    private void LoginUiClassicUpdateCreatePreview()
    {
        int slotIndex = Math.Clamp(_loginUiCreateSlotIndex, 0, 1);
        ref LoginUiCharSlot slot = ref _loginUiCharSlots[slotIndex];

        slot.Info = new MirCharacterInfo(string.Empty, _uiNewCharJob, _uiNewCharHair, Level: 1, _uiNewCharSex, Selected: true);
        slot.Selected = true;
        slot.FreezeState = false;
        slot.Unfreezing = false;
        slot.Freezing = false;
        slot.AniIndex = 0;
        slot.EffIndex = 0;
        slot.DarkLevel = 30;
        slot.StartMs = Environment.TickCount64;
        slot.MoreMs = slot.StartMs;
        slot.StartEffMs = slot.StartMs;

        ref LoginUiCharSlot other = ref _loginUiCharSlots[1 - slotIndex];
        other.Selected = false;
    }
}
