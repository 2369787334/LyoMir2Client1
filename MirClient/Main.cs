using DrawingColor = System.Drawing.Color;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using MirClient.Assets.Actors;
using MirClient.Assets.Light;
using MirClient.Assets.Palettes;
using MirClient.Assets.Wil;
using MirClient.Assets.Maps;
using MirClient.Assets.PackData;
using MirClient.Audio;
using MirClient.Core;
using MirClient.Core.Diagnostics;
using MirClient.Core.Effects;
using MirClient.Core.LocalConfig;
using MirClient.Core.Messages;
using MirClient.Core.Resources;
using MirClient.Core.Scenes;
using MirClient.Core.Systems;
using MirClient.Core.Threading;
using MirClient.Core.World;
using MirClient.Forms;
using MirClient.Rendering.D3D11;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;
using MirClient.Protocol.Startup;
using MirClient.Protocol.Text;
using Vortice.Direct3D;
using Vortice.Mathematics;

namespace MirClient;

internal partial class Main : Form
{
    private enum DealButton
    {
        None = 0,
        Gold = 1,
        End = 2,
        Cancel = 3
    }

    private enum MarketButton
    {
        None = 0,
        Prev = 1,
        Next = 2,
        Refresh = 3,
        Find = 4,
        Action = 5,
        Close = 6
    }

    private enum StallButton
    {
        None = 0,
        Name = 1,
        Open = 2,
        Cancel = 3,
        Remove = 4,
        Close = 5
    }

    private enum UserStallButton
    {
        None = 0,
        Buy = 1,
        Close = 2
    }

    private enum ItemDragSource
    {
        None = 0,
        Bag = 1,
        Use = 2,
        Refine = 3
    }

    private enum UiWindowDragTarget
    {
        None = 0,
        Bag = 1,
        HeroBag = 2,
        State = 3,
        Merchant = 4,
        Settings = 5,
        Mall = 6
    }

    private sealed record MerchantClickPoint(DrawingRectangle Rect, string Command);

    private enum BookClickKind
    {
        None = 0,
        Close = 1,
        Prev = 2,
        Next = 3,
        Confirm = 4
    }

    private sealed record BookClickPoint(DrawingRectangle Rect, BookClickKind Kind);

    private enum RefineClickKind
    {
        None = 0,
        Slot = 1,
        Ok = 2,
        Close = 3
    }

    private sealed record RefineClickPoint(DrawingRectangle Rect, RefineClickKind Kind, int Index);

    private enum BoxClickKind
    {
        None = 0,
        Slot = 1,
        Flash = 2,
        Get = 3,
        Close = 4
    }

    private sealed record BoxClickPoint(DrawingRectangle Rect, BoxClickKind Kind, int Index);

    private enum YbDealClickKind
    {
        None = 0,
        Item = 1,
        Buy = 2,
        Cancel = 3,
        CancelSell = 4,
        Close = 5
    }

    private sealed record YbDealClickPoint(DrawingRectangle Rect, YbDealClickKind Kind, int Index);

    private enum MerchantMenuClickKind
    {
        None = 0,
        Item = 1,
        Prev = 2,
        Action = 3,
        Next = 4,
        Close = 5
    }

    private sealed record MerchantMenuClickPoint(DrawingRectangle Rect, MerchantMenuClickKind Kind, int Index);

    private enum MallClickKind
    {
        None = 0,
        Close = 1,
        Category = 2,
        PrevPage = 3,
        NextPage = 4,
        Item = 5,
        Buy = 6,
        Gift = 7
    }

    private sealed record MallClickPoint(DrawingRectangle Rect, MallClickKind Kind, byte Class, int Index);

    private enum SettingsClickKind
    {
        None = 0,
        ShowActorName = 1,
        DuraWarning = 2,
        AutoAttack = 3,
        ShowDropItems = 4,
        HideDeathBody = 5
    }

    private sealed record SettingsClickPoint(DrawingRectangle Rect, SettingsClickKind Kind);

    private sealed record StateMagicClickPoint(DrawingRectangle Rect, bool Hero, ushort MagicId);
    private sealed record StateMagicKeyClickPoint(DrawingRectangle Rect, byte Key);
    private sealed record ServerListItem(string Name, string Status)
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Status) ? Name : $"{Name} ({Status})";
    }

    private readonly record struct ChatLine(string Text, Color4 Color, long TimestampMs);
    private readonly record struct SysMessageLine(string Text, long TimestampMs);
    private sealed class SysMarqueeEntry
    {
        public SysMarqueeEntry(string text, byte foreIndex, byte backIndex)
        {
            Text = text;
            ForeIndex = foreIndex;
            BackIndex = backIndex;
        }

        public string Text { get; }
        public byte ForeIndex { get; }
        public byte BackIndex { get; }
        public float Offset { get; set; }
    }

    private readonly D3D11RenderControl _renderControl = new()
    {
        Dock = DockStyle.Fill
    };

    private readonly SplitContainer _mainSplit;

    private readonly MirClientSession _session = new();
    private readonly MirMessagePump _serverMessagePump;
    private readonly MaketSystem _maketSystem;
    private readonly MirLogicLoop? _logicLoop = null;
    private readonly bool _logicThreadEnabled;
    private readonly object _logicSync = new();

    private readonly TextBox _txtClientParam = new()
    {
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = false,
        Height = 90,
        Dock = DockStyle.Top
    };

    private readonly TextBox _txtAccount = new() { PlaceholderText = "Account", Dock = DockStyle.Top };
    private readonly TextBox _txtPassword = new() { PlaceholderText = "Password", UseSystemPasswordChar = true, Dock = DockStyle.Top };
    private readonly TextBox _txtCharacter = new() { PlaceholderText = "Character (preferred / create if none)", Dock = DockStyle.Top };
    private readonly Label _lblStartup = new()
    {
        Text = "ClientParamStr: (not decoded)",
        AutoSize = false,
        Height = 48,
        Dock = DockStyle.Top
    };

    private readonly Label _lblSceneStage = new()
    {
        Text = "Stage: Idle  Scene: Intro",
        AutoSize = false,
        Height = 18,
        Dock = DockStyle.Top
    };

    private readonly Label _lblServers = new()
    {
        Text = "Servers (from login gate)",
        AutoSize = false,
        Height = 18,
        Dock = DockStyle.Top
    };

    private readonly ListBox _lstServers = new()
    {
        Dock = DockStyle.Top,
        Height = 90,
        IntegralHeight = false,
        HorizontalScrollbar = true
    };

    private readonly Label _lblCharacters = new()
    {
        Text = "Characters",
        AutoSize = false,
        Height = 18,
        Dock = DockStyle.Top
    };

    private readonly ListBox _lstCharacters = new()
    {
        Dock = DockStyle.Top,
        Height = 110,
        IntegralHeight = false,
        HorizontalScrollbar = true
    };

    private readonly Button _btnDecode = new() { Text = "Decode ClientParamStr", Dock = DockStyle.Top, Height = 32 };
    private readonly Button _btnCreateCharacter = new() { Text = "Create Character", Dock = DockStyle.Top, Height = 32 };
    private readonly Button _btnLogin = new() { Text = "Login / Enter Game", Dock = DockStyle.Top, Height = 36 };
    private readonly Button _btnDisconnect = new() { Text = "Disconnect", Dock = DockStyle.Top, Height = 32, Enabled = false };

    private readonly TextBox _txtLog = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        BackColor = DrawingColor.Black,
        ForeColor = DrawingColor.Gainsboro
    };

    private MirStartupInfo? _startup;
    private CancellationTokenSource? _loginCts;
    private MirMessageDispatcher? _dispatcher;
    private bool _reconnectInProgress;
    private bool _loginFlowInProgress;
    private long _loginStageDisconnectStartMs;
    private MirSessionStage _transitionStagePrev = MirSessionStage.Idle;
    private MirSessionStage _transitionStage = MirSessionStage.Idle;
    private long _transitionStageEnterMs = Environment.TickCount64;
    private string _loginAccount = string.Empty;
    private string _loginPassword = string.Empty;
    private string _loginServerListRaw = string.Empty;
    private string _selectedServerName = string.Empty;
    private string _selectedCharacterName = string.Empty;
    private int _loginCertification;
    private string? _lastRunGateHost;
    private int _lastRunGatePort;
    private bool _passwordInputMode;
    private string _whisperName = string.Empty;
    private bool _chatInputActive;
    private readonly UiTextInput _uiChatInput = new("Chat", password: false, multiline: false, maxGbkBytes: 200, trimWhitespace: false);
    private long _chatInputFocusStartMs;
    private readonly List<string> _chatSendHistory = new(64);
    private int _chatSendHistoryIndex;
    private bool _running;
    private bool _isBorderlessFullscreen;
    private FormBorderStyle _prevBorderStyle;
    private FormWindowState _prevWindowState;
    private bool _prevTopMost;
    private const int ChatOverlayMaxLines = 80;
    private const long ChatOverlayTtlMs = 20_000;
    private const long ChatOverlayFadeMs = 3_000;
    private const int ViewChatLine = 9;
    private const int ChatHistoryMaxLines = 200;
    private const int SysMessageMaxLines = 10;
    private const long SysMessageTopTtlMs = 3_000;
    private const long SysMessageBottomTtlMs = 3_000;
    private const long SysMessageBottomRightTtlMs = 4_000;
    private const int HumanFrame = 600;
    private const int CboFrame = 2000;
    private const int SndHitSword = 52;
    private const int SndFireHit = 137;
    private const int SndStruckShort = 60;
    private const int SndManStruck = 138;
    private const int SndWomanStruck = 139;
    private const int SndManDie = 144;
    private const int SndWomanDie = 145;
    private const int MaxMagicLv = 3;

    private D3D11SpriteBatch? _spriteBatch;
    private int _spriteBatchMaxSprites = 8192;
    private D3D11Texture2D? _demoTexture;
    private D3D11Texture2D? _whiteTexture;

    private const long IntroSplashDurationMs = 3_000;
    private const long IntroSplashSkipDelayMs = 100;
    private bool _introSplashActive;
    private long _introSplashStartMs;
    private long _introSplashEndMs;
    private D3D11Texture2D? _introLogoTexture;
    private bool _introLogoTextureLoadFailed;

    private readonly D3D11Texture2D?[] _fogLightTextures = new D3D11Texture2D?[6];
    private readonly bool[] _fogLightTextureLoadFailed = new bool[6];
    private D3D11Texture2D? _fogLightMapTexture;
    private D3D11TextRenderer? _textRenderer;
    private D3D11TextureCache<WilImageKey>? _wilTextureCache;
    private D3D11TextureCache<PackDataImageKey>? _dataTextureCache;
    private int _renderDeviceVersion;
    private bool _demoTextureOwnedByCache;
    private readonly WilImageCache _wilImageCache;
    private Task<WilImage?>? _wilImageTask;
    private bool _wilImageTaskHandled;
    private readonly PackDataImageCache _packDataImageCache;
    private Task<PackDataImage?>? _packDataImageTask;
    private bool _packDataImageTaskHandled;

    private long _cpuCacheBytes = 256L * 1024 * 1024;
    private long _cpuWilCacheBytes;
    private long _cpuDataCacheBytes;

    private long _gpuCacheBytes = 512L * 1024 * 1024;
    private long _gpuWilCacheBytes;
    private long _gpuDataCacheBytes;
    private int _assetDecodeConcurrency;

    private readonly StringBuilder _perfBuilder = new(512);
    private readonly StringBuilder _hotbarBuilder = new(256);
    private readonly StringBuilder _stateOverlayBuilder = new(1024);
    private string _perfOverlayText = string.Empty;
    private string _magicHotbarLine1 = string.Empty;
    private string _magicHotbarLine2 = string.Empty;
    private string _magicHotbarLine3 = string.Empty;
    private string _stateOverlayText = string.Empty;
    private bool _stateOverlayVisible;
    private readonly StringBuilder _heroStateOverlayBuilder = new(768);
    private string _heroStateOverlayText = string.Empty;
    private bool _heroStateOverlayVisible;
    private bool _hideDeathBody;
    private bool _showDropItems = true;
    private bool _showActorNames = true;
    private bool _autoAttack;
    private bool _duraWarning = true;
    private bool _groupOverlayVisible = true;
    private long _dealTryCooldownUntilMs;
    private long _duraWarningNextMs;
    private int _autoHitTargetRecogId;
    private long _autoChaseNextStartMs;
    private long _perfWindowStartTimestamp;
    private int _perfWindowFrames;
    private double _perfWindowAccumCpuMs;
    private double _perfWindowAccumFrameMs;
    private double _avgFps;
    private double _avgCpuFrameMs;
    private double _avgFrameMs;
    private double _lastCpuFrameMs;
    private double _lastFrameMs;
    private long _lastOverlayUpdateTimestamp;
    private int _targetFps = 60;
    private bool _limitFpsWhenVSyncOff = true;
    private MirPerfCsvLogger? _perfLogger;

    private readonly string? _wilPath;
    private readonly int _wilIndex;

    private readonly string? _dataPath;
    private readonly int _dataIndex;

    private readonly string? _mapPath;
    private bool _mapLoadAttempted;
    private MirMapFile? _map;
    private readonly Func<int, int, bool> _isCurrentMapWalkable;
    private readonly MirWorldState _world = new();
    private readonly MirSceneManager _sceneManager = new();
    private readonly MirStageSceneRouter _stageSceneRouter;
    private readonly MirSceneContext _sceneContext;
    private readonly LoginScene _loginScene;
    private readonly SelectCountryScene _selectCountryScene;
    private readonly SelectServerScene _selectServerScene;
    private readonly SelectCharacterScene _selectCharacterScene;
    private readonly LoadingScene _loadingScene;
    private readonly LoginNoticeScene _loginNoticeScene;
    private readonly CommandThrottleSystem _commandThrottle = new();
    private readonly MiniMapSystem _miniMapSystem = new();
    private readonly MiniMapRequestSystem _miniMapRequestSystem;
    private readonly ViewRangeSystem _viewRangeSystem;
    private readonly AutoMoveSystem _autoMoveSystem = new();
    private readonly TargetingSystem _targetingSystem = new();
    private readonly TargetingActionSystem _targetingActionSystem;
    private readonly LevelRankSystem _levelRankSystem;
    private readonly BoxSystem _boxSystem;
    private readonly BookSystem _bookSystem;
    private readonly YbDealSystem _ybDealSystem;
    private readonly ItemDialogSystem _itemDialogSystem;
    private readonly BindDialogSystem _bindDialogSystem;
    private readonly TreasureDialogSystem _treasureDialogSystem;
    private readonly GuildSystem _guildSystem;
    private readonly GroupSystem _groupSystem;
    private readonly SeriesSkillSystem _seriesSkillSystem;
    private readonly MissionSystem _missionSystem = new();
    private readonly DealSystem _dealSystem;
    private readonly StorageSystem _storageSystem;
    private readonly MerchantTradeSystem _merchantTradeSystem;
    private readonly MerchantMenuSystem _merchantMenuSystem;
    private readonly MerchantDialogSystem _merchantDialogSystem;
    private readonly InventoryPendingSystem _inventoryPendingSystem;
    private readonly EquipSystem _equipSystem;
    private readonly DropItemSystem _dropItemSystem;
    private readonly ItemSumCountSystem _itemSumCountSystem;
    private readonly HeroBagExchangeSystem _heroBagExchangeSystem;
    private readonly BagUseSystem _bagUseSystem;
    private readonly PickupSystem _pickupSystem;
    private readonly InventoryQuerySystem _inventoryQuerySystem;
    private readonly UserNameQuerySystem _userNameQuerySystem;
    private readonly SpellCastSystem _spellCastSystem;
    private readonly ChatSendSystem _chatSendSystem;
    private readonly SoftCloseSystem _softCloseSystem;
    private readonly QueryValueSendSystem _queryValueSendSystem;
    private readonly ActSendSystem _actSendSystem;
    private readonly AutoReconnectSystem _autoReconnectSystem;
    private readonly KeyboardMoveSystem _keyboardMoveSystem;
    private readonly AutoMoveSendSystem _autoMoveSendSystem;
    private readonly AutoMoveStartSystem _autoMoveStartSystem;
    private readonly BasicHitSystem _basicHitSystem;
    private readonly DoorOverrideSystem _doorOverrideSystem;
    private readonly StallSystem _stallSystem;
    private readonly UserStallSystem _userStallSystem;
    private readonly MarketSystem _marketSystem;

    private readonly Queue<PackDataImageKey> _mapTilePrefetchQueue = new();
    private readonly HashSet<PackDataImageKey> _mapTilePrefetchSet = new(PackDataImageKeyComparer.Instance);
    private readonly Queue<WilImageKey> _mapWilPrefetchQueue = new();
    private readonly HashSet<WilImageKey> _mapWilPrefetchSet = new(WilImageKeyComparer.Instance);
    private DrawingSize _mapTilePrefetchLogicalSize;
    private string _mapTilePrefetchResourceRoot = string.Empty;
    private bool _mapTilePrefetchDirty = true;
    private int _mapEffectAction;
    private readonly Dictionary<string, bool> _dataFileExists = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<PackDataImageKey, (short Px, short Py)> _dataImagePivots = new(PackDataImageKeyComparer.Instance);
    private readonly Dictionary<WilImageKey, (short Px, short Py)> _wilImagePivots = new(WilImageKeyComparer.Instance);
    private readonly Dictionary<int, (long LastActionStartTimestamp, byte RushDir)> _humanRushDirStates = new();
    private readonly Dictionary<int, MoveTimingState> _moveTimingStates = new();
    private const long CorpseTtlMs = 25_000;
    private readonly object _corpseLock = new();
    private readonly Dictionary<int, CorpseMarker> _corpseMarkers = new();
    private readonly List<int> _corpseRemoveKeys = new(32);
    private readonly List<ActorMarker> _corpseDrawActors = new(64);
    private long _lastCorpsePruneMs;
    private readonly List<NameDrawInfo> _nameDrawList = new(64);
    private readonly List<NameDrawInfo> _uiTextDrawList = new(160);
    private readonly List<DrawingRectangle> _nameUiOcclusionRects = new(24);
    private readonly List<MagicEffDrawInfo> _magicEffDraws = new(96);
    private readonly List<ActorDrawInfo> _actorDraws = new(128);
    private readonly List<MerchantClickPoint> _merchantClickPoints = new(32);
    private readonly List<MerchantMenuClickPoint> _merchantMenuClickPoints = new(32);
    private DrawingRectangle? _merchantDialogPanelRect;
    private DrawingRectangle? _merchantDialogCloseRect;
    private DrawingRectangle? _merchantMenuPanelRect;
    private DrawingRectangle? _merchantSellPanelRect;
    private DrawingRectangle? _merchantSellOkRect;
    private DrawingRectangle? _merchantSellCloseRect;
    private DrawingRectangle? _merchantSellSpotRect;
    private ClientItem _merchantSellItem;
    private int _merchantSellLastMerchantId;
    private MirMerchantMode _merchantSellLastMode;
    private int _merchantDialogTopLine;
    private int _merchantDialogLastMerchantId;
    private string _merchantDialogLastSaying = string.Empty;
    private int _merchantDialogLastVisibleLines;
    private int _merchantDialogLastTotalLines;
    private readonly List<BookClickPoint> _bookClickPoints = new(8);
    private DrawingRectangle? _bookPanelRect;
    private readonly List<RefineClickPoint> _refineClickPoints = new(8);
    private DrawingRectangle? _refinePanelRect;
    private readonly List<BoxClickPoint> _boxClickPoints = new(16);
    private DrawingRectangle? _boxPanelRect;
    private readonly List<YbDealClickPoint> _ybDealClickPoints = new(16);
    private DrawingRectangle? _ybDealPanelRect;
    private readonly List<ChatLine> _chatLines = new(64);
    private int _chatBoardTop;
    private int _chatWindowLines = 12;
    private bool _chatStatusLarge;
    private readonly object _sysMessageLock = new();
    private readonly List<SysMessageLine> _sysMsgTop = new(12);
    private readonly List<SysMessageLine> _sysMsgBottom = new(12);
    private readonly List<SysMessageLine> _sysMsgBottomRight = new(12);
    private readonly List<SysMarqueeEntry> _sysMarquee = new(6);
    private bool _heroEnergyFlashOn = true;
    private long _heroEnergyFlashTickMs;
    private readonly MirSoundManager _soundManager = new();
    private readonly Dictionary<string, float> _halfTextWidthCache = new(StringComparer.Ordinal);
    private float _nameTextHeightBackBuffer;
    private readonly ItemDescTable _itemDescTable = new();
    private readonly MapDescTable _mapDescTable = new();
    private readonly ItemFilterStore _itemFilterStore = new();

    private bool _bagWindowVisible;
    private bool _heroBagView;
    private bool _stateWindowVisible;
    private int _stateWindowPage;
    private int _stateMagicPage;
    private DrawingRectangle? _stateMagicPageUpRect;
    private DrawingRectangle? _stateMagicPageDownRect;
    private readonly List<StateMagicClickPoint> _stateMagicClickPoints = new(8);
    private readonly List<StateMagicKeyClickPoint> _stateMagicKeyClickPoints = new(32);
    private DrawingRectangle? _stateMagicKeyPanelRect;
    private DrawingRectangle? _stateMagicKeyCloseRect;
    private bool _stateMagicKeyDialogOpen;
    private bool _stateMagicKeyDialogHero;
    private ushort _stateMagicKeyDialogMagicId;
    private DrawingRectangle? _bagPanelRect;
    private DrawingRectangle? _bagCloseRect;
    private DrawingRectangle? _heroBagPanelRect;
    private DrawingRectangle? _heroBagCloseRect;
    private DrawingRectangle? _statePanelRect;
    private DrawingRectangle? _stateCloseRect;
    private DrawingRectangle? _bottomMagicHotbarRect;
    private DrawingRectangle? _bottomMiniMapButtonRect;
    private DrawingRectangle? _bottomTradeButtonRect;
    private DrawingRectangle? _bottomGuildButtonRect;
    private DrawingRectangle? _bottomGroupButtonRect;
    private bool _settingsWindowVisible;
    private DrawingRectangle? _settingsPanelRect;
    private DrawingRectangle? _settingsCloseRect;
    private readonly List<SettingsClickPoint> _settingsClickPoints = new(8);

    private readonly List<MallClickPoint> _mallClickPoints = new(80);
    private DrawingRectangle? _mallPanelRect;
    private DrawingRectangle? _mallCloseRect;
    private DrawingRectangle? _mallToggleButtonRect;
    private bool _mallWindowVisible;
    private int _mallSelectedClass;
    private int _mallPage;
    private byte _mallSelectedItemClass = byte.MaxValue;
    private int _mallSelectedItemIndex = -1;
    private long _mallLastRequestMs;

    private bool _bagWindowPosSet;
    private int _bagWindowPosX;
    private int _bagWindowPosY;
    private bool _heroBagWindowPosSet;
    private int _heroBagWindowPosX;
    private int _heroBagWindowPosY;
    private bool _stateWindowPosSet;
    private int _stateWindowPosX;
    private int _stateWindowPosY;
    private bool _merchantDialogPosSet;
    private int _merchantDialogPosX;
    private int _merchantDialogPosY;
    private bool _settingsWindowPosSet;
    private int _settingsWindowPosX;
    private int _settingsWindowPosY;
    private bool _mallWindowPosSet;
    private int _mallWindowPosX;
    private int _mallWindowPosY;

    private UiWindowDragTarget _uiWindowDragTarget;
    private int _uiWindowDragOffsetX;
    private int _uiWindowDragOffsetY;
    private int _uiWindowDragW;
    private int _uiWindowDragH;
    private bool _bagLoadedFromServer;
    private IReadOnlyList<(int X, int Y)> _wayPoints = Array.Empty<(int X, int Y)>();
    private bool _itemDragActive;
    private ItemDragSource _itemDragSource;
    private int _itemDragSourceIndex = -1;
    private bool _itemDragHero;
    private ClientItem _itemDragItem;
    private readonly ClientItem[] _refineItems = new ClientItem[3];
    private int _refinePendingTakeOffSlot = -1;
    private int _refinePendingTakeOffMakeIndex;
    private long _refinePendingTakeOffSinceMs;
    private long _refineLastSendMs;
    private int _testReceiveCount;
    private string _uiTooltipText = string.Empty;
    private Vector2 _uiTooltipLogicalPos;
    private float _uiTooltipWidthBackBuffer;
    private float _uiTooltipHeightBackBuffer;
    private bool _magicWindowVisible;
    private bool _magicWindowHeroView;
    private int _magicWindowTopIndex;
    private int _magicWindowPageSize = 16;

    private DrawingSize _lastBackBufferSize;
    private DrawingSize _lastLogicalSize;
    private Vector2 _lastViewScale;
    private Vector2 _lastViewOffset;
    private DrawingRectangle _lastViewportRect;
    private int _lastDrawCalls;
    private int _lastTextureBinds;
    private int _lastSprites;
    private int _lastScissorChanges;

    private const long HoldMoveStartDelayMs = 300;
    private const long HoldMoveUpdateIntervalMs = 120;
    private int _lastMouseClientX;
    private int _lastMouseClientY;
    private bool _holdMoveActive;
    private bool _holdMoveWantsRun;
    private long _holdMoveStartMs;
    private long _holdMoveLastUpdateMs;
    private int _holdMoveLastMapX = int.MinValue;
    private int _holdMoveLastMapY = int.MinValue;

    public Main(string[]? args = null)
    {
        InitializeComponent();
        _isCurrentMapWalkable = IsCurrentMapWalkable;

        Text = "Legend of mir2";
        KeyPreview = true;
        _perfWindowStartTimestamp = Stopwatch.GetTimestamp();

        _serverMessagePump = new MirMessagePump(_session);
        _serverMessagePump.HandlerError += ex => AppendLog($"[msg] handler error: {ex.GetType().Name}: {ex.Message}");
        _maketSystem = new MaketSystem(_session, _world, AppendLog);
        _levelRankSystem = new LevelRankSystem(_session, _world, _commandThrottle, AppendLog);
        _boxSystem = new BoxSystem(_session, _world, AppendLog);
        _bookSystem = new BookSystem(_session, _world, AppendLog);
        _ybDealSystem = new YbDealSystem(_session, _world, AppendLog);
        _itemDialogSystem = new ItemDialogSystem(_session, _world, AppendLog);
        _bindDialogSystem = new BindDialogSystem(_session, _world, AppendLog);
        _treasureDialogSystem = new TreasureDialogSystem(_session, _world, AppendLog);
        _guildSystem = new GuildSystem(_session, _world, AppendLog);
        _groupSystem = new GroupSystem(_session, _world, AppendLog);
        _seriesSkillSystem = new SeriesSkillSystem(_session, _world, AppendLog);
        _dealSystem = new DealSystem(_session, _world, AppendLog);
        _storageSystem = new StorageSystem(_session, _world, AppendLog);
        _merchantTradeSystem = new MerchantTradeSystem(_session, _world, AppendLog);
        _merchantMenuSystem = new MerchantMenuSystem(_session, _world, AppendLog);
        _merchantDialogSystem = new MerchantDialogSystem(_session, _world, AppendLog);
        _inventoryPendingSystem = new InventoryPendingSystem(_world, AppendLog);
        _equipSystem = new EquipSystem(_session, _world, _inventoryPendingSystem, AppendLog);
        _dropItemSystem = new DropItemSystem(_session, _inventoryPendingSystem, AppendLog);
        _itemSumCountSystem = new ItemSumCountSystem(_session, _world, _inventoryPendingSystem, AppendLog);
        _heroBagExchangeSystem = new HeroBagExchangeSystem(_session, _world, _inventoryPendingSystem, AppendLog);
        _bagUseSystem = new BagUseSystem(_session, _world, _inventoryPendingSystem, AppendLog);
        _pickupSystem = new PickupSystem(_session, _commandThrottle, AppendLog);
        _inventoryQuerySystem = new InventoryQuerySystem(_session, AppendLog);
        _userNameQuerySystem = new UserNameQuerySystem(_session, _world);
        _spellCastSystem = new SpellCastSystem(_session, _commandThrottle, _autoMoveSystem, AppendLog);
        _chatSendSystem = new ChatSendSystem(_session, AppendLog);
        _softCloseSystem = new SoftCloseSystem(_session);
        _queryValueSendSystem = new QueryValueSendSystem(_session, AppendLog);
        _actSendSystem = new ActSendSystem(_session, _world, AppendLog);
        _autoReconnectSystem = new AutoReconnectSystem(AppendLog);
        _keyboardMoveSystem = new KeyboardMoveSystem(_commandThrottle, _autoMoveSystem, _actSendSystem);
        _autoMoveSendSystem = new AutoMoveSendSystem(_autoMoveSystem, _commandThrottle, _actSendSystem);
        _autoMoveStartSystem = new AutoMoveStartSystem(_autoMoveSystem, _autoMoveSendSystem, AppendLog);
        _basicHitSystem = new BasicHitSystem(_commandThrottle, _autoMoveSystem, _actSendSystem, AppendLog);
        _targetingActionSystem = new TargetingActionSystem(_targetingSystem, _autoMoveSystem, AppendLog);
        _doorOverrideSystem = new DoorOverrideSystem(_world);
        _stallSystem = new StallSystem(_maketSystem, _world, AppendLog);
        _userStallSystem = new UserStallSystem(_maketSystem, _world, AppendLog);
        _marketSystem = new MarketSystem(_maketSystem, _world, AppendLog);
        if (int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_TARGET_FPS"), out int targetFps) && targetFps is > 0 and <= 1000)
            _targetFps = targetFps;

        _limitFpsWhenVSyncOff = Environment.GetEnvironmentVariable("MIRCLIENT_LIMIT_FPS") != "0";
        _perfLogger = MirPerfCsvLogger.TryCreateFromEnvironment(AppendLog);

        _logicThreadEnabled = Environment.GetEnvironmentVariable("MIRCLIENT_LOGIC_THREAD") == "1";
        if (_logicThreadEnabled)
        {
            _logicLoop = new MirLogicLoop(
                (ts, ms) =>
                {
                    if (_reconnectInProgress || !_running)
                        return true;

                    RunLogicFrame(ts, ms);
                    return true;
                },
                _targetFps);
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_MAX_PACKETS_PER_FRAME"), out int maxPackets) && maxPackets is > 0 and <= 10_000)
            _serverMessagePump.MaxPacketsPerPump = maxPackets;

        if (int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_PACKET_PUMP_BUDGET_MS"), out int budgetMs) && budgetMs is >= 0 and <= 50)
            _serverMessagePump.BudgetMs = budgetMs;

        if (long.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_CPU_CACHE_MB"), out long cpuMb) && cpuMb is > 0 and <= 8192)
            _cpuCacheBytes = cpuMb * 1024L * 1024L;

        if (long.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_GPU_CACHE_MB"), out long gpuMb) && gpuMb is > 0 and <= 8192)
            _gpuCacheBytes = gpuMb * 1024L * 1024L;

        if (int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_SPRITE_BATCH_MAX_SPRITES"), out int maxSprites) &&
            maxSprites is >= 512 and <= 131_072)
        {
            _spriteBatchMaxSprites = maxSprites;
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_ASSET_DECODE_CONCURRENCY"), out int decodeConcurrency) &&
            decodeConcurrency is >= 0 and <= 64)
        {
            _assetDecodeConcurrency = decodeConcurrency;
        }

        SplitCacheBudgets();

        _wilImageCache = new WilImageCache(new WilImageCacheOptions(_cpuWilCacheBytes, MaxConcurrentDecodes: _assetDecodeConcurrency));
        _packDataImageCache = new PackDataImageCache(new PackDataImageCacheOptions(_cpuDataCacheBytes, MaxConcurrentDecodes: _assetDecodeConcurrency));

        _wilPath = Environment.GetEnvironmentVariable("MIRCLIENT_WIL");
        if (!int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_WIL_INDEX"), out _wilIndex))
            _wilIndex = 0;

        _dataPath = Environment.GetEnvironmentVariable("MIRCLIENT_DATA");
        if (!int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_DATA_INDEX"), out _dataIndex))
            _dataIndex = 0;

        _mapPath = Environment.GetEnvironmentVariable("MIRCLIENT_MAP");

        _mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 900,
            FixedPanel = FixedPanel.Panel2
        };

        _mainSplit.Panel1.Controls.Add(_renderControl);
        _renderControl.TabStop = true;
        _renderControl.MouseDown += (_, _) => _renderControl.Focus();

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        right.Controls.Add(_txtLog);
        right.Controls.Add(_lblSceneStage);
        right.Controls.Add(_lstCharacters);
        right.Controls.Add(_lblCharacters);
        right.Controls.Add(_lstServers);
        right.Controls.Add(_lblServers);
        right.Controls.Add(_btnDisconnect);
        right.Controls.Add(_btnLogin);
        right.Controls.Add(_btnCreateCharacter);
        right.Controls.Add(_txtCharacter);
        right.Controls.Add(_txtPassword);
        right.Controls.Add(_txtAccount);
        right.Controls.Add(_btnDecode);
        right.Controls.Add(_lblStartup);
        right.Controls.Add(_txtClientParam);
        _mainSplit.Panel2.Controls.Add(right);
        _mainSplit.Panel2Collapsed = true;

        Controls.Add(_mainSplit);

        _btnDecode.Click += (_, _) => DecodeClientParamStr();
        _btnCreateCharacter.Click += async (_, _) => await CreateCharacterAsync();
        _btnLogin.Click += async (_, _) => await AdvanceLoginFlowAsync();
        _btnDisconnect.Click += async (_, _) =>
        {
            DisconnectBehavior behavior = DisconnectBehavior.None;
            if (_session.Stage is MirSessionStage.RunGate or MirSessionStage.InGame && _session.CanReconnectToSelGate)
                behavior = DisconnectBehavior.ReturnToSelectCharacter;
            await DisconnectAsync(behavior);
        };
        _session.Log += AppendLog;
        _session.StageChanged += _ =>
        {
            if (IsDisposed || Disposing)
                return;

            if (InvokeRequired)
            {
                try { BeginInvoke(UpdateLoginActionButton); } catch {  }
                return;
            }

            UpdateLoginActionButton();
        };
        UpdateLoginActionButton();

        _soundManager.Log = AppendLog;
        _soundManager.Enabled = Environment.GetEnvironmentVariable("MIRCLIENT_AUDIO") != "0";

        if (float.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_BGM_VOLUME"), out float bgmVolume))
            _soundManager.SetMusicVolume(bgmVolume);

        if (float.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_SFX_VOLUME"), out float sfxVolume))
            _soundManager.SetSfxVolume(sfxVolume);

        _miniMapRequestSystem = new MiniMapRequestSystem(_session, AppendLog);
        _viewRangeSystem = new ViewRangeSystem(_session, AppendLog);

        _sceneContext = new MirSceneContext(_session, _world, AppendLog);
        _loginScene = new LoginScene(_sceneContext);
        _selectCountryScene = new SelectCountryScene(_sceneContext);
        _selectServerScene = new SelectServerScene(_sceneContext);
        _selectCharacterScene = new SelectCharacterScene(_sceneContext);
        _loadingScene = new LoadingScene(_sceneContext);
        _loginNoticeScene = new LoginNoticeScene(_sceneContext);

        _sceneManager.Register(MirSceneId.Intro, new IntroScene(_sceneContext));
        _sceneManager.Register(MirSceneId.Login, _loginScene);
        _sceneManager.Register(MirSceneId.SelectCountry, _selectCountryScene);
        _sceneManager.Register(MirSceneId.SelectServer, _selectServerScene);
        _sceneManager.Register(MirSceneId.SelectCharacter, _selectCharacterScene);
        _sceneManager.Register(MirSceneId.Loading, _loadingScene);
        _sceneManager.Register(MirSceneId.LoginNotice, _loginNoticeScene);
        _sceneManager.Register(MirSceneId.Play, new PlayScene(_sceneContext));
        _sceneManager.SceneChanged += (_, to) => ApplyHostUiForScene(to);
        _sceneManager.Switch(MirSceneId.Intro);

        _lstServers.SelectedIndexChanged += (_, _) =>
        {
            if (_lstServers.SelectedItem is not ServerListItem item)
                return;

            if (_startup == null)
                return;

            if (string.IsNullOrWhiteSpace(item.Name))
                return;

            string trimmed = item.Name.Trim();
            if (string.Equals(_startup.ServerName, trimmed, StringComparison.OrdinalIgnoreCase))
                return;

            _startup = _startup with { ServerName = trimmed };
            _selectedServerName = trimmed;
            _lblStartup.Text = $"LoginGate: {_startup.ServerAddress}:{_startup.ServerPort}  ServerName: {_startup.ServerName}  ResDir: {_startup.ResourceDir}";
        };

        _lstCharacters.SelectedIndexChanged += (_, _) =>
        {
            if (_lstCharacters.SelectedItem is not string s)
                return;

            string name = s.Trim();
            if (name.StartsWith('*'))
                name = name[1..].Trim();

            if (string.IsNullOrWhiteSpace(name))
                return;

            _txtCharacter.Text = name;
            _selectedCharacterName = name;
            _selectCharacterScene.SelectCharacter(name);
        };

        _stageSceneRouter = new MirStageSceneRouter(
            _session,
            _sceneManager,
            dispatch: action =>
            {
                if (IsDisposed || Disposing)
                    return;

                if (InvokeRequired)
                {
                    try { BeginInvoke(action); } catch {  }
                    return;
                }

                action();
            },
            log: AppendLog);

        _renderControl.MouseDown += async (_, e) => await HandleRenderControlMouseDownAsync(e);
        _renderControl.MouseMove += (_, e) => HandleRenderControlMouseMove(e);
        _renderControl.MouseUp += (_, e) => HandleRenderControlMouseUp(e);
        _renderControl.MouseLeave += (_, _) => EndUiWindowDrag();
        _renderControl.MouseWheel += (_, e) => HandleRenderControlMouseWheel(e);
        _renderControl.KeyPress += (_, e) => HandleRenderControlKeyPress(e.KeyChar);

        string? clientParam = args is { Length: > 0 } ? args[0] : null;
        if (!string.IsNullOrWhiteSpace(clientParam))
        {
            _txtClientParam.Text = clientParam;
            DecodeClientParamStr();
        }

        EnsureStartupInfo("default");
    }

    private async Task CreateCharacterAsync()
    {
        if (_startup == null)
        {
            AppendLog("[ui] decode ClientParamStr first.");
            return;
        }

        (UiTextPromptResult result, string value) = await PromptTextAsync(
            title: "Create Character",
            prompt: $"Character name (GBK max {Grobal2.ActorNameLen} bytes)",
            buttons: UiTextPromptButtons.OkCancel,
            maxGbkBytes: Grobal2.ActorNameLen).ConfigureAwait(true);

        if (result != UiTextPromptResult.Ok)
            return;

        string trimmed = value;

        if (string.IsNullOrWhiteSpace(trimmed))
            return;

        _txtCharacter.Text = trimmed;
        _selectCharacterScene.SelectCharacter(trimmed);
        await LoginAsync(createIfMissing: true);
    }

    private void ApplyHostUiForScene(MirSceneId sceneId)
    {
        if (IsDisposed || Disposing)
            return;

        Text = sceneId == MirSceneId.Intro ? "MirClient (D3D11)" : $"MirClient (D3D11) - {sceneId}";
        _lblSceneStage.Text = $"Stage: {_session.Stage}  Scene: {sceneId}  Connected: {_session.IsConnected}";

        if (sceneId == MirSceneId.Play)
        {
            try { _renderControl.Focus(); } catch {  }
        }
    }

    private void ApplyLoginResultToUi(MirLoginResult result)
    {
        _lstServers.BeginUpdate();
        try
        {
            _lstServers.Items.Clear();
            IReadOnlyList<ServerListItem> servers = ParseServerListRaw(result.ServerListRaw);
            foreach (ServerListItem s in servers)
                _lstServers.Items.Add(s);

            int selectedIndex = -1;
            for (int i = 0; i < servers.Count; i++)
            {
                if (string.Equals(servers[i].Name, result.ServerName, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                    break;
                }
            }

            if (selectedIndex >= 0)
                _lstServers.SelectedIndex = selectedIndex;
        }
        finally
        {
            _lstServers.EndUpdate();
        }

        _lstCharacters.BeginUpdate();
        try
        {
            _lstCharacters.Items.Clear();

            int selectedIndex = -1;
            for (int i = 0; i < result.Characters.Count; i++)
            {
                MirCharacterInfo c = result.Characters[i];
                string display = c.Selected ? $"* {c.Name}" : c.Name;
                _lstCharacters.Items.Add(display);
                if (selectedIndex < 0 &&
                    string.Equals(c.Name, result.SelectedCharacterName, StringComparison.OrdinalIgnoreCase))
                {
                    selectedIndex = i;
                }
            }

            if (selectedIndex >= 0 && (uint)selectedIndex < (uint)_lstCharacters.Items.Count)
                _lstCharacters.SelectedIndex = selectedIndex;
        }
        finally
        {
            _lstCharacters.EndUpdate();
        }
    }

    private static IReadOnlyList<ServerListItem> ParseServerListRaw(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<ServerListItem>();

        string[] parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return Array.Empty<ServerListItem>();

        var list = new List<ServerListItem>(parts.Length / 2);
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            string name = parts[i].Trim();
            string status = parts[i + 1].Trim();
            if (name.Length == 0)
                continue;
            list.Add(new ServerListItem(name, status));
        }

        return list;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _running = true;
        Application.Idle += OnApplicationIdle;
        if (_logicThreadEnabled)
            _logicLoop?.Start();
        _soundManager.Initialize(Handle);
        _soundManager.SetResourceRoot(GetResourceRootDir());
        BeginIntroSplash();
    }
    private bool _startupScreenApplied;

    private void BeginIntroSplash()
    {
        if (IsDisposed || Disposing)
            return;

        HideLoginUi();

        _introSplashActive = true;
        _introSplashStartMs = Environment.TickCount64;
        _introSplashEndMs = _introSplashStartMs + IntroSplashDurationMs;

        _sceneManager.Switch(MirSceneId.Intro);
    }

    private void RequestIntroSplashSkip()
    {
        if (!_introSplashActive)
            return;

        long nowMs = Environment.TickCount64;
        long targetEnd = nowMs + IntroSplashSkipDelayMs;
        if (_introSplashEndMs > targetEnd)
            _introSplashEndMs = targetEnd;
    }

    private void TickIntroSplash(long nowMs)
    {
        if (!_introSplashActive)
            return;

        if (nowMs < _introSplashEndMs)
            return;

        _introSplashActive = false;
        _sceneManager.Switch(MirSceneId.Login);
        BeginPromptLogin();
    }

    private void BeginPromptLogin()
    {
        if (IsDisposed || Disposing)
            return;

        ShowLoginUi(LoginUiScreen.Login);
    }

    private void ToggleDebugPanel()
    {
        if (IsDisposed || Disposing)
            return;

        _mainSplit.Panel2Collapsed = !_mainSplit.Panel2Collapsed;
    }

    private void ToggleBorderlessFullscreen(string source)
    {
        if (IsDisposed || Disposing)
            return;

        if (_isBorderlessFullscreen)
        {
            FormBorderStyle = _prevBorderStyle;
            WindowState = _prevWindowState;
            TopMost = _prevTopMost;
            _isBorderlessFullscreen = false;
            AppendLog($"[ui] fullscreen off ({source})");
            return;
        }

        _prevBorderStyle = FormBorderStyle;
        _prevWindowState = WindowState;
        _prevTopMost = TopMost;

        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Maximized;
        TopMost = true;
        _isBorderlessFullscreen = true;
        AppendLog($"[ui] fullscreen on ({source})");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.Alt) != 0 && (keyData & Keys.KeyCode) == Keys.Enter)
        {
            ToggleBorderlessFullscreen("Alt+Enter");
            return true;
        }

        if (_introSplashActive)
            RequestIntroSplashSkip();

        if (keyData == (Keys.Control | Keys.F12))
        {
            ToggleDebugPanel();
            return true;
        }

        
        if (keyData == Keys.F12 && _session.Stage != MirSessionStage.InGame)
        {
            ToggleDebugPanel();
            return true;
        }

        if (keyData == Keys.F2 && _session.Stage == MirSessionStage.Idle && !_introSplashActive)
        {
            BeginPromptLogin();
            return true;
        }

        if (TryHandleLoginUiCmdKey(keyData))
            return true;

        
        if (LoginUiVisible || _introSplashActive || _sceneManager.CurrentId != MirSceneId.Play)
            return base.ProcessCmdKey(ref msg, keyData);

        
        if (!_mainSplit.Panel2Collapsed && !_renderControl.Focused)
            return base.ProcessCmdKey(ref msg, keyData);

        bool handled;
        lock (_logicSync)
            handled = TryHandleInGameKey(keyData);
        if (handled)
            return true;

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void HandleRenderControlKeyPress(char keyChar)
    {
        if (_introSplashActive)
        {
            RequestIntroSplashSkip();
            return;
        }

        if (LoginUiVisible)
        {
            HandleLoginUiKeyPress(keyChar);
            return;
        }

        if (!_chatInputActive)
        {
            if (_sceneManager.CurrentId != MirSceneId.Play)
                return;

            if (keyChar == ' ')
            {
                OpenChatInput(string.Empty);
                return;
            }

            if (keyChar is '@' or '!' or '/')
            {
                if (keyChar == '/')
                    OpenChatInput(_whisperName.Length > 2 ? $"/{_whisperName} " : "/");
                else
                    OpenChatInput(keyChar.ToString());
                return;
            }

            return;
        }

        if (char.IsControl(keyChar))
            return;

        _uiChatInput.Append(keyChar.ToString());
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _running = false;
        Application.Idle -= OnApplicationIdle;
        _logicLoop?.Stop();
        _perfLogger?.Dispose();
        _perfLogger = null;

        _stageSceneRouter.Dispose();

        _loginCts?.Cancel();
        _loginCts?.Dispose();
        _loginCts = null;
        _ = _session.DisposeAsync();

        _spriteBatch?.Dispose();
        _spriteBatch = null;
        if (!_demoTextureOwnedByCache)
            _demoTexture?.Dispose();
        _demoTexture = null;
        _whiteTexture?.Dispose();
        _whiteTexture = null;
        _fogLightMapTexture?.Dispose();
        _fogLightMapTexture = null;
        for (int i = 0; i < _fogLightTextures.Length; i++)
        {
            _fogLightTextures[i]?.Dispose();
            _fogLightTextures[i] = null;
            _fogLightTextureLoadFailed[i] = false;
        }
        _textRenderer?.Dispose();
        _textRenderer = null;
        _wilTextureCache?.Dispose();
        _wilTextureCache = null;
        _dataTextureCache?.Dispose();
        _dataTextureCache = null;
        _wilImageCache.Dispose();
        _packDataImageCache.Dispose();
        _soundManager.Dispose();

        base.OnFormClosed(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_session.Stage is MirSessionStage.RunGate or MirSessionStage.InGame)
        {
            TrySaveClientSetForCurrentCharacter();
            TrySaveItemFilterOverrides();
            TrySaveWayPointForCurrentMap();
            TrySaveBagCache();
            _softCloseSystem.TrySendAsync().GetAwaiter().GetResult();

            try
            {
                _session.DisconnectAsync().GetAwaiter().GetResult();
            }
            catch
            {
                
            }
        }

        base.OnFormClosing(e);
    }

    private void OnApplicationIdle(object? sender, EventArgs e)
    {
        while (_running && AppStillIdle)
        {
            long frameStart = Stopwatch.GetTimestamp();
            long nowMs = Environment.TickCount64;
            if (!_logicThreadEnabled)
                RunLogicFrame(frameStart, nowMs);
            _renderControl.Render(new Color4(0.06f, 0.06f, 0.12f, 1.0f), frame =>
            {
                lock (_logicSync)
                {
                EnsureDemoResources(frame);
                _nameDrawList.Clear();
                _uiTextDrawList.Clear();
                _merchantClickPoints.Clear();
                _merchantDialogPanelRect = null;
                _merchantDialogCloseRect = null;
                _merchantMenuClickPoints.Clear();
                _merchantMenuPanelRect = null;
                _merchantSellPanelRect = null;
                _merchantSellOkRect = null;
                _merchantSellCloseRect = null;
                _merchantSellSpotRect = null;
                _bookClickPoints.Clear();
                _bookPanelRect = null;
                _refineClickPoints.Clear();
                _refinePanelRect = null;
                _boxClickPoints.Clear();
                _boxPanelRect = null;
                _ybDealClickPoints.Clear();
                _ybDealPanelRect = null;
                _uiTooltipText = string.Empty;
                _uiTooltipWidthBackBuffer = 0f;
                _uiTooltipHeightBackBuffer = 0f;

                if (_spriteBatch == null || _demoTexture == null)
                    return;

                using var wilTexFrame = _wilTextureCache?.BeginFrame();
                using var dataTexFrame = _dataTextureCache?.BeginFrame();

                var logicalSize = _startup is { ScreenWidth: > 0, ScreenHeight: > 0 }
                    ? new DrawingSize(_startup.ScreenWidth, _startup.ScreenHeight)
                    : frame.BackBufferSize;

                D3D11ViewTransform view = D3D11ViewTransform.Create(frame.BackBufferSize, logicalSize, D3D11ScaleMode.IntegerFit);
                int w = view.LogicalSize.Width;
                int h = view.LogicalSize.Height;

                _lastBackBufferSize = frame.BackBufferSize;
                _lastLogicalSize = view.LogicalSize;
                _lastViewScale = view.Scale;
                _lastViewOffset = view.Offset;
                _lastViewportRect = view.ViewportRect;

                TickIntroSplash(nowMs);
                if (_introSplashActive && TryDrawIntroSplash(frame, view, out _))
                {
                    frame.Context.Flush();
                    return;
                }

                float t = Environment.TickCount64 / 1000.0f;
                int dx = (int)(MathF.Sin(t) * 24);
                int dy = (int)(MathF.Cos(t * 0.9f) * 18);

                int texW = _demoTexture.Width;
                int texH = _demoTexture.Height;
                int drawW = Math.Min(512, texW);
                int drawH = Math.Min(512, texH);

                int drawCalls = 0;
                int textureBinds = 0;
                int sprites = 0;
                int scissorChanges = 0;

                bool drewScene = false;
                bool drewMapTiles = false;

                if (_map != null)
                {
                    drewMapTiles = TryDrawMapTiles(frame, view, out SpriteBatchStats mapStats);
                    if (drewMapTiles)
                    {
                        drawCalls += mapStats.DrawCalls;
                        textureBinds += mapStats.TextureBinds;
                        sprites += mapStats.Sprites;
                        scissorChanges += mapStats.ScissorChanges;
                        drewScene = true;
                    }
                    else if (_whiteTexture != null)
                    {
                        const int cellPixels = 8;
                        int cellsX = Math.Min(_map.Width, w / cellPixels);
                        int cellsY = Math.Min(_map.Height, h / cellPixels);

                        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
                        for (int y = 0; y < cellsY; y++)
                        {
                            for (int x = 0; x < cellsX; x++)
                            {
                                bool walkable = IsCurrentMapWalkable(x, y);
                                var c = walkable
                                    ? new Color4(0.08f, 0.12f, 0.18f, 0.35f)
                                    : new Color4(0.95f, 0.2f, 0.2f, 0.85f);

                                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(x * cellPixels, y * cellPixels, cellPixels, cellPixels), color: c);
                            }
                        }
                        _spriteBatch.End();

                        SpriteBatchStats overlayStats = _spriteBatch.Stats;
                        drawCalls += overlayStats.DrawCalls;
                        textureBinds += overlayStats.TextureBinds;
                        sprites += overlayStats.Sprites;
                        scissorChanges += overlayStats.ScissorChanges;
                        drewScene = true;
                    }
                }

                if (!drewScene && TryDrawLoginNoticeBackground(frame, view, out SpriteBatchStats loginNoticeBgStats))
                {
                    drawCalls += loginNoticeBgStats.DrawCalls;
                    textureBinds += loginNoticeBgStats.TextureBinds;
                    sprites += loginNoticeBgStats.Sprites;
                    scissorChanges += loginNoticeBgStats.ScissorChanges;
                    drewScene = true;
                }

                if (!drewScene)
                {
                    var clip = DrawingRectangle.Intersect(
                        new DrawingRectangle(80, 80, 380, 220),
                        new DrawingRectangle(0, 0, w, h));

                    _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend, clip);
                    _spriteBatch.Draw(_demoTexture, new DrawingRectangle(40 + dx, 40 + dy, drawW, drawH));
                    _spriteBatch.End();

                    SpriteBatchStats demoStats = _spriteBatch.Stats;
                    drawCalls += demoStats.DrawCalls;
                    textureBinds += demoStats.TextureBinds;
                    sprites += demoStats.Sprites;
                    scissorChanges += demoStats.ScissorChanges;
                }
                else if (!drewMapTiles && _map != null)
                {
                    _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
                    _spriteBatch.Draw(
                        _demoTexture,
                        new DrawingRectangle(Math.Max(0, w - 260), 60, 200, 200),
                        color: new Color4(1, 1, 1, 0.85f));
                    _spriteBatch.End();

                    SpriteBatchStats demoStats = _spriteBatch.Stats;
                    drawCalls += demoStats.DrawCalls;
                    textureBinds += demoStats.TextureBinds;
                    sprites += demoStats.Sprites;
                    scissorChanges += demoStats.ScissorChanges;
                }

                if (TryDrawHud(frame, view, out SpriteBatchStats hudStats))
                {
                    drawCalls += hudStats.DrawCalls;
                    textureBinds += hudStats.TextureBinds;
                    sprites += hudStats.Sprites;
                    scissorChanges += hudStats.ScissorChanges;
                }

                if (TryDrawBottomBarUi(frame, view, out SpriteBatchStats bottomStats))
                {
                    drawCalls += bottomStats.DrawCalls;
                    textureBinds += bottomStats.TextureBinds;
                    sprites += bottomStats.Sprites;
                    scissorChanges += bottomStats.ScissorChanges;
                }

                if (TryDrawMiniMapUi(frame, view, out SpriteBatchStats miniMapStats))
                {
                    drawCalls += miniMapStats.DrawCalls;
                    textureBinds += miniMapStats.TextureBinds;
                    sprites += miniMapStats.Sprites;
                    scissorChanges += miniMapStats.ScissorChanges;
                }

                if (TryDrawStateWindowUi(frame, view, out SpriteBatchStats stateStats))
                {
                    drawCalls += stateStats.DrawCalls;
                    textureBinds += stateStats.TextureBinds;
                    sprites += stateStats.Sprites;
                    scissorChanges += stateStats.ScissorChanges;
                }

                if (TryDrawInGameUi(frame, view, out SpriteBatchStats uiStats))
                {
                    drawCalls += uiStats.DrawCalls;
                    textureBinds += uiStats.TextureBinds;
                    sprites += uiStats.Sprites;
                    scissorChanges += uiStats.ScissorChanges;
                }

                if (TryDrawTreasureDialogUi(frame, view, out SpriteBatchStats treasureStats))
                {
                    drawCalls += treasureStats.DrawCalls;
                    textureBinds += treasureStats.TextureBinds;
                    sprites += treasureStats.Sprites;
                    scissorChanges += treasureStats.ScissorChanges;
                }

                if (TryDrawItemDialogUi(frame, view, out SpriteBatchStats itemDlgStats))
                {
                    drawCalls += itemDlgStats.DrawCalls;
                    textureBinds += itemDlgStats.TextureBinds;
                    sprites += itemDlgStats.Sprites;
                    scissorChanges += itemDlgStats.ScissorChanges;
                }

                if (TryDrawBindDialogUi(frame, view, out SpriteBatchStats bindDlgStats))
                {
                    drawCalls += bindDlgStats.DrawCalls;
                    textureBinds += bindDlgStats.TextureBinds;
                    sprites += bindDlgStats.Sprites;
                    scissorChanges += bindDlgStats.ScissorChanges;
                }

                if (TryDrawMerchantUi(frame, view, out SpriteBatchStats merchantStats))
                {
                    drawCalls += merchantStats.DrawCalls;
                    textureBinds += merchantStats.TextureBinds;
                    sprites += merchantStats.Sprites;
                    scissorChanges += merchantStats.ScissorChanges;
                }

                if (TryDrawMallUi(frame, view, out SpriteBatchStats mallStats))
                {
                    drawCalls += mallStats.DrawCalls;
                    textureBinds += mallStats.TextureBinds;
                    sprites += mallStats.Sprites;
                    scissorChanges += mallStats.ScissorChanges;
                }

                if (TryDrawYbDealUi(frame, view, out SpriteBatchStats ybStats))
                {
                    drawCalls += ybStats.DrawCalls;
                    textureBinds += ybStats.TextureBinds;
                    sprites += ybStats.Sprites;
                    scissorChanges += ybStats.ScissorChanges;
                }

                if (TryDrawGroupGuildUi(frame, view, out SpriteBatchStats guildStats))
                {
                    drawCalls += guildStats.DrawCalls;
                    textureBinds += guildStats.TextureBinds;
                    sprites += guildStats.Sprites;
                    scissorChanges += guildStats.ScissorChanges;
                }

                if (TryDrawLevelRankUi(frame, view, out SpriteBatchStats rankStats))
                {
                    drawCalls += rankStats.DrawCalls;
                    textureBinds += rankStats.TextureBinds;
                    sprites += rankStats.Sprites;
                    scissorChanges += rankStats.ScissorChanges;
                }

                if (TryDrawMagicWindowUi(frame, view, out SpriteBatchStats magicStats))
                {
                    drawCalls += magicStats.DrawCalls;
                    textureBinds += magicStats.TextureBinds;
                    sprites += magicStats.Sprites;
                    scissorChanges += magicStats.ScissorChanges;
                }

                if (TryDrawSeriesSkillUi(frame, view, out SpriteBatchStats seriesStats))
                {
                    drawCalls += seriesStats.DrawCalls;
                    textureBinds += seriesStats.TextureBinds;
                    sprites += seriesStats.Sprites;
                    scissorChanges += seriesStats.ScissorChanges;
                }

                if (TryDrawMissionUi(frame, view, out SpriteBatchStats missionStats))
                {
                    drawCalls += missionStats.DrawCalls;
                    textureBinds += missionStats.TextureBinds;
                    sprites += missionStats.Sprites;
                    scissorChanges += missionStats.ScissorChanges;
                }

                if (TryDrawOpenBoxUi(frame, view, out SpriteBatchStats boxStats))
                {
                    drawCalls += boxStats.DrawCalls;
                    textureBinds += boxStats.TextureBinds;
                    sprites += boxStats.Sprites;
                    scissorChanges += boxStats.ScissorChanges;
                }

                if (TryDrawBookUi(frame, view, out SpriteBatchStats bookStats))
                {
                    drawCalls += bookStats.DrawCalls;
                    textureBinds += bookStats.TextureBinds;
                    sprites += bookStats.Sprites;
                    scissorChanges += bookStats.ScissorChanges;
                }

                if (TryDrawRefineUi(frame, view, out SpriteBatchStats refineStats))
                {
                    drawCalls += refineStats.DrawCalls;
                    textureBinds += refineStats.TextureBinds;
                    sprites += refineStats.Sprites;
                    scissorChanges += refineStats.ScissorChanges;
                }

                if (TryDrawSettingsWindowUi(frame, view, out SpriteBatchStats settingsStats))
                {
                    drawCalls += settingsStats.DrawCalls;
                    textureBinds += settingsStats.TextureBinds;
                    sprites += settingsStats.Sprites;
                    scissorChanges += settingsStats.ScissorChanges;
                }

                if (TryDrawChatInputUi(frame, view, out SpriteBatchStats chatInputStats))
                {
                    drawCalls += chatInputStats.DrawCalls;
                    textureBinds += chatInputStats.TextureBinds;
                    sprites += chatInputStats.Sprites;
                    scissorChanges += chatInputStats.ScissorChanges;
                }

                if (TryDrawSystemNoticeBar(frame, view, out SpriteBatchStats sysNoticeStats))
                {
                    drawCalls += sysNoticeStats.DrawCalls;
                    textureBinds += sysNoticeStats.TextureBinds;
                    sprites += sysNoticeStats.Sprites;
                    scissorChanges += sysNoticeStats.ScissorChanges;
                }

                if (TryDrawStageTransitionMask(frame, view, out SpriteBatchStats stageMaskStats))
                {
                    drawCalls += stageMaskStats.DrawCalls;
                    textureBinds += stageMaskStats.TextureBinds;
                    sprites += stageMaskStats.Sprites;
                    scissorChanges += stageMaskStats.ScissorChanges;
                }

                if (TryDrawLoginUi(frame, view, out SpriteBatchStats loginUiStats))
                {
                    drawCalls += loginUiStats.DrawCalls;
                    textureBinds += loginUiStats.TextureBinds;
                    sprites += loginUiStats.Sprites;
                    scissorChanges += loginUiStats.ScissorChanges;
                }

                AppendMagicHotbarOverlay(view);
                AppendSocialOverlay(view);

                _lastDrawCalls = drawCalls;
                _lastTextureBinds = textureBinds;
                _lastSprites = sprites;
                _lastScissorChanges = scissorChanges;

                string tooltipText = _uiTooltipText;
                DrawingRectangle? tooltipBgRect = null;
                float tooltipTextX = 0;
                float tooltipTextY = 0;

                if (!string.IsNullOrWhiteSpace(tooltipText))
                {
                    Vector2 p = view.ToBackBuffer(_uiTooltipLogicalPos);

                    float tipW = Math.Max(0, _uiTooltipWidthBackBuffer);
                    float tipH = _uiTooltipHeightBackBuffer;
                    if (tipH <= 0)
                        tipH = 20f;

                    const int pad = 4;
                    float bgW = tipW + (pad * 2);
                    float bgH = tipH + (pad * 2);

                    float x = p.X - pad;
                    float y = p.Y - pad;

                    float maxX = frame.BackBufferSize.Width - 12;
                    float maxY = frame.BackBufferSize.Height - 12;
                    if (x + bgW > maxX)
                        x = Math.Max(12, maxX - bgW);
                    if (y + bgH > maxY)
                        y = Math.Max(12, maxY - bgH);

                    if (x < 12)
                        x = 12;
                    if (y < 12)
                        y = 12;

                    int ix = (int)MathF.Floor(x);
                    int iy = (int)MathF.Floor(y);
                    int iw = (int)MathF.Ceiling(bgW);
                    int ih = (int)MathF.Ceiling(bgH);

                    tooltipBgRect = new DrawingRectangle(ix, iy, iw, ih);
                    tooltipTextX = ix + pad;
                    tooltipTextY = iy + pad;
                }

                if (tooltipBgRect is { } bg && _spriteBatch != null && _whiteTexture != null)
                {
                    _spriteBatch.Begin(frame.Context, frame.BackBufferSize, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
                    _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(bg.Left - 1, bg.Top - 1, bg.Width + 2, bg.Height + 2), color: new Color4(0f, 0f, 0f, 0.75f));
                    _spriteBatch.Draw(_whiteTexture, bg, color: new Color4(0.05f, 0.05f, 0.07f, 0.92f));
                    _spriteBatch.End();
                }

                if (_textRenderer != null)
                {
                    _textRenderer.Begin(frame.SwapChain, frame.BackBufferSize);

                    if (_nameDrawList.Count > 0)
                    {
                        List<DrawingRectangle> uiOcclusionRects = _nameUiOcclusionRects;
                        uiOcclusionRects.Clear();

                        void AddOcclusionRect(DrawingRectangle? logicalRect)
                        {
                            if (logicalRect is not { } r || r.Width <= 0 || r.Height <= 0)
                                return;

                            uiOcclusionRects.Add(view.ToBackBuffer(r));
                        }

                        AddOcclusionRect(_merchantDialogPanelRect);
                        AddOcclusionRect(_merchantMenuPanelRect);
                        AddOcclusionRect(_merchantSellPanelRect);
                        AddOcclusionRect(_bookPanelRect);
                        AddOcclusionRect(_refinePanelRect);
                        AddOcclusionRect(_boxPanelRect);
                        AddOcclusionRect(_ybDealPanelRect);
                        AddOcclusionRect(_stateMagicKeyPanelRect);
                        AddOcclusionRect(_bagPanelRect);
                        AddOcclusionRect(_heroBagPanelRect);
                        AddOcclusionRect(_statePanelRect);
                        AddOcclusionRect(_settingsPanelRect);
                        AddOcclusionRect(_mallPanelRect);
                        AddOcclusionRect(_loginUiModalRect);

                        if (_sceneManager.CurrentId == MirSceneId.Play)
                        {
                            const int bottomUiHeight = 251;
                            int hLogical = view.LogicalSize.Height;
                            int bottomTop = Math.Max(0, hLogical - bottomUiHeight);
                            var bottomRect = new DrawingRectangle(0, bottomTop, view.LogicalSize.Width, bottomUiHeight);
                            uiOcclusionRects.Add(view.ToBackBuffer(bottomRect));
                        }

                        float nameTextHeight = _nameTextHeightBackBuffer;
                        if (nameTextHeight <= 0)
                        {
                            nameTextHeight = _textRenderer.MeasureText("Ay").Height;
                            if (nameTextHeight <= 0)
                                nameTextHeight = 12f;
                            _nameTextHeightBackBuffer = nameTextHeight;
                        }

                        foreach (NameDrawInfo name in _nameDrawList)
                        {
                            Vector2 p = view.ToBackBuffer(new Vector2(name.X, name.Y));

                            if (uiOcclusionRects.Count > 0)
                            {
                                float textWidth = MeasureHalfTextWidth(name.Text) * 2f;
                                int left = (int)MathF.Round(p.X);
                                int top = (int)MathF.Round(p.Y);
                                int right = left + (int)MathF.Ceiling(Math.Max(0f, textWidth));
                                int bottom = top + (int)MathF.Ceiling(nameTextHeight);

                                var nameRect = DrawingRectangle.FromLTRB(left, top, right, bottom);

                                bool occluded = false;
                                foreach (DrawingRectangle occ in uiOcclusionRects)
                                {
                                    if (occ.IntersectsWith(nameRect))
                                    {
                                        occluded = true;
                                        break;
                                    }
                                }

                                if (occluded)
                                    continue;
                            }

                            _textRenderer.DrawText(name.Text, p.X + 1, p.Y + 1, new Color4(0, 0, 0, 0.75f));
                            _textRenderer.DrawText(name.Text, p.X, p.Y, name.Color);
                        }
                    }

                    if (_sceneManager.CurrentId != MirSceneId.Play && !string.IsNullOrWhiteSpace(_world.MapTitle))
                    {
                        string label = _world.MapMusicId >= 0 ? $"{_world.MapTitle}  (music {_world.MapMusicId})" : _world.MapTitle;
                        Vector2 pMap = view.ToBackBuffer(new Vector2(16, 64));
                        _textRenderer.DrawText(label, pMap.X, pMap.Y, new Color4(0.98f, 0.92f, 0.75f, 1));
                    }

                    if (_sceneManager.CurrentId != MirSceneId.Play &&
                        (!string.IsNullOrWhiteSpace(_world.GameGoldName) || !string.IsNullOrWhiteSpace(_world.GamePointName)))
                    {
                        string label = string.IsNullOrWhiteSpace(_world.GamePointName)
                            ? $"{_world.GameGoldName}: {_world.GameGold}"
                            : $"{_world.GameGoldName}: {_world.GameGold}  {_world.GamePointName}: {_world.GamePoint}";

                        Vector2 pGold = view.ToBackBuffer(new Vector2(16, 84));
                        _textRenderer.DrawText(label, pGold.X, pGold.Y, new Color4(0.9f, 0.95f, 0.98f, 1));
                    }

                    if (_sceneManager.CurrentId != MirSceneId.Play && !string.IsNullOrWhiteSpace(_world.HudOverlayText))
                    {
                        Vector2 pHud = view.ToBackBuffer(new Vector2(16, 104));
                        _textRenderer.DrawText(_world.HudOverlayText, pHud.X, pHud.Y, new Color4(0.92f, 0.92f, 0.92f, 1));
                    }

                    if (_world.CollectExpLevel != 0)
                    {
                        uint expMax = _world.CollectExpMax == 0 ? 1u : _world.CollectExpMax;
                        uint ipMax = _world.CollectIpExpMax == 0 ? 1u : _world.CollectIpExpMax;
                        bool full = _world.CollectExp >= expMax && _world.CollectIpExp >= ipMax;
                        Color4 c = full ? new Color4(0.55f, 0.95f, 0.55f, 1f) : new Color4(0.9f, 0.95f, 0.98f, 1);
                        string label = $"聚灵 Lv{_world.CollectExpLevel}: 经验 {_world.CollectExp}/{expMax}  内功 {_world.CollectIpExp}/{ipMax}";
                        Vector2 pCollect = view.ToBackBuffer(new Vector2(16, 124));
                        _textRenderer.DrawText(label, pCollect.X, pCollect.Y, c);
                    }

                    Vector2 p1 = view.ToBackBuffer(new Vector2(16, 12));
                    _textRenderer.DrawText(_perfOverlayText, p1.X, p1.Y, new Color4(0.92f, 0.92f, 0.92f, 1));

                    if (_stateOverlayVisible && !string.IsNullOrWhiteSpace(_stateOverlayText))
                    {
                        Vector2 p = view.ToBackBuffer(new Vector2(16, 160));
                        _textRenderer.DrawText(_stateOverlayText, p.X, p.Y, new Color4(0.92f, 0.92f, 0.92f, 1));
                    }

                    if (_heroStateOverlayVisible && !string.IsNullOrWhiteSpace(_heroStateOverlayText))
                    {
                        Vector2 p = view.ToBackBuffer(new Vector2(16, 160));
                        _textRenderer.DrawText(_heroStateOverlayText, p.X, p.Y, new Color4(0.92f, 0.92f, 0.92f, 1));
                    }

                    AppendLoginUiText(view);
                    AppendSceneOverlay(view);
                    AppendChatOverlay(view);
                    AppendSystemMessageOverlay(view);
                    AppendChatInputOverlay(view);

                    if (_uiTextDrawList.Count > 0)
                    {
                        foreach (NameDrawInfo uiText in _uiTextDrawList)
                        {
                            Vector2 p = view.ToBackBuffer(new Vector2(uiText.X, uiText.Y));
                            _textRenderer.DrawText(uiText.Text, p.X + 1, p.Y + 1, new Color4(0, 0, 0, 0.75f));
                            _textRenderer.DrawText(uiText.Text, p.X, p.Y, uiText.Color);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(tooltipText))
                    {
                        float x = tooltipTextX;
                        float y = tooltipTextY;

                        _textRenderer.DrawText(tooltipText, x + 1, y + 1, new Color4(0, 0, 0, 0.85f));
                        _textRenderer.DrawText(tooltipText, x, y, new Color4(0.98f, 0.96f, 0.92f, 1));
                    }
                    _textRenderer.End();
                }
                }
            });
            long afterRender = Stopwatch.GetTimestamp();

            LimitFrameRateIfNeeded(frameStart, afterRender);
            long frameEnd = Stopwatch.GetTimestamp();
            UpdatePerformanceStats(frameStart, afterRender, frameEnd);
        }
    }

    private void LimitFrameRateIfNeeded(long frameStartTimestamp, long afterRenderTimestamp)
    {
        if (_renderControl.VSync || !_limitFpsWhenVSyncOff)
            return;

        int targetFps = _targetFps;
        if (targetFps <= 0)
            return;

        long desiredEnd = frameStartTimestamp + (long)(Stopwatch.Frequency / (double)targetFps);
        if (afterRenderTimestamp >= desiredEnd)
            return;

        double remainingMs = (desiredEnd - afterRenderTimestamp) * 1000.0 / Stopwatch.Frequency;
        if (remainingMs > 2)
            Thread.Sleep((int)(remainingMs - 1));

        while (Stopwatch.GetTimestamp() < desiredEnd)
            Thread.SpinWait(20);
    }

    private void UpdatePerformanceStats(long frameStartTimestamp, long afterRenderTimestamp, long frameEndTimestamp)
    {
        long nowMs = Environment.TickCount64;

        _lastCpuFrameMs = (afterRenderTimestamp - frameStartTimestamp) * 1000.0 / Stopwatch.Frequency;
        _lastFrameMs = (frameEndTimestamp - frameStartTimestamp) * 1000.0 / Stopwatch.Frequency;

        _perfWindowFrames++;
        _perfWindowAccumCpuMs += _lastCpuFrameMs;
        _perfWindowAccumFrameMs += _lastFrameMs;

        long windowTicks = frameEndTimestamp - _perfWindowStartTimestamp;
        if (windowTicks >= Stopwatch.Frequency / 2)
        {
            double windowSeconds = windowTicks / (double)Stopwatch.Frequency;
            _avgFps = _perfWindowFrames / windowSeconds;
            _avgCpuFrameMs = _perfWindowAccumCpuMs / _perfWindowFrames;
            _avgFrameMs = _perfWindowAccumFrameMs / _perfWindowFrames;

            _perfWindowFrames = 0;
            _perfWindowAccumCpuMs = 0;
            _perfWindowAccumFrameMs = 0;
            _perfWindowStartTimestamp = frameEndTimestamp;
        }

        if (_lastOverlayUpdateTimestamp == 0 || (frameEndTimestamp - _lastOverlayUpdateTimestamp) >= Stopwatch.Frequency / 4)
        {
            _lastOverlayUpdateTimestamp = frameEndTimestamp;
            BuildPerformanceOverlayText();
            BuildMagicHotbarOverlayText();
            if (_stateOverlayVisible)
                BuildStateOverlayText();
            else
                _stateOverlayText = string.Empty;

            if (_heroStateOverlayVisible)
                BuildHeroStateOverlayText();
            else
                _heroStateOverlayText = string.Empty;
        }

        if (_perfLogger != null)
        {
            var cpuWil = _wilImageCache.ImageCacheStats;
            var cpuData = _packDataImageCache.ImageCacheStats;
            var gpuWil = _wilTextureCache?.Stats;
            var gpuData = _dataTextureCache?.Stats;

            var sample = new MirPerfCsvSample(
                Stage: _session.Stage,
                MapTitle: _world.MapTitle,
                CenterX: _world.MapCenterSet ? _world.MapCenterX : (int?)null,
                CenterY: _world.MapCenterSet ? _world.MapCenterY : (int?)null,
                ActorCount: _world.Actors.Count,
                AvgFps: _avgFps,
                AvgFrameMs: _avgFrameMs,
                AvgCpuMs: _avgCpuFrameMs,
                LastFrameMs: _lastFrameMs,
                LastCpuMs: _lastCpuFrameMs,
                DrawCalls: _lastDrawCalls,
                TextureBinds: _lastTextureBinds,
                Sprites: _lastSprites,
                ScissorChanges: _lastScissorChanges,
                VSync: _renderControl.VSync,
                TargetFps: _targetFps,
                CpuWil: new MirPerfCacheStats(cpuWil.Count, cpuWil.CurrentWeight, cpuWil.BudgetWeight, cpuWil.Hits, cpuWil.Misses),
                CpuData: new MirPerfCacheStats(cpuData.Count, cpuData.CurrentWeight, cpuData.BudgetWeight, cpuData.Hits, cpuData.Misses),
                GpuWil: gpuWil.HasValue
                    ? new MirPerfCacheStats(gpuWil.Value.Count, gpuWil.Value.CurrentBytes, gpuWil.Value.BudgetBytes, gpuWil.Value.Hits, gpuWil.Value.Misses)
                    : null,
                GpuData: gpuData.HasValue
                    ? new MirPerfCacheStats(gpuData.Value.Count, gpuData.Value.CurrentBytes, gpuData.Value.BudgetBytes, gpuData.Value.Hits, gpuData.Value.Misses)
                    : null,
                BackBufferWidth: _lastBackBufferSize.Width,
                BackBufferHeight: _lastBackBufferSize.Height,
                LogicalWidth: _lastLogicalSize.Width,
                LogicalHeight: _lastLogicalSize.Height,
                ViewScaleX: _lastViewScale.X,
                ViewScaleY: _lastViewScale.Y,
                ViewOffsetX: _lastViewOffset.X,
                ViewOffsetY: _lastViewOffset.Y);

            _perfLogger.Tick(nowMs, sample);
        }
    }

#if false
    private void ConfigurePerfLoggingFromEnvironment()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_PERF_LOG_INTERVAL_MS"), out int intervalMs) &&
            intervalMs is >= 0 and <= 600_000)
        {
            _perfLogIntervalMs = intervalMs;
        }

        if (int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_PERF_HITCH_MS"), out int hitchMs) &&
            hitchMs is >= 1 and <= 60_000)
        {
            _perfHitchThresholdMs = hitchMs;
        }

        string? path = Environment.GetEnvironmentVariable("MIRCLIENT_PERF_LOG_PATH");
        if (string.IsNullOrWhiteSpace(path))
            return;

        path = path.Trim();
        try
        {
            string resolved = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
            _perfLogPath = Path.GetFullPath(resolved);
        }
        catch
        {
            _perfLogPath = path;
        }

        _perfLogNextWriteMs = Environment.TickCount64;
        AppendLog($"[perf] log enabled: {_perfLogPath} interval={_perfLogIntervalMs}ms hitch={_perfHitchThresholdMs}ms");
    }

    private void TickPerfLogging(long nowMs)
    {
        if (string.IsNullOrWhiteSpace(_perfLogPath))
            return;

        _perfMaxFrameMs = Math.Max(_perfMaxFrameMs, _lastFrameMs);

        bool isHitch = _perfHitchThresholdMs > 0 && _lastFrameMs >= _perfHitchThresholdMs;
        if (isHitch)
        {
            if (!_perfHitchActive)
            {
                _perfHitchActive = true;
                _perfHitchCount++;
                _perfLastHitchLogMs = nowMs;
                TryWritePerfLogRow(nowMs, "hitch");
            }
        }
        else
        {
            _perfHitchActive = false;
        }

        int intervalMs = _perfLogIntervalMs;
        if (intervalMs <= 0)
            return;

        if (_perfLogNextWriteMs == 0 || nowMs >= _perfLogNextWriteMs)
        {
            _perfLogNextWriteMs = nowMs + intervalMs;
            TryWritePerfLogRow(nowMs, "sample");
        }
    }

    private const string PerfLogHeader =
        "timeUtc,uptimeMs,event,stage,mapTitle,centerX,centerY,actors," +
        "avgFps,avgFrameMs,avgCpuMs,lastFrameMs,lastCpuMs," +
        "drawCalls,texBinds,sprites,scissorChanges,vsync,targetFps," +
        "cpuWilCount,cpuWilMb,cpuWilBudgetMb,cpuWilHits,cpuWilMisses," +
        "cpuDataCount,cpuDataMb,cpuDataBudgetMb,cpuDataHits,cpuDataMisses," +
        "gpuWilCount,gpuWilMb,gpuWilBudgetMb,gpuWilHits,gpuWilMisses," +
        "gpuDataCount,gpuDataMb,gpuDataBudgetMb,gpuDataHits,gpuDataMisses," +
        "gpuTotalCount,gpuTotalMb,gpuTotalBudgetMb," +
        "backBufferW,backBufferH,logicalW,logicalH,viewScaleX,viewScaleY,viewOffsetX,viewOffsetY," +
        "gcTotalMb,gcGen0,gcGen1,gcGen2,procWorkingSetMb,procPrivateMb," +
        "hitchThresholdMs,hitchCount,maxFrameMs";

    private void TryWritePerfLogRow(long nowMs, string eventName)
    {
        StreamWriter? writer = EnsurePerfLogWriter();
        if (writer == null)
            return;

        long gcBytes = 0;
        int gc0 = 0;
        int gc1 = 0;
        int gc2 = 0;
        try
        {
            gcBytes = GC.GetTotalMemory(forceFullCollection: false);
            gc0 = GC.CollectionCount(0);
            gc1 = GC.CollectionCount(1);
            gc2 = GC.CollectionCount(2);
        }
        catch
        {
        }

        long wsBytes = 0;
        long privateBytes = 0;
        try
        {
            using Process process = Process.GetCurrentProcess();
            wsBytes = process.WorkingSet64;
            privateBytes = process.PrivateMemorySize64;
        }
        catch
        {
        }

        var cpuWil = _wilImageCache.ImageCacheStats;
        var cpuData = _packDataImageCache.ImageCacheStats;

        var gpuWil = _wilTextureCache?.Stats;
        var gpuData = _dataTextureCache?.Stats;

        long? gpuWilCur = gpuWil?.CurrentBytes;
        long? gpuWilBudget = gpuWil?.BudgetBytes;
        long? gpuDataCur = gpuData?.CurrentBytes;
        long? gpuDataBudget = gpuData?.BudgetBytes;

        long? gpuTotalCur = (gpuWilCur.HasValue && gpuDataCur.HasValue) ? gpuWilCur + gpuDataCur : null;
        long? gpuTotalBudget = (gpuWilBudget.HasValue && gpuDataBudget.HasValue) ? gpuWilBudget + gpuDataBudget : null;
        int? gpuTotalCount = (gpuWil.HasValue && gpuData.HasValue) ? gpuWil.Value.Count + gpuData.Value.Count : null;

        _perfLogLineBuilder.Clear();

        AppendCsvCell(_perfLogLineBuilder, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, nowMs);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, eventName);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _session.Stage.ToString());
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _world.MapTitle);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _world.MapCenterSet ? _world.MapCenterX : (int?)null);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _world.MapCenterSet ? _world.MapCenterY : (int?)null);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _world.Actors.Count);
        _perfLogLineBuilder.Append(',');

        AppendCsvCell(_perfLogLineBuilder, _avgFps);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _avgFrameMs);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _avgCpuFrameMs);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _lastFrameMs);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _lastCpuFrameMs);
        _perfLogLineBuilder.Append(',');

        AppendCsvCell(_perfLogLineBuilder, _lastDrawCalls);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _lastTextureBinds);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _lastSprites);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _lastScissorChanges);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _renderControl.VSync ? 1 : 0);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _targetFps);
        _perfLogLineBuilder.Append(',');

        AppendCsvCell(_perfLogLineBuilder, cpuWil.Count);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, cpuWil.CurrentWeight / (1024 * 1024));
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, cpuWil.BudgetWeight / (1024 * 1024));
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, cpuWil.Hits);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, cpuWil.Misses);
        _perfLogLineBuilder.Append(',');

        AppendCsvCell(_perfLogLineBuilder, cpuData.Count);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, cpuData.CurrentWeight / (1024 * 1024));
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, cpuData.BudgetWeight / (1024 * 1024));
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, cpuData.Hits);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, cpuData.Misses);
        _perfLogLineBuilder.Append(',');

        AppendCsvCell(_perfLogLineBuilder, gpuWil?.Count);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gpuWilCur.HasValue ? gpuWilCur.Value / (1024 * 1024) : (long?)null);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gpuWilBudget.HasValue ? gpuWilBudget.Value / (1024 * 1024) : (long?)null);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gpuWil?.Hits);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gpuWil?.Misses);
        _perfLogLineBuilder.Append(',');

        AppendCsvCell(_perfLogLineBuilder, gpuData?.Count);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gpuDataCur.HasValue ? gpuDataCur.Value / (1024 * 1024) : (long?)null);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gpuDataBudget.HasValue ? gpuDataBudget.Value / (1024 * 1024) : (long?)null);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gpuData?.Hits);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gpuData?.Misses);
        _perfLogLineBuilder.Append(',');

        AppendCsvCell(_perfLogLineBuilder, gpuTotalCount);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gpuTotalCur.HasValue ? gpuTotalCur.Value / (1024 * 1024) : (long?)null);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gpuTotalBudget.HasValue ? gpuTotalBudget.Value / (1024 * 1024) : (long?)null);
        _perfLogLineBuilder.Append(',');

        AppendCsvCell(_perfLogLineBuilder, _lastBackBufferSize.Width);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _lastBackBufferSize.Height);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _lastLogicalSize.Width);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _lastLogicalSize.Height);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _lastViewScale.X);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _lastViewScale.Y);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _lastViewOffset.X);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _lastViewOffset.Y);
        _perfLogLineBuilder.Append(',');

        AppendCsvCell(_perfLogLineBuilder, gcBytes / (1024 * 1024));
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gc0);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gc1);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, gc2);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, wsBytes / (1024 * 1024));
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, privateBytes / (1024 * 1024));
        _perfLogLineBuilder.Append(',');

        AppendCsvCell(_perfLogLineBuilder, _perfHitchThresholdMs);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _perfHitchCount);
        _perfLogLineBuilder.Append(',');
        AppendCsvCell(_perfLogLineBuilder, _perfMaxFrameMs);

        try
        {
            writer.WriteLine(_perfLogLineBuilder.ToString());
            writer.Flush();
        }
        catch
        {
        }
    }

    private StreamWriter? EnsurePerfLogWriter()
    {
        if (_perfLogWriter != null)
            return _perfLogWriter;

        if (string.IsNullOrWhiteSpace(_perfLogPath))
            return null;

        try
        {
            string path = _perfLogPath;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            byte[] bom = Encoding.UTF8.GetPreamble();
            bool writeHeader = !File.Exists(path) || new FileInfo(path).Length <= bom.Length;

            FileStream stream;
            if (writeHeader)
            {
                stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                if (bom.Length > 0)
                    stream.Write(bom, 0, bom.Length);
            }
            else
            {
                stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            }

            _perfLogWriter = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            if (writeHeader)
                _perfLogWriter.WriteLine(PerfLogHeader);

            return _perfLogWriter;
        }
        catch (Exception ex)
        {
            AppendLog($"[perf] log init failed: {ex.GetType().Name}: {ex.Message}");
            ClosePerfLogWriter();
            _perfLogPath = null;
            return null;
        }
    }

    private void ClosePerfLogWriter()
    {
        try
        {
            _perfLogWriter?.Dispose();
        }
        catch
        {
        }

        _perfLogWriter = null;
    }

    private static void AppendCsvCell(StringBuilder builder, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        bool needsQuote = false;
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (ch is ',' or '"' or '\r' or '\n')
            {
                needsQuote = true;
                break;
            }
        }

        if (!needsQuote)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (ch == '"')
                builder.Append("\"\"");
            else
                builder.Append(ch);
        }
        builder.Append('"');
    }

    private static void AppendCsvCell(StringBuilder builder, int value) =>
        builder.Append(value.ToString(CultureInfo.InvariantCulture));

    private static void AppendCsvCell(StringBuilder builder, int? value)
    {
        if (!value.HasValue)
            return;

        AppendCsvCell(builder, value.Value);
    }

    private static void AppendCsvCell(StringBuilder builder, long value) =>
        builder.Append(value.ToString(CultureInfo.InvariantCulture));

    private static void AppendCsvCell(StringBuilder builder, long? value)
    {
        if (!value.HasValue)
            return;

        AppendCsvCell(builder, value.Value);
    }

    private static void AppendCsvCell(StringBuilder builder, double value)
    {
        if (!double.IsFinite(value))
            return;

        builder.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
    }
#endif

    private void BuildPerformanceOverlayText()
    {
        _perfBuilder.Clear();

        bool vsync = _renderControl.VSync;
        _perfBuilder.Append("FPS ");
        _perfBuilder.Append(_avgFps <= 0 ? "-" : _avgFps.ToString("0.0"));
        _perfBuilder.Append("  Frame ");
        _perfBuilder.Append(_avgFrameMs <= 0 ? "-" : _avgFrameMs.ToString("0.0"));
        _perfBuilder.Append("ms  CPU ");
        _perfBuilder.Append(_avgCpuFrameMs <= 0 ? "-" : _avgCpuFrameMs.ToString("0.0"));
        _perfBuilder.Append("ms  (last ");
        _perfBuilder.Append(_lastCpuFrameMs.ToString("0.0"));
        _perfBuilder.Append("ms)");
        _perfBuilder.AppendLine();

        _perfBuilder.Append("DC ");
        _perfBuilder.Append(_lastDrawCalls);
        _perfBuilder.Append("  Tex ");
        _perfBuilder.Append(_lastTextureBinds);
        _perfBuilder.Append("  Spr ");
        _perfBuilder.Append(_lastSprites);
        _perfBuilder.Append("  Sci ");
        _perfBuilder.Append(_lastScissorChanges);
        _perfBuilder.Append("  VSync ");
        _perfBuilder.Append(vsync ? "on" : "off");
        _perfBuilder.Append("  Target ");
        _perfBuilder.Append(_targetFps);
        _perfBuilder.AppendLine();

        var imgStats = _wilImageCache.ImageCacheStats;
        var dataStats = _packDataImageCache.ImageCacheStats;
        _perfBuilder.Append("CPU WIL ");
        _perfBuilder.Append(imgStats.Count);
        _perfBuilder.Append("img ");
        _perfBuilder.Append(imgStats.CurrentWeight / (1024 * 1024));
        _perfBuilder.Append('/');
        _perfBuilder.Append(imgStats.BudgetWeight / (1024 * 1024));
        _perfBuilder.Append("MB  DATA ");
        _perfBuilder.Append(dataStats.Count);
        _perfBuilder.Append("img ");
        _perfBuilder.Append(dataStats.CurrentWeight / (1024 * 1024));
        _perfBuilder.Append('/');
        _perfBuilder.Append(dataStats.BudgetWeight / (1024 * 1024));
        _perfBuilder.Append("MB");

        if (_wilTextureCache != null || _dataTextureCache != null)
        {
            long gpuCur = (_wilTextureCache?.Stats.CurrentBytes ?? 0) + (_dataTextureCache?.Stats.CurrentBytes ?? 0);
            long gpuBudget = (_wilTextureCache?.Stats.BudgetBytes ?? 0) + (_dataTextureCache?.Stats.BudgetBytes ?? 0);
            int gpuCount = (_wilTextureCache?.Stats.Count ?? 0) + (_dataTextureCache?.Stats.Count ?? 0);

            _perfBuilder.Append("  GPU ");
            _perfBuilder.Append(gpuCount);
            _perfBuilder.Append("tex ");
            _perfBuilder.Append(gpuCur / (1024 * 1024));
            _perfBuilder.Append('/');
            _perfBuilder.Append(gpuBudget / (1024 * 1024));
            _perfBuilder.Append("MB");
        }

        _perfBuilder.AppendLine();

        if (_lastBackBufferSize.Width > 0 && _lastBackBufferSize.Height > 0)
        {
            _perfBuilder.Append("BackBuffer ");
            _perfBuilder.Append(_lastBackBufferSize.Width);
            _perfBuilder.Append('x');
            _perfBuilder.Append(_lastBackBufferSize.Height);
            _perfBuilder.Append("  Logical ");
            _perfBuilder.Append(_lastLogicalSize.Width);
            _perfBuilder.Append('x');
            _perfBuilder.Append(_lastLogicalSize.Height);
            _perfBuilder.Append("  Scale ");
            _perfBuilder.Append(_lastViewScale.X.ToString("0.###"));
            _perfBuilder.Append("  Offset ");
            _perfBuilder.Append((int)_lastViewOffset.X);
            _perfBuilder.Append(',');
            _perfBuilder.Append((int)_lastViewOffset.Y);
            _perfBuilder.AppendLine();
        }

        _perfBuilder.Append("Actors ");
        _perfBuilder.Append(_world.Actors.Count);
        _perfBuilder.Append("  Center ");
        if (_world.MapCenterSet)
        {
            _perfBuilder.Append(_world.MapCenterX);
            _perfBuilder.Append(',');
            _perfBuilder.Append(_world.MapCenterY);
        }
        else
        {
            _perfBuilder.Append('-');
        }
        _perfBuilder.AppendLine();

        _perfBuilder.Append("Stage: ");
        _perfBuilder.Append(_session.Stage);

        _perfOverlayText = _perfBuilder.ToString();
    }

    private void BuildMagicHotbarOverlayText()
    {
        if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
        {
            _magicHotbarLine1 = string.Empty;
            _magicHotbarLine2 = string.Empty;
            _magicHotbarLine3 = string.Empty;
            return;
        }

        IReadOnlyList<ClientMagic> magics = _world.MyMagics;
        if (magics.Count == 0)
        {
            _magicHotbarLine1 = string.Empty;
            _magicHotbarLine2 = string.Empty;
            _magicHotbarLine3 = string.Empty;
            return;
        }

        _magicHotbarLine1 = BuildMagicHotbarLine(startSlot: 0, count: 4, magics);
        _magicHotbarLine2 = BuildMagicHotbarLine(startSlot: 4, count: 4, magics);
        _magicHotbarLine3 = "F1-F8:Cast  Space:Hit  M:MiniMap  ESC:DropItems";
    }

    private void BuildStateOverlayText()
    {
        _stateOverlayBuilder.Clear();

        _stateOverlayBuilder.Append("State (F10)").AppendLine();
        _stateOverlayBuilder.Append("Fashion: ").Append(_world.ShowFashion ? "on" : "off");
        _stateOverlayBuilder.Append("  HeroFashion: ").Append(_world.HeroShowFashion ? "on" : "off");
        _stateOverlayBuilder.Append("  Luck: ").Append(_world.MyLuck);
        _stateOverlayBuilder.Append("  Energy: ").Append(_world.MyEnergy);
        _stateOverlayBuilder.Append("  Hungry: ").Append(_world.MyHungryState);
        _stateOverlayBuilder.AppendLine();

        _stateOverlayBuilder.Append("Titles: srv ").Append(_world.ServerTitles.Length);
        _stateOverlayBuilder.Append("  my ").Append(_world.MyTitles.Length);
        _stateOverlayBuilder.Append("  hero ").Append(_world.HeroTitles.Length);
        _stateOverlayBuilder.AppendLine();

        _stateOverlayBuilder.Append("Heroes: ").Append(_world.Heroes.Length);
        if (!string.IsNullOrWhiteSpace(_world.SelectedHeroName))
            _stateOverlayBuilder.Append("  selected ").Append(_world.SelectedHeroName);
        _stateOverlayBuilder.AppendLine();

        _stateOverlayBuilder.Append("Detect: ");
        if (_world.DetectItemSet)
        {
            _stateOverlayBuilder.Append(_world.DetectItem.NameString);
            _stateOverlayBuilder.Append(" #").Append(_world.DetectItem.MakeIndex);
            if (_world.DetectItemMineId != 0)
                _stateOverlayBuilder.Append(" mine=").Append(_world.DetectItemMineId);
        }
        else
        {
            _stateOverlayBuilder.Append('-');
        }
        _stateOverlayBuilder.AppendLine();

        _stateOverlayBuilder.Append("Suite: ").Append(_world.SuiteItems.Length);
        _stateOverlayBuilder.Append("  AllshineBytes: ").Append(_world.AllshineBytes.Length);
        _stateOverlayBuilder.AppendLine();

        _stateOverlayBuilder.Append("Venation: ");
        for (int i = 0; i < _world.VenationInfos.Length; i++)
        {
            VenationInfo v = _world.VenationInfos[i];
            if (i > 0)
                _stateOverlayBuilder.Append("  ");
            _stateOverlayBuilder.Append(i + 1).Append(":L").Append(v.Level).Append(" P").Append(v.Point);
        }
        _stateOverlayBuilder.AppendLine();

        _stateOverlayBuilder.Append("HeroVenation: ");
        for (int i = 0; i < _world.HeroVenationInfos.Length; i++)
        {
            VenationInfo v = _world.HeroVenationInfos[i];
            if (i > 0)
                _stateOverlayBuilder.Append("  ");
            _stateOverlayBuilder.Append(i + 1).Append(":L").Append(v.Level).Append(" P").Append(v.Point);
        }
        _stateOverlayBuilder.AppendLine();

        _stateOverlayBuilder.Append("SeriesSkillArr: ");
        for (int i = 0; i < _world.SeriesSkillArr.Length; i++)
        {
            if (i > 0)
                _stateOverlayBuilder.Append(' ');
            _stateOverlayBuilder.Append(_world.SeriesSkillArr[i]);
        }
        _stateOverlayBuilder.AppendLine();

        if (_world.TryGetMyself(out ActorMarker myself))
        {
            _stateOverlayBuilder.Append("IPower: ").Append(myself.IPower);
            _stateOverlayBuilder.AppendLine();
        }

        _stateOverlayBuilder.Append("Guild: ");
        if (!string.IsNullOrWhiteSpace(_world.MyGuildName))
        {
            _stateOverlayBuilder.Append(_world.MyGuildName);
            if (!string.IsNullOrWhiteSpace(_world.MyGuildRankName))
                _stateOverlayBuilder.Append(" / ").Append(_world.MyGuildRankName);
            if (_world.GuildCommanderMode)
                _stateOverlayBuilder.Append(" (cmd)");
        }
        else
        {
            _stateOverlayBuilder.Append('-');
        }
        _stateOverlayBuilder.AppendLine();

        _stateOverlayBuilder.Append("MiniMap: ").Append(_world.MiniMapVisible ? "on" : "off");
        if (_world.MiniMapIndex >= 0)
            _stateOverlayBuilder.Append(" idx=").Append(_world.MiniMapIndex);
        _stateOverlayBuilder.AppendLine();

        if (_world.LastUserState is { } user)
        {
            _stateOverlayBuilder.Append("UserState: ").Append(user.UserName);
            if (!string.IsNullOrWhiteSpace(user.GuildName))
                _stateOverlayBuilder.Append("  ").Append(user.GuildName);
            _stateOverlayBuilder.Append("  titles=").Append(user.Titles.Count);
            _stateOverlayBuilder.Append("  use=").Append(user.UseItems.Count);
            _stateOverlayBuilder.AppendLine();
        }

        _stateOverlayText = _stateOverlayBuilder.ToString();
    }

    private void BuildHeroStateOverlayText()
    {
        _heroStateOverlayBuilder.Clear();
        _heroStateOverlayBuilder.Append("HeroState (N)").AppendLine();

        if (!_world.HeroActorIdSet || _world.HeroActorId == 0)
        {
            _heroStateOverlayBuilder.Append("(no hero)").AppendLine();
            _heroStateOverlayText = _heroStateOverlayBuilder.ToString();
            return;
        }

        int heroId = _world.HeroActorId;
        _heroStateOverlayBuilder.Append("HeroId: ").Append(heroId);
        if (_world.TryGetActor(heroId, out ActorMarker heroMarker) && !string.IsNullOrWhiteSpace(heroMarker.UserName))
            _heroStateOverlayBuilder.Append("  ").Append(heroMarker.UserName.Trim());
        _heroStateOverlayBuilder.AppendLine();

        _heroStateOverlayBuilder.Append("Loyalty: ");
        if (!string.IsNullOrWhiteSpace(_world.HeroLoyalty))
            _heroStateOverlayBuilder.Append(_world.HeroLoyalty.Trim());
        else
            _heroStateOverlayBuilder.Append('-');
        _heroStateOverlayBuilder.AppendLine();

        if (_world.HeroAbilitySet)
        {
            Ability a = _world.HeroAbility;
            _heroStateOverlayBuilder.Append("Lv ").Append(a.Level);
            _heroStateOverlayBuilder.Append("  HP ").Append(a.HP).Append('/').Append(a.MaxHP);
            _heroStateOverlayBuilder.Append("  MP ").Append(a.MP).Append('/').Append(a.MaxMP);
            _heroStateOverlayBuilder.AppendLine();

            _heroStateOverlayBuilder.Append("Gold: ").Append(_world.HeroGold);
            _heroStateOverlayBuilder.Append("  Job: ").Append(_world.HeroJob);
            _heroStateOverlayBuilder.Append("  IPowerLv: ").Append(_world.HeroIPowerLevel);
            _heroStateOverlayBuilder.Append("  Glory: ").Append(_world.HeroGloryPoint);
            _heroStateOverlayBuilder.AppendLine();
        }
        else
        {
            _heroStateOverlayBuilder.Append("(waiting hero ability...)").AppendLine();
        }

        if (_world.HeroMaxEnergy > 0)
        {
            _heroStateOverlayBuilder.Append("Energy: ").Append(_world.HeroEnergy).Append('/').Append(_world.HeroMaxEnergy);
            if (_world.HeroEnergyType != 0)
                _heroStateOverlayBuilder.Append(" (type ").Append(_world.HeroEnergyType).Append(')');
            _heroStateOverlayBuilder.AppendLine();
        }

        _heroStateOverlayText = _heroStateOverlayBuilder.ToString();
    }

    private string BuildMagicHotbarLine(int startSlot, int count, IReadOnlyList<ClientMagic> magics)
    {
        _hotbarBuilder.Clear();

        for (int i = 0; i < count; i++)
        {
            int slot = startSlot + i;
            if (i > 0)
                _hotbarBuilder.Append("  ");

            _hotbarBuilder.Append('F');
            _hotbarBuilder.Append(slot + 1);
            _hotbarBuilder.Append(':');

            if ((uint)slot < (uint)magics.Count)
            {
                ClientMagic magic = magics[slot];
                string name = magic.Def.MagicNameString;
                if (string.IsNullOrWhiteSpace(name))
                    name = $"#{magic.Def.MagicId}";

                _hotbarBuilder.Append(name);
            }
            else
            {
                _hotbarBuilder.Append('-');
            }
        }

        return _hotbarBuilder.ToString();
    }

    private void AppendMagicHotbarOverlay(D3D11ViewTransform view)
    {
        if (_sceneManager.CurrentId == MirSceneId.Play)
            return;

        if (string.IsNullOrWhiteSpace(_magicHotbarLine1) &&
            string.IsNullOrWhiteSpace(_magicHotbarLine2) &&
            string.IsNullOrWhiteSpace(_magicHotbarLine3))
        {
            return;
        }

        const int x0 = 16;
        const int y0 = 162;
        const int lineH = 18;

        int y = y0;
        var color = new Color4(0.92f, 0.92f, 0.92f, 1);
        var hintColor = new Color4(0.75f, 0.75f, 0.75f, 1);

        if (!string.IsNullOrWhiteSpace(_magicHotbarLine1))
        {
            _uiTextDrawList.Add(new NameDrawInfo(_magicHotbarLine1, x0, y, color));
            y += lineH;
        }

        if (!string.IsNullOrWhiteSpace(_magicHotbarLine2))
        {
            _uiTextDrawList.Add(new NameDrawInfo(_magicHotbarLine2, x0, y, color));
            y += lineH;
        }

        if (!string.IsNullOrWhiteSpace(_magicHotbarLine3))
        {
            _uiTextDrawList.Add(new NameDrawInfo(_magicHotbarLine3, x0, y, hintColor));
        }
    }

    private void AppendSocialOverlay(D3D11ViewTransform view)
    {
        const int x0 = 16;
        const int y0 = 216;
        const int lineH = 18;

        int y = y0;

        if (!string.IsNullOrWhiteSpace(_world.MyGuildName))
        {
            string rank = string.IsNullOrWhiteSpace(_world.MyGuildRankName) ? string.Empty : $" ({_world.MyGuildRankName.Trim()})";
            _uiTextDrawList.Add(new NameDrawInfo($"Guild: {_world.MyGuildName.Trim()}{rank}", x0, y, new Color4(0.6f, 0.88f, 1.0f, 1f)));
            y += lineH;
        }

        if (!_groupOverlayVisible || _world.GroupMembers.Count == 0)
            return;

        _uiTextDrawList.Add(new NameDrawInfo($"Group ({_world.GroupMembers.Count})", x0, y, new Color4(0.55f, 0.95f, 0.55f, 1f)));
        y += lineH;

        const int maxMembers = 10;
        int show = Math.Min(maxMembers, _world.GroupMembers.Count);
        for (int i = 0; i < show; i++)
        {
            string name = _world.GroupMembers[i].Trim();
            if (name.Length == 0)
                continue;

            _uiTextDrawList.Add(new NameDrawInfo($"{i + 1}. {name}", x0, y, new Color4(0.55f, 0.95f, 0.55f, 1f)));
            y += lineH;
        }

        if (_world.GroupMembers.Count > maxMembers)
            _uiTextDrawList.Add(new NameDrawInfo($"... +{_world.GroupMembers.Count - maxMembers}", x0, y, new Color4(0.55f, 0.95f, 0.55f, 1f)));
    }

    private void AddChatLine(string text, Color4 color)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        string line = text.TrimEnd();
        if (line.Length == 0)
            return;

        if (color.R >= 0.9f && color.G <= 0.5f && color.B <= 0.5f)
            Core.Diagnostics.MirErrorLog.Write($"[ui] {line}");

        long nowMs = Environment.TickCount64;

        lock (_chatLines)
        {
            int visibleLines = GetInGameChatVisibleLines();
            if (visibleLines <= 0)
                visibleLines = ViewChatLine;

            int wrapWidthLogical = GetInGameChatWrapWidthLogicalNoLock();
            foreach (string wrapped in WrapChatBoardLine(line, wrapWidthLogical))
            {
                if (wrapped.Length == 0)
                    continue;

                _chatLines.Add(new ChatLine(wrapped, color, nowMs));

                if (_chatLines.Count > ChatHistoryMaxLines)
                {
                    _chatLines.RemoveAt(0);
                    if (_chatLines.Count - _chatBoardTop < visibleLines && _chatBoardTop > 0)
                        _chatBoardTop = Math.Max(0, _chatBoardTop - 1);
                }
                else
                {
                    if (_chatLines.Count - _chatBoardTop > visibleLines)
                        _chatBoardTop = Math.Min(_chatLines.Count - 1, _chatBoardTop + 1);
                }
            }

            ClampChatBoardTopNoLock();
        }
    }

    private void AddSystemTopMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        long nowMs = Environment.TickCount64;
        lock (_sysMessageLock)
        {
            if (_sysMsgTop.Count >= SysMessageMaxLines)
                _sysMsgTop.RemoveAt(0);
            _sysMsgTop.Add(new SysMessageLine(text.Trim(), nowMs));
        }
    }

    private void AddSystemBottomMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        long nowMs = Environment.TickCount64;
        lock (_sysMessageLock)
        {
            if (_sysMsgBottom.Count >= SysMessageMaxLines)
                _sysMsgBottom.RemoveAt(0);
            _sysMsgBottom.Add(new SysMessageLine(text.Trim(), nowMs));
        }
    }

    private void AddSystemBottomRightMessage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        long nowMs = Environment.TickCount64;
        lock (_sysMessageLock)
        {
            if (_sysMsgBottomRight.Count >= SysMessageMaxLines)
                _sysMsgBottomRight.RemoveAt(0);
            _sysMsgBottomRight.Add(new SysMessageLine(text.Trim(), nowMs));
        }
    }

    private void AddSystemMarqueeMessage(string text, ushort colorParam)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        byte fore = (byte)(colorParam & 0xFF);
        byte back = (byte)((colorParam >> 8) & 0xFF);
        lock (_sysMessageLock)
        {
            if (_sysMarquee.Count >= SysMessageMaxLines)
                _sysMarquee.RemoveAt(0);
            _sysMarquee.Add(new SysMarqueeEntry(text.Trim(), fore, back));
        }
    }

    private void ClearSystemMessages()
    {
        lock (_sysMessageLock)
        {
            _sysMsgTop.Clear();
            _sysMsgBottom.Clear();
            _sysMsgBottomRight.Clear();
            _sysMarquee.Clear();
        }
    }

    private int GetInGameChatVisibleLines()
    {
        return ViewChatLine;
    }

    private DrawingRectangle GetInGameChatBoardRect(DrawingSize logicalSize)
    {
        const int chatLeft = 208;
        const int bottomMargin = 22;
        const int lineH = 12;

        int visibleLines = GetInGameChatVisibleLines();
        if (visibleLines <= 0)
            return DrawingRectangle.Empty;

        int width = Math.Max(0, (logicalSize.Width / 2 - 214) * 2);
        int height = visibleLines * lineH;

        int bottom = Math.Max(0, logicalSize.Height - bottomMargin);
        int top = Math.Max(0, bottom - height);

        if (chatLeft >= logicalSize.Width)
            return DrawingRectangle.Empty;

        if (width <= 0)
            width = Math.Max(0, logicalSize.Width - chatLeft);

        if (chatLeft + width > logicalSize.Width)
            width = Math.Max(0, logicalSize.Width - chatLeft);

        return new DrawingRectangle(chatLeft, top, width, Math.Max(0, height));
    }

    private int GetInGameChatWrapWidthLogicalNoLock()
    {
        int screenW = _lastLogicalSize.Width;
        if (screenW <= 0)
            return 0;

        int width = (screenW / 2 - 214) * 2;
        return Math.Max(0, width);
    }

    private IEnumerable<string> WrapChatBoardLine(string text, int maxWidthLogical)
    {
        if (string.IsNullOrEmpty(text))
            yield break;

        if (_textRenderer == null || maxWidthLogical <= 0)
        {
            yield return text;
            yield break;
        }

        float scaleX = _lastViewScale.X;
        if (scaleX <= 0.0001f)
            scaleX = 1f;

        float maxWidthPx = maxWidthLogical * scaleX;
        if (maxWidthPx <= 1f)
        {
            yield return text;
            yield break;
        }

        string remaining = text;
        while (remaining.Length > 0)
        {
            int take = FindMaxChatPrefixLength(remaining, maxWidthPx);
            if (take <= 0 || take >= remaining.Length)
            {
                yield return remaining;
                yield break;
            }

            yield return remaining[..take];

            string rest = remaining[take..];
            if (rest.Length == 0)
                yield break;

            remaining = " " + rest.TrimStart();
        }
    }

    private int FindMaxChatPrefixLength(string text, float maxWidthPx)
    {
        if (_textRenderer == null || string.IsNullOrEmpty(text))
            return text.Length;

        int high = text.Length;
        int low = 1;
        int best = 0;

        while (low <= high)
        {
            int mid = (low + high) / 2;
            mid = AdjustSubstringLengthForSurrogates(text, mid);
            if (mid <= 0)
            {
                low = 1;
                continue;
            }

            float widthPxMeasured = _textRenderer.MeasureTextWidth(text.Substring(0, mid));
            if (widthPxMeasured <= maxWidthPx)
            {
                best = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (best <= 0)
            return AdjustSubstringLengthForSurrogates(text, 1);

        return best;
    }

    private static int AdjustSubstringLengthForSurrogates(string text, int length)
    {
        if (length <= 0)
            return 0;

        if (length >= text.Length)
            return text.Length;

        if (char.IsHighSurrogate(text[length - 1]) && char.IsLowSurrogate(text[length]))
            return length - 1;

        return length;
    }

    private void ClampChatBoardTopNoLock()
    {
        int count = _chatLines.Count;
        if (count <= 0)
        {
            _chatBoardTop = 0;
            return;
        }

        int maxTop = count - 1;
        if (_chatBoardTop < 0)
            _chatBoardTop = 0;
        else if (_chatBoardTop > maxTop)
            _chatBoardTop = maxTop;
    }

    private void ScrollChatBoard(int deltaLines)
    {
        if (deltaLines == 0)
            return;

        lock (_chatLines)
        {
            if (_chatLines.Count == 0)
                return;

            _chatBoardTop += deltaLines;
            ClampChatBoardTopNoLock();
        }
    }

    private bool TryHandleInGameChatMouseDown(Vector2 logical, bool ctrl)
    {
        if (_lastLogicalSize.Width <= 0 || _lastLogicalSize.Height <= 0)
            return false;

        DrawingRectangle chatStatusRect = GetInGameChatStatusButtonRect(_lastLogicalSize);
        if (chatStatusRect.Width > 0 &&
            logical.X >= chatStatusRect.Left && logical.X < chatStatusRect.Right &&
            logical.Y >= chatStatusRect.Top && logical.Y < chatStatusRect.Bottom)
        {
            _chatStatusLarge = !_chatStatusLarge;
            AppendLog($"[ui] chat status: {(_chatStatusLarge ? "large" : "normal")}");
            return true;
        }

        DrawingRectangle chatRangeRect = GetInGameChatRangeButtonRect(_lastLogicalSize);
        if (chatRangeRect.Width > 0 &&
            logical.X >= chatRangeRect.Left && logical.X < chatRangeRect.Right &&
            logical.Y >= chatRangeRect.Top && logical.Y < chatRangeRect.Bottom)
        {
            CycleChatWindowLines(logUi: true);
            return true;
        }

        DrawingRectangle chatRect = GetInGameChatBoardRect(_lastLogicalSize);
        if (chatRect.Width <= 0 || chatRect.Height <= 0)
            return false;

        if (logical.X < chatRect.Left || logical.X >= chatRect.Right || logical.Y < chatRect.Top || logical.Y >= chatRect.Bottom)
            return false;

        const int lineH = 12;
        int lineIndex = (int)((logical.Y - chatRect.Top) / lineH);

        string? selectedLine = null;
        lock (_chatLines)
        {
            int count = _chatLines.Count;
            if (count > 0)
            {
                int top = Math.Clamp(_chatBoardTop, 0, count - 1);
                int idx = top + lineIndex;
                if ((uint)idx < (uint)count)
                    selectedLine = _chatLines[idx].Text;
            }
        }

        if (selectedLine == null)
        {
            OpenChatInput(string.Empty);
            return true;
        }

        if (ctrl)
        {
            OpenChatInput(selectedLine);
            return true;
        }

        string uname = ExtractUserNameFromChatLine(selectedLine);
        OpenChatInput(uname.Length > 0 ? $"/{uname} " : string.Empty);
        return true;
    }

    private bool TryHandleInGameBottomUiMouseDown(Vector2 logical, bool bagUiActive, CancellationToken token)
    {
        if (_sceneManager.CurrentId != MirSceneId.Play)
            return false;

        if (_lastLogicalSize.Width <= 0 || _lastLogicalSize.Height <= 0)
            return false;

        const int bottomUiHeight = 251;
        int screenW = _lastLogicalSize.Width;
        int screenH = _lastLogicalSize.Height;
        int bottomTop = Math.Max(0, screenH - bottomUiHeight);

        if (logical.Y < bottomTop || logical.Y >= screenH)
            return false;

        const int btnSize = 32;

        DrawingRectangle myStateRect = new(screenW - 157, bottomTop + 62, btnSize, btnSize);
        DrawingRectangle myBagRect = new(screenW - 118, bottomTop + 42, btnSize, btnSize);
        DrawingRectangle myMagicRect = new(screenW - 78, bottomTop + 22, btnSize, btnSize);
        DrawingRectangle optionRect = new(screenW - 36, bottomTop + 12, btnSize, btnSize);

        if (Contains(myStateRect))
        {
            if (_stateWindowVisible && _stateWindowPage == 0)
            {
                _stateWindowVisible = false;
                _stateMagicKeyDialogOpen = false;
                AppendLog("[ui] state closed (bottom)");
                return true;
            }

            _stateWindowVisible = true;
            _stateWindowPage = 0;
            _stateMagicKeyDialogOpen = false;
            AppendLog("[ui] state opened (bottom)");
            return true;
        }

        if (Contains(myBagRect))
        {
            ClearItemDrag();
            _heroBagView = false;
            _bagWindowVisible = !_bagWindowVisible;

            if (!_bagWindowVisible)
            {
                _treasureDialogSystem.Close(logUi: false);
                _stallSystem.CloseWindow(logUi: false);
                _userStallSystem.Close(logUi: false);

                if (_marketSystem.Visible)
                {
                    _ = _maketSystem.TrySendMarketCloseAsync(token);
                    _marketSystem.Reset(clearWorld: true);
                }
            }

            AppendLog(_bagWindowVisible ? "[ui] bag opened (bottom)" : "[ui] bag closed (bottom)");
            return true;
        }

        if (Contains(myMagicRect))
        {
            if (_stateWindowVisible && _stateWindowPage == 3)
            {
                _stateWindowVisible = false;
                _stateMagicKeyDialogOpen = false;
                AppendLog("[ui] magic closed (bottom)");
                return true;
            }

            _stateWindowVisible = true;
            _stateWindowPage = 3;
            _stateMagicPage = 0;
            _stateMagicKeyDialogOpen = false;
            AppendLog("[ui] magic opened (bottom)");
            return true;
        }

        if (Contains(optionRect))
        {
            _settingsWindowVisible = !_settingsWindowVisible;
            EndUiWindowDrag();
            AppendLog(_settingsWindowVisible ? "[ui] settings opened (bottom)" : "[ui] settings closed (bottom)");
            return true;
        }

        if (_bottomMagicHotbarRect is { } hotbarRect &&
            logical.X >= hotbarRect.Left && logical.X < hotbarRect.Right &&
            logical.Y >= hotbarRect.Top && logical.Y < hotbarRect.Bottom)
        {
            const int slots = 8;
            int slotW = hotbarRect.Width / slots;
            if (slotW <= 0)
                slotW = 1;

            int slot = (int)((logical.X - hotbarRect.Left) / slotW);
            slot = Math.Clamp(slot, 0, slots - 1);

            int? mouseMapX = null;
            int? mouseMapY = null;
            if (TryGetMouseMapCell(out int mx, out int my))
            {
                mouseMapX = mx;
                mouseMapY = my;
            }

            bool ctrl = (Control.ModifierKeys & Keys.Control) != 0;
            char desiredKey = (char)((ctrl ? 'E' : '1') + slot);

            bool foundByKey = false;
            IReadOnlyList<ClientMagic> magics = _world.MyMagics;
            for (int i = 0; i < magics.Count; i++)
            {
                ClientMagic magic = magics[i];
                if (magic.Key != desiredKey)
                    continue;

                _spellCastSystem.TryCastMagic(_world, _targetingSystem, slot, magic, mouseMapX, mouseMapY, token);
                foundByKey = true;
                break;
            }

            if (!foundByKey)
                _spellCastSystem.TryCastHotbarMagic(_world, _targetingSystem, slot, mouseMapX, mouseMapY, token);

            return true;
        }

        const int botIconSize = 30;
        DrawingRectangle miniMapRect = _bottomMiniMapButtonRect ?? new DrawingRectangle(219, bottomTop + 104, botIconSize, botIconSize);
        DrawingRectangle tradeRect = _bottomTradeButtonRect ?? new DrawingRectangle(219 + 30, bottomTop + 104, botIconSize, botIconSize);
        DrawingRectangle guildRect = _bottomGuildButtonRect ?? new DrawingRectangle(219 + (30 * 2), bottomTop + 104, botIconSize, botIconSize);
        DrawingRectangle groupRect = _bottomGroupButtonRect ?? new DrawingRectangle(219 + (30 * 3), bottomTop + 104, botIconSize, botIconSize);

        if (Contains(miniMapRect))
        {
            if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
                return true;

            if (!_world.MyselfRecogIdSet)
                return true;

            long nowMs = Environment.TickCount64;
            MiniMapSystem.MiniMapToggleResult result = _miniMapSystem.Toggle(nowMs);

            if (result.Request)
            {
                _miniMapRequestSystem.TryRequest(token);
                return true;
            }

            if (result.ViewLevel <= 0)
                AppendLog("[ui] minimap closed (bottom)");
            else
                AppendLog($"[ui] minimap level={result.ViewLevel} (bottom)");
            return true;
        }

        if (Contains(tradeRect))
        {
            if (!_session.IsConnected || !_world.MyselfRecogIdSet)
                return true;

            long nowMs = Environment.TickCount64;
            if (nowMs < _dealTryCooldownUntilMs)
                return true;

            _dealTryCooldownUntilMs = nowMs + 3000;

            _ = _session.SendClientStringAsync(Grobal2.CM_DEALTRY, 0, 0, 0, 0, string.Empty, token);
            AppendLog("[deal] CM_DEALTRY (bottom)");
            return true;
        }

        if (Contains(guildRect))
        {
            if (!_world.MyselfRecogIdSet)
                return true;

            _guildSystem.ToggleDialog(token, logUi: true);
            return true;
        }

        if (Contains(groupRect))
        {
            _groupSystem.TryToggleGroupMode(token);
            return true;
        }

        
        return !bagUiActive;

        bool Contains(DrawingRectangle rect)
            => logical.X >= rect.Left && logical.X < rect.Right && logical.Y >= rect.Top && logical.Y < rect.Bottom;
    }

    private DrawingRectangle GetInGameChatStatusButtonRect(DrawingSize logicalSize)
    {
        DrawingRectangle inputRect = GetChatInputRect(logicalSize);
        if (inputRect.Width <= 0 || inputRect.Height <= 0)
            return DrawingRectangle.Empty;

        return new DrawingRectangle(inputRect.Left + 133, inputRect.Top + 2, 18, 17);
    }

    private DrawingRectangle GetInGameChatRangeButtonRect(DrawingSize logicalSize)
    {
        DrawingRectangle inputRect = GetChatInputRect(logicalSize);
        if (inputRect.Width <= 0 || inputRect.Height <= 0)
            return DrawingRectangle.Empty;

        return new DrawingRectangle(inputRect.Left + 158, inputRect.Top + 3, 18, 17);
    }

    private void CycleChatWindowLines(bool logUi)
    {
        lock (_chatLines)
        {
            int oldVisibleLines = GetInGameChatVisibleLines();
            bool followTail = oldVisibleLines > 0 && _chatBoardTop >= Math.Max(0, _chatLines.Count - oldVisibleLines);

            int nextLines = _chatWindowLines + 4;
            if (nextLines > 12 || nextLines % 4 != 0)
                nextLines = 4;
            _chatWindowLines = nextLines;

            ClampChatBoardTopNoLock();

            if (followTail)
            {
                int newVisibleLines = GetInGameChatVisibleLines();
                _chatBoardTop = Math.Max(0, _chatLines.Count - newVisibleLines);
            }
        }

        if (logUi)
            AppendLog($"[ui] chat window lines: {_chatWindowLines}");
    }

    private static string ExtractUserNameFromChatLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return string.Empty;

        string candidate = line.Trim();

        int cut = candidate.IndexOfAny(['(', '!', '*', '/', ')']);
        if (cut >= 0)
            candidate = candidate[..cut];

        int end = candidate.IndexOfAny([' ', '=', ':']);
        if (end >= 0)
            candidate = candidate[..end];

        candidate = candidate.Trim();
        if (candidate.Length == 0)
            return string.Empty;

        if (candidate[0] is '/' or '(' or ' ' or '[')
            return string.Empty;

        return candidate;
    }

    private float MeasureHalfTextWidth(string text)
    {
        if (_textRenderer == null || string.IsNullOrEmpty(text))
            return 0f;

        lock (_halfTextWidthCache)
        {
            if (_halfTextWidthCache.TryGetValue(text, out float cached))
                return cached;

            float half = _textRenderer.MeasureTextWidth(text) * 0.5f;
            _halfTextWidthCache[text] = half;
            return half;
        }
    }

    private void AppendChatOverlay(D3D11ViewTransform view)
    {
        if (_textRenderer == null)
            return;

        long nowMs = Environment.TickCount64;
        ChatLine[] lines;
        int chatBoardTop;

        lock (_chatLines)
        {
            if (_chatLines.Count == 0)
                return;

            lines = _chatLines.ToArray();
            chatBoardTop = _chatBoardTop;
        }

        if (_sceneManager.CurrentId == MirSceneId.Play)
        {
            const int lineHInGame = 12;

            int maxIndex = lines.Length - 1;
            if (maxIndex < 0)
                return;

            int visibleLinesInGame = GetInGameChatVisibleLines();
            if (visibleLinesInGame <= 0)
                return;

            DrawingRectangle chatRect = GetInGameChatBoardRect(view.LogicalSize);
            if (chatRect.Width <= 0 || chatRect.Height <= 0)
                return;

            int top = chatBoardTop;
            if (top < 0)
                top = 0;
            if (top > maxIndex)
                top = maxIndex;

            for (int i = 0; i < visibleLinesInGame; i++)
            {
                int idx = top + i;
                if (idx > maxIndex)
                    break;

                float lineY = chatRect.Top + (i * lineHInGame);
                Vector2 p = view.ToBackBuffer(new Vector2(chatRect.Left, lineY));
                _textRenderer.DrawText(lines[idx].Text, p.X + 1, p.Y + 1, new Color4(0, 0, 0, 0.75f));
                _textRenderer.DrawText(lines[idx].Text, p.X, p.Y, lines[idx].Color);
            }

            return;
        }

        const int marginX = 16;
        const int marginBottom = 16;
        const int lineH = 18;

        int bottomLimit = view.LogicalSize.Height - marginBottom;
        if (_merchantDialogPanelRect is { } merchantRect)
            bottomLimit = Math.Min(bottomLimit, merchantRect.Top - 8);
        if (_chatInputActive)
            bottomLimit -= (lineH + 4);

        int maxVisibleLines = Math.Min(12, Math.Max(0, (bottomLimit - 24) / lineH));
        if (maxVisibleLines <= 0)
            return;

        float y = bottomLimit - lineH;
        int drawn = 0;
        for (int i = lines.Length - 1; i >= 0 && drawn < maxVisibleLines; i--)
        {
            long age = nowMs - lines[i].TimestampMs;
            if (age < 0)
                age = 0;

            if (age >= ChatOverlayTtlMs)
                continue;

            float alpha = 1f;
            if (age > ChatOverlayTtlMs - ChatOverlayFadeMs)
                alpha = Math.Clamp((ChatOverlayTtlMs - age) / (float)ChatOverlayFadeMs, 0f, 1f);

            Color4 baseColor = lines[i].Color;
            var color = new Color4(baseColor.R, baseColor.G, baseColor.B, baseColor.A * alpha);
            var shadow = new Color4(0, 0, 0, 0.75f * alpha);

            Vector2 p = view.ToBackBuffer(new Vector2(marginX, y));
            _textRenderer.DrawText(lines[i].Text, p.X + 1, p.Y + 1, shadow);
            _textRenderer.DrawText(lines[i].Text, p.X, p.Y, color);

            y -= lineH;
            drawn++;
        }
    }

    private void AppendSystemMessageOverlay(D3D11ViewTransform view)
    {
        if (_textRenderer == null)
            return;

        if (_sceneManager.CurrentId != MirSceneId.Play)
            return;

        int screenW = view.LogicalSize.Width;
        int screenH = view.LogicalSize.Height;
        if (screenW <= 0 || screenH <= 0)
            return;

        float scaleX = view.Scale.X;
        if (scaleX <= 0.0001f)
            scaleX = 1f;

        long nowMs = Environment.TickCount64;

        lock (_sysMessageLock)
        {
            while (_sysMsgTop.Count > 0 && nowMs - _sysMsgTop[0].TimestampMs >= SysMessageTopTtlMs)
                _sysMsgTop.RemoveAt(0);

            if (_sysMsgTop.Count > 0)
            {
                float y = 30;
                for (int i = 0; i < _sysMsgTop.Count; i++)
                {
                    DrawShadowedText(_sysMsgTop[i].Text, 20, y, new Color4(0.55f, 0.95f, 0.55f, 1f), new Color4(0, 0, 0, 0.75f));
                    y += 16;
                }
            }

            while (_sysMsgBottom.Count > 0 && nowMs - _sysMsgBottom[0].TimestampMs >= SysMessageBottomTtlMs)
                _sysMsgBottom.RemoveAt(0);

            if (_sysMsgBottom.Count > 0)
            {
                float y = screenH - 250;
                for (int i = 0; i < _sysMsgBottom.Count; i++)
                {
                    Color4 color = IsSysMsgGreen(_sysMsgBottom[i].Text)
                        ? new Color4(0.55f, 0.95f, 0.55f, 1f)
                        : new Color4(1.0f, 0.35f, 0.35f, 1f);
                    DrawShadowedText(_sysMsgBottom[i].Text, 20, y, color, new Color4(0, 0, 0, 0.75f));
                    y -= 16;
                }
            }

            while (_sysMsgBottomRight.Count > 0 && nowMs - _sysMsgBottomRight[0].TimestampMs >= SysMessageBottomRightTtlMs)
                _sysMsgBottomRight.RemoveAt(0);

            if (_sysMsgBottomRight.Count > 0)
            {
                float y = screenH - 270;
                for (int i = 0; i < _sysMsgBottomRight.Count; i++)
                {
                    string text = _sysMsgBottomRight[i].Text;
                    float widthLogical = _textRenderer.MeasureTextWidth(text) / scaleX;
                    float x = screenW - widthLogical - 14;
                    DrawShadowedText(text, x, y, new Color4(1.0f, 0.35f, 0.35f, 1f), new Color4(0, 0, 0, 0.75f));
                    y -= 16;
                }
            }

            if (_sysMarquee.Count > 0)
            {
                int right = screenW - 5;
                const float y = 6;
                for (int i = _sysMarquee.Count - 1; i >= 0; i--)
                {
                    SysMarqueeEntry entry = _sysMarquee[i];
                    bool canDraw = i == _sysMarquee.Count - 1;

                    if (!canDraw && i + 1 < _sysMarquee.Count)
                    {
                        SysMarqueeEntry next = _sysMarquee[i + 1];
                        float nextWidth = _textRenderer.MeasureTextWidth(next.Text) / scaleX;
                        if (next.Offset >= nextWidth + 82f)
                            canDraw = true;
                    }

                    if (!canDraw)
                        continue;

                    float x = right - entry.Offset;
                    Color4 fore = ToColor4(Mir2ColorTable.GetArgb(entry.ForeIndex));
                    Color4 back = ToColor4(Mir2ColorTable.GetArgb(entry.BackIndex));
                    DrawShadowedText(entry.Text, x, y, fore, back);
                    entry.Offset += 1f;

                    float width = _textRenderer.MeasureTextWidth(entry.Text) / scaleX;
                    if (entry.Offset >= width + screenW)
                        _sysMarquee.RemoveAt(i);
                }
            }
        }

        void DrawShadowedText(string text, float x, float y, Color4 color, Color4 shadow)
        {
            Vector2 p = view.ToBackBuffer(new Vector2(x, y));
            _textRenderer.DrawText(text, p.X + 1, p.Y + 1, shadow);
            _textRenderer.DrawText(text, p.X, p.Y, color);
        }

        static bool IsSysMsgGreen(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return text.Contains("经验", StringComparison.Ordinal) || text.Contains("升级", StringComparison.Ordinal);
        }
    }

    private DrawingRectangle GetChatInputRect(DrawingSize logicalSize)
    {
        if (_sceneManager.CurrentId == MirSceneId.Play)
        {
            const int chatX = 210;
            const int yFromBottom = 16;
            const int h = 12;

            int chatW = Math.Max(0, (logicalSize.Width / 2 - 207) * 2);
            if (chatX >= logicalSize.Width)
                return DrawingRectangle.Empty;
            if (chatW <= 0)
                chatW = Math.Max(0, logicalSize.Width - chatX);
            if (chatX + chatW > logicalSize.Width)
                chatW = Math.Max(0, logicalSize.Width - chatX);

            int chatY = Math.Max(0, logicalSize.Height - yFromBottom);
            if (_merchantDialogPanelRect is { } merchantRect2)
                chatY = Math.Min(chatY, merchantRect2.Top - 8 - h);
            chatY = Math.Max(0, chatY);

            return new DrawingRectangle(chatX, chatY, chatW, h);
        }

        const int marginX = 16;
        const int marginBottom = 16;
        const int height = 28;

        int w = Math.Max(0, logicalSize.Width - (marginX * 2));
        if (w > 740)
            w = 740;
        if (w <= 0)
            w = logicalSize.Width;

        int x = marginX;
        if (x + w > logicalSize.Width)
            x = 0;

        int y = logicalSize.Height - marginBottom - height;
        if (_merchantDialogPanelRect is { } merchantRect)
            y = Math.Min(y, merchantRect.Top - 8 - height);
        y = Math.Max(0, y);

        return new DrawingRectangle(x, y, w, height);
    }

    private bool TryDrawChatInputUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!_chatInputActive)
            return false;

        if (LoginUiVisible)
            return false;

        if (_sceneManager.CurrentId == MirSceneId.Play)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        int w = view.LogicalSize.Width;
        int h = view.LogicalSize.Height;
        if (w <= 0 || h <= 0)
            return false;

        DrawingRectangle rect = GetChatInputRect(view.LogicalSize);
        if (rect.Width <= 0 || rect.Height <= 0)
            return false;

        Color4 border = _passwordInputMode ? new Color4(0.95f, 0.65f, 0.35f, 1f) : new Color4(0, 0, 0, 0.65f);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.78f));
        _spriteBatch.Draw(
            _whiteTexture,
            new DrawingRectangle(rect.X + 1, rect.Y + 1, Math.Max(0, rect.Width - 2), Math.Max(0, rect.Height - 2)),
            color: new Color4(0.12f, 0.12f, 0.16f, 0.92f));
        DrawRectBorder(rect, border);
        _spriteBatch.End();

        stats = _spriteBatch.Stats;
        return true;
    }

    private bool TryDrawSystemNoticeBar(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (_sceneManager.CurrentId != MirSceneId.Play)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        int w = view.LogicalSize.Width;
        if (w <= 0)
            return false;

        lock (_sysMessageLock)
        {
            if (_sysMarquee.Count == 0)
                return false;
        }

        const int barH = 28;
        Color4 barColor = ToColor4(Mir2ColorTable.GetArgb(152), alpha: 150f / 255f);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(0, 0, w, barH), color: barColor);
        _spriteBatch.End();

        stats = _spriteBatch.Stats;
        return true;
    }

    private void AppendChatInputOverlay(D3D11ViewTransform view)
    {
        if (_textRenderer == null)
            return;

        if (!_chatInputActive || LoginUiVisible)
            return;

        DrawingRectangle rect = GetChatInputRect(view.LogicalSize);
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        bool caretOn = ((Environment.TickCount64 - _chatInputFocusStartMs) / 500) % 2 == 0;
        string display = _uiChatInput.GetDisplayText();

        string prefix = _passwordInputMode ? "[pwd] " : string.Empty;
        string text = prefix + display;

        float textX = rect.X + 2;
        float textY = rect.Y - 2;
        if (textY < 0)
            textY = 0;

        Vector2 p = view.ToBackBuffer(new Vector2(textX, textY));
        _textRenderer.DrawText(text, p.X + 1, p.Y + 1, new Color4(0, 0, 0, 0.75f));
        _textRenderer.DrawText(text, p.X, p.Y, new Color4(0.92f, 0.92f, 0.92f, 1));

        if (caretOn)
        {
            float caretX = p.X + _textRenderer.MeasureTextWidth(text);
            _textRenderer.DrawText("|", caretX, p.Y, new Color4(0, 0, 0, 1));
        }
    }

    private void AppendSceneOverlay(D3D11ViewTransform view)
    {
        if (_textRenderer == null)
            return;

        if (LoginUiVisible)
            return;

        if (_sceneManager.CurrentId == MirSceneId.Play)
            return;

        bool connected = _session.IsConnected;

        string text = _session.Stage switch
        {
            MirSessionStage.Idle => "Intro",
            MirSessionStage.LoginGate => "Connecting LoginGate...",
            MirSessionStage.SelectCountry => "Select Country",
            MirSessionStage.SelectServer => "Select Server",
            MirSessionStage.SelectGate => "Connecting SelGate...",
            MirSessionStage.SelectCharacter => "Select Character",
            MirSessionStage.RunGate => "Connecting RunGate...",
            MirSessionStage.InGame => connected ? string.Empty : "Reconnecting...",
            _ => "Loading..."
        };

        if (string.IsNullOrWhiteSpace(text))
            return;

        string hint = _session.Stage switch
        {
            MirSessionStage.Idle => "F2: Login    F12: Debug Panel",
            _ => string.Empty
        };

        Vector2 center = view.ToBackBuffer(new Vector2(view.LogicalSize.Width * 0.5f, view.LogicalSize.Height * 0.5f));
        float y = center.Y - (string.IsNullOrWhiteSpace(hint) ? 16 : 26);

        float x = center.X - MeasureHalfTextWidth(text);

        _textRenderer.DrawText(text, x + 1, y + 1, new Color4(0, 0, 0, 0.85f));
        _textRenderer.DrawText(text, x, y, new Color4(0.95f, 0.95f, 0.95f, 1));

        if (!string.IsNullOrWhiteSpace(hint))
        {
            float y2 = y + 22;
            float x2 = center.X - MeasureHalfTextWidth(hint);
            _textRenderer.DrawText(hint, x2 + 1, y2 + 1, new Color4(0, 0, 0, 0.85f));
            _textRenderer.DrawText(hint, x2, y2, new Color4(0.85f, 0.85f, 0.85f, 1));
        }
    }

    private void PruneChatLinesNoLock(long nowMs)
    {
        if (_chatLines.Count == 0)
            return;

        int removeCount = 0;
        while (removeCount < _chatLines.Count)
        {
            long age = nowMs - _chatLines[removeCount].TimestampMs;
            if (age >= 0 && age >= ChatOverlayTtlMs)
            {
                removeCount++;
                continue;
            }

            break;
        }

        if (removeCount > 0)
            _chatLines.RemoveRange(0, removeCount);
    }

    private void EnsureDemoResources(D3D11Frame frame)
    {
        if (_renderDeviceVersion != frame.DeviceVersion)
        {
            _spriteBatch?.Dispose();
            _spriteBatch = null;

            if (!_demoTextureOwnedByCache)
                _demoTexture?.Dispose();

            _demoTexture = null;
            _demoTextureOwnedByCache = false;

            _whiteTexture?.Dispose();
            _whiteTexture = null;

            _introLogoTexture?.Dispose();
            _introLogoTexture = null;
            _introLogoTextureLoadFailed = false;

            _fogLightMapTexture?.Dispose();
            _fogLightMapTexture = null;

            for (int i = 0; i < _fogLightTextures.Length; i++)
            {
                _fogLightTextures[i]?.Dispose();
                _fogLightTextures[i] = null;
                _fogLightTextureLoadFailed[i] = false;
            }

            _textRenderer?.Dispose();
            _textRenderer = null;

            _wilTextureCache?.Dispose();
            _wilTextureCache = null;
            _dataTextureCache?.Dispose();
            _dataTextureCache = null;

            _wilImageTask = null;
            _packDataImageTask = null;
            _renderDeviceVersion = frame.DeviceVersion;
        }

        _spriteBatch ??= new D3D11SpriteBatch(frame.Device, maxSprites: _spriteBatchMaxSprites);
        _whiteTexture ??= D3D11Texture2D.CreateWhite(frame.Device);
        _textRenderer ??= new D3D11TextRenderer(frame.Device);
        _wilTextureCache ??= new D3D11TextureCache<WilImageKey>(_gpuWilCacheBytes, WilImageKeyComparer.Instance);
        _dataTextureCache ??= new D3D11TextureCache<PackDataImageKey>(_gpuDataCacheBytes, PackDataImageKeyComparer.Instance);

        if (_demoTexture == null)
        {
            _demoTexture = D3D11Texture2D.CreateCheckerboard(
                frame.Device,
                width: 256,
                height: 256,
                cellSize: 16,
                colorA: DrawingColor.FromArgb(255, 50, 60, 80),
                colorB: DrawingColor.FromArgb(255, 120, 140, 190));
            _demoTextureOwnedByCache = false;
        }

        if (!string.IsNullOrWhiteSpace(_dataPath))
        {
            if (_packDataImageTask == null)
            {
                _packDataImageTask = _packDataImageCache.GetImageAsync(_dataPath, _dataIndex);
                _packDataImageTaskHandled = false;
            }

            if (!_packDataImageTaskHandled && _packDataImageTask.IsCompletedSuccessfully)
            {
                PackDataImage? dataImage = _packDataImageTask.Result;
                if (dataImage != null)
                {
                    string fullPath = Path.GetFullPath(_dataPath);
                    var key = new PackDataImageKey(fullPath, _dataIndex);
                    D3D11Texture2D tex = _dataTextureCache.GetOrCreate(
                        key,
                        () => D3D11Texture2D.CreateFromBgra32(frame.Device, dataImage.Bgra32, dataImage.Width, dataImage.Height));

                    if (_demoTexture != null && !_demoTextureOwnedByCache)
                        _demoTexture.Dispose();

                    _demoTexture = tex;
                    _demoTextureOwnedByCache = true;
                    AppendLog($"[data] cached {fullPath}#{_dataIndex} {dataImage.Width}x{dataImage.Height} px={dataImage.Px} py={dataImage.Py}");
                }
                else
                {
                    AppendLog($"[data] decode failed: {_dataPath}#{_dataIndex}");
                }

                _packDataImageTaskHandled = true;
            }
            else if (!_packDataImageTaskHandled && _packDataImageTask.IsFaulted)
            {
                Exception? ex = _packDataImageTask.Exception?.GetBaseException();
                AppendLog($"[data] load error: {ex?.GetType().Name}: {ex?.Message}");
                _packDataImageTaskHandled = true;
            }
        }
        else if (!string.IsNullOrWhiteSpace(_wilPath))
        {
            if (_wilImageTask == null)
            {
                _wilImageTask = _wilImageCache.GetImageAsync(_wilPath, _wilIndex);
                _wilImageTaskHandled = false;
            }

            if (!_wilImageTaskHandled && _wilImageTask.IsCompletedSuccessfully)
            {
                WilImage? wilImage = _wilImageTask.Result;
                if (wilImage != null)
                {
                    string wilFullPath = Path.GetFullPath(_wilPath);
                    var key = new WilImageKey(wilFullPath, _wilIndex);
                    D3D11Texture2D tex = _wilTextureCache.GetOrCreate(
                        key,
                        () => D3D11Texture2D.CreateFromBgra32(frame.Device, wilImage.Bgra32, wilImage.Width, wilImage.Height));

                    if (_demoTexture != null && !_demoTextureOwnedByCache)
                        _demoTexture.Dispose();

                    _demoTexture = tex;
                    _demoTextureOwnedByCache = true;
                    AppendLog($"[wil] cached {wilFullPath}#{_wilIndex} {wilImage.Width}x{wilImage.Height} px={wilImage.Px} py={wilImage.Py}");
                }
                else
                {
                    AppendLog($"[wil] decode failed: {_wilPath}#{_wilIndex}");
                }

                _wilImageTaskHandled = true;
            }
            else if (!_wilImageTaskHandled && _wilImageTask.IsFaulted)
            {
                Exception? ex = _wilImageTask.Exception?.GetBaseException();
                AppendLog($"[wil] load error: {ex?.GetType().Name}: {ex?.Message}");
                _wilImageTaskHandled = true;
            }
        }
    }

    private void SplitCacheBudgets()
    {
        _cpuWilCacheBytes = Math.Max(0, _cpuCacheBytes / 2);
        _cpuDataCacheBytes = Math.Max(0, _cpuCacheBytes - _cpuWilCacheBytes);

        _gpuWilCacheBytes = Math.Max(0, _gpuCacheBytes / 2);
        _gpuDataCacheBytes = Math.Max(0, _gpuCacheBytes - _gpuWilCacheBytes);
    }

    private void InvalidateMapTilePrefetch()
    {
        _mapTilePrefetchDirty = true;
        _mapTilePrefetchQueue.Clear();
        _mapTilePrefetchSet.Clear();
        _mapWilPrefetchQueue.Clear();
        _mapWilPrefetchSet.Clear();
    }

    private bool TryDrawMapTiles(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats combinedStats)
    {
        combinedStats = default;

        if (_map == null || _spriteBatch == null || (_dataTextureCache == null && _wilTextureCache == null))
            return false;

        string resourceRoot = GetResourceRootDir();
        string dataDir = Path.Combine(resourceRoot, "Data");
        if (!Directory.Exists(dataDir))
            return false;

        if (TryResolveTilesArchivePath(dataDir, unit: 0) == null && TryResolveSmTilesArchivePath(dataDir, unit: 0) == null)
            return false;

        string? magicPath = TryResolveArchiveFilePath(dataDir, "Magic");
        long nowTimestamp = Stopwatch.GetTimestamp();
        long nowMs = Environment.TickCount64;

        Vector2 mouseLogical = new(float.NegativeInfinity, float.NegativeInfinity);
        try
        {
            System.Drawing.Point mouseClient = _renderControl.PointToClient(Cursor.Position);
            if (view.ViewportRect.Contains(mouseClient))
            {
                mouseLogical = new Vector2(
                    (mouseClient.X - view.Offset.X) / Math.Max(0.0001f, view.Scale.X),
                    (mouseClient.Y - view.Offset.Y) / Math.Max(0.0001f, view.Scale.Y));
            }
        }
        catch
        {
            
        }

        int centerX = _world.MapCenterSet ? _world.MapCenterX : _map.Width / 2;
        int centerY = _world.MapCenterSet ? _world.MapCenterY : _map.Height / 2;
        centerX = Math.Clamp(centerX, 0, Math.Max(0, _map.Width - 1));
        centerY = Math.Clamp(centerY, 0, Math.Max(0, _map.Height - 1));

        int unitX = Grobal2.UNITX;
        int unitY = Grobal2.UNITY;
        int w = view.LogicalSize.Width;
        int h = view.LogicalSize.Height;

        int offsetX = (int)Math.Round((w / 2.0) / unitX) + 1;
        int offsetY = (int)Math.Round((h / 2.0) / unitY);

        int left = centerX - offsetX;
        int right = centerX + offsetX;
        int top = centerY - offsetY;
        int bottom = centerY + offsetY - 1;

        EnsureMapTilePrefetchQueue(view.LogicalSize, resourceRoot, dataDir, left, right, top, bottom);
        PumpMapTilePrefetch(maxPerFrame: 2);

        int aax = ((w - unitX) / 2) % unitX;

        int shiftX = 0;
        int shiftY = 0;
        if (_world.MyselfRecogIdSet && _world.TryGetActor(_world.MyselfRecogId, out ActorMarker myself))
        {
            float rx = myself.X;
            float ry = myself.Y;

            if (MirDirection.IsMoveAction(myself.Action))
            {
                (int moveFrames, int moveFrameTimeMs) = GetActorMoveTiming(_world.MyselfRecogId, myself);
                long totalMs = (long)moveFrames * moveFrameTimeMs;
                if (totalMs > 0)
                {
                    long elapsedMs = (nowTimestamp - myself.ActionStartTimestamp) * 1000 / Stopwatch.Frequency;
                    if (elapsedMs >= 0 && elapsedMs < totalMs)
                    {
                        float t = Math.Clamp(elapsedMs / (float)totalMs, 0f, 1f);
                        rx = myself.FromX + ((myself.X - myself.FromX) * t);
                        ry = myself.FromY + ((myself.Y - myself.FromY) * t);
                    }
                }
            }

            shiftX = (int)Math.Round((rx - centerX) * unitX);
            shiftY = (int)Math.Round((ry - centerY) * unitY);
        }

        int defx = (-unitX * 2) - shiftX + aax;
        int defy = (-unitY * 2) - shiftY;

        string?[] tilesPaths = new string?[256];
        string?[] smTilesPaths = new string?[256];
        string?[] objectsPaths = new string?[256];

        string? GetTilesPath(byte unit)
        {
            int idx = unit;
            string? path = tilesPaths[idx];
            if (path != null)
                return path.Length == 0 ? null : path;

            path = TryResolveTilesArchivePath(dataDir, unit);
            tilesPaths[idx] = path ?? string.Empty;
            return path;
        }

        string? GetSmTilesPath(byte unit)
        {
            int idx = unit;
            string? path = smTilesPaths[idx];
            if (path != null)
                return path.Length == 0 ? null : path;

            path = TryResolveSmTilesArchivePath(dataDir, unit);
            smTilesPaths[idx] = path ?? string.Empty;
            return path;
        }

        string? GetObjectsPath(byte unit)
        {
            int idx = unit;
            string? path = objectsPaths[idx];
            if (path != null)
                return path.Length == 0 ? null : path;

            path = TryResolveObjectsArchivePath(dataDir, unit);
            objectsPaths[idx] = path ?? string.Empty;
            return path;
        }

        bool IsDoorOpenAt(int x, int y, MirMapCell cell)
        {
            bool open = (cell.DoorOffset & 0x80) != 0;

            int key = (x << 16) | (y & 0xFFFF);
            if (_world.DoorOpenOverrides.TryGetValue(key, out bool overrideOpen))
                open = overrideOpen;

            return open;
        }

	        bool TryGetTileTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
	        {
	            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
	                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase) ||
	                archivePath.EndsWith(".wzl", StringComparison.OrdinalIgnoreCase))
	            {
                if (_wilTextureCache == null)
                {
                    texture = null!;
                    return false;
                }

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

                EnqueueMapTilePrefetch(key);
                texture = null!;
                return false;
            }

            if (_dataTextureCache == null)
            {
                texture = null!;
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

            EnqueueMapTilePrefetch(dataKey);
            texture = null!;
            return false;
        }

            bool TryGetTileTextureWithPivot(string archivePath, int imageIndex, out D3D11Texture2D texture, out (short Px, short Py) pivot)
            {
                pivot = default;

                if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                    archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase) ||
                    archivePath.EndsWith(".wzl", StringComparison.OrdinalIgnoreCase))
                {
                    texture = null!;
                    if (_wilTextureCache == null)
                        return false;

                    var key = new WilImageKey(archivePath, imageIndex);
                    if (_wilTextureCache.TryGet(key, out texture!))
                    {
                        if (_wilImagePivots.TryGetValue(key, out pivot))
                            return true;
                    }

                    if (_wilImageCache.TryGetImage(key, out WilImage image))
                    {
                        pivot = (image.Px, image.Py);
                        _wilImagePivots[key] = pivot;
                        texture = _wilTextureCache.GetOrCreate(
                            key,
                            () => D3D11Texture2D.CreateFromBgra32(frame.Device, image.Bgra32, image.Width, image.Height));
                        return true;
                    }

                    EnqueueMapTilePrefetch(key);
                    return false;
                }

                texture = null!;
                if (_dataTextureCache == null)
                    return false;

                var dataKey = new PackDataImageKey(archivePath, imageIndex);
                if (_dataTextureCache.TryGet(dataKey, out texture!))
                {
                    if (_dataImagePivots.TryGetValue(dataKey, out pivot))
                        return true;
                }

                if (_packDataImageCache.TryGetImage(dataKey, out PackDataImage dataImage))
                {
                    pivot = (dataImage.Px, dataImage.Py);
                    _dataImagePivots[dataKey] = pivot;
                    texture = _dataTextureCache.GetOrCreate(
                        dataKey,
                        () => D3D11Texture2D.CreateFromBgra32(frame.Device, dataImage.Bgra32, dataImage.Width, dataImage.Height));
                    return true;
                }

                EnqueueMapTilePrefetch(dataKey);
                return false;
            }

        int drawCalls = 0;
        int textureBinds = 0;
        int sprites = 0;
        int scissorChanges = 0;

        int xStart = left - 2;
        int xEnd = right + 1;
        int yStart = top - 1;
        int yEnd = bottom + 1;

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.Opaque);
        for (int y = yStart; y <= yEnd; y++)
        {
            if ((uint)y >= (uint)_map.Height)
                continue;

            int py = ((y - top - 1) * unitY) + defy;
            for (int x = xStart; x <= xEnd; x++)
            {
                if ((uint)x >= (uint)_map.Width)
                    continue;

                if (((x | y) & 1) != 0)
                    continue;

                int px = ((x - left) * unitX) + defx;
                MirMapCell cell = _map.GetCell(x, y);
                int imgNumber = cell.BkIndex;
                if (imgNumber <= 0)
                    continue;

                imgNumber--;
                string? tilesPath = GetTilesPath(cell.Tiles);
                if (tilesPath == null)
                    continue;

                if (!TryGetTileTexture(tilesPath, imgNumber, out D3D11Texture2D tex))
                    continue;

                _spriteBatch.Draw(tex, new DrawingRectangle(px, py, tex.Width, tex.Height));
            }
        }
        _spriteBatch.End();

        SpriteBatchStats passStats = _spriteBatch.Stats;
        drawCalls += passStats.DrawCalls;
        textureBinds += passStats.TextureBinds;
        sprites += passStats.Sprites;
        scissorChanges += passStats.ScissorChanges;

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        for (int y = yStart; y <= yEnd; y++)
        {
            if ((uint)y >= (uint)_map.Height)
                continue;

            int py = ((y - top - 1) * unitY) + defy;
            for (int x = xStart; x <= xEnd; x++)
            {
                if ((uint)x >= (uint)_map.Width)
                    continue;

                int px = ((x - left) * unitX) + defx;
                MirMapCell cell = _map.GetCell(x, y);
                int imgNumber = cell.MidIndex;
                if (imgNumber <= 0)
                    continue;

                imgNumber--;
                string? smTilesPath = GetSmTilesPath(cell.SmTiles);
                if (smTilesPath == null)
                    continue;

                if (!TryGetTileTexture(smTilesPath, imgNumber, out D3D11Texture2D tex))
                    continue;

                _spriteBatch.Draw(tex, new DrawingRectangle(px, py, tex.Width, tex.Height));
            }
        }
        _spriteBatch.End();

        passStats = _spriteBatch.Stats;
        drawCalls += passStats.DrawCalls;
        textureBinds += passStats.TextureBinds;
        sprites += passStats.Sprites;
        scissorChanges += passStats.ScissorChanges;

        if (TryResolveObjectsArchivePath(dataDir, unit: 0) != null)
        {
            const int longHeightImage = 35;
            int objXStart = left - 2;
            int objXEnd = right + 2;
            int objYStart = top;
            int objYEnd = bottom + longHeightImage;

            long aniFrameCounter = nowMs / 50;
            string? aniTilesPath = TryResolveArchiveFilePath(dataDir, "AniTiles1");
            bool isN6 = string.Equals(Path.GetFileNameWithoutExtension(_map.MapPath), "n6", StringComparison.OrdinalIgnoreCase);

            
            _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
            for (int y = objYStart; y <= objYEnd; y++)
            {
                if ((uint)y >= (uint)_map.Height)
                    continue;

                int baseY = ((y - top - 1) * unitY) + defy;
                for (int x = objXStart; x <= objXEnd; x++)
                {
                    if ((uint)x >= (uint)_map.Width)
                        continue;

                    int baseX = ((x - left) * unitX) + defx;
                    MirMapCell cell = _map.GetCell(x, y);
                    int frIndex = cell.FrIndex;
                    if (frIndex <= 0)
                        continue;

                    string? objectsPath = GetObjectsPath(cell.Area);
                    if (objectsPath == null)
                        continue;

                    int ani = cell.AniFrame;
                    if ((ani & 0x80) != 0)
                        continue;

                    int imgNumber = frIndex;
                    if (ani > 0)
                    {
                        int aniTick = cell.AniTick;
                        int total = ani + (ani * aniTick);
                        if (total > 0)
                            imgNumber += (int)((aniFrameCounter % total) / (1 + aniTick));
                    }

                    if (IsDoorOpenAt(x, y, cell) && (cell.DoorIndex & 0x7F) > 0)
                        imgNumber += cell.DoorOffset & 0x7F;

                    imgNumber--;
                    if (imgNumber < 0)
                        continue;

                    if (!TryGetTileTexture(objectsPath, imgNumber, out D3D11Texture2D tex))
                        continue;

                    if (tex.Width != unitX || tex.Height != unitY)
                        continue;

                    int destY = baseY + unitY - tex.Height;
                    _spriteBatch.Draw(tex, new DrawingRectangle(baseX, destY, tex.Width, tex.Height));
                }
            }
            _spriteBatch.End();

            passStats = _spriteBatch.Stats;
            drawCalls += passStats.DrawCalls;
            textureBinds += passStats.TextureBinds;
            sprites += passStats.Sprites;
            scissorChanges += passStats.ScissorChanges;

            
            _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
            if (_mapEffectAction > 20)
                _mapEffectAction = 0;

            for (int y = objYStart; y <= objYEnd; y++)
            {
                if ((uint)y >= (uint)_map.Height)
                    continue;

                int baseY = ((y - top - 1) * unitY) + defy;
                for (int x = objXStart; x <= objXEnd; x++)
                {
                    if ((uint)x >= (uint)_map.Width)
                        continue;

                    int baseX = ((x - left) * unitX) + defx;
                    MirMapCell cell = _map.GetCell(x, y);

                    int midIndex = cell.MidIndex;
                    if (midIndex > 0)
                    {
                        if ((cell.DoorIndex2 != 0 && _mapEffectAction == cell.DoorIndex2) ||
                            (cell.DoorOffset2 != 0 && _mapEffectAction >= cell.DoorOffset2))
                        {
                            _mapEffectAction = 0;
                        }

                        if (_mapEffectAction > 20)
                            _mapEffectAction = 0;

                        if (cell.DoorIndex2 > 0)
                        {
                            int step = cell.FrImg2 & 0xFF;
                            int imgNumber = midIndex - 1;
                            if (step > 0)
                                imgNumber += _mapEffectAction * step;

                            string? smTilesPath = GetSmTilesPath(cell.SmTiles);
                            if (smTilesPath != null && imgNumber >= 0 && TryGetTileTexture(smTilesPath, imgNumber, out D3D11Texture2D tex))
                                _spriteBatch.Draw(tex, new DrawingRectangle(baseX, baseY, tex.Width, tex.Height));
                        }
                    }

                    if (aniTilesPath != null)
                    {
                        int tani = cell.DoorOffset2;
                        bool blend = (tani & 0x80) != 0;
                        tani &= 0x7F;

                        int taniIndex = cell.BkImg2;
                        if (!blend && taniIndex > 0 && tani > 0)
                        {
                            int offset = cell.AniFrame2 ^ 0x2000;
                            int frameIndex = (int)(aniFrameCounter % tani);
                            int imgNumber = (taniIndex - 1) + (offset * frameIndex);
                            if (imgNumber >= 0 && TryGetTileTexture(aniTilesPath, imgNumber, out D3D11Texture2D tex))
                            {
                                int destY = baseY + unitY - tex.Height;
                                _spriteBatch.Draw(tex, new DrawingRectangle(baseX, destY, tex.Width, tex.Height));
                            }
                        }
                    }

                    int frIndex = cell.FrIndex;
                    if (frIndex <= 0)
                        continue;

                    string? objectsPath = GetObjectsPath(cell.Area);
                    if (objectsPath == null)
                        continue;

                    int ani = cell.AniFrame;
                    bool blendObj = (ani & 0x80) != 0;
                    if (blendObj)
                        continue;

                    int imgNumber2 = frIndex;
                    if (ani > 0)
                    {
                        int aniTick = cell.AniTick;
                        int total = ani + (ani * aniTick);
                        if (total > 0)
                            imgNumber2 += (int)((aniFrameCounter % total) / (1 + aniTick));
                    }

                    if (IsDoorOpenAt(x, y, cell) && (cell.DoorIndex & 0x7F) > 0)
                        imgNumber2 += cell.DoorOffset & 0x7F;

                    imgNumber2--;
                    if (imgNumber2 < 0)
                        continue;

                    if (!TryGetTileTexture(objectsPath, imgNumber2, out D3D11Texture2D objTex))
                        continue;

                    if (objTex.Width == unitX && objTex.Height == unitY)
                        continue;

                    int objY = baseY + unitY - objTex.Height;
                    _spriteBatch.Draw(objTex, new DrawingRectangle(baseX, objY, objTex.Width, objTex.Height));
                }
            }
            _spriteBatch.End();

            passStats = _spriteBatch.Stats;
            drawCalls += passStats.DrawCalls;
            textureBinds += passStats.TextureBinds;
            sprites += passStats.Sprites;
            scissorChanges += passStats.ScissorChanges;

            
            _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.Additive);
            for (int y = objYStart; y <= objYEnd; y++)
            {
                if ((uint)y >= (uint)_map.Height)
                    continue;

                int baseY = ((y - top - 1) * unitY) + defy;
                for (int x = objXStart; x <= objXEnd; x++)
                {
                    if ((uint)x >= (uint)_map.Width)
                        continue;

                    int baseX = ((x - left) * unitX) + defx;
                    MirMapCell cell = _map.GetCell(x, y);

                    if (aniTilesPath != null)
                    {
                        int tani = cell.DoorOffset2;
                        bool blend = (tani & 0x80) != 0;
                        tani &= 0x7F;

                        int taniIndex = cell.BkImg2;
                        if (blend && taniIndex > 0 && tani > 0)
                        {
                            int offset = cell.AniFrame2 ^ 0x2000;
                            int frameIndex = (int)(aniFrameCounter % tani);
                            int imgNumber = (taniIndex - 1) + (offset * frameIndex);
                            if (imgNumber >= 0 && TryGetTileTextureWithPivot(aniTilesPath, imgNumber, out D3D11Texture2D tex, out (short Px, short Py) pivot))
                            {
                                int drawX = baseX + pivot.Px - 2;
                                int drawY = baseY + pivot.Py - 68;
                                _spriteBatch.Draw(tex, new DrawingRectangle(drawX, drawY, tex.Width, tex.Height));
                            }
                        }
                    }

                    int frIndex = cell.FrIndex;
                    if (frIndex <= 0)
                        continue;

                    string? objectsPath = GetObjectsPath(cell.Area);
                    if (objectsPath == null)
                        continue;

                    int ani = cell.AniFrame;
                    bool blendObj = (ani & 0x80) != 0;
                    if (!blendObj)
                        continue;

                    ani &= 0x7F;

                    int imgNumber2 = frIndex;
                    if (ani > 0)
                    {
                        int aniTick = cell.AniTick;
                        int total = ani + (ani * aniTick);
                        if (total > 0)
                            imgNumber2 += (int)((aniFrameCounter % total) / (1 + aniTick));
                    }

                    if (IsDoorOpenAt(x, y, cell) && (cell.DoorIndex & 0x7F) > 0)
                        imgNumber2 += cell.DoorOffset & 0x7F;

                    imgNumber2--;
                    if (imgNumber2 < 0)
                        continue;

                    if (!TryGetTileTextureWithPivot(objectsPath, imgNumber2, out D3D11Texture2D objTex, out (short Px, short Py) pivot2))
                        continue;

                    int drawX2 = baseX + pivot2.Px - 2;
                    int drawY2 = baseY + pivot2.Py - 68;

                    if (isN6)
                    {
                        drawX2 = baseX + pivot2.Px - 72;
                        drawY2 -= 140;
                    }
                    else if (objTex.Width == 128 && objTex.Height == 128)
                    {
                        drawX2 = baseX + pivot2.Px - 3;
                        drawY2 += cell.Temp0;
                    }

                    _spriteBatch.Draw(objTex, new DrawingRectangle(drawX2, drawY2, objTex.Width, objTex.Height));
                }
            }
            _spriteBatch.End();

            passStats = _spriteBatch.Stats;
            drawCalls += passStats.DrawCalls;
            textureBinds += passStats.TextureBinds;
            sprites += passStats.Sprites;
            scissorChanges += passStats.ScissorChanges;
        }

        if (!_world.MapMoving && _world.MapEvents.Count > 0)
        {
            string? eventPath = TryResolveArchiveFilePath(dataDir, "Event") ?? TryResolveArchiveFilePath(dataDir, "Effect/Event");
            string? effectPath = TryResolveArchiveFilePath(dataDir, "Effect");
            string? mon6Path = TryResolveArchiveFilePath(dataDir, "Mon6");
            string? mon7Path = TryResolveArchiveFilePath(dataDir, "Mon7");
            string? stateEffectPath = TryResolveArchiveFilePath(dataDir, "StateEffect");
            string? magic7Images2Path = TryResolveArchiveFilePath(dataDir, "Magic7");
            string? magic7Path = TryResolveArchiveFilePath(dataDir, "magic7-16") ?? magic7Images2Path;

            _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
            SpriteBlendMode blendMode = SpriteBlendMode.AlphaBlend;

            foreach ((int _, MapEventMarker ev) in _world.MapEvents)
            {
                int x = ev.X;
                int y = ev.Y;
                if ((uint)x >= (uint)_map.Width || (uint)y >= (uint)_map.Height)
                    continue;

                if (x < left - 2 || x > right + 2 || y < top - 1 || y > bottom + 40)
                    continue;

                int baseX = ((x - left) * unitX) + defx;
                int baseY = ((y - top - 1) * unitY) + defy;

                if (!TryResolveEventTexture(ev, nowMs, out string? archivePath, out int imageIndex, out bool blend))
                    continue;

                if (string.IsNullOrWhiteSpace(archivePath))
                    continue;

                SpriteBlendMode desired = blend ? SpriteBlendMode.Additive : SpriteBlendMode.AlphaBlend;
                if (desired != blendMode)
                {
                    _spriteBatch.SetBlendMode(desired);
                    blendMode = desired;
                }

                if (!TryGetTileTextureWithPivot(archivePath, imageIndex, out D3D11Texture2D tex, out (short Px, short Py) pivot))
                    continue;

                _spriteBatch.Draw(tex, new DrawingRectangle(baseX + pivot.Px, baseY + pivot.Py, tex.Width, tex.Height));
            }

            _spriteBatch.End();

            passStats = _spriteBatch.Stats;
            drawCalls += passStats.DrawCalls;
            textureBinds += passStats.TextureBinds;
            sprites += passStats.Sprites;
            scissorChanges += passStats.ScissorChanges;

            bool TryResolveEventTexture(MapEventMarker ev, long nowMs, out string? archivePath, out int imageIndex, out bool blend)
            {
                archivePath = null;
                imageIndex = 0;
                blend = false;

                long ageMs = nowMs - ev.StartTimestampMs;
                if (ageMs < 0)
                    ageMs = 0;

                long curFrame = ageMs / 20;

                switch (ev.EventType)
                {
                    case Grobal2.ET_PILESTONES:
                    {
                        int param = ev.EventParam;
                        if (param <= 0)
                            param = 1;
                        if (param > 5)
                            param = 5;

                        if (eventPath != null)
                        {
                            archivePath = eventPath;
                            imageIndex = 10 + (param - 1);
                            return true;
                        }

                        if (effectPath == null)
                            return false;

                        archivePath = effectPath;
                        imageIndex = 64 + (param - 1);
                        return true;
                    }
                    case Grobal2.ET_HOLYCURTAIN:
                    {
                        blend = true;
                        int frameIndex = (int)(curFrame % 10);

                        if (eventPath != null)
                        {
                            archivePath = eventPath;
                            imageIndex = 20 + frameIndex;
                            return true;
                        }

                        if (magicPath == null)
                            return false;

                        archivePath = magicPath;
                        imageIndex = 1390 + frameIndex;
                        return true;
                    }
                    case Grobal2.ET_FIRE:
                    {
                        blend = true;
                        int anim = (int)((curFrame / 2) % 6);

                        if (ev.EventLevel is >= 1 and <= 3 && magic7Path != null)
                        {
                            int baseIndex = 80 + (ev.EventLevel * 10);
                            int frameIndex = (int)((curFrame / 2) % 8);
                            archivePath = magic7Path;
                            imageIndex = baseIndex + frameIndex;
                            return true;
                        }

                        if (magicPath == null)
                            return false;

                        archivePath = magicPath;
                        imageIndex = 1630 + anim;
                        return true;
                    }
                    case Grobal2.ET_SCULPEICE:
                    {
                        if (eventPath != null)
                        {
                            archivePath = eventPath;
                            imageIndex = 59;
                            return true;
                        }

                        if (mon7Path == null)
                            return false;

                        archivePath = mon7Path;
                        imageIndex = 1349;
                        return true;
                    }
                    case Grobal2.ET_NIMBUS_1:
                    case Grobal2.ET_NIMBUS_2:
                    case Grobal2.ET_NIMBUS_3:
                    {
                        blend = true;
                        if (stateEffectPath == null)
                            return false;

                        archivePath = stateEffectPath;
                        imageIndex = (int)(curFrame % 19);
                        return true;
                    }
                    case Grobal2.ET_DIGOUTZOMBI:
                    case 2:
                    {
                        int dir = ev.Dir;
                        if (dir < 0 || dir > 7)
                            dir = Math.Clamp(ev.EventParam, 0, 7);

                        if (eventPath != null)
                        {
                            archivePath = eventPath;
                            imageIndex = dir;
                            return true;
                        }

                        if (mon6Path == null)
                            return false;

                        archivePath = mon6Path;
                        imageIndex = 420 + dir;
                        return true;
                    }
                }

                return false;
            }
        }

        if (_world.DropItems.Count > 0)
        {
            
            string? dnItemsPath = TryResolveArchiveFilePath(dataDir, "DnItems");
            string? dnItems2Path = TryResolveArchiveFilePath(dataDir, "DnItems2");
            string? shineDnItemsPath = TryResolveArchiveFilePath(dataDir, "ShineDnItems");

            const int maxNames = 60;
            int nameCount = 0;
            int prefetchBudget = 48;
            bool filterEnabled = _itemFilterStore.Rules.Count > 0;

            _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);

            foreach ((int _, DropItemMarker drop) in _world.DropItems)
            {
                string displayName = GetDropItemDisplayName(drop.Name);
                int x = drop.X;
                int y = drop.Y;
                if ((uint)x >= (uint)_map.Width || (uint)y >= (uint)_map.Height)
                    continue;

                if (x < left - 2 || x > right + 2 || y < top - 1 || y > bottom + 1)
                    continue;

                int baseX = ((x - left) * unitX) + defx;
                int baseY = ((y - top - 1) * unitY) + defy;

                int looks = drop.Looks;
                if (TryResolveItemIconArchive(looks, out string? archivePath, out int imageIndex) && archivePath != null)
                {
                    if (TryGetItemTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                    {
                        int destX = baseX + (unitX / 2) - (tex.Width / 2);
                        int destY = baseY + (unitY / 2) - (tex.Height / 2);
                        var rect = new DrawingRectangle(destX, destY, tex.Width, tex.Height);
                        _spriteBatch.Draw(tex, rect);

                        bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                                       mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                        if (hovered && _whiteTexture != null)
                            _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(1f, 1f, 1f, 0.25f));

                        if (hovered && string.IsNullOrWhiteSpace(_uiTooltipText))
                        {
                            string tooltip = BuildDropItemTooltip(x, y);
                            if (!string.IsNullOrWhiteSpace(tooltip))
                                SetTooltip(tooltip, new Vector2(baseX + 6, baseY - 2));
                        }
                    }
                    else if (prefetchBudget > 0)
                    {
                        PrefetchItemImage(archivePath, imageIndex);
                        prefetchBudget--;
                    }
                }

                if (_showDropItems && nameCount < maxNames && !string.IsNullOrWhiteSpace(displayName))
                {
                    bool showName = true;
                    bool rare = false;
                    if (filterEnabled && _itemFilterStore.Rules.TryGetValue(displayName, out ItemFilterStore.ItemFilterRule rule))
                    {
                        showName = rule.Show;
                        rare = rule.Rare;
                    }

                    if (rare || !filterEnabled || showName)
                    {
                        float nameX = baseX + (unitX / 2) - MeasureHalfTextWidth(displayName);
                        float nameY = baseY - 23;
                        Color4 color = rare
                            ? new Color4(1f, 0f, 0f, 1f)
                            : new Color4(0.35f, 0.75f, 1f, 1f);
                        _nameDrawList.Add(new NameDrawInfo(displayName, nameX, nameY, color));
                        nameCount++;
                    }
                }
            }

            _spriteBatch.End();

            passStats = _spriteBatch.Stats;
            drawCalls += passStats.DrawCalls;
            textureBinds += passStats.TextureBinds;
            sprites += passStats.Sprites;
            scissorChanges += passStats.ScissorChanges;

            bool TryResolveItemIconArchive(int looks, out string? archivePath, out int imageIndex)
            {
                archivePath = null;
                imageIndex = 0;

                if (looks < 0)
                    return false;

                if (looks < 10_000)
                {
                    archivePath = dnItemsPath;
                    imageIndex = looks;
                    return archivePath != null;
                }

                if (looks < 20_000)
                {
                    archivePath = dnItems2Path ?? dnItemsPath;
                    imageIndex = looks - 10_000;
                    return archivePath != null;
                }

                if (looks < 30_000)
                {
                    archivePath = shineDnItemsPath ?? dnItemsPath;
                    imageIndex = looks - 20_000;
                    return archivePath != null;
                }

                archivePath = dnItemsPath;
                imageIndex = looks % 10_000;
                return archivePath != null;
            }

            static string GetDropItemDisplayName(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return string.Empty;

                int split = name.IndexOf('\\');
                if (split >= 0)
                    name = name[..split];

                return name.Trim();
            }

            string BuildDropItemTooltip(int mapX, int mapY)
            {
                if (_world.DropItems.Count == 0)
                    return string.Empty;

                var lines = new List<string>(4);
                foreach ((int _, DropItemMarker item) in _world.DropItems)
                {
                    if (item.X != mapX || item.Y != mapY)
                        continue;

                    string name = GetDropItemDisplayName(item.Name);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    lines.Add(name);
                    if (lines.Count >= 10)
                        break;
                }

                return lines.Count == 0 ? string.Empty : string.Join('\n', lines);
            }

	            bool TryGetItemTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
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

	            void PrefetchItemImage(string archivePath, int imageIndex)
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
        }

        if (_world.Actors.Count > 0)
        {
            int halfX = unitX / 2;
            int markerW = Math.Max(6, Math.Min(18, unitX / 2));
            int markerH = Math.Max(6, Math.Min(20, unitY));

            string? humPath = TryResolveArchiveFilePath(dataDir, "Hum");
            string? hum2Path = TryResolveArchiveFilePath(dataDir, "Hum2");
            string? hum3Path = TryResolveArchiveFilePath(dataDir, "Hum3");
            string? hairPath = TryResolveArchiveFilePath(dataDir, "Hair");
            string? hair2Path = TryResolveArchiveFilePath(dataDir, "Hair2");
            string? weaponPath = TryResolveArchiveFilePath(dataDir, "Weapon");
            string? weapon2Path = TryResolveArchiveFilePath(dataDir, "Weapon2");
            string? weapon3Path = TryResolveArchiveFilePath(dataDir, "Weapon3");
            string? npcPath = TryResolveArchiveFilePath(dataDir, "Npc");
            string? npc2Path = TryResolveArchiveFilePath(dataDir, "Npc2");
            string? humEffectPath = TryResolveArchiveFilePath(dataDir, "HumEffect");
            string? humEffect2Path = TryResolveArchiveFilePath(dataDir, "HumEffect2");
            string? humEffect3Path = TryResolveArchiveFilePath(dataDir, "HumEffect3");
            string? effectImgPath = TryResolveArchiveFilePath(dataDir, "Effect");
            string? dragonPath = TryResolveArchiveFilePath(dataDir, "Dragon");
            string? magic2Path = TryResolveArchiveFilePath(dataDir, "Magic2");
            string? magic3Path = TryResolveArchiveFilePath(dataDir, "Magic3");
            string? magic4Path = TryResolveArchiveFilePath(dataDir, "Magic4");
            string? magic5Path = TryResolveArchiveFilePath(dataDir, "Magic5");
            string? magic6Path = TryResolveArchiveFilePath(dataDir, "Magic6");
            string? magic7Images2Path = TryResolveArchiveFilePath(dataDir, "Magic7");
            string? magic7Path = TryResolveArchiveFilePath(dataDir, "magic7-16") ?? magic7Images2Path;
            string? magic8Images2Path = TryResolveArchiveFilePath(dataDir, "Magic8");
            string? magic8Path = TryResolveArchiveFilePath(dataDir, "magic8-16") ?? magic8Images2Path;
            string? magic9Path = TryResolveArchiveFilePath(dataDir, "Magic9");
            string? magic10Path = TryResolveArchiveFilePath(dataDir, "Magic10");
            string? ui1Path = TryResolveArchiveFilePath(dataDir, "ui1");
            string? opUiPath = TryResolveArchiveFilePath(dataDir, "NewopUI");
            string? prguse2Path = TryResolveArchiveFilePath(dataDir, "Prguse2");
            string? cboEffectPath = TryResolveArchiveFilePath(dataDir, "cboEffect");
            string? cboHumPath = TryResolveArchiveFilePath(dataDir, "cbohum");
            string? cboHum3Path = TryResolveArchiveFilePath(dataDir, "cbohum3");
            string? cboHairPath = TryResolveArchiveFilePath(dataDir, "cbohair");
            string? cboWeaponPath = TryResolveArchiveFilePath(dataDir, "cboweapon");
            string? cboWeapon3Path = TryResolveArchiveFilePath(dataDir, "cboweapon3");
            int prefetchBudget = 12;

            CancellationToken token = _loginCts?.Token ?? CancellationToken.None;
            _userNameQuerySystem.Pump(nowMs, left, right, top, bottom, token);

	            bool TryGetActorTextureWithPivot(
	                string archivePath,
	                int imageIndex,
	                out D3D11Texture2D texture,
	                out (short Px, short Py) pivot)
	            {
	                if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
	                    archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase) ||
	                    archivePath.EndsWith(".wzl", StringComparison.OrdinalIgnoreCase))
	                {
                    pivot = default;
                    texture = null!;

                    if (_wilTextureCache == null)
                        return false;

                    var key = new WilImageKey(archivePath, imageIndex);
                    if (_wilTextureCache.TryGet(key, out texture!))
                    {
                        if (_wilImagePivots.TryGetValue(key, out pivot))
                            return true;
                    }

                    if (_wilImageCache.TryGetImage(key, out WilImage image))
                    {
                        pivot = (image.Px, image.Py);
                        _wilImagePivots[key] = pivot;
                        texture = _wilTextureCache.GetOrCreate(
                            key,
                            () => D3D11Texture2D.CreateFromBgra32(frame.Device, image.Bgra32, image.Width, image.Height));
                        return true;
                    }

                    if (prefetchBudget > 0)
                    {
                        _ = _wilImageCache.GetImageAsyncFullPath(key.WilPath, key.ImageIndex);
                        prefetchBudget--;
                    }

                    return false;
                }

                pivot = default;
                texture = null!;

                if (_dataTextureCache == null)
                    return false;

                var dataKey = new PackDataImageKey(archivePath, imageIndex);
                if (_dataTextureCache.TryGet(dataKey, out texture!))
                {
                    if (_dataImagePivots.TryGetValue(dataKey, out pivot))
                        return true;
                }

                if (_packDataImageCache.TryGetImage(dataKey, out PackDataImage dataImage))
                {
                    pivot = (dataImage.Px, dataImage.Py);
                    _dataImagePivots[dataKey] = pivot;
                    texture = _dataTextureCache.GetOrCreate(
                        dataKey,
                        () => D3D11Texture2D.CreateFromBgra32(frame.Device, dataImage.Bgra32, dataImage.Width, dataImage.Height));
                    return true;
                }

                if (prefetchBudget > 0)
                {
                    _ = _packDataImageCache.GetImageAsyncFullPath(dataKey.DataPath, dataKey.ImageIndex);
                    prefetchBudget--;
                }

                return false;
            }

            string?[] monPaths = new string?[41];
            string? _dragonPath = null;
            string? _effectPath = null;
            string? _skeletonPath = null;

            string? ResolveCachedArchivePath(ref string? cachedPath, string baseName)
            {
                if (cachedPath != null)
                    return cachedPath.Length == 0 ? null : cachedPath;

                string? resolved = TryResolveArchiveFilePath(dataDir, baseName);
                cachedPath = resolved ?? string.Empty;
                return resolved;
            }

            string? GetMonFilePath(int monFile)
            {
                if (monFile < 1 || monFile > 40)
                    return null;

                string? cached = monPaths[monFile];
                if (cached != null)
                    return cached.Length == 0 ? null : cached;

                string? resolved = TryResolveArchiveFilePath(dataDir, $"Mon{monFile}");
                monPaths[monFile] = resolved ?? string.Empty;
                return resolved;
            }

            string? GetMonArchivePath(int appearance)
            {
                if (appearance < 0)
                    return GetMonFilePath(1);

                
                if (appearance >= 1000)
                    return GetMonFilePath(1);

                int group = appearance / 10;
                if (group is >= 0 and <= 39)
                    return GetMonFilePath(group + 1);

                return group switch
                {
                    70 => (appearance % 100) is >= 0 and <= 2
                        ? ResolveCachedArchivePath(ref _skeletonPath, "Mon-kulou") ?? GetMonFilePath(28)
                        : GetMonFilePath(28),
                    80 => ResolveCachedArchivePath(ref _dragonPath, "Dragon") ?? GetMonFilePath(1),
                    81 or 82 => GetMonFilePath(36),
                    90 => appearance is 904 or 905 or 906
                        ? GetMonFilePath(34)
                        : ResolveCachedArchivePath(ref _effectPath, "Effect") ?? GetMonFilePath(1),
                    _ => GetMonFilePath(1)
                };
            }

            string? ResolveEffectArchivePath(MagicEffectArchiveRef archive)
            {
                return archive.Kind switch
                {
                    MagicEffectArchiveKind.Magic => magicPath,
                    MagicEffectArchiveKind.Magic2 => magic2Path,
                    MagicEffectArchiveKind.Magic3 => magic3Path,
                    MagicEffectArchiveKind.Magic4 => magic4Path,
                    MagicEffectArchiveKind.Magic5 => magic5Path,
                    MagicEffectArchiveKind.Magic6 => magic6Path,
                    MagicEffectArchiveKind.Magic7 => magic7Path,
                    MagicEffectArchiveKind.Magic7Images2 => magic7Images2Path,
                    MagicEffectArchiveKind.Magic8 => magic8Path,
                    MagicEffectArchiveKind.Magic8Images2 => magic8Images2Path,
                    MagicEffectArchiveKind.Magic9 => magic9Path,
                    MagicEffectArchiveKind.Magic10 => magic10Path,
                    MagicEffectArchiveKind.Ui1 => ui1Path,
                    MagicEffectArchiveKind.CboEffect => cboEffectPath,
                    MagicEffectArchiveKind.Prguse2 => prguse2Path,
                    MagicEffectArchiveKind.Dragon => dragonPath,
                    MagicEffectArchiveKind.Effect => effectImgPath,
                    MagicEffectArchiveKind.Mon => GetMonFilePath(archive.Value),
                    _ => null
                };
            }

            bool TryGetMagicEffectBase(int mag, out string archivePath, out int startIndex)
            {
                return TryGetMagicEffectBaseWithType(mag, mType: 0, out archivePath, out startIndex);
            }

            bool TryGetMagicEffectBaseWithType(int mag, int mType, out string archivePath, out int startIndex)
            {
                archivePath = string.Empty;
                startIndex = 0;

                int selfX = 0;
                int selfY = 0;
                if (_world.TryGetMyself(out ActorMarker myself))
                {
                    selfX = myself.X;
                    selfY = myself.Y;
                }
                else if (_world.MapCenterSet)
                {
                    selfX = _world.MapCenterX;
                    selfY = _world.MapCenterY;
                }

                if (!MagicEffectAtlas.TryGetEffectBase(mag, mType, selfX, selfY, out MagicEffectArchiveRef archive, out int baseIndex))
                    return false;

                string? resolved = ResolveEffectArchivePath(archive);

                if (resolved == null)
                    return false;

                archivePath = resolved;
                startIndex = baseIndex;
                return true;
            }

            int GetSpellEffectFrames(ActorMarker actor)
            {
                int effectNumber = actor.MagicEffectNumber;
                if (effectNumber <= 0)
                    return 0;

                return effectNumber switch
                {
                    26 => 20,
                    35 => 15,
                    43 => 20,
                    120 => 12,
                    122 => 8,
                    _ => 10
                };
            }

            bool TryGetSpellEffectKey(ActorMarker actor, long elapsedMs, out string archivePath, out int imageIndex)
            {
                archivePath = string.Empty;
                imageIndex = 0;

                if (actor.Action != Grobal2.SM_SPELL)
                    return false;

                int effectNumber = actor.MagicEffectNumber;
                if (effectNumber <= 0)
                    return false;

                int spellFrames = GetSpellEffectFrames(actor);
                if (spellFrames <= 0)
                    return false;

                (int _, int _, int _, int frameTimeMs, _) = MirActionTiming.GetHumanActionInfo(Grobal2.SM_SPELL);
                if (frameTimeMs <= 0)
                    frameTimeMs = 60;

                if (effectNumber == 26)
                    frameTimeMs = Math.Max(1, frameTimeMs / 2);

                long effectElapsedMs = elapsedMs;
                if (actor.MagicAnimStartMs > 0)
                    effectElapsedMs = nowMs - actor.MagicAnimStartMs;
                else if (actor.MagicWaitStartMs > 0)
                    effectElapsedMs = nowMs - actor.MagicWaitStartMs;
                if (effectElapsedMs < 0)
                    effectElapsedMs = 0;

                int effectFrame = (int)(effectElapsedMs / frameTimeMs);
                if (effectFrame is < 0 or >= 255 || effectFrame >= spellFrames)
                    return false;

                switch (effectNumber)
                {
                    case 120:
                        if (ui1Path == null) return false;
                        archivePath = ui1Path;
                        imageIndex = 1210 + effectFrame;
                        return true;
                    case 121:
                        if (magic8Images2Path == null) return false;
                        archivePath = magic8Images2Path;
                        imageIndex = 70 + effectFrame;
                        return true;
                    case 122:
                        if (magic7Images2Path == null) return false;
                        archivePath = magic7Images2Path;
                        imageIndex = 840 + effectFrame;
                        return true;
                }

                int mag = effectNumber - 1;
                int spellLv = actor.MagicSpellLevel;
                int poison = actor.MagicPoison;

                if (effectNumber is 10 or 29 or 34 or 48 && spellLv > MaxMagicLv)
                {
                    mag += 500;
                }
                else if (effectNumber is 4 or 8 or 9 or 11 or 12 or 15 or 21)
                {
                    switch (spellLv / 4)
                    {
                        case 1:
                            mag += 1001;
                            break;
                        case 2:
                            mag += 2001;
                            break;
                        case 3:
                            mag += 3001;
                            break;
                    }
                }

                if (!TryGetMagicEffectBase(mag, out archivePath, out int baseIndex))
                    return false;

                if (effectNumber == 4 && poison == 2 && (spellLv / 4) is >= 1 and <= 3)
                    baseIndex += 210;

                imageIndex = baseIndex + effectFrame;
                return true;
            }

            int ComputeHumanFrameIndex(int recogId, ActorMarker actor, long elapsedMs)
            {
                int dir = actor.Dir & 7;
                const int standFrameMs = 200;
                int standFrame = (int)((nowMs / standFrameMs) % 4);
                int standIndex = (dir * 8) + standFrame;

                if (actor.Action == Grobal2.SM_TURN)
                    return standIndex;

                if (actor.Action == Grobal2.SM_DEATH)
                {
                    (int start, int frames, int skip, _, _) = MirActionTiming.GetHumanActionInfo(actor.Action);
                    if (frames <= 0)
                        return standIndex;

                    return start + (dir * (frames + skip)) + (frames - 1);
                }

                if (actor.Action == Grobal2.SM_BACKSTEP)
                {
                    (int backstepStart, int backstepFrames, int backstepSkip, int backstepFrameTimeMs, _) = MirActionTiming.GetHumanActionInfo(actor.Action);
                    if (MirDirection.IsMoveAction(actor.Action))
                    {
                        MirActionTiming.MoveTiming moveTiming = GetActorMoveTiming(recogId, actor);
                        if (moveTiming.Frames > 0 && moveTiming.FrameTimeMs > 0)
                        {
                            backstepFrames = moveTiming.Frames;
                            backstepFrameTimeMs = moveTiming.FrameTimeMs;
                        }
                    }

                    if (elapsedMs < 0 || backstepFrameTimeMs <= 0 || backstepFrames <= 0)
                        return standIndex;

                    long backstepTotalMs = (long)backstepFrames * backstepFrameTimeMs;
                    if (backstepTotalMs <= 0 || elapsedMs >= backstepTotalMs)
                        return standIndex;

                    int frame = (int)(elapsedMs / backstepFrameTimeMs);
                    if (frame >= backstepFrames)
                        frame = backstepFrames - 1;
                    if (frame < 0)
                        frame = 0;

                    int reversedFrame = (backstepFrames - 1) - frame;
                    return backstepStart + (dir * (backstepFrames + backstepSkip)) + reversedFrame;
                }

                if (actor.Action == Grobal2.SM_SMITELONGHIT)
                {
                    (int phase1Start, int phase1Frames, int phase1Skip, int phase1FrameTimeMs, _) = MirActionTiming.GetHumanActionInfo(actor.Action);
                    if (elapsedMs < 0 || phase1FrameTimeMs <= 0 || phase1Frames <= 0)
                        return standIndex;

                    long phase1Ms = (long)phase1Frames * phase1FrameTimeMs;
                    if (phase1Ms > 0 && elapsedMs < phase1Ms)
                    {
                        int phase1Frame = (int)(elapsedMs / phase1FrameTimeMs);
                        if (phase1Frame >= phase1Frames)
                            phase1Frame = phase1Frames - 1;

                        return phase1Start + (dir * (phase1Frames + phase1Skip)) + phase1Frame;
                    }

                    
                    const int phase2Start = 320;
                    const int phase2Frames = 6;
                    const int phase2Skip = 4;
                    const int phase2FrameTimeMs = 80;

                    long phase2ElapsedMs = elapsedMs - phase1Ms;
                    if (phase2ElapsedMs < 0)
                        return standIndex;

                    long phase2Ms = (long)phase2Frames * phase2FrameTimeMs;
                    if (phase2Ms <= 0 || phase2ElapsedMs >= phase2Ms)
                        return standIndex;

                    int phase2Frame = (int)(phase2ElapsedMs / phase2FrameTimeMs);
                    if (phase2Frame >= phase2Frames)
                        phase2Frame = phase2Frames - 1;

                    return phase2Start + (dir * (phase2Frames + phase2Skip)) + phase2Frame;
                }

                (int actionStartBase, int actionFrames, int actionSkip, int actionFrameTimeMs, bool holdLast) = MirActionTiming.GetHumanActionInfo(actor.Action);
                if (MirDirection.IsMoveAction(actor.Action))
                {
                    MirActionTiming.MoveTiming moveTiming = GetActorMoveTiming(recogId, actor);
                    if (moveTiming.Frames > 0 && moveTiming.FrameTimeMs > 0)
                    {
                        actionFrames = moveTiming.Frames;
                        actionFrameTimeMs = moveTiming.FrameTimeMs;
                    }
                }

                if (elapsedMs < 0 || actionFrameTimeMs <= 0 || actionFrames <= 0)
                    return standIndex;

                if (actor.Action == Grobal2.SM_RUSH && recogId != 0)
                {
                    if (!_humanRushDirStates.TryGetValue(recogId, out var rushState))
                        rushState = (LastActionStartTimestamp: -1, RushDir: 0);

                    if (rushState.LastActionStartTimestamp != actor.ActionStartTimestamp)
                    {
                        rushState = (LastActionStartTimestamp: actor.ActionStartTimestamp, RushDir: rushState.RushDir == 0 ? (byte)1 : (byte)0);
                        _humanRushDirStates[recogId] = rushState;
                    }

                    
                    actionStartBase = rushState.RushDir == 1 ? 128 : 131;
                }

                long totalMs = (long)actionFrames * actionFrameTimeMs;
                if (totalMs <= 0)
                    return standIndex;

                if (elapsedMs >= totalMs)
                {
                    if (!holdLast || actor.Action == Grobal2.SM_TURN)
                        return standIndex;

                    return actionStartBase + (dir * (actionFrames + actionSkip)) + (actionFrames - 1);
                }

                int actionFrame = (int)(elapsedMs / actionFrameTimeMs);
                if (actionFrame >= actionFrames)
                    actionFrame = actionFrames - 1;

                return actionStartBase + (dir * (actionFrames + actionSkip)) + actionFrame;
            }

            int ComputeMonsterFrameIndex(int recogId, ActorMarker actor, long elapsedMs)
            {
                int dir = actor.Dir & 7;
                int appearance = FeatureCodec.Appearance(actor.Feature);
                int race = FeatureCodec.Race(actor.Feature);

                bool forceDir0 = race is 33 or 34 or 35;
                if (forceDir0)
                    dir = 0;

                MonsterActions.MonsterAction actionSet = MonsterActions.GetRaceByPm(race, appearance);
                int moveFrameTimeMs = 0;
                if (MirDirection.IsMoveAction(actor.Action))
                {
                    MirActionTiming.MoveTiming moveTiming = GetActorMoveTiming(recogId, actor);
                    moveFrameTimeMs = moveTiming.FrameTimeMs;
                }

                if (race == 98)
                {
                    MonsterActions.ActionInfo stand = actionSet.ActStand;
                    MonsterActions.ActionInfo die = actionSet.ActDie;

                    int standFrame = stand.Start + dir;
                    if (actor.Action == Grobal2.SM_DEATH && die.Frames > 0)
                        return die.Start + (die.Frames - 1);

                    return standFrame;
                }

                if (race == 99)
                {
                    MonsterActions.ActionInfo stand = actionSet.ActStand;
                    MonsterActions.ActionInfo attack = actionSet.ActAttack;
                    MonsterActions.ActionInfo critical = actionSet.ActCritical;
                    MonsterActions.ActionInfo die = actionSet.ActDie;

                    bool doorOpen = dir >= 3;
                    int standStride = stand.Frames + stand.Skip;
                    int closedFrame = stand.Frames > 0 ? stand.Start + (dir * standStride) : dir * 10;
                    int openFrame = critical.Start;
                    int dieLastFrame = die.Frames > 0 ? die.Start + (die.Frames - 1) : closedFrame;

                    int ComputeNoDirActionFrame(MonsterActions.ActionInfo actionInfo, int endedFrameIndex)
                    {
                        if (actionInfo.Frames <= 0)
                            return endedFrameIndex;

                        int initialFrame = actionInfo.Start;
                        if (actionInfo.FrameTimeMs <= 0 || elapsedMs < 0)
                            return initialFrame;

                        long totalMs = (long)actionInfo.Frames * actionInfo.FrameTimeMs;
                        if (totalMs <= 0)
                            return initialFrame;

                        if (elapsedMs >= totalMs)
                            return endedFrameIndex;

                        int frame = (int)(elapsedMs / actionInfo.FrameTimeMs);
                        if (frame >= actionInfo.Frames)
                            frame = actionInfo.Frames - 1;
                        if (frame < 0)
                            frame = 0;

                        return actionInfo.Start + frame;
                    }

                    int ComputeDoorDirActionFrame(MonsterActions.ActionInfo actionInfo, int actionFrameTimeMs, int endedFrameIndex)
                    {
                        if (actionInfo.Frames <= 0)
                            return endedFrameIndex;

                        int baseIndex = actionInfo.Start + (dir * (actionInfo.Frames + actionInfo.Skip));
                        if (actionFrameTimeMs <= 0 || elapsedMs < 0)
                            return baseIndex;

                        long totalMs = (long)actionInfo.Frames * actionFrameTimeMs;
                        if (totalMs <= 0)
                            return baseIndex;

                        if (elapsedMs >= totalMs)
                            return endedFrameIndex;

                        int frame = (int)(elapsedMs / actionFrameTimeMs);
                        if (frame >= actionInfo.Frames)
                            frame = actionInfo.Frames - 1;
                        if (frame < 0)
                            frame = 0;

                        return baseIndex + frame;
                    }

                    return actor.Action switch
                    {
                        Grobal2.SM_DIGUP => ComputeNoDirActionFrame(attack, openFrame),
                        Grobal2.SM_DIGDOWN => ComputeNoDirActionFrame(critical, closedFrame),
                        Grobal2.SM_STRUCK => ComputeDoorDirActionFrame(
                            actionSet.ActStruck,
                            actionFrameTimeMs: stand.FrameTimeMs,
                            endedFrameIndex: doorOpen ? openFrame : closedFrame),
                        Grobal2.SM_NOWDEATH => ComputeNoDirActionFrame(die, dieLastFrame),
                        Grobal2.SM_DEATH => dieLastFrame,
                        Grobal2.SM_TURN => doorOpen ? openFrame : closedFrame,
                        _ => doorOpen ? openFrame : closedFrame
                    };
                }

                bool ignoreAllDir = race == 43;
                bool ignoreDir = ignoreAllDir || (race is >= 117 and <= 119);
                bool isKillingHerbLike = race is 13 or 80 or 33 or 34 or 35;
                bool digNoDir = race is 13 or 80;

                int StandIndex(bool ignoreDirection = false)
                {
                    MonsterActions.ActionInfo stand = actionSet.ActStand;
                    if (stand.Frames <= 0)
                        return ignoreDirection ? 0 : dir * 10;

                    int baseIndex = ignoreDirection ? stand.Start : stand.Start + (dir * (stand.Frames + stand.Skip));
                    if (stand.FrameTimeMs <= 0 || elapsedMs < 0)
                        return baseIndex;

                    long totalMs = (long)stand.Frames * stand.FrameTimeMs;
                    if (totalMs <= 0)
                        return baseIndex;

                    int frame = (int)((elapsedMs % totalMs) / stand.FrameTimeMs);
                    if (frame >= stand.Frames)
                        frame = stand.Frames - 1;

                    return baseIndex + frame;
                }

                int ComputeActionFrame(MonsterActions.ActionInfo actionInfo, bool holdLast, bool ignoreDirection = false, bool reverse = false, int frameTimeOverrideMs = 0)
                {
                    if (actionInfo.Frames <= 0)
                        return StandIndex(ignoreDirection);

                    int baseIndex = ignoreDirection ? actionInfo.Start : actionInfo.Start + (dir * (actionInfo.Frames + actionInfo.Skip));
                    int initialFrame = reverse ? actionInfo.Frames - 1 : 0;
                    int frameTimeMs = frameTimeOverrideMs > 0 ? frameTimeOverrideMs : actionInfo.FrameTimeMs;
                    if (frameTimeMs <= 0 || elapsedMs < 0)
                        return baseIndex + initialFrame;

                    long totalMs = (long)actionInfo.Frames * frameTimeMs;
                    if (totalMs <= 0)
                        return baseIndex + initialFrame;

                    if (elapsedMs >= totalMs)
                    {
                        if (!holdLast)
                            return StandIndex(ignoreDirection);

                        return baseIndex + (reverse ? 0 : (actionInfo.Frames - 1));
                    }

                    int frame = (int)(elapsedMs / frameTimeMs);
                    if (frame >= actionInfo.Frames)
                        frame = actionInfo.Frames - 1;
                    if (frame < 0)
                        frame = 0;

                    if (reverse)
                        frame = (actionInfo.Frames - 1) - frame;

                    return baseIndex + frame;
                }

                switch (actor.Action)
                {
                    case Grobal2.SM_TURN:
                        return StandIndex(ignoreDir || isKillingHerbLike);
                    case Grobal2.SM_WALK:
                    case Grobal2.SM_RUN:
                    case Grobal2.SM_HORSERUN:
                    case Grobal2.SM_BACKSTEP:
                    case Grobal2.SM_RUSH:
                    case Grobal2.SM_RUSHEX:
                    case Grobal2.SM_RUSHKUNG:
                        return ComputeActionFrame(actionSet.ActWalk, holdLast: false, ignoreDirection: ignoreDir, frameTimeOverrideMs: moveFrameTimeMs);
                    case Grobal2.SM_FLYAXE:
                    case Grobal2.SM_THROW:
                    case Grobal2.SM_HIT:
                        if (race == 33)
                            return ComputeActionFrame(actionSet.ActCritical, holdLast: false);
                        return ComputeActionFrame(actionSet.ActAttack, holdLast: false, ignoreDirection: ignoreAllDir);
                    case Grobal2.SM_DIGUP:
                    {
                        if (isKillingHerbLike)
                            return ComputeActionFrame(actionSet.ActWalk, holdLast: false, ignoreDirection: digNoDir);

                        if (appearance is 351 or 827)
                            return ComputeActionFrame(actionSet.ActDeath, holdLast: false);

                        if (race == 23 || race is >= 91 and <= 93)
                            return ComputeActionFrame(actionSet.ActDeath, holdLast: false, ignoreDirection: true);

                        return ComputeActionFrame(actionSet.ActCritical, holdLast: false, ignoreDirection: ignoreAllDir);
                    }
                    case Grobal2.SM_DIGDOWN:
                    {
                        if (isKillingHerbLike)
                            return ComputeActionFrame(actionSet.ActDeath, holdLast: false, ignoreDirection: digNoDir);

                        if (race == 55)
                            return ComputeActionFrame(actionSet.ActCritical, holdLast: false, reverse: true);

                        if (race == 23 || race is >= 91 and <= 93)
                            return ComputeActionFrame(actionSet.ActDeath, holdLast: false, ignoreDirection: true);

                        return ComputeActionFrame(actionSet.ActDeath, holdLast: false, ignoreDirection: ignoreDir);
                    }
                    case Grobal2.SM_LIGHTING:
                    case Grobal2.SM_LIGHTING_1:
                    case Grobal2.SM_LIGHTING_2:
                    case Grobal2.SM_LIGHTING_3:
                        return ComputeActionFrame(actionSet.ActCritical, holdLast: false, ignoreDirection: ignoreAllDir);
                    case Grobal2.SM_SPELL:
                        return actor.MagicSerial == 23
                            ? ComputeActionFrame(actionSet.ActCritical, holdLast: false, ignoreDirection: ignoreAllDir)
                            : ComputeActionFrame(actionSet.ActAttack, holdLast: false, ignoreDirection: ignoreAllDir);
                    case Grobal2.SM_STRUCK:
                        if (race == Grobal2.RCC_GUARD)
                            return StandIndex();
                        return ComputeActionFrame(actionSet.ActStruck, holdLast: false, ignoreDirection: ignoreDir);
                    case Grobal2.SM_DEATH:
                    {
                        MonsterActions.ActionInfo die = actionSet.ActDie;
                        if (die.Frames <= 0)
                            return StandIndex();
                        int dieBase = ignoreDir ? die.Start : die.Start + (dir * (die.Frames + die.Skip));
                        return dieBase + (die.Frames - 1);
                    }
                    case Grobal2.SM_NOWDEATH:
                        return ComputeActionFrame(actionSet.ActDie, holdLast: true, ignoreDirection: ignoreDir);
                    case Grobal2.SM_SKELETON:
                    {
                        MonsterActions.ActionInfo death = actionSet.ActDeath;
                        if (death.Frames <= 0)
                            return StandIndex();

                        int baseIndex = ignoreDir ? death.Start : death.Start + dir;
                        if (death.FrameTimeMs <= 0 || elapsedMs < 0)
                            return baseIndex;

                        long totalMs = (long)death.Frames * death.FrameTimeMs;
                        if (totalMs <= 0)
                            return baseIndex;

                        if (elapsedMs >= totalMs)
                            return baseIndex + (death.Frames - 1);

                        int frame = (int)(elapsedMs / death.FrameTimeMs);
                        if (frame >= death.Frames)
                            frame = death.Frames - 1;
                        if (frame < 0)
                            frame = 0;

                        return baseIndex + frame;
                    }
                    case Grobal2.SM_ALIVE:
                        return ComputeActionFrame(actionSet.ActDeath, holdLast: false, ignoreDirection: race == 117);
                    default:
                        return StandIndex(ignoreDir || isKillingHerbLike);
                }
            }

            bool TryGetMonsterKey(int recogId, ActorMarker actor, long elapsedMs, out string archivePath, out int imageIndex)
            {
                archivePath = string.Empty;
                imageIndex = 0;

                int race = FeatureCodec.Race(actor.Feature);
                if (race == Grobal2.RCC_MERCHANT)
                    return false;

                int appearance = FeatureCodec.Appearance(actor.Feature);
                string? monPath = GetMonArchivePath(appearance);
                if (monPath == null)
                    return false;

                int offset = AppearanceOffsets.GetMonsterOffset(appearance);
                int frame = ComputeMonsterFrameIndex(recogId, actor, elapsedMs);
                archivePath = monPath;
                imageIndex = offset + frame;
                return true;
            }

            bool TryGetGhostShipMonsterShadowOffset(int appearance, out int shadowOffset)
            {
                shadowOffset = 0;

                if (appearance == 812)
                {
                    shadowOffset = 320;
                    return true;
                }

                if (appearance is 825 or 827)
                {
                    shadowOffset = 560;
                    return true;
                }

                if (appearance is 351 or 354 or 356 or 359 or 813 or 815 or 818 or 819 or 820 or 821 or 822)
                {
                    shadowOffset = 480;
                    return true;
                }

                return false;
            }

            bool TryGetGhostShipHitEffectKey(ActorMarker actor, long elapsedMs, out string archivePath, out int imageIndex)
            {
                archivePath = string.Empty;
                imageIndex = 0;

                if (actor.Action != Grobal2.SM_HIT)
                    return false;

                int appearance = FeatureCodec.Appearance(actor.Feature);
                int mag = appearance switch
                {
                    354 => 103,
                    815 => 303,
                    _ => 0
                };

                if (mag == 0)
                    return false;

                int race = FeatureCodec.Race(actor.Feature);
                MonsterActions.MonsterAction actionSet = MonsterActions.GetRaceByPm(race, appearance);

                MonsterActions.ActionInfo attack = actionSet.ActAttack;
                if (attack.Frames <= 0 || attack.FrameTimeMs <= 0)
                    return false;

                long totalMs = (long)attack.Frames * attack.FrameTimeMs;
                if (elapsedMs < 0 || elapsedMs >= totalMs)
                    return false;

                int effectFrame = (int)(elapsedMs / attack.FrameTimeMs);
                if (effectFrame >= attack.Frames)
                    effectFrame = attack.Frames - 1;
                if (effectFrame < 0)
                    effectFrame = 0;

                if (!TryGetMagicEffectBaseWithType(mag, mType: 1, out archivePath, out int baseIndex))
                    return false;

                int dir = actor.Dir & 7;
                imageIndex = baseIndex + (dir * 10) + effectFrame;
                return true;
            }

            int ComputeNpcFrameIndex(ActorMarker actor, long elapsedMs)
            {
                int appearance = FeatureCodec.Appearance(actor.Feature);
                MonsterActions.MonsterAction actionSet = MonsterActions.GetRaceByPm(Grobal2.RCC_MERCHANT, appearance);

                int dir3 = actor.Dir % 3;
                bool ignoreDirStand =
                    appearance is (>= 54 and <= 59) or (>= 70 and <= 75) or (>= 81 and <= 85) or (>= 90 and <= 92) or
                        (>= 94 and <= 98) or (>= 112 and <= 123) or (>= 130 and <= 132);

                int StandIndex()
                {
                    MonsterActions.ActionInfo stand = actionSet.ActStand;
                    if (stand.Frames <= 0)
                        return 0;

                    int baseIndex = ignoreDirStand ? stand.Start : stand.Start + (dir3 * (stand.Frames + stand.Skip));
                    if (stand.FrameTimeMs <= 0 || elapsedMs < 0)
                        return baseIndex;

                    long totalMs = (long)stand.Frames * stand.FrameTimeMs;
                    if (totalMs <= 0)
                        return baseIndex;

                    long t = Math.Max(0, elapsedMs);
                    int frame = (int)((t % totalMs) / stand.FrameTimeMs);
                    if (frame >= stand.Frames)
                        frame = stand.Frames - 1;

                    return baseIndex + frame;
                }

                int ComputeActionFrame(MonsterActions.ActionInfo actionInfo, bool holdLast, bool ignoreDirAction)
                {
                    if (actionInfo.Frames <= 0)
                        return StandIndex();

                    int baseIndex = ignoreDirAction ? actionInfo.Start : actionInfo.Start + (dir3 * (actionInfo.Frames + actionInfo.Skip));
                    if (actionInfo.FrameTimeMs <= 0 || elapsedMs < 0)
                        return baseIndex;

                    long totalMs = (long)actionInfo.Frames * actionInfo.FrameTimeMs;
                    if (totalMs <= 0)
                        return baseIndex;

                    long t = Math.Max(0, elapsedMs);
                    if (t >= totalMs)
                    {
                        if (!holdLast)
                            return StandIndex();

                        return baseIndex + (actionInfo.Frames - 1);
                    }

                    int frame = (int)(t / actionInfo.FrameTimeMs);
                    if (frame >= actionInfo.Frames)
                        frame = actionInfo.Frames - 1;

                    return baseIndex + frame;
                }

                switch (actor.Action)
                {
                    case Grobal2.SM_TURN:
                        return StandIndex();
                    case Grobal2.SM_WALK:
                    case Grobal2.SM_RUN:
                    case Grobal2.SM_HORSERUN:
                    case Grobal2.SM_BACKSTEP:
                    case Grobal2.SM_RUSH:
                    case Grobal2.SM_RUSHEX:
                    case Grobal2.SM_RUSHKUNG:
                        return ComputeActionFrame(actionSet.ActWalk, holdLast: false, ignoreDirAction: ignoreDirStand);
                    case Grobal2.SM_HIT:
                    {
                        bool useStand =
                            appearance is 33 or 34 or 52 ||
                                appearance is (>= 54 and <= 58) or (>= 104 and <= 106) or 110 or (>= 112 and <= 117) or 121 or 132 or 133;

                        MonsterActions.ActionInfo info = useStand ? actionSet.ActStand : actionSet.ActAttack;
                        bool ignoreDirHit = useStand ? ignoreDirStand : ignoreDirStand || appearance == 111;
                        return ComputeActionFrame(info, holdLast: false, ignoreDirAction: ignoreDirHit);
                    }
                    case Grobal2.SM_DIGUP:
                    {
                        if (appearance == 52)
                            return ComputeActionFrame(actionSet.ActStand, holdLast: false, ignoreDirAction: ignoreDirStand);
                        return ComputeActionFrame(actionSet.ActCritical, holdLast: false, ignoreDirAction: ignoreDirStand);
                    }
                    case Grobal2.SM_STRUCK:
                        return ComputeActionFrame(actionSet.ActStruck, holdLast: false, ignoreDirAction: ignoreDirStand);
                    case Grobal2.SM_DEATH:
                    {
                        MonsterActions.ActionInfo die = actionSet.ActDie;
                        if (die.Frames <= 0)
                            return StandIndex();

                        int dieBase = ignoreDirStand ? die.Start : die.Start + (dir3 * (die.Frames + die.Skip));
                        return dieBase + (die.Frames - 1);
                    }
                    case Grobal2.SM_NOWDEATH:
                        return ComputeActionFrame(actionSet.ActDie, holdLast: true, ignoreDirAction: ignoreDirStand);
                    case Grobal2.SM_SKELETON:
                        return ComputeActionFrame(actionSet.ActDeath, holdLast: true, ignoreDirAction: ignoreDirStand);
                    default:
                        return StandIndex();
                }
            }

            bool TryGetNpcBodyKey(ActorMarker actor, long elapsedMs, out string archivePath, out int imageIndex)
            {
                archivePath = string.Empty;
                imageIndex = 0;

                int appearance = FeatureCodec.Appearance(actor.Feature);
                if (appearance is (>= 42 and <= 47) or (>= 54 and <= 58))
                    return false;

                string? resolvedNpcPath = appearance is (>= 100 and < 130) ? npc2Path : npcPath;
                if (resolvedNpcPath == null)
                    return false;

                int offset = AppearanceOffsets.GetNpcOffset(appearance);
                if (appearance is (>= 112 and <= 123) or (130 or 131))
                {
                    archivePath = resolvedNpcPath;
                    imageIndex = offset;
                    return true;
                }

                int frame = ComputeNpcFrameIndex(actor, elapsedMs);
                archivePath = resolvedNpcPath;
                imageIndex = offset + frame;
                return true;
            }

            bool TryGetNpcEffectKey(ActorMarker actor, long elapsedMs, out string archivePath, out int imageIndex, out (short Dx, short Dy) extraOffset)
            {
                archivePath = string.Empty;
                imageIndex = 0;
                extraOffset = default;

                int appearance = FeatureCodec.Appearance(actor.Feature);

                string? resolvedNpcPath = appearance is (>= 100 and < 130) ? npc2Path : npcPath;
                if (resolvedNpcPath == null)
                    return false;

                int dir3 = actor.Dir % 3;
                int offset = AppearanceOffsets.GetNpcOffset(appearance);

                int effectStart = 0;
                int effectFrames = 0;
                int effectFrameTimeMs = 0;
                bool effectUsesDir = false;
                bool effectUsesNpcOffset = true;
                int effectBaseIndex = 0;

                if (actor.Action == Grobal2.SM_DIGUP)
                {
                    if (appearance == 52)
                    {
                        effectStart = 60;
                        effectFrames = 12;
                        effectFrameTimeMs = 100;
                    }
                    else if (appearance == 85)
                    {
                        MonsterActions.MonsterAction actionSet = MonsterActions.GetRaceByPm(Grobal2.RCC_MERCHANT, appearance);
                        effectStart = 127;
                        effectFrames = 35;
                        effectFrameTimeMs = actionSet.ActCritical.FrameTimeMs;
                    }
                }
                else if (actor.Action == Grobal2.SM_HIT)
                {
                    if (appearance == 84)
                    {
                        MonsterActions.MonsterAction actionSet = MonsterActions.GetRaceByPm(Grobal2.RCC_MERCHANT, appearance);
                        effectStart = 14;
                        effectFrames = 8;
                        effectFrameTimeMs = actionSet.ActAttack.FrameTimeMs;
                    }
                    else if (appearance == 51)
                    {
                        effectStart = 60;
                        effectFrames = 8;
                        effectFrameTimeMs = 200;
                    }
                }

                if (effectFrames <= 0)
                {
                    switch (appearance)
                    {
                        case 33:
                        case 34:
                            effectStart = 0;
                            effectFrames = 10;
                            effectFrameTimeMs = 300;
                            break;
                        default:
                            if (appearance is (>= 54 and <= 58) or (>= 94 and <= 98))
                            {
                                effectStart = 0;
                                effectFrames = 9;
                                effectFrameTimeMs = 150;
                            }
                            else if (appearance is (>= 42 and <= 47))
                            {
                                effectStart = 0;
                                effectFrames = 20;
                                effectFrameTimeMs = 100;
                            }
                            else if (appearance is (>= 118 and <= 120))
                            {
                                effectStart = 10;
                                effectFrames = 16;
                                effectFrameTimeMs = 200;
                            }
                            else if (appearance is (122 or 123))
                            {
                                effectStart = 20;
                                effectFrames = 9;
                                effectFrameTimeMs = 200;
                            }
                            else if (appearance == 131)
                            {
                                effectStart = 10;
                                effectFrames = 12;
                                effectFrameTimeMs = 100;
                            }
                            else if (appearance == 132)
                            {
                                effectStart = 20;
                                effectFrames = 20;
                                effectFrameTimeMs = 100;
                            }
                            else if (appearance == 51)
                            {
                                effectStart = 60;
                                effectFrames = 8;
                                effectFrameTimeMs = 150;
                            }
                            else if (appearance is >= 60 and <= 67)
                            {
                                effectStart = 0;
                                effectFrames = 4;
                                effectFrameTimeMs = 500;
                                effectUsesDir = true;
                                effectUsesNpcOffset = false;
                                effectBaseIndex = 3540;
                            }
                            else if (appearance == 68)
                            {
                                effectStart = 60;
                                effectFrames = 4;
                                effectFrameTimeMs = 500;
                                effectUsesDir = true;
                            }
                            else if (appearance is (>= 70 and <= 75) or (90 or 91))
                            {
                                effectStart = 4;
                                effectFrames = 4;
                                effectFrameTimeMs = 500;
                            }
                            break;
                    }
                }

                if (effectFrames <= 0 || effectFrameTimeMs <= 0)
                    return false;

                long t = Math.Max(0, elapsedMs);
                int localFrame = (int)((t / effectFrameTimeMs) % effectFrames);
                int effectFrame = effectStart + localFrame;

                int idx = effectUsesNpcOffset ? offset + effectFrame : effectBaseIndex + effectFrame;
                if (effectUsesDir)
                    idx += dir3 * 10;

                extraOffset = appearance switch
                {
                    42 => (71, 5),
                    43 => (71, 37),
                    44 => (7, 12),
                    45 => (6, 12),
                    46 => (7, 12),
                    47 => (8, -12),
                    _ => default
                };

                archivePath = resolvedNpcPath;
                imageIndex = idx;
                return true;
            }

            bool TryGetHumanBodyKey(int feature, int frame, bool useCboLib, out string archivePath, out int imageIndex)
            {
                archivePath = string.Empty;
                imageIndex = 0;

                if (FeatureCodec.Race(feature) != 0)
                    return false;

                int dress = FeatureCodec.Dress(feature);
                int sex = dress & 1;
                if (dress is >= 24 and <= 27)
                    dress = 18 + sex;

                if (useCboLib)
                {
                    if (cboHumPath == null)
                        return false;

                    int cboFileIdx = (dress - sex) >> 1;
                    if (cboFileIdx >= 75)
                        return false;

                    if (cboFileIdx is >= 25 and < 50)
                    {
                        int cboDress = dress - 26;
                        if (cboDress < 0)
                            return false;

                        archivePath = cboHumPath;
                        imageIndex = (cboDress * CboFrame) + frame;
                        return true;
                    }

                    if (cboFileIdx >= 50)
                    {
                        if (cboHum3Path == null)
                            return false;

                        int cboDress = dress - 100;
                        if (cboDress < 0)
                            return false;

                        archivePath = cboHum3Path;
                        imageIndex = (cboDress * CboFrame) + frame;
                        return true;
                    }

                    archivePath = cboHumPath;
                    imageIndex = (dress * CboFrame) + frame;
                    return true;
                }

                if ((uint)frame >= HumanFrame)
                    return false;

                int fileIdx = (dress - sex) >> 1;

                if (fileIdx < 75)
                {
                    if (fileIdx is >= 25 and < 50)
                    {
                        if (hum2Path == null)
                            return false;

                        int hum2Dress = dress - 50;
                        if (hum2Dress < 0)
                            return false;

                        archivePath = hum2Path;
                        imageIndex = (hum2Dress * HumanFrame) + frame;
                        return true;
                    }

                    if (fileIdx >= 50)
                    {
                        if (hum3Path == null)
                            return false;

                        int hum3Dress = dress - 100;
                        if (hum3Dress < 0)
                            return false;

                        archivePath = hum3Path;
                        imageIndex = (hum3Dress * HumanFrame) + frame;
                        return true;
                    }

                    if (humPath == null)
                        return false;

                    archivePath = humPath;
                    imageIndex = (dress * HumanFrame) + frame;
                    return true;
                }

                string? extraHumPath = TryResolveArchiveFilePath(dataDir, $"Hum{fileIdx}");
                if (extraHumPath == null)
                    return false;

                archivePath = extraHumPath;
                imageIndex = (sex * HumanFrame) + frame;
                return true;
            }

            bool TryGetHumanHairKey(int feature, int frame, bool useCboLib, out string archivePath, out int imageIndex)
            {
                archivePath = string.Empty;
                imageIndex = 0;

                if (FeatureCodec.Race(feature) != 0)
                    return false;

                int hair = FeatureCodec.Hair(feature);
                int dress = FeatureCodec.Dress(feature);
                int sex = dress & 1;

                int hairEx = 0;
                if (hair >= 10)
                {
                    hairEx = hair / 10;
                    hair %= 10;
                }

                if (useCboLib)
                {
                    if (cboHairPath == null)
                        return false;

                    int cboOffset;
                    if (hairEx == 0)
                    {
                        int nHairEx = ((hair - sex) >> 1) + 1;
                        cboOffset = CboFrame * (((nHairEx - 1) * 2) + sex);
                    }
                    else
                    {
                        int nHairEx = hairEx + sex + (hairEx % 4);
                        cboOffset = CboFrame * nHairEx;
                    }

                    if (cboOffset < 0)
                        return false;

                    archivePath = cboHairPath;
                    imageIndex = cboOffset + frame;
                    return true;
                }

                if ((uint)frame >= HumanFrame)
                    return false;

                if (hairEx > 0 && hair2Path != null)
                {
                    archivePath = hair2Path;
                    imageIndex = (HumanFrame * (((hairEx - 1) * 2) + sex)) + frame;
                    return true;
                }

                if (hairPath == null)
                    return false;

                int hairIndex = (hair * 2) + sex;
                if (hairIndex <= 1)
                    return false;

                archivePath = hairPath;
                imageIndex = (hairIndex * HumanFrame) + frame;
                return true;
            }

            bool TryGetHumanWeaponKey(int feature, int frame, bool useCboLib, out string archivePath, out int imageIndex)
            {
                archivePath = string.Empty;
                imageIndex = 0;

                if (FeatureCodec.Race(feature) != 0)
                    return false;

                int weapon = FeatureCodec.Weapon(feature);
                if (weapon < 2)
                    return false;

                int dress = FeatureCodec.Dress(feature);
                int sex = dress & 1;
                int fileIdx = (weapon - sex) / 2;

                if (useCboLib)
                {
                    if (fileIdx >= 100 || cboWeaponPath == null)
                        return false;

                    if (fileIdx is >= 50 and < 75)
                    {
                        int cboWeapon = weapon - 24;
                        if (cboWeapon < 0)
                            return false;

                        archivePath = cboWeaponPath;
                        imageIndex = (cboWeapon * CboFrame) + frame;
                        return true;
                    }

                    if (fileIdx >= 75)
                    {
                        if (cboWeapon3Path == null)
                            return false;

                        int cboWeapon = weapon - 150;
                        if (cboWeapon < 0)
                            return false;

                        archivePath = cboWeapon3Path;
                        imageIndex = (cboWeapon * CboFrame) + frame;
                        return true;
                    }

                    archivePath = cboWeaponPath;
                    imageIndex = (weapon * CboFrame) + frame;
                    return true;
                }

                if ((uint)frame >= HumanFrame)
                    return false;

                if (fileIdx < 100)
                {
                    if (fileIdx is >= 50 and < 75)
                    {
                        if (weapon2Path == null)
                            return false;

                        int idx = weapon - 100;
                        if (idx < 0)
                            return false;

                        archivePath = weapon2Path;
                        imageIndex = (idx * HumanFrame) + frame;
                        return true;
                    }

                    if (fileIdx >= 75)
                    {
                        if (weapon3Path == null)
                            return false;

                        int idx = weapon - 150;
                        if (idx < 0)
                            return false;

                        archivePath = weapon3Path;
                        imageIndex = (idx * HumanFrame) + frame;
                        return true;
                    }

                    if (weaponPath == null)
                        return false;

                    archivePath = weaponPath;
                    imageIndex = (weapon * HumanFrame) + frame;
                    return true;
                }

                string? extraWeaponPath = TryResolveArchiveFilePath(dataDir, $"Weapon{fileIdx}");
                if (extraWeaponPath == null)
                    return false;

                archivePath = extraWeaponPath;
                imageIndex = (sex * HumanFrame) + frame;
                return true;
            }

            bool TryGetHumanEffectKey(ActorMarker actor, int frame, out string archivePath, out int imageIndex, out int effectId)
            {
                archivePath = string.Empty;
                imageIndex = 0;
                effectId = FeatureExCodec.Effect(actor.FeatureEx);
                if (effectId <= 0)
                    return false;

                if (effectId == 50)
                {
                    if (effectImgPath == null)
                        return false;

                    if (frame > 536)
                        return false;

                    int anim = (int)((nowMs / 100) % 20);
                    archivePath = effectImgPath;
                    imageIndex = 352 + anim;
                    return true;
                }

                int baseOffset = (effectId - 1) * HumanFrame;
                int idx;
                if (frame < 64)
                {
                    int anim = (int)((nowMs / 200) % 8);
                    int dir = actor.Dir & 7;
                    idx = (dir * 8) + anim;
                }
                else
                {
                    idx = frame;
                }

                if (effectId >= 35)
                {
                    if (humEffect3Path == null)
                        return false;

                    int start = effectId switch
                    {
                        41 => 7200,
                        42 => 7800,
                        43 => 12000,
                        44 => 12600,
                        45 => 13200,
                        46 => 13800,
                        _ => baseOffset - 20400
                    };

                    int image = start + idx;
                    if (image < 0)
                        return false;

                    archivePath = humEffect3Path;
                    imageIndex = image;
                    return true;
                }

                if (effectId >= 20)
                {
                    if (humEffect2Path == null)
                        return false;

                    int image = (baseOffset - 12000) + idx;
                    if (image < 0)
                        return false;

                    archivePath = humEffect2Path;
                    imageIndex = image;
                    return true;
                }

                if (humEffectPath == null)
                    return false;

                archivePath = humEffectPath;
                imageIndex = baseOffset + idx;
                return true;
            }

            void DrawHumanStateEffects(ActorMarker actor, long elapsedMs, int baseX, int baseY)
            {
                int status = actor.Status;
                if (status == 0)
                    return;

                if (actor.Action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH or Grobal2.SM_SKELETON)
                    return;

                if ((status & 0x0010_0000) != 0 && magicPath != null)
                {
                    const int MagBubbleBase = 3890;
                    const int MagBubbleStruckBase = 3900;

                    int idx = MagBubbleBase + (int)((nowMs / 200) % 3);
                    if (actor.Action == Grobal2.SM_STRUCK)
                    {
                        int struckFrame = (int)(Math.Max(0, elapsedMs) / 200);
                        if (struckFrame < 3)
                            idx = MagBubbleStruckBase + struckFrame;
                    }

                    if (TryGetActorTextureWithPivot(magicPath, idx, out D3D11Texture2D tex, out (short Px, short Py) pivot))
                    {
                        _spriteBatch.Draw(
                            tex,
                            new DrawingRectangle(baseX + pivot.Px, baseY + pivot.Py, tex.Width, tex.Height));
                    }
                }

                if ((status & 0x0008_0000) != 0 && magic6Path != null)
                {
                    int idx = 730 + (int)((nowMs / 200) % 2);
                    if (actor.Action == Grobal2.SM_STRUCK)
                    {
                        int struckFrame = (int)(Math.Max(0, elapsedMs) / 200);
                        if (struckFrame < 6)
                            idx = 720;
                    }

                    if (TryGetActorTextureWithPivot(magic6Path, idx, out D3D11Texture2D tex, out (short Px, short Py) pivot))
                    {
                        _spriteBatch.Draw(
                            tex,
                            new DrawingRectangle(baseX + pivot.Px, baseY + pivot.Py, tex.Width, tex.Height));
                    }
                }

                if ((status & 0x0004_0000) != 0 && magic8Path != null)
                {
                    int idx = 2040 + (int)((nowMs / 120) % 8);
                    if (TryGetActorTextureWithPivot(magic8Path, idx, out D3D11Texture2D tex, out (short Px, short Py) pivot))
                    {
                        _spriteBatch.Draw(
                            tex,
                            new DrawingRectangle(baseX + pivot.Px, baseY + pivot.Py, tex.Width, tex.Height));
                    }
                }

                if ((status & 0x0002_0000) != 0 && magic10Path != null)
                {
                    int idx = 160 + (int)((nowMs / 80) % 26);
                    if (TryGetActorTextureWithPivot(magic10Path, idx, out D3D11Texture2D tex, out (short Px, short Py) pivot))
                    {
                        _spriteBatch.Draw(
                            tex,
                            new DrawingRectangle(baseX + pivot.Px, baseY + pivot.Py, tex.Width, tex.Height));
                    }
                }
            }

            void DrawActorHealthBar(int recogId, ActorMarker actor, int sayX, int sayY, bool showHpNumberConfig, bool showRedHpLabelConfig)
            {
                if (_whiteTexture == null)
                    return;

                const int BarWidth = 31;
                const int BarHeight = 2;

                bool isMerchant = FeatureCodec.Race(actor.Feature) == Grobal2.RCC_MERCHANT;
                if (!isMerchant && IsActorDead(actor))
                    return;

                int x = sayX - (BarWidth / 2);
                int y = sayY - 9;

                if (isMerchant)
                {
                    _spriteBatch.Draw(
                        _whiteTexture,
                        new DrawingRectangle(x - 1, y - 1, BarWidth + 2, BarHeight + 2),
                        color: new Color4(0f, 0f, 0f, 0.6f));
                    _spriteBatch.Draw(
                        _whiteTexture,
                        new DrawingRectangle(x, y, BarWidth - 1, BarHeight),
                        color: new Color4(0f, 0f, 1f, 0.85f));
                    return;
                }

                int maxHp = actor.MaxHp;
                if ((showHpNumberConfig || actor.OpenHealth) && maxHp > 1)
                {
                    int safeHp = Math.Min(maxHp, Math.Max(0, actor.Hp));
                    string label = $"{safeHp}/{maxHp}";
                    QueueNameLine(label, sayX, sayY - 22, new Color4(1f, 1f, 1f, 1f));
                }

                if (!actor.OpenHealth && !actor.InstanceOpenHealth && !showRedHpLabelConfig)
                    return;

                int right = BarWidth;
                if (maxHp > 0)
                    right = Math.Min(BarWidth, (int)Math.Round(BarWidth / (float)maxHp * actor.Hp));

                int fillWidth = right - 1;

                _spriteBatch.Draw(
                    _whiteTexture,
                    new DrawingRectangle(x - 1, y - 1, BarWidth + 2, BarHeight + 2),
                    color: new Color4(0f, 0f, 0f, 0.6f));

                if (fillWidth <= 0)
                    return;

                bool friendly = actor.IsMyself || (_world.HeroActorIdSet && recogId == _world.HeroActorId);
                Color4 fillColor = friendly
                    ? new Color4(0.2f, 0.95f, 0.25f, 0.9f)
                    : new Color4(0.95f, 0.25f, 0.2f, 0.85f);

                _spriteBatch.Draw(
                    _whiteTexture,
                    new DrawingRectangle(x, y, fillWidth, BarHeight),
                    color: fillColor);
            }

            List<MagicEffDrawInfo> magicEffDraws = _magicEffDraws;
            magicEffDraws.Clear();
            IReadOnlyList<MagicEffInstance> magicEffs = _world.MagicEffs;

            int magicEffSelfX = 0;
            int magicEffSelfY = 0;
            if (_world.TryGetMyself(out ActorMarker magicEffSelf))
            {
                magicEffSelfX = magicEffSelf.X;
                magicEffSelfY = magicEffSelf.Y;
            }
            else if (_world.MapCenterSet)
            {
                magicEffSelfX = _world.MapCenterX;
                magicEffSelfY = _world.MapCenterY;
            }

            for (int i = 0; i < magicEffs.Count; i++)
            {
                MagicEffInstance effect = magicEffs[i];

                int effectNumber = effect.EffectNumber;
                if (effectNumber <= 0)
                    continue;

                long elapsed = nowMs - effect.StartMs;
                if (elapsed < 0)
                    elapsed = 0;

                MagicType magicType = (MagicType)effect.EffectType;
                MagicEffTimelineInfo timeline = MagicEffTimeline.Get(effect.EffectType);
                int travelDurationMs = effect.TravelDurationMs;
                if (travelDurationMs < 0)
                    travelDurationMs = 0;

                float rx;
                float ry;
                float toX = effect.ToX;
                float toY = effect.ToY;
                int idx;
                string? archivePath;
                int pixelOffsetX = 0;
                int pixelOffsetY = 0;

                if (effect.TargetActorId != 0 && _world.TryGetActor(effect.TargetActorId, out ActorMarker target))
                {
                    long targetElapsedMs = (nowTimestamp - target.ActionStartTimestamp) * 1000 / Stopwatch.Frequency;
                    float targetX = target.X;
                    float targetY = target.Y;

                if (MirDirection.IsMoveAction(target.Action))
                {
                    (int moveFrames, int moveFrameTimeMs) = GetActorMoveTiming(effect.TargetActorId, target);
                    long totalMs = (long)moveFrames * moveFrameTimeMs;
                    if (totalMs > 0 && targetElapsedMs >= 0 && targetElapsedMs < totalMs)
                    {
                            float t = Math.Clamp(targetElapsedMs / (float)totalMs, 0f, 1f);
                            targetX = target.FromX + ((target.X - target.FromX) * t);
                            targetY = target.FromY + ((target.Y - target.FromY) * t);
                        }
                    }

                    toX = targetX;
                    toY = targetY;
                }

                bool drawFlight = timeline.HasFlight && travelDurationMs > 0 && elapsed < travelDurationMs;
                if (drawFlight)
                {
                    int flyFrames = timeline.FlightFrames;
                    int frameTimeMs = timeline.FrameTimeMs;

                    bool isBujaukGroundEffect = magicType == MagicType.BujaukGroundEffect && effectNumber is 11 or 12 or 46 or 74;
                    if (isBujaukGroundEffect)
                    {
                        
                        flyFrames = 3;
                        frameTimeMs = effectNumber == 74 ? 80 : 50;
                    }

                    bool isTeleportBujauk = magicType == MagicType.ExploBujauk && effectNumber == 75;
                    if (isTeleportBujauk)
                    {
                        
                        flyFrames = 10;
                        frameTimeMs = 80;
                    }

                    
                    if (effectNumber is 63 or 100 or 101 or 121 or 122)
                        frameTimeMs = 100;

                    
                    if (effectNumber == 39)
                        flyFrames = 4;

                    if (flyFrames <= 0 || frameTimeMs <= 0)
                        continue;

                    int effectFrame = (int)(elapsed / frameTimeMs);
                    if (flyFrames > 0)
                        effectFrame %= flyFrames;
                    if (effectFrame < 0)
                        effectFrame = 0;

                    float t = Math.Clamp(elapsed / (float)travelDurationMs, 0f, 1f);
                    rx = effect.FromX + ((toX - effect.FromX) * t);
                    ry = effect.FromY + ((toY - effect.FromY) * t);

                    const int FlyOmaAxeBase = 447;

                    if (isBujaukGroundEffect)
                    {
                        if (effectNumber == 74)
                        {
                            archivePath = ResolveEffectArchivePath(new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic5));
                            if (archivePath == null)
                                continue;

                            idx = 10 + ((effect.Dir16 / 2) * 10) + effectFrame;
                        }
                        else
                        {
                            archivePath = ResolveEffectArchivePath(new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic));
                            if (archivePath == null)
                                continue;

                            idx = 1160 + (effect.Dir16 * 10) + effectFrame;
                        }
                    }
                    else if (magicType == MagicType.FlyAxe)
                    {
                        archivePath = ResolveEffectArchivePath(new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic));
                        if (archivePath == null)
                            continue;

                        idx = FlyOmaAxeBase + (effect.Dir16 * 10) + effectFrame;
                    }
                    else if (magicType == MagicType.FlyArrow)
                    {
                        archivePath = ResolveEffectArchivePath(new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic));
                        if (archivePath == null)
                            continue;

                        idx = FlyOmaAxeBase + effect.Dir16 + effectFrame;
                        pixelOffsetY = -46;
                    }
                    else if (magicType == MagicType.FlyBug)
                    {
                        archivePath = ResolveEffectArchivePath(new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic));
                        if (archivePath == null)
                            continue;

                        idx = FlyOmaAxeBase + ((effect.Dir16 / 2) * 10) + effectFrame;
                    }
                    else if (magicType == MagicType.FireBall)
                    {
                        archivePath = ResolveEffectArchivePath(new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic));
                        if (archivePath == null)
                            continue;

                        int dxPx = (int)Math.Round((toX - rx) * unitX);
                        int dyPx = (int)Math.Round((toY - ry) * unitY);
                        byte dir = MirDirection.GetFlyDirection(0, 0, dxPx, dyPx);
                        idx = FlyOmaAxeBase + (dir * 10) + effectFrame;
                    }
                    else if (isTeleportBujauk)
                    {
                        archivePath = ResolveEffectArchivePath(new MagicEffectArchiveRef(MagicEffectArchiveKind.Magic5));
                        if (archivePath == null)
                            continue;

                        idx = 10 + ((effect.Dir16 / 2) * 10) + effectFrame;
                    }
                    else
                    {
                        if (!TryGetMagicEffectBaseWithType(effectNumber - 1, mType: 0, out archivePath, out int baseIndex))
                            continue;

                        idx = baseIndex + MagicEffTimeline.FlyBaseOffset + (effect.Dir16 * 10) + effectFrame;
                    }
                }
                else
                {
                    if (magicType is MagicType.FlyAxe or MagicType.FlyArrow)
                        continue;

                    long explosionElapsed = elapsed - travelDurationMs;
                    if (explosionElapsed < 0)
                        explosionElapsed = 0;

                    if (!MagicEffExplosionAtlas.TryGetInfo(effectNumber, effect.EffectType, effect.MagicLevel, magicEffSelfX, magicEffSelfY, out MagicEffectArchiveRef archive, out int startIndex, out int frames, out int frameTimeMs))
                        continue;

                    archivePath = ResolveEffectArchivePath(archive);
                    if (archivePath == null)
                        continue;

                    if (frames <= 0 || frameTimeMs <= 0)
                        continue;

                    long total = (long)frames * frameTimeMs;
                    if (total > 0 && explosionElapsed >= total)
                        continue;

                    int effectFrame = (int)(explosionElapsed / frameTimeMs);
                    if (effectFrame >= frames)
                        effectFrame = frames - 1;
                    if (effectFrame < 0)
                        effectFrame = 0;

                    idx = startIndex + effectFrame;
                    rx = toX;
                    ry = toY;
                }

                if (rx < 0 || ry < 0 || rx >= _map.Width || ry >= _map.Height)
                    continue;

                if (rx < left - 4 || rx > right + 4 || ry < top - 4 || ry > bottom + 40)
                    continue;

                int row = (int)Math.Round(ry);
                magicEffDraws.Add(new MagicEffDrawInfo(row, (int)Math.Round(rx), rx, ry, archivePath, idx, pixelOffsetX, pixelOffsetY));
            }

            if (magicEffDraws.Count > 1)
            {
                magicEffDraws.Sort(static (a, b) =>
                {
                    int cmp = a.Row.CompareTo(b.Row);
                    if (cmp != 0)
                        return cmp;
                    return a.X.CompareTo(b.X);
                });
            }

            void DrawMagicEff(MagicEffDrawInfo draw)
            {
                int baseX = (int)Math.Round(((draw.Rx - left) * unitX) + defx) + draw.PixelOffsetX;
                int baseY = (int)Math.Round(((draw.Ry - top - 1) * unitY) + defy) + draw.PixelOffsetY;

                if (TryGetActorTextureWithPivot(draw.ArchivePath, draw.ImageIndex, out D3D11Texture2D tex, out (short Px, short Py) pivot))
                {
                    _spriteBatch.SetBlendMode(SpriteBlendMode.Additive);
                    _spriteBatch.Draw(
                        tex,
                        new DrawingRectangle(baseX + pivot.Px, baseY + pivot.Py, tex.Width, tex.Height));
                }
            }

            _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);

            const float NameLineHeight = 12f;

            static bool IsActorDead(ActorMarker actor) =>
                actor.Action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH or Grobal2.SM_SKELETON;

            static int GetActorSayY(ActorMarker actor, int baseY) =>
                baseY + (IsActorDead(actor) ? -12 : -47);

            void QueueNameLine(string text, int centerX, float y, Color4 color)
            {
                if (string.IsNullOrEmpty(text))
                    return;

                float x = centerX - MeasureHalfTextWidth(text);
                _nameDrawList.Add(new NameDrawInfo(text, x, y, color));
            }

            void QueueDelimitedNameLines(string text, int centerX, float startY, Color4 color, bool exploreFirstLine)
            {
                if (string.IsNullOrEmpty(text))
                    return;

                int line = 0;
                int start = 0;
                for (int i = 0; i <= text.Length && line < 10; i++)
                {
                    if (i != text.Length && text[i] != '\\')
                        continue;

                    string part = text[start..i];
                    if (!string.IsNullOrEmpty(part))
                    {
                        Color4 lineColor = exploreFirstLine && line == 0
                            ? new Color4(0f, 1f, 0f, 1f)
                            : color;
                        QueueNameLine(part, centerX, startY + (line * NameLineHeight), lineColor);
                        line++;
                    }

                    start = i + 1;
                }
            }

            Color4 GetActorDrawEffectColor(ActorMarker actor, bool highlight)
            {
                Color4 color = highlight
                    ? new Color4(1.15f, 1.15f, 1.15f, 1f)
                    : new Color4(1f, 1f, 1f, 1f);

                const int StateEffectGreen = unchecked((int)0x80000000);
                const int StateEffectRed = 0x40000000;
                const int StateEffectBlue = 0x20000000;
                const int StateEffectYellow = 0x10000000;
                const int StateEffectFuchsia = 0x08000000;
                const int StateEffectGrayScale = 0x04000000;
                const int StateEffectPurple = 0x02000000;
                const int StateEffectWhite = 0x01000000;

                int status = actor.Status;
                if ((status & StateEffectGreen) != 0)
                    color = ToColor4(Mir2ColorTable.GetArgb(222));
                if ((status & StateEffectRed) != 0)
                    color = new Color4(1f, 0f, 0f, 1f);
                if ((status & StateEffectBlue) != 0)
                    color = new Color4(0f, 0f, 1f, 1f);
                if ((status & StateEffectYellow) != 0)
                    color = new Color4(1f, 1f, 0f, 1f);
                if ((status & StateEffectFuchsia) != 0)
                    color = new Color4(1f, 0f, 1f, 1f);
                if ((status & StateEffectGrayScale) != 0)
                    color = new Color4(-1f, -1f, -1f, 1f);
                if ((status & StateEffectPurple) != 0)
                    color = new Color4(0.65f, 0.25f, 1.0f, 1f);
                if ((status & StateEffectWhite) != 0)
                    color = new Color4(1f, 1f, 1f, 1f);

                return color;
            }

            void QueueActorName(ActorMarker actor, int anchorX, float nameY, Color4 color)
            {
                if (!_showActorNames)
                    return;

                if (string.IsNullOrEmpty(actor.UserName))
                    return;
                QueueNameLine(actor.UserName, anchorX, nameY, color);
            }

            void QueueFocusActorName(ActorMarker actor, int centerX, int sayY, Color4 color, bool explore)
            {
                string text;
                if (!string.IsNullOrWhiteSpace(actor.DescUserName) && !string.IsNullOrWhiteSpace(actor.UserName))
                    text = $"{actor.DescUserName}\\{actor.UserName}";
                else if (!string.IsNullOrWhiteSpace(actor.DescUserName))
                    text = actor.DescUserName;
                else
                    text = actor.UserName;

                if (string.IsNullOrWhiteSpace(text))
                    return;

                if (explore)
                    text = $"(可探索)\\{text}";

                float startY = sayY + (explore ? 18f : 30f);
                QueueDelimitedNameLines(text, centerX, startY, color, exploreFirstLine: explore);
            }

            static Color4 GetTitleColor(sbyte level) => level switch
            {
                0 => new Color4(1f, 1f, 1f, 1f),
                1 => new Color4(0.29f, 0.84f, 0.39f, 1f),
                2 => new Color4(0.91f, 0.63f, 0.00f, 1f),
                3 => new Color4(1.00f, 0.21f, 0.69f, 1f),
                4 => new Color4(0.00f, 0.38f, 0.92f, 1f),
                5 => new Color4(0.36f, 0.96f, 1.00f, 1f),
                15 => new Color4(0.60f, 0.60f, 0.60f, 1f),
                _ => new Color4(0.36f, 0.96f, 1.00f, 1f)
            };

            bool TryGetServerTitle(int titleIndex, out ClientStdItem title)
            {
                title = default;

                if (titleIndex <= 0)
                    return false;

                ReadOnlySpan<ClientStdItem> titles = _world.ServerTitles;
                int idx = titleIndex - 1;
                if ((uint)idx >= (uint)titles.Length)
                    return false;

                title = titles[idx];
                return title.Reserved != 0 || !string.IsNullOrWhiteSpace(title.NameString) || title.Looks != 0;
            }

            void DrawActorTitle(int anchorX, float titleY, int titleIndex)
            {
                if (!_showActorNames)
                    return;

                if (!TryGetServerTitle(titleIndex, out ClientStdItem title))
                    return;

                ushort looks = title.Looks;
                bool iconOnly = title.Reserved != 0;

                string text = iconOnly ? string.Empty : title.NameString;
                Color4 color = GetTitleColor(title.Source);

                const int IconTextGap = 4;

                D3D11Texture2D iconTex = null!;
                bool hasIcon = ui1Path != null &&
                    looks > 0 &&
                    TryGetActorTextureWithPivot(ui1Path, looks, out iconTex, out _);

                float iconW = hasIcon ? iconTex.Width : 0f;
                float iconH = hasIcon ? iconTex.Height : 0f;

                float textW = string.IsNullOrEmpty(text) ? 0f : (MeasureHalfTextWidth(text) * 2f);

                if (hasIcon)
                {
                    float iconY = titleY - Math.Max(0f, (iconH - NameLineHeight) * 0.5f);
                    int dy = (int)MathF.Round(iconY);

                    if (!string.IsNullOrEmpty(text))
                    {
                        float totalW = iconW + IconTextGap + textW;
                        float leftX = anchorX - (totalW * 0.5f);
                        int dx = (int)MathF.Round(leftX);

                        _spriteBatch.Draw(
                            iconTex,
                            new DrawingRectangle(dx, dy, iconTex.Width, iconTex.Height));

                        _nameDrawList.Add(new NameDrawInfo(text, leftX + iconW + IconTextGap, titleY, color));
                        return;
                    }

                    int centeredX = anchorX - (iconTex.Width / 2);
                    _spriteBatch.Draw(
                        iconTex,
                        new DrawingRectangle(centeredX, dy, iconTex.Width, iconTex.Height));
                    return;
                }

                if (!string.IsNullOrEmpty(text))
                {
                    float x = anchorX - MeasureHalfTextWidth(text);
                    _nameDrawList.Add(new NameDrawInfo(text, x, titleY, color));
                }
            }

            void DrawActorDamagePopup(ActorMarker actor, int anchorX, float baseY)
            {
                if (actor.LastDamageTimestampMs <= 0 || actor.LastDamage == 0)
                    return;

                const long DamageDurationMs = 1200;
                long dt = nowMs - actor.LastDamageTimestampMs;
                if (dt is < 0 or >= DamageDurationMs)
                    return;

                float tPop = dt / (float)DamageDurationMs;
                float popY = baseY - 28 - (tPop * 16f);

                int damage = actor.LastDamage;
                if (damage > 0 && TryDrawDamageDigits(anchorX, popY, damage))
                    return;

                string text = damage.ToString();
                float popX = anchorX - MeasureHalfTextWidth(text);
                _nameDrawList.Add(new NameDrawInfo(text, popX, popY, new Color4(1.0f, 0.35f, 0.25f, 1.0f)));
            }

            bool TryDrawDamageDigits(int anchorX, float y, int damage)
            {
                if (opUiPath == null)
                    return false;

                string text = damage.ToString();
                if (text.Length == 0)
                    return false;

                var digits = new D3D11Texture2D[text.Length];
                int totalWidth = 0;

                for (int i = 0; i < text.Length; i++)
                {
                    int digit = text[i] - '0';
                    if ((uint)digit >= 10u)
                        return false;

                    int idx = 170 + digit;
                    if (!TryGetActorTextureWithPivot(opUiPath, idx, out D3D11Texture2D tex, out _))
                        return false;

                    digits[i] = tex;
                    totalWidth += tex.Width;
                }

                float x = anchorX - (totalWidth * 0.5f);
                int dy = (int)MathF.Round(y);

                for (int i = 0; i < digits.Length; i++)
                {
                    D3D11Texture2D tex = digits[i];
                    int dx = (int)MathF.Round(x);
                    _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, tex.Width, tex.Height));
                    x += tex.Width;
                }

                return true;
            }

            static int GetStallDrawDir(byte actorDir)
            {
                int dir = actorDir & 7;
                return dir is 1 or 3 or 5 or 7 ? dir : 5;
            }

            static bool TryGetStallBackSpriteInfo(ushort stallType, int stallDir, out int imageIndex, out int offsetX, out int offsetY)
            {
                const int StallLooksBase = 80;

                imageIndex = 0;
                offsetX = 0;
                offsetY = 0;

                switch (stallDir)
                {
                    case 1:
                        switch (stallType)
                        {
                            case 0:
                                imageIndex = StallLooksBase + 10;
                                offsetX = -20;
                                offsetY = -22;
                                return true;
                            case 1:
                                imageIndex = StallLooksBase + 14;
                                offsetX = -6;
                                offsetY = -40;
                                return true;
                            case 2:
                                imageIndex = StallLooksBase + 18;
                                offsetX = -18;
                                offsetY = -54;
                                return true;
                        }
                        break;
                    case 3:
                        if (stallType == 0)
                        {
                            imageIndex = StallLooksBase + 7;
                            offsetX = -25;
                            offsetY = -10;
                            return true;
                        }
                        break;
                    case 5:
                        if (stallType == 0)
                        {
                            imageIndex = StallLooksBase + 9;
                            offsetX = -47;
                            offsetY = -10;
                            return true;
                        }
                        break;
                    case 7:
                        switch (stallType)
                        {
                            case 0:
                                imageIndex = StallLooksBase + 8;
                                offsetX = -52;
                                offsetY = -30;
                                return true;
                            case 1:
                                imageIndex = StallLooksBase + 12;
                                offsetX = -46;
                                offsetY = -44;
                                return true;
                            case 2:
                                imageIndex = StallLooksBase + 16;
                                offsetX = -56;
                                offsetY = -48;
                                return true;
                        }
                        break;
                }

                return false;
            }

            static bool TryGetStallFrontSpriteInfo(ushort stallType, int stallDir, out int imageIndex, out int offsetX, out int offsetY)
            {
                const int StallLooksBase = 80;

                imageIndex = 0;
                offsetX = 0;
                offsetY = 0;

                switch (stallDir)
                {
                    case 3:
                        switch (stallType)
                        {
                            case 1:
                                imageIndex = StallLooksBase + 11;
                                offsetX = -8;
                                offsetY = -24;
                                return true;
                            case 2:
                                imageIndex = StallLooksBase + 15;
                                offsetX = -16;
                                offsetY = -20;
                                return true;
                        }
                        break;
                    case 5:
                        switch (stallType)
                        {
                            case 1:
                                imageIndex = StallLooksBase + 13;
                                offsetX = -48;
                                offsetY = -18;
                                return true;
                            case 2:
                                imageIndex = StallLooksBase + 17;
                                offsetX = -50;
                                offsetY = -18;
                                return true;
                        }
                        break;
                }

                return false;
            }

            void DrawStallSprite(int baseX, int baseY, int imageIndex, int offsetX, int offsetY)
            {
                if (opUiPath == null)
                    return;

                if (!TryGetActorTextureWithPivot(opUiPath, imageIndex, out D3D11Texture2D tex, out _))
                    return;

                _spriteBatch.Draw(tex, new DrawingRectangle(baseX + offsetX, baseY + offsetY, tex.Width, tex.Height));
            }

            int magicEffDrawIndex = 0;

            static int GetActorDownDrawLevel(ActorMarker actor)
            {
                byte race = FeatureCodec.Race(actor.Feature);
                if (race == 0 || race == Grobal2.RCC_MERCHANT)
                    return 0;

                ushort action = actor.Action;

                
                if (race == 99)
                {
                    int dir = actor.Dir & 7;
                    bool open = dir >= 3;
                    bool dead = action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH;
                    return (open || dead) ? 2 : 1;
                }

                
                if ((action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH) && race is >= 117 and <= 120)
                {
                    ushort appearance = FeatureCodec.Appearance(actor.Feature);
                    if ((appearance >= 30 && appearance <= 34) || appearance == 151)
                        return 1;
                }

                return 0;
            }

            static int GetActorRowPriority(ActorMarker actor)
            {
                byte race = FeatureCodec.Race(actor.Feature);
                if (race == 98) 
                    return 0;

                ushort action = actor.Action;
                if (action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH or Grobal2.SM_SKELETON)
                    return 1;

                return 2;
            }

            bool showHpNumberConfig = !_world.ClientConfigSet || _world.ClientConfig.ShowHpNumber != 0;
            bool showRedHpLabelConfig = !_world.ClientConfigSet || _world.ClientConfig.ShowRedHpLabel != 0;

            List<ActorDrawInfo> actorDraws = _actorDraws;
            actorDraws.Clear();
            foreach ((int recogId, ActorMarker actor) in _world.Actors)
            {
                long elapsedMs = (nowTimestamp - actor.ActionStartTimestamp) * 1000 / Stopwatch.Frequency;

                if (_hideDeathBody && actor.Action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH)
                {
                    byte race = FeatureCodec.Race(actor.Feature);
                    if (race != 0 && !actor.ItemExplore)
                    {
                        int durationMs = MirActionTiming.GetActionDurationMs(actor.Action);
                        if (durationMs <= 0 || elapsedMs >= durationMs)
                            continue;
                    }
                }

                int rx = actor.X;
                int ry = actor.Y;
                int pixelOffsetX = 0;
                int pixelOffsetY = 0;

                if (MirDirection.IsMoveAction(actor.Action))
                {
                    (int frames, int frameTimeMs) = GetActorMoveTiming(recogId, actor);
                    long totalMs = (long)frames * frameTimeMs;
                    if (totalMs > 0 && frames > 0 && frameTimeMs > 0 && elapsedMs >= 0 && elapsedMs < totalMs)
                    {
                        float t = Math.Clamp(elapsedMs / (float)totalMs, 0f, 1f);
                        float remain = 1f - t;
                        pixelOffsetX = (int)Math.Round((actor.FromX - actor.X) * unitX * remain);
                        pixelOffsetY = (int)Math.Round((actor.FromY - actor.Y) * unitY * remain);
                    }
                }

                int ax = actor.X;
                int ay = actor.Y;
                if ((uint)ax >= (uint)_map.Width || (uint)ay >= (uint)_map.Height)
                    continue;

                if (rx < left - 4 || rx > right + 4 || ry < top - 4 || ry > bottom + 40)
                    continue;

                int actorRow = ry - GetActorDownDrawLevel(actor);
                int priority = GetActorRowPriority(actor);
                int sortX = rx;

                actorDraws.Add(new ActorDrawInfo(recogId, actor, actorRow, sortX, priority, rx, ry, pixelOffsetX, pixelOffsetY, elapsedMs));
            }

            if (!_hideDeathBody)
            {
                _corpseDrawActors.Clear();
                lock (_corpseLock)
                {
                    if (_corpseMarkers.Count > 0)
                    {
                        if (nowMs - _lastCorpsePruneMs >= 1000)
                        {
                            _lastCorpsePruneMs = nowMs;
                            PruneCorpseMarkersNoLock(nowMs);
                        }

                        foreach ((int id, CorpseMarker marker) in _corpseMarkers)
                        {
                            if (_world.Actors.ContainsKey(id))
                                continue;

                            _corpseDrawActors.Add(marker.Actor);
                        }
                    }
                }

                for (int i = 0; i < _corpseDrawActors.Count; i++)
                {
                    ActorMarker actor = _corpseDrawActors[i];

                    long elapsedMs = (nowTimestamp - actor.ActionStartTimestamp) * 1000 / Stopwatch.Frequency;

                    int rx = actor.X;
                    int ry = actor.Y;

                    int ax = actor.X;
                    int ay = actor.Y;
                    if ((uint)ax >= (uint)_map.Width || (uint)ay >= (uint)_map.Height)
                        continue;

                    if (rx < left - 4 || rx > right + 4 || ry < top - 4 || ry > bottom + 40)
                        continue;

                    int actorRow = ry - GetActorDownDrawLevel(actor);
                    int priority = GetActorRowPriority(actor);
                    int sortX = rx;

                    actorDraws.Add(new ActorDrawInfo(0, actor, actorRow, sortX, priority, rx, ry, 0, 0, elapsedMs));
                }
            }

            if (actorDraws.Count > 1)
            {
                actorDraws.Sort(static (a, b) =>
                {
                    int cmp = a.Row.CompareTo(b.Row);
                    if (cmp != 0)
                        return cmp;

                    cmp = a.Priority.CompareTo(b.Priority);
                    if (cmp != 0)
                        return cmp;

                    cmp = a.SortX.CompareTo(b.SortX);
                    if (cmp != 0)
                        return cmp;

                    return a.RecogId.CompareTo(b.RecogId);
                });
            }

            ActorMarker focusActor = default;
            int focusSayX = 0;
            int focusSayY = 0;
            bool focusSet = false;

            foreach (ActorDrawInfo draw in actorDraws)
            {
                int actorRow = draw.Row;
                while (magicEffDrawIndex < magicEffDraws.Count && magicEffDraws[magicEffDrawIndex].Row < actorRow)
                    DrawMagicEff(magicEffDraws[magicEffDrawIndex++]);

                int recogId = draw.RecogId;
                ActorMarker actor = draw.Actor;
                long elapsedMs = draw.ElapsedMs;
                int rx = draw.X;
                int ry = draw.Y;
                int pixelOffsetX = draw.PixelOffsetX;
                int pixelOffsetY = draw.PixelOffsetY;

                int baseX = ((rx - left) * unitX) + defx + pixelOffsetX;
                int baseY = ((ry - top - 1) * unitY) + defy + pixelOffsetY;
                int anchorX = baseX + halfX;
                int anchorY = baseY + unitY;
                int sayY = GetActorSayY(actor, baseY);

                _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);
                bool selected = recogId != 0 && recogId == _targetingSystem.SelectedRecogId;
                if (selected)
                {
                    focusActor = actor;
                    focusSayX = anchorX;
                    focusSayY = sayY;
                    focusSet = true;
                }

                bool isHuman = FeatureCodec.Race(actor.Feature) == 0;
                if (isHuman)
                {
                    int nFrame = ComputeHumanFrameIndex(recogId, actor, elapsedMs);
                    bool useCboLib = actor.Action is Grobal2.SM_RUSHEX or Grobal2.SM_SMITEHIT or Grobal2.SM_SMITELONGHIT or Grobal2.SM_SMITELONGHIT2 or Grobal2.SM_SMITELONGHIT3 or Grobal2.SM_SMITEWIDEHIT or Grobal2.SM_SMITEWIDEHIT2;
                    bool hasStall = _world.Stalls.TryGetValue(recogId, out StallActorMarker stallMarker) && stallMarker.Open;
                    int stallDir = hasStall ? GetStallDrawDir(actor.Dir) : 0;
                    Color4 actorEffectColor = GetActorDrawEffectColor(actor, selected);

                    if (hasStall && TryGetStallBackSpriteInfo(stallMarker.Looks, stallDir, out int stallBackIndex, out int stallBackDx, out int stallBackDy))
                        DrawStallSprite(baseX, baseY, stallBackIndex, stallBackDx, stallBackDy);

                    bool hasHumEffect = false;
                    bool humEffectBehind = false;
                    bool humEffectFront = false;
                    D3D11Texture2D humEffectTex = null!;
                    (short Px, short Py) humEffectPivot = default;

                    if (TryGetHumanEffectKey(actor, nFrame, out string humEffectArchivePath, out int humEffectIndex, out int humEffectId) &&
                        TryGetActorTextureWithPivot(humEffectArchivePath, humEffectIndex, out humEffectTex, out humEffectPivot))
                    {
                        int dir = actor.Dir & 7;
                        humEffectBehind = dir is >= 3 and <= 5;
                        humEffectFront = !humEffectBehind || humEffectId == 50;
                        hasHumEffect = true;

                        if (humEffectBehind)
                        {
                            _spriteBatch.SetBlendMode(SpriteBlendMode.BeBlend);
                            _spriteBatch.Draw(
                                humEffectTex,
                                new DrawingRectangle(baseX + humEffectPivot.Px, baseY + humEffectPivot.Py, humEffectTex.Width, humEffectTex.Height));
                            _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);
                        }
                    }

                    if (TryGetHumanBodyKey(actor.Feature, nFrame, useCboLib, out string bodyPath, out int bodyIndex) &&
                        TryGetActorTextureWithPivot(bodyPath, bodyIndex, out D3D11Texture2D bodyTex, out (short Px, short Py) bodyPivot))
                    {
                        bool weaponBefore = GetHumanWeaponOrder(FeatureCodec.Dress(actor.Feature) & 1, nFrame) == 0;

                        if (weaponBefore && TryGetHumanWeaponKey(actor.Feature, nFrame, useCboLib, out string weaponArchivePath, out int weaponIndex))
                        {
                            if (TryGetActorTextureWithPivot(weaponArchivePath, weaponIndex, out D3D11Texture2D weaponTex, out (short Px, short Py) weaponPivot))
                            {
                                _spriteBatch.Draw(
                                    weaponTex,
                                    new DrawingRectangle(baseX + weaponPivot.Px, baseY + weaponPivot.Py, weaponTex.Width, weaponTex.Height));
                            }
                        }

                        _spriteBatch.Draw(
                            bodyTex,
                            new DrawingRectangle(baseX + bodyPivot.Px, baseY + bodyPivot.Py, bodyTex.Width, bodyTex.Height),
                            color: actorEffectColor);

                        if (TryGetHumanHairKey(actor.Feature, nFrame, useCboLib, out string hairArchivePath, out int hairIndex))
                        {
                            if (TryGetActorTextureWithPivot(hairArchivePath, hairIndex, out D3D11Texture2D hairTex, out (short Px, short Py) hairPivot))
                            {
                                _spriteBatch.Draw(
                                    hairTex,
                                    new DrawingRectangle(baseX + hairPivot.Px, baseY + hairPivot.Py, hairTex.Width, hairTex.Height),
                                    color: actorEffectColor);
                            }
                        }

                        if (!weaponBefore && TryGetHumanWeaponKey(actor.Feature, nFrame, useCboLib, out string frontWeaponPath, out int frontWeaponIndex))
                        {
                            if (TryGetActorTextureWithPivot(frontWeaponPath, frontWeaponIndex, out D3D11Texture2D weaponTex, out (short Px, short Py) weaponPivot))
                            {
                                _spriteBatch.Draw(
                                    weaponTex,
                                    new DrawingRectangle(baseX + weaponPivot.Px, baseY + weaponPivot.Py, weaponTex.Width, weaponTex.Height));
                            }
                        }

                        if (hasHumEffect && humEffectFront)
                        {
                            _spriteBatch.SetBlendMode(SpriteBlendMode.BeBlend);
                            _spriteBatch.Draw(
                                humEffectTex,
                                new DrawingRectangle(baseX + humEffectPivot.Px, baseY + humEffectPivot.Py, humEffectTex.Width, humEffectTex.Height));
                            _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);
                        }

                        _spriteBatch.SetBlendMode(SpriteBlendMode.BeBlend);
                        DrawHumanStateEffects(actor, elapsedMs, baseX, baseY);
                        _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);

                        if (hasStall && TryGetStallFrontSpriteInfo(stallMarker.Looks, stallDir, out int stallFrontIndex, out int stallFrontDx, out int stallFrontDy))
                            DrawStallSprite(baseX, baseY, stallFrontIndex, stallFrontDx, stallFrontDy);

                        if (TryGetSpellEffectKey(actor, elapsedMs, out string spellArchivePath, out int spellIndex) &&
                            TryGetActorTextureWithPivot(spellArchivePath, spellIndex, out D3D11Texture2D spellTex, out (short Px, short Py) spellPivot))
                        {
                            _spriteBatch.SetBlendMode(SpriteBlendMode.BeBlend);
                            _spriteBatch.Draw(
                                spellTex,
                                new DrawingRectangle(baseX + spellPivot.Px, baseY + spellPivot.Py, spellTex.Width, spellTex.Height));
                            _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);
                        }

                        if (actor.WeaponEffect && magicPath != null)
                        {
                            const int WeaponEffectBase = 3750;
                            const int WeaponEffectFrameMs = 120;
                            const int MaxWeaponEffectFrames = 5;

                            int effectFrame = (int)((nowMs - actor.WeaponEffectStartMs) / WeaponEffectFrameMs);
                            if (effectFrame is >= 0 and < MaxWeaponEffectFrames)
                            {
                                int dir = actor.Dir & 7;
                                int idx = WeaponEffectBase + (dir * 10) + effectFrame;
                                if (TryGetActorTextureWithPivot(magicPath, idx, out D3D11Texture2D effTex, out (short Px, short Py) effPivot))
                                {
                                    _spriteBatch.SetBlendMode(SpriteBlendMode.BeBlend);
                                    _spriteBatch.Draw(
                                        effTex,
                                        new DrawingRectangle(baseX + effPivot.Px, baseY + effPivot.Py, effTex.Width, effTex.Height));
                                    _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);
                                }
                            }
                        }

                        if (_showActorNames)
                        {
                            bool showHpNumberOffsets = showHpNumberConfig && actor.MaxHp > 1;

                            if (hasStall && !string.IsNullOrWhiteSpace(stallMarker.Name))
                            {
                                float stallY = showHpNumberOffsets ? sayY - 36 : sayY - 24;
                                QueueNameLine(stallMarker.Name, anchorX, stallY, ToColor4(Mir2ColorTable.GetArgb(94)));
                            }

                            if (actor.TitleIndex > 0)
                            {
                                float titleY = hasStall
                                    ? (showHpNumberOffsets ? sayY - 55 : sayY - 43)
                                    : (showHpNumberOffsets ? sayY - 40 : sayY - 28);
                                DrawActorTitle(anchorX, titleY, actor.TitleIndex);
                            }

                            if (!selected && !string.IsNullOrEmpty(actor.UserName))
                            {
                                Color4 color = ToColor4(Mir2ColorTable.GetArgb(actor.NameColor));
                                float nameY = sayY + 30;
                                QueueActorName(actor, anchorX, nameY, color);
                            }
                        }

                         DrawActorDamagePopup(actor, anchorX, baseY + bodyPivot.Py);
                         DrawActorHealthBar(recogId, actor, anchorX, sayY, showHpNumberConfig, showRedHpLabelConfig);
                         continue;
                     }
                 }
                else
                {
                    byte race = FeatureCodec.Race(actor.Feature);
                    if (race == Grobal2.RCC_MERCHANT)
                    {
                        short headPy = 0;
                        bool bodyDrawn = false;
                        bool anyDrawn = false;
                        Color4 actorEffectColor = GetActorDrawEffectColor(actor, selected);

                        if (TryGetNpcBodyKey(actor, elapsedMs, out string npcArchivePath, out int npcIndex) &&
                            TryGetActorTextureWithPivot(npcArchivePath, npcIndex, out D3D11Texture2D npcTex, out (short Px, short Py) npcPivot))
                        {
                            _spriteBatch.Draw(
                                npcTex,
                                new DrawingRectangle(baseX + npcPivot.Px, baseY + npcPivot.Py, npcTex.Width, npcTex.Height),
                                color: actorEffectColor);
                            headPy = npcPivot.Py;
                            bodyDrawn = true;
                            anyDrawn = true;
                        }

                        if (TryGetNpcEffectKey(actor, elapsedMs, out string npcEffArchivePath, out int npcEffIndex, out (short Dx, short Dy) npcEffOffset) &&
                            TryGetActorTextureWithPivot(npcEffArchivePath, npcEffIndex, out D3D11Texture2D npcEffTex, out (short Px, short Py) npcEffPivot))
                        {
                            _spriteBatch.Draw(
                                npcEffTex,
                                new DrawingRectangle(baseX + npcEffPivot.Px + npcEffOffset.Dx, baseY + npcEffPivot.Py + npcEffOffset.Dy, npcEffTex.Width, npcEffTex.Height));

                            if (!bodyDrawn)
                                headPy = (short)(npcEffPivot.Py + npcEffOffset.Dy);

                            anyDrawn = true;
                        }

                        if (anyDrawn)
                        {
                            if (_showActorNames && !selected && !string.IsNullOrEmpty(actor.UserName))
                            {
                                float nameY = sayY + 30;
                                QueueActorName(actor, anchorX, nameY, new Color4(0, 1, 0, 1));
                            }

                             DrawActorDamagePopup(actor, anchorX, baseY + headPy);
                             DrawActorHealthBar(recogId, actor, anchorX, sayY, showHpNumberConfig, showRedHpLabelConfig);
                             continue;
                        }
                    }
                    else if (TryGetMonsterKey(recogId, actor, elapsedMs, out string monArchivePath, out int monIndex))
                    {
                        int appearance = FeatureCodec.Appearance(actor.Feature);
                        int actorRace = FeatureCodec.Race(actor.Feature);

                        if (actor.Action == Grobal2.SM_DIGUP)
                        {
                            if (actorRace == 23)
                            {
                                string? mon4Path = GetMonFilePath(4);
                                if (mon4Path != null &&
                                    TryGetActorTextureWithPivot(mon4Path, monIndex, out D3D11Texture2D digTex, out (short Px, short Py) digPivot))
                                {
                                    _spriteBatch.SetBlendMode(SpriteBlendMode.Additive);
                                    _spriteBatch.Draw(
                                        digTex,
                                        new DrawingRectangle(baseX + digPivot.Px, baseY + digPivot.Py, digTex.Width, digTex.Height));
                                    _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);

                                    if (_showActorNames && !selected && !string.IsNullOrEmpty(actor.UserName))
                                    {
                                        Color4 color = ToColor4(Mir2ColorTable.GetArgb(actor.NameColor));
                                        float nameY = sayY + 30;
                                        QueueActorName(actor, anchorX, nameY, color);
                                    }

                                    DrawActorDamagePopup(actor, anchorX, baseY + digPivot.Py);
                                    DrawActorHealthBar(recogId, actor, anchorX, sayY, showHpNumberConfig, showRedHpLabelConfig);
                                    continue;
                                }
                            }
                            else if (actorRace is >= 91 and <= 93)
                            {
                                if (magic7Path != null)
                                {
                                    int offset = AppearanceOffsets.GetMonsterOffset(appearance);
                                    int frameIndex = monIndex - offset;
                                    if (TryGetActorTextureWithPivot(magic7Path, frameIndex, out D3D11Texture2D digTex, out (short Px, short Py) digPivot))
                                    {
                                        _spriteBatch.SetBlendMode(SpriteBlendMode.Additive);
                                        _spriteBatch.Draw(
                                            digTex,
                                            new DrawingRectangle(baseX + digPivot.Px, baseY + digPivot.Py, digTex.Width, digTex.Height));
                                        _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);

                                        if (_showActorNames && !selected && !string.IsNullOrEmpty(actor.UserName))
                                        {
                                            Color4 color = ToColor4(Mir2ColorTable.GetArgb(actor.NameColor));
                                            float nameY = sayY + 30;
                                            QueueActorName(actor, anchorX, nameY, color);
                                        }

                                        DrawActorDamagePopup(actor, anchorX, baseY + digPivot.Py);
                                        DrawActorHealthBar(recogId, actor, anchorX, sayY, showHpNumberConfig, showRedHpLabelConfig);
                                        continue;
                                    }
                                }
                            }
                        }

                        if (TryGetActorTextureWithPivot(monArchivePath, monIndex, out D3D11Texture2D monTex, out (short Px, short Py) monPivot))
                        {
                            Color4 actorEffectColor = GetActorDrawEffectColor(actor, selected);
                            if (TryGetGhostShipMonsterShadowOffset(appearance, out int shadowOffset))
                            {
                                string? shadowPath = GetMonFilePath(36);
                                int shadowIndex = monIndex + shadowOffset;

                                if (shadowPath != null &&
                                    shadowIndex >= 0 &&
                                    TryGetActorTextureWithPivot(shadowPath, shadowIndex, out D3D11Texture2D shadowTex, out (short Px, short Py) shadowPivot))
                                {
                                    _spriteBatch.SetBlendMode(SpriteBlendMode.BeBlend);
                                    _spriteBatch.Draw(
                                        shadowTex,
                                        new DrawingRectangle(baseX + shadowPivot.Px, baseY + shadowPivot.Py, shadowTex.Width, shadowTex.Height));
                                    _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);
                                }
                            }

                            _spriteBatch.Draw(
                                monTex,
                                new DrawingRectangle(baseX + monPivot.Px, baseY + monPivot.Py, monTex.Width, monTex.Height),
                                color: actorEffectColor);

                            if (actorRace == 98 && appearance is >= 901 and <= 903)
                            {
                                int dir = actor.Dir & 7;
                                int offset = AppearanceOffsets.GetMonsterOffset(appearance);
                                int brokenIndex = offset + 8 + dir;
                                if (TryGetActorTextureWithPivot(monArchivePath, brokenIndex, out D3D11Texture2D brokenTex, out (short Px, short Py) brokenPivot))
                                {
                                    _spriteBatch.Draw(
                                        brokenTex,
                                        new DrawingRectangle(baseX + brokenPivot.Px, baseY + brokenPivot.Py, brokenTex.Width, brokenTex.Height));
                                }

                                if (actor.Action is Grobal2.SM_NOWDEATH or Grobal2.SM_DIGUP)
                                {
                                    MonsterActions.MonsterAction actionSet = MonsterActions.GetRaceByPm(actorRace, appearance);
                                    MonsterActions.ActionInfo die = actionSet.ActDie;
                                    if (die.Frames > 0)
                                    {
                                        int frameTimeMs = die.FrameTimeMs > 0 ? die.FrameTimeMs : 0;
                                        int progress = frameTimeMs > 0 && elapsedMs >= 0 ? (int)(elapsedMs / frameTimeMs) : 0;
                                        if (progress >= die.Frames)
                                            progress = die.Frames - 1;
                                        if (progress < 0)
                                            progress = 0;

                                        const int WallLeftBrokenEffectBase = 224;
                                        const int WallRightBrokenEffectBase = 240;
                                        int effectBase = appearance == 901 ? WallLeftBrokenEffectBase : WallRightBrokenEffectBase;
                                        int effectIndex = effectBase + progress;

                                        if (TryGetActorTextureWithPivot(monArchivePath, effectIndex, out D3D11Texture2D wallEffTex, out (short Px, short Py) wallEffPivot))
                                        {
                                            _spriteBatch.SetBlendMode(SpriteBlendMode.Additive);
                                            _spriteBatch.Draw(
                                                wallEffTex,
                                                new DrawingRectangle(baseX + wallEffPivot.Px, baseY + wallEffPivot.Py, wallEffTex.Width, wallEffTex.Height));
                                            _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);
                                        }
                                    }
                                }
                            }
                            else if (actorRace == 99 && actor.Action == Grobal2.SM_NOWDEATH)
                            {
                                MonsterActions.MonsterAction actionSet = MonsterActions.GetRaceByPm(actorRace, appearance);
                                MonsterActions.ActionInfo die = actionSet.ActDie;
                                if (die.Frames > 0)
                                {
                                    int frameTimeMs = die.FrameTimeMs > 0 ? die.FrameTimeMs : 0;
                                    int progress = frameTimeMs > 0 && elapsedMs >= 0 ? (int)(elapsedMs / frameTimeMs) : 0;
                                    if (progress >= die.Frames)
                                        progress = die.Frames - 1;
                                    if (progress < 0)
                                        progress = 0;

                                    const int DoorDeathEffectBase = 120;
                                    int effectIndex = DoorDeathEffectBase + progress;
                                    if (TryGetActorTextureWithPivot(monArchivePath, effectIndex, out D3D11Texture2D doorEffTex, out (short Px, short Py) doorEffPivot))
                                    {
                                        _spriteBatch.SetBlendMode(SpriteBlendMode.Additive);
                                        _spriteBatch.Draw(
                                            doorEffTex,
                                            new DrawingRectangle(baseX + doorEffPivot.Px, baseY + doorEffPivot.Py, doorEffTex.Width, doorEffTex.Height));
                                        _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);
                                    }
                                }
                            }

                            if (actor.Action == Grobal2.SM_NOWDEATH && actorRace is 14 or 15 or 17 or 53)
                            {
                                MonsterActions.MonsterAction actionSet = MonsterActions.GetRaceByPm(actorRace, appearance);
                                MonsterActions.ActionInfo die = actionSet.ActDie;
                                if (die.Frames > 0)
                                {
                                    int frameTimeMs = die.FrameTimeMs > 0 ? die.FrameTimeMs : 0;
                                    int progress = frameTimeMs > 0 && elapsedMs >= 0 ? (int)(elapsedMs / frameTimeMs) : 0;
                                    if (progress >= die.Frames)
                                        progress = die.Frames - 1;
                                    if (progress < 0)
                                        progress = 0;

                                    const int DeathEffectBase = 340;
                                    string? mon3Path = GetMonFilePath(3);
                                    if (mon3Path != null &&
                                        TryGetActorTextureWithPivot(mon3Path, DeathEffectBase + progress, out D3D11Texture2D deathEffTex, out (short Px, short Py) deathEffPivot))
                                    {
                                        _spriteBatch.SetBlendMode(SpriteBlendMode.Additive);
                                        _spriteBatch.Draw(
                                            deathEffTex,
                                            new DrawingRectangle(baseX + deathEffPivot.Px, baseY + deathEffPivot.Py, deathEffTex.Width, deathEffTex.Height));
                                        _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);
                                    }
                                }
                            }

                            if (magic8Path != null && appearance is 703 or 704 or 705 or 706 or 707 or 708)
                            {
                                int effectIndex = -1;
                                if (actor.Action == Grobal2.SM_DIGUP)
                                {
                                    int baseIndex = appearance switch
                                    {
                                        703 => 220,
                                        705 => 230,
                                        707 => 240,
                                        _ => -1
                                    };

                                    if (baseIndex >= 0)
                                    {
                                        MonsterActions.MonsterAction actionSet = MonsterActions.GetRaceByPm(actorRace, appearance);
                                        MonsterActions.ActionInfo death = actionSet.ActDeath;
                                        if (death.Frames > 0)
                                        {
                                            int frameTimeMs = death.FrameTimeMs > 0 ? death.FrameTimeMs : 0;
                                            int progress = frameTimeMs > 0 && elapsedMs >= 0 ? (int)(elapsedMs / frameTimeMs) : 0;
                                            if (progress >= death.Frames)
                                                progress = death.Frames - 1;
                                            if (progress < 0)
                                                progress = 0;
                                            effectIndex = baseIndex + progress;
                                        }
                                    }
                                }
                                else if (actor.Action == Grobal2.SM_NOWDEATH)
                                {
                                    int baseIndex = appearance switch
                                    {
                                        703 or 704 => 1970,
                                        705 or 706 => 1980,
                                        707 or 708 => 1990,
                                        _ => -1
                                    };

                                    if (baseIndex >= 0)
                                    {
                                        MonsterActions.MonsterAction actionSet = MonsterActions.GetRaceByPm(actorRace, appearance);
                                        MonsterActions.ActionInfo die = actionSet.ActDie;
                                        if (die.Frames > 0)
                                        {
                                            int frameTimeMs = die.FrameTimeMs > 0 ? die.FrameTimeMs : 0;
                                            int progress = frameTimeMs > 0 && elapsedMs >= 0 ? (int)(elapsedMs / frameTimeMs) : 0;
                                            if (progress >= die.Frames)
                                                progress = die.Frames - 1;
                                            if (progress < 0)
                                                progress = 0;
                                            effectIndex = baseIndex + progress;
                                        }
                                    }
                                }

                                if (effectIndex >= 0 &&
                                    TryGetActorTextureWithPivot(magic8Path, effectIndex, out D3D11Texture2D effTex, out (short Px, short Py) effPivot))
                                {
                                    _spriteBatch.SetBlendMode(SpriteBlendMode.Additive);
                                    _spriteBatch.Draw(
                                        effTex,
                                        new DrawingRectangle(baseX + effPivot.Px, baseY + effPivot.Py, effTex.Width, effTex.Height));
                                    _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);
                                }
                            }

                            if (actorRace == 33 && actor.Action == Grobal2.SM_HIT)
                            {
                                MonsterActions.MonsterAction actionSet = MonsterActions.GetRaceByPm(actorRace, appearance);
                                MonsterActions.ActionInfo critical = actionSet.ActCritical;

                                long t = Math.Max(0, elapsedMs);
                                int criticalFrameTimeMs = critical.FrameTimeMs > 0 ? critical.FrameTimeMs : 0;

                                if (criticalFrameTimeMs > 0)
                                {
                                    int hitFrame = (int)(t / criticalFrameTimeMs);
                                    if (hitFrame >= 5)
                                    {
                                        const int EffectFrameTimeMs = 62;
                                        const int EffectFrames = 10;

                                        long effectElapsed = t - (5L * criticalFrameTimeMs);
                                        int effectFrame = (int)(effectElapsed / EffectFrameTimeMs);
                                        if (effectFrame is >= 0 and < EffectFrames)
                                        {
                                            int effectIndex = 100 + effectFrame;
                                            if (TryGetActorTextureWithPivot(monArchivePath, effectIndex, out D3D11Texture2D effTex, out (short Px, short Py) effPivot))
                                            {
                                                _spriteBatch.SetBlendMode(SpriteBlendMode.Additive);
                                                _spriteBatch.Draw(
                                                    effTex,
                                                    new DrawingRectangle(baseX + effPivot.Px, baseY + effPivot.Py, effTex.Width, effTex.Height));
                                                _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);
                                            }
                                        }
                                    }
                                }
                            }

                            if (TryGetGhostShipHitEffectKey(actor, elapsedMs, out string hitArchivePath, out int hitIndex) &&
                                TryGetActorTextureWithPivot(hitArchivePath, hitIndex, out D3D11Texture2D hitTex, out (short Px, short Py) hitPivot))
                            {
                                _spriteBatch.SetBlendMode(SpriteBlendMode.Additive);
                                _spriteBatch.Draw(
                                    hitTex,
                                    new DrawingRectangle(baseX + hitPivot.Px, baseY + hitPivot.Py, hitTex.Width, hitTex.Height));
                                _spriteBatch.SetBlendMode(SpriteBlendMode.AlphaBlend);
                            }

                            if (_showActorNames && !selected && !string.IsNullOrEmpty(actor.UserName))
                            {
                                Color4 color = ToColor4(Mir2ColorTable.GetArgb(actor.NameColor));
                                float nameY = sayY + 30;
                                QueueActorName(actor, anchorX, nameY, color);
                            }

                             DrawActorDamagePopup(actor, anchorX, baseY + monPivot.Py);
                             DrawActorHealthBar(recogId, actor, anchorX, sayY, showHpNumberConfig, showRedHpLabelConfig);
                             continue;
                         }
                     }
                }

                DrawActorHealthBar(recogId, actor, anchorX, sayY, showHpNumberConfig, showRedHpLabelConfig);

                if (_showActorNames && !selected && !string.IsNullOrEmpty(actor.UserName))
                {
                    byte race = FeatureCodec.Race(actor.Feature);
                    Color4 color = race == Grobal2.RCC_MERCHANT ? new Color4(0, 1, 0, 1) : ToColor4(Mir2ColorTable.GetArgb(actor.NameColor));
                    float nameY = sayY + 30;
                    QueueActorName(actor, anchorX, nameY, color);
                }
            }

            if (focusSet)
            {
                bool explore = focusActor.ItemExplore && focusActor.Action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH;
                byte focusRace = FeatureCodec.Race(focusActor.Feature);
                Color4 focusColor = focusRace == Grobal2.RCC_MERCHANT
                    ? new Color4(0, 1, 0, 1)
                    : ToColor4(Mir2ColorTable.GetArgb(focusActor.NameColor));
                QueueFocusActorName(focusActor, focusSayX, focusSayY, focusColor, explore);
            }

            while (magicEffDrawIndex < magicEffDraws.Count)
                DrawMagicEff(magicEffDraws[magicEffDrawIndex++]);

            _spriteBatch.SetBlendMode(SpriteBlendMode.Additive);

            IReadOnlyCollection<LoopNormalEffect> loopNormalEffects = _world.LoopNormalEffects;
            foreach (LoopNormalEffect effect in loopNormalEffects)
            {
                if (!NormalEffectAtlas.TryGetInfo(effect.Type, out MagicEffectArchiveRef archive, out int startIndex, out int frames, out int frameTimeMs))
                    continue;

                string? archivePath = ResolveEffectArchivePath(archive);
                if (archivePath == null)
                    continue;

                if (frames <= 0 || frameTimeMs <= 0)
                    continue;

                long elapsed = nowMs - effect.StartMs;
                if (elapsed < 0)
                    elapsed = 0;

                int effectFrame = (int)((elapsed / frameTimeMs) % frames);
                if (effectFrame < 0)
                    effectFrame = 0;

                int idx = startIndex + effectFrame;

                int x = effect.X;
                int y = effect.Y;
                if ((uint)x >= (uint)_map.Width || (uint)y >= (uint)_map.Height)
                    continue;

                if (x < left - 4 || x > right + 4 || y < top - 4 || y > bottom + 40)
                    continue;

                int baseX = ((x - left) * unitX) + defx;
                int baseY = ((y - top - 1) * unitY) + defy;

                if (TryGetActorTextureWithPivot(archivePath, idx, out D3D11Texture2D tex, out (short Px, short Py) pivot))
                {
                    _spriteBatch.Draw(
                        tex,
                        new DrawingRectangle(baseX + pivot.Px, baseY + pivot.Py, tex.Width, tex.Height));
                }
            }

            IReadOnlyList<NormalEffect> normalEffects = _world.NormalEffects;
            for (int i = 0; i < normalEffects.Count; i++)
            {
                NormalEffect effect = normalEffects[i];

                if (!NormalEffectAtlas.TryGetInfo(effect.Type, out MagicEffectArchiveRef archive, out int startIndex, out int frames, out int frameTimeMs))
                    continue;

                string? archivePath = ResolveEffectArchivePath(archive);
                if (archivePath == null)
                    continue;

                if (frames <= 0 || frameTimeMs <= 0)
                    continue;

                long elapsed = nowMs - effect.StartMs;
                if (elapsed < 0)
                    elapsed = 0;

                long total = (long)frames * frameTimeMs;
                if (total > 0 && elapsed >= total)
                    continue;

                int effectFrame = frameTimeMs > 0 ? (int)(elapsed / frameTimeMs) : 0;
                if (effectFrame >= frames)
                    effectFrame = frames - 1;
                if (effectFrame < 0)
                    effectFrame = 0;

                int idx = startIndex + effectFrame;

                int x = effect.X;
                int y = effect.Y;
                if ((uint)x >= (uint)_map.Width || (uint)y >= (uint)_map.Height)
                    continue;

                if (x < left - 4 || x > right + 4 || y < top - 4 || y > bottom + 40)
                    continue;

                int baseX = ((x - left) * unitX) + defx;
                int baseY = ((y - top - 1) * unitY) + defy;

                if (TryGetActorTextureWithPivot(archivePath, idx, out D3D11Texture2D tex, out (short Px, short Py) pivot))
                {
                    _spriteBatch.Draw(
                        tex,
                        new DrawingRectangle(baseX + pivot.Px, baseY + pivot.Py, tex.Width, tex.Height));
                }
            }

            int mapMagicSelfX = 0;
            int mapMagicSelfY = 0;
            if (_world.TryGetMyself(out ActorMarker mapMagicSelf))
            {
                mapMagicSelfX = mapMagicSelf.X;
                mapMagicSelfY = mapMagicSelf.Y;
            }
            else if (_world.MapCenterSet)
            {
                mapMagicSelfX = _world.MapCenterX;
                mapMagicSelfY = _world.MapCenterY;
            }

            IReadOnlyList<MapMagicEffect> magicEffects = _world.MapMagicEffects;
            for (int i = 0; i < magicEffects.Count; i++)
            {
                MapMagicEffect effect = magicEffects[i];

                int effectNumber = effect.EffectNumber;
                if (effectNumber <= 0)
                    continue;

                if (!MapMagicEffectAtlas.TryGetInfo(effectNumber, effect.EffectType, mapMagicSelfX, mapMagicSelfY, out MagicEffectArchiveRef archive, out int startIndex, out int frames, out int frameTimeMs))
                    continue;

                string? archivePath = ResolveEffectArchivePath(archive);
                if (archivePath == null)
                    continue;

                long elapsed = nowMs - effect.StartMs;
                if (elapsed < 0)
                    elapsed = 0;

                if (frames <= 0 || frameTimeMs <= 0)
                    continue;

                long total = (long)frames * frameTimeMs;
                if (total > 0 && elapsed >= total)
                    continue;

                int effectFrame = (int)(elapsed / frameTimeMs);
                if (effectFrame >= frames)
                    effectFrame = frames - 1;
                if (effectFrame < 0)
                    effectFrame = 0;

                int idx = startIndex + effectFrame;

                int x = effect.X;
                int y = effect.Y;
                if ((uint)x >= (uint)_map.Width || (uint)y >= (uint)_map.Height)
                    continue;

                if (x < left - 4 || x > right + 4 || y < top - 4 || y > bottom + 40)
                    continue;

                int baseX = ((x - left) * unitX) + defx;
                int baseY = ((y - top - 1) * unitY) + defy;

                if (TryGetActorTextureWithPivot(archivePath, idx, out D3D11Texture2D tex, out (short Px, short Py) pivot))
                {
                    _spriteBatch.Draw(
                        tex,
                        new DrawingRectangle(baseX + pivot.Px, baseY + pivot.Py, tex.Width, tex.Height));
                }
            }

            IReadOnlyCollection<LoopScreenEffect> loopScreenEffects = _world.LoopScreenEffects;
            foreach (LoopScreenEffect effect in loopScreenEffects)
            {
                if (!NormalEffectAtlas.TryGetInfo(effect.Type, out MagicEffectArchiveRef archive, out int startIndex, out int frames, out int frameTimeMs))
                    continue;

                string? archivePath = ResolveEffectArchivePath(archive);
                if (archivePath == null)
                    continue;

                if (frames <= 0 || frameTimeMs <= 0)
                    continue;

                long elapsed = nowMs - effect.StartMs;
                if (elapsed < 0)
                    elapsed = 0;

                int effectFrame = (int)((elapsed / frameTimeMs) % frames);
                if (effectFrame < 0)
                    effectFrame = 0;

                int idx = startIndex + effectFrame;

                int px = effect.X;
                int py = effect.Y;
                if (px <= 0 && py <= 0)
                {
                    px = w / 2;
                    py = h / 2;
                }

                if (TryGetActorTextureWithPivot(archivePath, idx, out D3D11Texture2D tex, out (short Px, short Py) pivot))
                {
                    _spriteBatch.Draw(
                        tex,
                        new DrawingRectangle(px + pivot.Px, py + pivot.Py, tex.Width, tex.Height));
                }
            }

            IReadOnlyList<StruckEffect> struckEffects = _world.StruckEffects;
            for (int i = 0; i < struckEffects.Count; i++)
            {
                StruckEffect effect = struckEffects[i];

                if (!_world.TryGetActor(effect.ActorId, out ActorMarker actor))
                    continue;

                if (!StruckEffectAtlas.TryGetInfo(effect.Type, out MagicEffectArchiveRef archive, out int startIndex, out int frames, out int frameTimeMs))
                    continue;

                string? archivePath = ResolveEffectArchivePath(archive);
                if (archivePath == null)
                    continue;

                if (frames <= 0 || frameTimeMs <= 0)
                    continue;

                long elapsed = nowMs - effect.StartMs;
                if (elapsed < 0)
                    elapsed = 0;

                long total = (long)frames * frameTimeMs;
                if (total > 0 && elapsed >= total)
                    continue;

                int effectFrame = frameTimeMs > 0 ? (int)(elapsed / frameTimeMs) : 0;
                if (effectFrame >= frames)
                    effectFrame = frames - 1;
                if (effectFrame < 0)
                    effectFrame = 0;

                int idx = startIndex + effectFrame;

                long actorElapsedMs = (nowTimestamp - actor.ActionStartTimestamp) * 1000 / Stopwatch.Frequency;
                float rx = actor.X;
                float ry = actor.Y;

                if (MirDirection.IsMoveAction(actor.Action))
                {
                    (int moveFrames, int moveFrameTimeMs) = GetActorMoveTiming(effect.ActorId, actor);
                    long totalMs = (long)moveFrames * moveFrameTimeMs;
                    if (totalMs > 0 && actorElapsedMs >= 0 && actorElapsedMs < totalMs)
                    {
                        float t = Math.Clamp(actorElapsedMs / (float)totalMs, 0f, 1f);
                        rx = actor.FromX + ((actor.X - actor.FromX) * t);
                        ry = actor.FromY + ((actor.Y - actor.FromY) * t);
                    }
                }

                int ax = actor.X;
                int ay = actor.Y;
                if ((uint)ax >= (uint)_map.Width || (uint)ay >= (uint)_map.Height)
                    continue;

                if (rx < left - 4 || rx > right + 4 || ry < top - 4 || ry > bottom + 40)
                    continue;

                int baseX = (int)Math.Round(((rx - left) * unitX) + defx);
                int baseY = (int)Math.Round(((ry - top - 1) * unitY) + defy);

                if (TryGetActorTextureWithPivot(archivePath, idx, out D3D11Texture2D tex, out (short Px, short Py) pivot))
                {
                    _spriteBatch.Draw(
                        tex,
                        new DrawingRectangle(baseX + pivot.Px, baseY + pivot.Py, tex.Width, tex.Height));
                }
            }

            _spriteBatch.End();

            passStats = _spriteBatch.Stats;
            drawCalls += passStats.DrawCalls;
            textureBinds += passStats.TextureBinds;
            sprites += passStats.Sprites;
            scissorChanges += passStats.ScissorChanges;

            if (_world.ViewFog && !_world.ForceNotViewFog && _world.DarkLevel is > 0 and < 30)
            {
                if (_fogLightMapTexture == null ||
                    _fogLightMapTexture.RenderTargetView == null ||
                    _fogLightMapTexture.Width != w ||
                    _fogLightMapTexture.Height != h)
                {
                    _fogLightMapTexture?.Dispose();
                    _fogLightMapTexture = D3D11Texture2D.CreateRenderTarget(frame.Device, w, h);
                }

                D3D11Texture2D fogMap = _fogLightMapTexture!;

                Color4 fogBaseColor = _world.DarkLevel == 1
                    ? new Color4(15f / 255f, 15f / 255f, 15f / 255f, 1f)
                    : new Color4(85f / 255f, 85f / 255f, 85f / 255f, 1f);

                frame.Context.OMSetRenderTargets(fogMap.RenderTargetView!, null);
                frame.Context.RSSetViewport(new Viewport(0, 0, w, h));
                frame.Context.ClearRenderTargetView(fogMap.RenderTargetView!, fogBaseColor);

                var fogView = D3D11ViewTransform.Create(new DrawingSize(w, h), new DrawingSize(w, h), D3D11ScaleMode.None);

                _spriteBatch.Begin(frame.Context, fogView, SpriteSampler.Point, SpriteBlendMode.SourceColorAdd);

                bool TryGetFogLightTexture(int light, out D3D11Texture2D texture)
                {
                    texture = null!;

                    int index = Math.Clamp(light, 0, _fogLightTextures.Length - 1);
                    if (_fogLightTextureLoadFailed[index])
                        return false;

                    if (_fogLightTextures[index] is { } cached)
                    {
                        texture = cached;
                        return true;
                    }

                    string fileName = $"lig0{(char)('a' + index)}.dat";
                    string? path = TryResolveDataFilePath(dataDir, fileName);
                    if (path == null)
                        return false;

                    try
                    {
                        LightDatFile mask = LightDatFile.Read(path);
                        byte[] bgra = mask.ToBgra32();
                        texture = D3D11Texture2D.CreateFromBgra32(frame.Device, bgra, mask.Width, mask.Height);
                        _fogLightTextures[index] = texture;
                        return true;
                    }
                    catch
                    {
                        _fogLightTextureLoadFailed[index] = true;
                        return false;
                    }
                }

                void DrawFogLight(int light, int baseX, int baseY)
                {
                    if (!TryGetFogLightTexture(light, out D3D11Texture2D tex))
                        return;

                    int x = baseX - ((tex.Width - unitX) / 2);
                    int y = baseY - ((tex.Height - unitY) / 2) - 5;
                    _spriteBatch.Draw(tex, new DrawingRectangle(x, y, tex.Width, tex.Height));
                }

                int mapLightLeft = Math.Max(0, left - 5);
                int mapLightRight = Math.Min(_map.Width - 1, right + 5);
                int mapLightTop = Math.Max(0, top - 4);
                int mapLightBottom = Math.Min(_map.Height - 1, bottom + 35);

                for (int y = mapLightTop; y <= mapLightBottom; y++)
                {
                    for (int x = mapLightLeft; x <= mapLightRight; x++)
                    {
                        MirMapCell cell = _map.GetCell(x, y);
                        if (cell.Light > 0)
                            DrawFogLight(cell.Light, ((x - left) * unitX) + defx, ((y - top - 1) * unitY) + defy);
                    }
                }

                foreach ((int actorId, ActorMarker actor) in _world.Actors)
                {
                    int light = actor.ChrLight;
                    if (!actor.IsMyself && light <= 0)
                        continue;

                    long actorElapsedMs = (nowTimestamp - actor.ActionStartTimestamp) * 1000 / Stopwatch.Frequency;
                    float rx = actor.X;
                    float ry = actor.Y;

                    if (MirDirection.IsMoveAction(actor.Action))
                    {
                        (int moveFrames, int moveFrameTimeMs) = GetActorMoveTiming(actorId, actor);
                        long totalMs = (long)moveFrames * moveFrameTimeMs;
                        if (totalMs > 0 && actorElapsedMs >= 0 && actorElapsedMs < totalMs)
                        {
                            float t = Math.Clamp(actorElapsedMs / (float)totalMs, 0f, 1f);
                            rx = actor.FromX + ((actor.X - actor.FromX) * t);
                            ry = actor.FromY + ((actor.Y - actor.FromY) * t);
                        }
                    }

                    int ax = actor.X;
                    int ay = actor.Y;
                    if ((uint)ax >= (uint)_map.Width || (uint)ay >= (uint)_map.Height)
                        continue;

                    if (rx < left - 4 || rx > right + 4 || ry < top - 4 || ry > bottom + 40)
                        continue;

                    int baseX = (int)Math.Round(((rx - left) * unitX) + defx);
                    int baseY = (int)Math.Round(((ry - top - 1) * unitY) + defy);

                    DrawFogLight(light, baseX, baseY);
                }

                static int GetMagicEffectLight(int effectNumber)
                {
                    
                    return effectNumber switch
                    {
                        63 or 100 or 101 or 121 or 122 => 3,
                        _ => 0
                    };
                }

                IReadOnlyList<MagicEffInstance> fogMagicEffs = _world.MagicEffs;
                for (int i = 0; i < fogMagicEffs.Count; i++)
                {
                    MagicEffInstance effect = fogMagicEffs[i];
                    int light = GetMagicEffectLight(effect.EffectNumber);
                    if (light <= 0)
                        continue;

                    long elapsed = nowMs - effect.StartMs;
                    if (elapsed < 0)
                        elapsed = 0;

                    int travelDurationMs = effect.TravelDurationMs;
                    if (travelDurationMs < 0)
                        travelDurationMs = 0;

                    float toX = effect.ToX;
                    float toY = effect.ToY;
                    if (effect.TargetActorId != 0 && _world.TryGetActor(effect.TargetActorId, out ActorMarker target))
                    {
                        long targetElapsedMs = (nowTimestamp - target.ActionStartTimestamp) * 1000 / Stopwatch.Frequency;
                        float targetX = target.X;
                        float targetY = target.Y;

                        if (MirDirection.IsMoveAction(target.Action))
                        {
                            (int moveFrames, int moveFrameTimeMs) = GetActorMoveTiming(effect.TargetActorId, target);
                            long totalMs = (long)moveFrames * moveFrameTimeMs;
                            if (totalMs > 0 && targetElapsedMs >= 0 && targetElapsedMs < totalMs)
                            {
                                float t = Math.Clamp(targetElapsedMs / (float)totalMs, 0f, 1f);
                                targetX = target.FromX + ((target.X - target.FromX) * t);
                                targetY = target.FromY + ((target.Y - target.FromY) * t);
                            }
                        }

                        toX = targetX;
                        toY = targetY;
                    }

                    MagicEffTimelineInfo timeline = MagicEffTimeline.Get(effect.EffectType);
                    bool drawFlight = timeline.HasFlight && travelDurationMs > 0 && elapsed < travelDurationMs;

                    float rx;
                    float ry;
                    if (drawFlight)
                    {
                        float t = Math.Clamp(elapsed / (float)travelDurationMs, 0f, 1f);
                        rx = effect.FromX + ((toX - effect.FromX) * t);
                        ry = effect.FromY + ((toY - effect.FromY) * t);
                    }
                    else
                    {
                        rx = toX;
                        ry = toY;
                    }

                    int ax = (int)Math.Round(rx);
                    int ay = (int)Math.Round(ry);
                    if ((uint)ax >= (uint)_map.Width || (uint)ay >= (uint)_map.Height)
                        continue;

                    if (rx < left - 4 || rx > right + 4 || ry < top - 4 || ry > bottom + 40)
                        continue;

                    int baseX = (int)Math.Round(((rx - left) * unitX) + defx);
                    int baseY = (int)Math.Round(((ry - top - 1) * unitY) + defy);
                    DrawFogLight(light, baseX, baseY);
                }

                _spriteBatch.End();

                passStats = _spriteBatch.Stats;
                drawCalls += passStats.DrawCalls;
                textureBinds += passStats.TextureBinds;
                sprites += passStats.Sprites;
                scissorChanges += passStats.ScissorChanges;

                frame.Context.OMSetRenderTargets(frame.RenderTargetView, null);
                frame.Context.RSSetViewport(new Viewport(0, 0, frame.BackBufferSize.Width, frame.BackBufferSize.Height));

                _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.Multiply);
                _spriteBatch.Draw(fogMap, new DrawingRectangle(0, 0, w, h));
                _spriteBatch.End();

                passStats = _spriteBatch.Stats;
                drawCalls += passStats.DrawCalls;
                textureBinds += passStats.TextureBinds;
                sprites += passStats.Sprites;
                scissorChanges += passStats.ScissorChanges;
            }
        }

        _mapEffectAction++;
        combinedStats = new SpriteBatchStats(drawCalls, textureBinds, sprites, scissorChanges);
        return true;
    }

    private void EnsureMapTilePrefetchQueue(DrawingSize logicalSize, string resourceRoot, string dataDir, int left, int right, int top, int bottom)
    {
        if (!_mapTilePrefetchDirty &&
            logicalSize == _mapTilePrefetchLogicalSize &&
            string.Equals(resourceRoot, _mapTilePrefetchResourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool resourceChanged = !string.Equals(resourceRoot, _mapTilePrefetchResourceRoot, StringComparison.OrdinalIgnoreCase);

        _mapTilePrefetchDirty = false;
        _mapTilePrefetchLogicalSize = logicalSize;
        _mapTilePrefetchResourceRoot = resourceRoot;

        _mapTilePrefetchQueue.Clear();
        _mapTilePrefetchSet.Clear();
        _mapWilPrefetchQueue.Clear();
        _mapWilPrefetchSet.Clear();
        if (resourceChanged)
            _dataFileExists.Clear();

        BuildMapTilePrefetchQueue(dataDir, left, right, top, bottom);
    }

    private void BuildMapTilePrefetchQueue(string dataDir, int left, int right, int top, int bottom)
    {
        if (_map == null || (_dataTextureCache == null && _wilTextureCache == null))
            return;

        int xStart = left - 2;
        int xEnd = right + 1;
        int yStart = top - 1;
        int yEnd = bottom + 1;

        string?[] tilesPaths = new string?[256];
        string?[] smTilesPaths = new string?[256];
        string?[] objectsPaths = new string?[256];

        string? GetTilesPath(byte unit)
        {
            int idx = unit;
            string? path = tilesPaths[idx];
            if (path != null)
                return path.Length == 0 ? null : path;

            path = TryResolveTilesArchivePath(dataDir, unit);
            tilesPaths[idx] = path ?? string.Empty;
            return path;
        }

        string? GetSmTilesPath(byte unit)
        {
            int idx = unit;
            string? path = smTilesPaths[idx];
            if (path != null)
                return path.Length == 0 ? null : path;

            path = TryResolveSmTilesArchivePath(dataDir, unit);
            smTilesPaths[idx] = path ?? string.Empty;
            return path;
        }

        string? GetObjectsPath(byte unit)
        {
            int idx = unit;
            string? path = objectsPaths[idx];
            if (path != null)
                return path.Length == 0 ? null : path;

            path = TryResolveObjectsArchivePath(dataDir, unit);
            objectsPaths[idx] = path ?? string.Empty;
            return path;
        }

	        void EnqueuePrefetch(string archivePath, int imageIndex)
	        {
	            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
	                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase) ||
	                archivePath.EndsWith(".wzl", StringComparison.OrdinalIgnoreCase))
	            {
                if (_wilTextureCache == null)
                    return;

                var key = new WilImageKey(archivePath, imageIndex);
                if (_wilTextureCache.TryGet(key, out _) || _wilImageCache.TryGetImage(key, out _))
                    return;

                EnqueueMapTilePrefetch(key);
                return;
            }

            if (_dataTextureCache == null)
                return;

            var dataKey = new PackDataImageKey(archivePath, imageIndex);
            if (_dataTextureCache.TryGet(dataKey, out _) || _packDataImageCache.TryGetImage(dataKey, out _))
                return;

            EnqueueMapTilePrefetch(dataKey);
        }

        for (int y = yStart; y <= yEnd; y++)
        {
            if ((uint)y >= (uint)_map.Height)
                continue;

            for (int x = xStart; x <= xEnd; x++)
            {
                if ((uint)x >= (uint)_map.Width)
                    continue;

                MirMapCell cell = _map.GetCell(x, y);

                if (((x | y) & 1) == 0)
                {
                    int imgNumber = cell.BkIndex;
                    if (imgNumber > 0)
                    {
                        imgNumber--;
                        string? tilesPath = GetTilesPath(cell.Tiles);
                        if (tilesPath != null)
                            EnqueuePrefetch(tilesPath, imgNumber);
                    }
                }

                int midNumber = cell.MidIndex;
                if (midNumber > 0)
                {
                    midNumber--;
                    string? smTilesPath = GetSmTilesPath(cell.SmTiles);
                    if (smTilesPath != null)
                        EnqueuePrefetch(smTilesPath, midNumber);
                }
            }
        }

        if (TryResolveObjectsArchivePath(dataDir, unit: 0) == null)
            return;

        const int longHeightImage = 35;
        int objXStart = left - 2;
        int objXEnd = right + 2;
        int objYStart = top;
        int objYEnd = bottom + longHeightImage;

        int aniCount = (int)(Environment.TickCount64 / 50);

        for (int y = objYStart; y <= objYEnd; y++)
        {
            if ((uint)y >= (uint)_map.Height)
                continue;

            for (int x = objXStart; x <= objXEnd; x++)
            {
                if ((uint)x >= (uint)_map.Width)
                    continue;

                MirMapCell cell = _map.GetCell(x, y);
                int frIndex = cell.FrIndex;
                if (frIndex <= 0)
                    continue;

                string? objectsPath = GetObjectsPath(cell.Area);
                if (objectsPath == null)
                    continue;

                int ani = cell.AniFrame;
                if ((ani & 0x80) != 0)
                    ani &= 0x7F;

                int imgNumber = frIndex;
                if (ani > 0)
                {
                    int aniTick = cell.AniTick;
                    int total = ani + (ani * aniTick);
                    if (total > 0)
                        imgNumber += (aniCount % total) / (1 + aniTick);
                }

                if ((cell.DoorOffset & 0x80) != 0 && (cell.DoorIndex & 0x7F) > 0)
                    imgNumber += cell.DoorOffset & 0x7F;

                imgNumber--;
                if (imgNumber < 0)
                    continue;

                EnqueuePrefetch(objectsPath, imgNumber);
            }
        }
    }

    private void EnqueueMapTilePrefetch(PackDataImageKey key)
    {
        if (_mapTilePrefetchSet.Add(key))
            _mapTilePrefetchQueue.Enqueue(key);
    }

    private void EnqueueMapTilePrefetch(WilImageKey key)
    {
        if (_mapWilPrefetchSet.Add(key))
            _mapWilPrefetchQueue.Enqueue(key);
    }

    private void PumpMapTilePrefetch(int maxPerFrame)
    {
        if (maxPerFrame <= 0)
            return;

        if (_mapTilePrefetchQueue.Count == 0 && _mapWilPrefetchQueue.Count == 0)
            return;

        int scheduled = 0;
        while (scheduled < maxPerFrame && (_mapTilePrefetchQueue.Count > 0 || _mapWilPrefetchQueue.Count > 0))
        {
            if (_mapWilPrefetchQueue.Count > 0)
            {
                WilImageKey key = _mapWilPrefetchQueue.Dequeue();
                _ = _wilImageCache.GetImageAsyncFullPath(key.WilPath, key.ImageIndex);
                scheduled++;
                continue;
            }

            PackDataImageKey dataKey = _mapTilePrefetchQueue.Dequeue();
            _ = _packDataImageCache.GetImageAsyncFullPath(dataKey.DataPath, dataKey.ImageIndex);
            scheduled++;
        }
    }

    private static string GetResourceRootFromDataDir(string dataDir)
    {
        if (string.IsNullOrWhiteSpace(dataDir))
            return AppContext.BaseDirectory;

        string trimmed = Path.TrimEndingDirectorySeparator(dataDir);
        if (string.Equals(Path.GetFileName(trimmed), "Data", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(trimmed) ?? trimmed;

        return trimmed;
    }

    private bool FileExistsCached(string fullPath)
    {
        if (_dataFileExists.TryGetValue(fullPath, out bool exists))
            return exists;

        exists = File.Exists(fullPath);
        _dataFileExists[fullPath] = exists;
        return exists;
    }

    private string? TryResolveFilePath(string baseDir, string resourceRoot, string fileName)
    {
        MirFileCandidates candidates = MirFilePathResolver.GetCandidates(baseDir, resourceRoot, fileName);

        if (candidates.First is { } p0 && FileExistsCached(p0))
            return p0;
        if (candidates.Second is { } p1 && FileExistsCached(p1))
            return p1;
        if (candidates.Third is { } p2 && FileExistsCached(p2))
            return p2;
        if (candidates.Fourth is { } p3 && FileExistsCached(p3))
            return p3;

        return null;
    }

    private string? TryResolveDataFilePath(string dataDir, string fileName)
    {
        string baseDir = AppContext.BaseDirectory;
        string resourceRoot = GetResourceRootFromDataDir(dataDir);
        return TryResolveFilePath(baseDir, resourceRoot, fileName);
    }

    private string? TryResolveArchiveFilePath(string dataDir, string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName))
            return null;

        string name = baseName.Replace('/', '\\').Trim();
        if (name.Length == 0)
            return null;

        string? Resolve(string candidate)
            => TryResolveDataFilePath(dataDir, candidate) ??
               TryResolveDataFilePath(dataDir, Path.ChangeExtension(candidate, ".wzl")) ??
               TryResolveDataFilePath(dataDir, Path.ChangeExtension(candidate, ".wil")) ??
               TryResolveDataFilePath(dataDir, Path.ChangeExtension(candidate, ".wis")) ??
               TryResolveDataFilePath(dataDir, Path.ChangeExtension(candidate, ".data"));

        
        string? resolved = Resolve(name);
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        
        
        if (name.Equals("WMain", StringComparison.OrdinalIgnoreCase))
            return Resolve("Prguse");
        if (name.Equals("WMain2", StringComparison.OrdinalIgnoreCase))
            return Resolve("Prguse2");
        if (name.Equals("WMain3", StringComparison.OrdinalIgnoreCase))
            return Resolve("Prguse3");

        return null;
    }

    private string? TryResolveTilesArchivePath(string dataDir, byte unit)
    {
        int unitNumber = unit;
        string baseName = unitNumber == 0 ? "Tiles" : $"Tiles{unitNumber + 1}";
        return TryResolveArchiveFilePath(dataDir, baseName);
    }

    private string? TryResolveSmTilesArchivePath(string dataDir, byte unit)
    {
        int unitNumber = unit;
        string baseName = unitNumber == 0 ? "SmTiles" : $"SmTiles{unitNumber + 1}";
        return TryResolveArchiveFilePath(dataDir, baseName);
    }

    private string? TryResolveObjectsArchivePath(string dataDir, byte unit)
    {
        int unitNumber = unit;
        string baseName = unitNumber == 0 ? "Objects" : $"Objects{unitNumber + 1}";
        return TryResolveArchiveFilePath(dataDir, baseName);
    }

    private void EnsureMapLoaded()
    {
        if (_mapLoadAttempted)
            return;

        _mapLoadAttempted = true;

        if (string.IsNullOrWhiteSpace(_mapPath))
            return;

        string mapPath = _mapPath.Trim();
        if (!File.Exists(mapPath))
        {
            string? resolved = TryResolveMapFilePath(mapPath, allowNew: true);
            if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                mapPath = resolved;
        }

        try
        {
            _map = MirMapFile.Open(mapPath);
            AppendLog($"[map] loaded {mapPath} {_map.Width}x{_map.Height} fmt={_map.Format} cell={_map.CellSizeBytes}");

            _world.SetMapCenter(
                x: Math.Clamp(_map.Width / 2, 0, Math.Max(0, _map.Width - 1)),
                y: Math.Clamp(_map.Height / 2, 0, Math.Max(0, _map.Height - 1)));
            InvalidateMapTilePrefetch();
        }
        catch (Exception ex)
        {
            AppendLog($"[map] load error: {ex.GetType().Name}: {ex.Message}");
            _map = null;
        }
    }

    private void RunLogicFrame(long frameStart, long nowMs)
    {
        lock (_logicSync)
        {
            if (_transitionStage != _session.Stage)
            {
                _transitionStagePrev = _transitionStage;
                _transitionStage = _session.Stage;
                _transitionStageEnterMs = nowMs;
            }

            _soundManager.Tick(nowMs);
            EnsureMapLoaded();
            if (!_reconnectInProgress)
                _serverMessagePump.Pump(frameStart);
            _inventoryPendingSystem.Tick(nowMs);
            _miniMapSystem.Tick(nowMs);
            _boxSystem.Tick();
            TickRefine(nowMs);

            AutoReconnectSystem.AutoReconnectAction action = _autoReconnectSystem.Tick(
                reconnectInProgress: _reconnectInProgress,
                messagePumpEnabled: _serverMessagePump.Enabled,
                stage: _session.Stage,
                isConnected: _session.IsConnected,
                lastRunGateHost: _lastRunGateHost,
                lastRunGatePort: _lastRunGatePort,
                nowMs: nowMs);

            if (action.Kind == AutoReconnectSystem.AutoReconnectActionKind.Disconnect)
            {
                AddChatLine("[net] Connection lost, returning to login.", new Color4(1.0f, 0.35f, 0.35f, 1f));
                _ = DisconnectAsync(DisconnectBehavior.PromptLogin);
            }
            else if (action.Kind == AutoReconnectSystem.AutoReconnectActionKind.Reconnect)
            {
                CancellationToken reconnectToken = _loginCts?.Token ?? CancellationToken.None;
                _ = StartReconnectAsync(action.Host!, action.Port, reconnectToken, source: "auto");
            }

            HandleLoginStageDisconnect(nowMs);
            _sceneManager.Tick(frameStart, nowMs);

            CancellationToken token = _loginCts?.Token ?? CancellationToken.None;
            TickDuraWarning(nowMs, token);
            TickAutoHit(nowMs, token);
            TickHoldMove(nowMs, token);

            if (CanSendNextMoveCommand(nowTimestamp: Stopwatch.GetTimestamp()))
            {
                _autoMoveSendSystem.Pump(
                    stage: _session.Stage,
                    mapLoaded: _map != null,
                    mapCenterSet: _world.MapCenterSet,
                    curX: _world.MapCenterX,
                    curY: _world.MapCenterY,
                    nowMs: nowMs,
                    isWalkable: _isCurrentMapWalkable,
                    token);
            }
        }
    }

    private bool CanSendNextMoveCommand(long nowTimestamp)
    {
        if (!_autoMoveSystem.Active)
            return true;

        if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
            return true;

        if (!_world.MyselfRecogIdSet || _world.MyselfRecogId == 0)
            return true;

        if (!_world.TryGetActor(_world.MyselfRecogId, out ActorMarker self))
            return true;

        if (!MirDirection.IsMoveAction(self.Action))
            return true;

        MirActionTiming.MoveTiming timing = GetActorMoveTiming(_world.MyselfRecogId, self);
        if (timing.Frames <= 0 || timing.FrameTimeMs <= 0)
            return true;

        long totalMs = (long)timing.Frames * timing.FrameTimeMs;
        if (totalMs <= 0)
            return true;

        if (nowTimestamp <= self.ActionStartTimestamp)
            return false;

        long elapsedMs = (nowTimestamp - self.ActionStartTimestamp) * 1000 / Stopwatch.Frequency;
        if (elapsedMs < 0)
            return false;

        return elapsedMs >= totalMs;
    }

    private void TickDuraWarning(long nowMs, CancellationToken token)
    {
        if (!_duraWarning)
            return;

        if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
            return;

        if (_duraWarningNextMs == 0)
            _duraWarningNextMs = nowMs + 60_000;

        if (nowMs < _duraWarningNextMs)
            return;

        _duraWarningNextMs = nowMs + 60_000;

        if (!_world.MyselfRecogIdSet)
            return;

        if (_world.TryGetMyself(out ActorMarker self) && self.Action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH or Grobal2.SM_SKELETON)
            return;

        const ushort lowDuraThreshold = 1500;

        static bool IsIgnoredDuraItem(ClientItem item)
        {
            byte stdMode = item.S.StdMode;
            return stdMode is 7 or 25;
        }

        foreach (ClientItem item in _world.UseItems.Values)
        {
            if (item.MakeIndex == 0 || string.IsNullOrWhiteSpace(item.NameString))
                continue;

            if (IsIgnoredDuraItem(item))
                continue;

            if (item.Dura > 0 && item.Dura < lowDuraThreshold)
                AddChatLine($"[耐久] {item.NameString} 快坏了 ({item.Dura}/{item.DuraMax})", new Color4(0.55f, 0.95f, 0.55f, 1f));
        }

        if (_world.HeroActorIdSet && _world.HeroActorId != 0)
        {
            if (_world.TryGetActor(_world.HeroActorId, out ActorMarker hero) && hero.Action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH or Grobal2.SM_SKELETON)
                return;

            foreach (ClientItem item in _world.HeroUseItems.Values)
            {
                if (item.MakeIndex == 0 || string.IsNullOrWhiteSpace(item.NameString))
                    continue;

                if (IsIgnoredDuraItem(item))
                    continue;

                if (item.Dura > 0 && item.Dura < lowDuraThreshold)
                    AddChatLine($"[耐久][英雄] {item.NameString} 快坏了 ({item.Dura}/{item.DuraMax})", new Color4(0.55f, 0.95f, 0.55f, 1f));
            }
        }
    }

    private void TickAutoHit(long nowMs, CancellationToken token)
    {
        if (_autoHitTargetRecogId == 0)
            return;

        if (_chatInputActive)
            return;

        if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame || !_world.MapCenterSet)
        {
            _autoHitTargetRecogId = 0;
            return;
        }

        if (!_world.TryGetActor(_autoHitTargetRecogId, out ActorMarker target) || target.IsMyself)
        {
            _autoHitTargetRecogId = 0;
            return;
        }

        if (target.Action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH or Grobal2.SM_SKELETON)
        {
            _autoHitTargetRecogId = 0;
            return;
        }

        int race = FeatureCodec.Race(target.Feature);
        if (race == Grobal2.RCC_MERCHANT)
        {
            _autoHitTargetRecogId = 0;
            return;
        }

        if (race == 0 && !_autoAttack && !IsShiftKeyDown())
            return;

        int myX = _world.MapCenterX;
        int myY = _world.MapCenterY;
        int dx = target.X - myX;
        int dy = target.Y - myY;

        if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1)
        {
            byte dir;
            if (dx != 0 || dy != 0)
            {
                dir = MirDirection.GetFlyDirection(myX, myY, target.X, target.Y);
            }
            else if (_world.TryGetMyself(out ActorMarker myself))
            {
                dir = (byte)(myself.Dir & 7);
            }
            else
            {
                return;
            }

            _basicHitSystem.TrySend(myX, myY, dir, _autoHitTargetRecogId, token);
            return;
        }

        MirMapFile? map = _map;
        if (map == null)
            return;

        if (_autoMoveSystem.Active)
            return;

        if (nowMs < _autoChaseNextStartMs)
            return;

        _autoChaseNextStartMs = nowMs + 500;

        _autoMoveStartSystem.TryStartAutoMove(
            stage: _session.Stage,
            mapLoaded: true,
            world: _world,
            requestedX: target.X,
            requestedY: target.Y,
            mapWidth: map.Width,
            mapHeight: map.Height,
            isWalkable: _isCurrentMapWalkable,
            wantsRun: false,
            nowMs: nowMs,
            token: token);
    }

    private void TickHoldMove(long nowMs, CancellationToken token)
    {
        if (!_holdMoveActive)
            return;

        if (_chatInputActive)
            return;

        if (_sceneManager.CurrentId != MirSceneId.Play ||
            _session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame ||
            _map == null ||
            !_world.MapCenterSet)
        {
            _holdMoveActive = false;
            return;
        }

        if (_bagWindowVisible ||
            _stateWindowVisible ||
            _settingsWindowVisible ||
            _world.MerchantDialogOpen ||
            _mallWindowVisible ||
            _ybDealSystem.Visible ||
            _world.BoxOpen ||
            _world.BookOpen ||
            _world.RefineOpen ||
            _treasureDialogSystem.Visible ||
            _itemDialogSystem.Visible ||
            _bindDialogSystem.Visible)
        {
            _holdMoveActive = false;
            return;
        }

        if (nowMs - _holdMoveStartMs < HoldMoveStartDelayMs)
            return;

        if (_holdMoveLastUpdateMs != 0 && nowMs - _holdMoveLastUpdateMs < HoldMoveUpdateIntervalMs)
            return;

        if (!TryGetLogicalPoint(new System.Drawing.Point(_lastMouseClientX, _lastMouseClientY), out Vector2 logical))
            return;

        const int bottomUiHeight = 251;
        int bottomTop = Math.Max(0, _lastLogicalSize.Height - bottomUiHeight);
        if (logical.Y >= bottomTop)
            return;

        if (!TryResolveMapCell(logical, out int mapX, out int mapY))
            return;

        if (mapX == _holdMoveLastMapX && mapY == _holdMoveLastMapY)
            return;

        _holdMoveLastMapX = mapX;
        _holdMoveLastMapY = mapY;
        _holdMoveLastUpdateMs = nowMs;

        MirMapFile? map = _map;
        if (map == null)
            return;

        if (_autoMoveStartSystem.TryStartAutoMove(
                stage: _session.Stage,
                mapLoaded: true,
                world: _world,
                requestedX: mapX,
                requestedY: mapY,
                mapWidth: map.Width,
                mapHeight: map.Height,
                isWalkable: _isCurrentMapWalkable,
                wantsRun: _holdMoveWantsRun,
                nowMs: nowMs,
                token: token))
        {
            _autoHitTargetRecogId = 0;
            _targetingSystem.ClearSelected();
        }
    }

    private void HandleLoginStageDisconnect(long nowMs)
    {
        if (_session.Stage == MirSessionStage.Idle || _session.IsConnected)
        {
            _loginStageDisconnectStartMs = 0;
            return;
        }

        if (_session.Stage is not (MirSessionStage.SelectCountry or MirSessionStage.SelectServer or MirSessionStage.SelectCharacter))
        {
            _loginStageDisconnectStartMs = 0;
            return;
        }

        if (_loginStageDisconnectStartMs == 0)
        {
            _loginStageDisconnectStartMs = nowMs;
            return;
        }

        const long disconnectThresholdMs = 1500;
        if (nowMs - _loginStageDisconnectStartMs < disconnectThresholdMs)
            return;

        _loginStageDisconnectStartMs = 0;
        AddChatLine("[net] Disconnected during login, returning to login.", new Color4(1.0f, 0.35f, 0.35f, 1f));
        _ = DisconnectAsync(DisconnectBehavior.PromptLogin);
    }

    private void DecodeClientParamStr()
    {
        try
        {
            string param = _txtClientParam.Text.Trim();
            if (string.IsNullOrWhiteSpace(param))
            {
                ApplyStartupInfo(CreateDefaultStartupInfo(), "[startup] default (empty ClientParamStr)", logUi: true);
                return;
            }

            ApplyStartupInfo(MirStartupInfo.DecodeClientParamStr(param), "[startup] decoded ok", logUi: true);
        }
        catch (Exception ex)
        {
            _startup = null;
            _startupScreenApplied = false;
            _lblStartup.Text = $"ClientParamStr: decode failed - {ex.Message}";
            AppendLog($"[startup] decode failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private MirStartupInfo EnsureStartupInfo(string source)
    {
        if (_startup != null)
            return _startup;

        MirStartupInfo info = CreateDefaultStartupInfo();
        ApplyStartupInfo(info, $"[startup] {source}", logUi: true);
        return info;
    }

    private void ApplyStartupInfo(MirStartupInfo startup, string source, bool logUi)
    {
        _startup = startup;
        _startupScreenApplied = false;

        _lblStartup.Text = $"LoginGate: {_startup.ServerAddress}:{_startup.ServerPort}  ServerName: {_startup.ServerName}  ResDir: {_startup.ResourceDir}";
        _renderControl.VSync = _startup.WaitVBlank;

        string lscfgPath = Path.Combine(AppContext.BaseDirectory, "lscfg.ini");
        if (!File.Exists(lscfgPath))
            lscfgPath = Path.Combine(Environment.CurrentDirectory, "lscfg.ini");
        if (TryReadIniBool(lscfgPath, "Setup", "MIRCLIENT_VSYNC", out bool vsync) ||
            TryReadIniBool(lscfgPath, "Setup", "VSync", out vsync) ||
            TryReadIniBool(lscfgPath, "Setup", "WaitVBlank", out vsync))
        {
            _renderControl.VSync = vsync;
        }

        ApplyStartupScreenSettings();
        _perfOverlayText = string.Empty;
        _soundManager.SetResourceRoot(GetResourceRootDir());
        _selectedServerName = _startup.ServerName?.Trim() ?? string.Empty;
        LoadLocalConfigFiles();

        if (logUi)
            AppendLog(source);
    }

    private static MirStartupInfo CreateDefaultStartupInfo() =>
        new(
            ServerName: string.Empty,
            ServerAddress: "127.0.0.1",
            ServerKey: string.Empty,
            UiPakKey: string.Empty,
            ResourceDir: @"Resource\",
            ServerPort: 7000,
            FullScreen: false,
            WaitVBlank: false,
            Use3D: false,
            Mini: true,
            ScreenWidth: 1024,
            ScreenHeight: 768,
            LocalMiniPort: 10555,
            ClientVersion: 1,
            Logo: string.Empty,
            PasswordFileName: string.Empty);

    private static bool TryReadIniBool(string filePath, string section, string key, out bool value)
    {
        value = default;

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return false;

        if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key))
            return false;

        string currentSection = string.Empty;
        foreach (string rawLine in File.ReadLines(filePath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line[0] is ';' or '#')
                continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            if (!string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                continue;

            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            string k = line[..eq].Trim();
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                continue;

            string v = line[(eq + 1)..].Trim();
            if (v.Length == 0)
                return false;

            if (v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }

            if (v == "0" || string.Equals(v, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "no", StringComparison.OrdinalIgnoreCase))
            {
                value = false;
                return true;
            }

            if (int.TryParse(v, out int n))
            {
                value = n != 0;
                return true;
            }

            return false;
        }

        return false;
    }

    private void ApplyStartupScreenSettings()
    {
        if (_startup == null)
            return;

        if (!_startupScreenApplied && _startup is { ScreenWidth: > 0, ScreenHeight: > 0 })
        {
            try
            {
                if (!_isBorderlessFullscreen)
                    ClientSize = new DrawingSize(_startup.ScreenWidth, _startup.ScreenHeight);
            }
            catch
            {
                
            }
        }

        _startupScreenApplied = true;

        if (_startup.FullScreen && !_isBorderlessFullscreen)
        {
            ToggleBorderlessFullscreen("StartupInfo");
        }
        else if (!_startup.FullScreen && _isBorderlessFullscreen)
        {
            ToggleBorderlessFullscreen("StartupInfo");
        }
    }

    private async Task AdvanceLoginFlowAsync()
    {
        if (_loginFlowInProgress)
            return;

        _loginFlowInProgress = true;
        try
        {
            switch (_session.Stage)
            {
                case MirSessionStage.Idle:
                case MirSessionStage.LoginGate:
                    await LoginGateStepAsync().ConfigureAwait(true);
                    break;
                case MirSessionStage.SelectCountry:
                case MirSessionStage.SelectServer:
                    await SelectServerStepAsync().ConfigureAwait(true);
                    break;
                case MirSessionStage.SelectCharacter:
                    await StartPlayStepAsync().ConfigureAwait(true);
                    break;
                case MirSessionStage.SelectGate:
                    AppendLog("[ui] waiting for SelGate...");
                    break;
                case MirSessionStage.RunGate:
                case MirSessionStage.InGame:
                    AppendLog("[ui] already in game.");
                    break;
                default:
                    await LoginGateStepAsync().ConfigureAwait(true);
                    break;
            }
        }
        finally
        {
            _loginFlowInProgress = false;
            UpdateLoginActionButton();
        }
    }

    private void UpdateLoginActionButton()
    {
        if (IsDisposed || Disposing)
            return;

        string sceneText = _sceneManager.CurrentId.ToString();
        _lblSceneStage.Text = $"Stage: {_session.Stage}  Scene: {sceneText}  Connected: {_session.IsConnected}";

        _btnDisconnect.Enabled = _session.Stage != MirSessionStage.Idle;

        if (_loginFlowInProgress)
        {
            _btnLogin.Enabled = false;
            return;
        }

        _btnLogin.Enabled = true;
        _btnLogin.Text = _session.Stage switch
        {
            MirSessionStage.Idle => "Login",
            MirSessionStage.LoginGate => "Login",
            MirSessionStage.SelectCountry => "Select Country",
            MirSessionStage.SelectServer => "Select Server",
            MirSessionStage.SelectGate => "Loading...",
            MirSessionStage.SelectCharacter => "Enter Game",
            MirSessionStage.RunGate => "In Game",
            MirSessionStage.InGame => "In Game",
            _ => "Login"
        };

        if (_session.Stage is MirSessionStage.SelectGate or MirSessionStage.RunGate or MirSessionStage.InGame)
            _btnLogin.Enabled = false;
    }

    private async Task LoginGateStepAsync()
    {
        MirStartupInfo startup = EnsureStartupInfo("login");

        string account = _txtAccount.Text.Trim();
        string password = _txtPassword.Text;
        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
        {
            AppendLog("[ui] account/password required.");
            return;
        }

        _loginAccount = account;
        _loginPassword = password;
        _loginCertification = 0;

        _btnLogin.Enabled = false;
        _btnDisconnect.Enabled = true;

        lock (_logicSync)
        {
            _serverMessagePump.Enabled = false;
            _serverMessagePump.Dispatcher = null;
            _dispatcher = null;
            _reconnectInProgress = false;
        }

        _loginCts?.Cancel();
        _loginCts?.Dispose();
        _loginCts = new CancellationTokenSource();
        CancellationToken token = _loginCts.Token;

        _lstServers.Items.Clear();
        _lstCharacters.Items.Clear();

        try
        {
            MirServerListResult serverList = await _session.LoginGateAsync(startup, account, password, token);
            _loginServerListRaw = serverList.ServerListRaw;
            ApplyServerListToUi(serverList.ServerListRaw, preferredServerName: startup.ServerName);
            AppendLog("[ui] login ok, please select server.");

            LoginUiSetServerList(serverList.ServerListRaw, preferredServerName: startup.ServerName);
            ShowLoginUi(LoginUiScreen.SelectServer);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            AppendLog("[ui] cancelled.");
        }
        catch (OperationCanceledException)
        {
            AppendLog("[ui] timeout.");
            AddChatLine("[login] Timeout.", new Color4(1.0f, 0.35f, 0.35f, 1f));
        }
        catch (Exception ex)
        {
            Core.Diagnostics.MirErrorLog.WriteException("LoginGateStepAsync", ex);
            AppendLog($"[ui] login failed: {ex.GetType().Name}: {ex.Message}");
            AddChatLine($"[login] Failed: {ex.Message}", new Color4(1.0f, 0.35f, 0.35f, 1f));
            await DisconnectAsync(DisconnectBehavior.PromptLogin).ConfigureAwait(true);
        }
        finally
        {
            UpdateLoginActionButton();
        }
    }

    private async Task SelectServerStepAsync()
    {
        _ = EnsureStartupInfo("select-server");

        if (!TryGetSelectedServerName(out string serverName))
        {
            AppendLog("[ui] select a server first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_loginAccount))
        {
            AppendLog("[ui] missing login account (restart login).");
            return;
        }

        _btnLogin.Enabled = false;

        if (_loginCts == null || _loginCts.IsCancellationRequested)
            _loginCts = new CancellationTokenSource();
        CancellationToken token = _loginCts.Token;

        try
        {
            MirSelectServerResult sel = await _session.SelectServerAsync(serverName, token);
            _loginCertification = sel.Certification;

            MirCharacterListResult chr = await _session.QueryCharactersAsync(sel.SelGateAddress, sel.SelGatePort, _loginAccount, sel.Certification, token);
            ApplyCharacterListToUi(chr.Characters);
            AppendLog("[ui] character list ready, please select character.");

            LoginUiSetCharacterList(chr.Characters);
            BeginLoginUiDoorOpening();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            AppendLog("[ui] cancelled.");
        }
        catch (OperationCanceledException)
        {
            AppendLog("[ui] timeout.");
            AddChatLine("[login] Timeout (select server).", new Color4(1.0f, 0.35f, 0.35f, 1f));
        }
        catch (Exception ex)
        {
            Core.Diagnostics.MirErrorLog.WriteException("SelectServerStepAsync", ex);
            AppendLog($"[ui] select server failed: {ex.GetType().Name}: {ex.Message}");
            AddChatLine($"[login] Select server failed: {ex.Message}", new Color4(1.0f, 0.35f, 0.35f, 1f));

            if (_session.Stage == MirSessionStage.SelectGate)
            {
                AddChatLine("[login] Returning to server selection...", new Color4(0.95f, 0.75f, 0.35f, 1f));
                await DisconnectAsync().ConfigureAwait(true);
                await LoginGateStepAsync().ConfigureAwait(true);
                return;
            }

            if (_session.Stage == MirSessionStage.SelectServer &&
                _session.IsConnected &&
                _startup != null &&
                !string.IsNullOrWhiteSpace(_loginServerListRaw))
            {
                LoginUiSetServerList(_loginServerListRaw, preferredServerName: _startup.ServerName);
                ShowLoginUi(LoginUiScreen.SelectServer);
                return;
            }

            await DisconnectAsync(DisconnectBehavior.PromptLogin).ConfigureAwait(true);
        }
        finally
        {
            UpdateLoginActionButton();
        }
    }

    private void SelectServerInUi(string serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return;

        string target = serverName.Trim();
        for (int i = 0; i < _lstServers.Items.Count; i++)
        {
            if (_lstServers.Items[i] is not ServerListItem item)
                continue;

            if (!string.Equals(item.Name, target, StringComparison.OrdinalIgnoreCase))
                continue;

            _lstServers.SelectedIndex = i;
            return;
        }
    }

    private async Task<bool> StartPlayStepAsync()
    {
        if (string.IsNullOrWhiteSpace(_loginAccount) || string.IsNullOrEmpty(_loginPassword))
        {
            AppendLog("[ui] missing login credentials (restart login).");
            return false;
        }

        if (_loginCertification == 0)
        {
            AppendLog("[ui] missing certification (restart login).");
            return false;
        }

        if (!TryGetSelectedCharacterName(out string characterName))
        {
            AppendLog("[ui] select a character first.");
            return false;
        }

        _selectedCharacterName = characterName.Trim();
        if (_startup != null && !string.IsNullOrWhiteSpace(_startup.ServerName))
            _selectedServerName = _startup.ServerName.Trim();
        LoadItemFilterOverridesForCurrentCharacter();
        LoadClientSetForCurrentCharacter();

        _btnLogin.Enabled = false;

        if (_loginCts == null || _loginCts.IsCancellationRequested)
            _loginCts = new CancellationTokenSource();
        CancellationToken token = _loginCts.Token;

        try
        {
            MirStartPlayResult startPlay = await _session.StartPlayAsync(_loginAccount, characterName, token);

            lock (_logicSync)
            {
                _lastRunGateHost = startPlay.RunGateAddress;
                _lastRunGatePort = startPlay.RunGatePort;
                _autoReconnectSystem.Reset();
            }

            await _session.EnterRunGateAsync(startPlay.RunGateAddress, startPlay.RunGatePort, _loginAccount, characterName, _loginCertification, token);

            _dispatcher = CreateDispatcher(token);
            _serverMessagePump.Dispatcher = _dispatcher;
            _serverMessagePump.Enabled = true;
            _serverMessagePump.Pump(Stopwatch.GetTimestamp());

            AppendLog($"[ui] entering game. RunGate={startPlay.RunGateAddress}:{startPlay.RunGatePort} Char={characterName}");
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            AppendLog("[ui] cancelled.");
        }
        catch (OperationCanceledException)
        {
            AppendLog("[ui] timeout.");
            AddChatLine("[login] Timeout (enter game).", new Color4(1.0f, 0.35f, 0.35f, 1f));
        }
        catch (Exception ex)
        {
            Core.Diagnostics.MirErrorLog.WriteException("StartPlayStepAsync", ex);
            AppendLog($"[ui] enter game failed: {ex.GetType().Name}: {ex.Message}");
            AddChatLine($"[login] Enter game failed: {ex.Message}", new Color4(1.0f, 0.35f, 0.35f, 1f));

            if (_session.Stage is MirSessionStage.SelectGate or MirSessionStage.RunGate)
            {
                if (_session.CanReconnectToSelGate)
                {
                    try
                    {
                        AddChatLine("[login] Returning to character selection...", new Color4(0.95f, 0.75f, 0.35f, 1f));
                        MirCharacterListResult chr = await _session.ReconnectToSelGateAndQueryCharactersAsync(token).ConfigureAwait(true);
                        ApplyCharacterListToUi(chr.Characters);
                        return false;
                    }
                    catch (Exception rex)
                    {
                        Core.Diagnostics.MirErrorLog.WriteException("StartPlayStepAsync.Rollback", rex);
                        AppendLog($"[ui] rollback to select character failed: {rex.GetType().Name}: {rex.Message}");
                    }
                }

                await DisconnectAsync(DisconnectBehavior.PromptLogin).ConfigureAwait(true);
            }
        }
        finally
        {
            UpdateLoginActionButton();
        }

        return false;
    }

    private bool TryGetSelectedServerName(out string serverName)
    {
        serverName = string.Empty;

        if (_lstServers.SelectedItem is ServerListItem item && !string.IsNullOrWhiteSpace(item.Name))
        {
            serverName = item.Name.Trim();
            return serverName.Length > 0;
        }

        if (_startup != null && !string.IsNullOrWhiteSpace(_startup.ServerName))
        {
            serverName = _startup.ServerName.Trim();
            return serverName.Length > 0;
        }

        return false;
    }

    private bool TryGetSelectedCharacterName(out string characterName)
    {
        characterName = string.Empty;

        string text = _txtCharacter.Text.Trim();
        if (!string.IsNullOrWhiteSpace(text))
        {
            characterName = text;
            return true;
        }

        if (_lstCharacters.SelectedItem is not string s)
            return false;

        string name = s.Trim();
        if (name.StartsWith('*'))
            name = name[1..].Trim();

        if (string.IsNullOrWhiteSpace(name))
            return false;

        characterName = name;
        return true;
    }

    private void ApplyServerListToUi(string raw, string? preferredServerName)
    {
        _lstServers.BeginUpdate();
        try
        {
            _lstServers.Items.Clear();
            IReadOnlyList<ServerListItem> servers = ParseServerListRaw(raw);
            foreach (ServerListItem s in servers)
                _lstServers.Items.Add(s);

            int selectedIndex = -1;
            if (!string.IsNullOrWhiteSpace(preferredServerName))
            {
                for (int i = 0; i < servers.Count; i++)
                {
                    if (string.Equals(servers[i].Name, preferredServerName, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            if (selectedIndex < 0 && servers.Count > 0)
                selectedIndex = 0;

            if (selectedIndex >= 0 && (uint)selectedIndex < (uint)_lstServers.Items.Count)
                _lstServers.SelectedIndex = selectedIndex;
        }
        finally
        {
            _lstServers.EndUpdate();
        }
    }

    private void ApplyCharacterListToUi(IReadOnlyList<MirCharacterInfo> characters)
    {
        _lstCharacters.BeginUpdate();
        try
        {
            _lstCharacters.Items.Clear();

            int selectedIndex = -1;
            for (int i = 0; i < characters.Count; i++)
            {
                MirCharacterInfo c = characters[i];
                string display = c.Selected ? $"* {c.Name}" : c.Name;
                _lstCharacters.Items.Add(display);
                if (selectedIndex < 0 && c.Selected)
                    selectedIndex = i;
            }

            if (selectedIndex < 0 && characters.Count > 0)
                selectedIndex = 0;

            if (selectedIndex >= 0 && (uint)selectedIndex < (uint)_lstCharacters.Items.Count)
                _lstCharacters.SelectedIndex = selectedIndex;
        }
        finally
        {
            _lstCharacters.EndUpdate();
        }
    }

    private async Task LoginAsync(bool createIfMissing)
    {
        if (_loginFlowInProgress)
            return;

        _loginFlowInProgress = true;
        UpdateLoginActionButton();

        if (_startup == null)
        {
            AppendLog("[ui] decode ClientParamStr first.");
            _loginFlowInProgress = false;
            UpdateLoginActionButton();
            return;
        }

        string account = _txtAccount.Text.Trim();
        string password = _txtPassword.Text;
        string character = _txtCharacter.Text.Trim();

        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrEmpty(password))
        {
            AppendLog("[ui] account/password required.");
            _loginFlowInProgress = false;
            UpdateLoginActionButton();
            return;
        }

        _btnLogin.Enabled = false;
        _btnDisconnect.Enabled = true;

        _serverMessagePump.Enabled = false;
        _serverMessagePump.Dispatcher = null;
        _dispatcher = null;
        _reconnectInProgress = false;

        _loginCts?.Cancel();
        _loginCts?.Dispose();
        _loginCts = new CancellationTokenSource();
        CancellationToken token = _loginCts.Token;

        bool loginSucceeded = false;

        try
        {
            var creds = new MirLoginCredentials(
                account,
                password,
                PreferredCharacterName: string.IsNullOrWhiteSpace(character) ? null : character,
                CreateIfMissing: createIfMissing);
            MirLoginResult result = await _loginScene.RunLoginAsync(_startup, creds, token);
            _selectServerScene.SetLoginResult(result);
            _selectCharacterScene.ApplyLoginResult(result);
            ApplyLoginResultToUi(result);

            loginSucceeded = true;
            AppendLog($"[ui] login flow done. RunGate={result.RunGateAddress}:{result.RunGatePort} Char={result.SelectedCharacterName}");
            _lastRunGateHost = result.RunGateAddress;
            _lastRunGatePort = result.RunGatePort;
            _autoReconnectSystem.Reset();

            _dispatcher = CreateDispatcher(token);
            _serverMessagePump.Dispatcher = _dispatcher;
            _serverMessagePump.Enabled = true;
            _serverMessagePump.Pump(Stopwatch.GetTimestamp());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            AppendLog("[ui] cancelled.");
        }
        catch (OperationCanceledException)
        {
            AppendLog("[ui] timeout.");
        }
        catch (Exception ex)
        {
            AppendLog($"[ui] login failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _loginFlowInProgress = false;
            UpdateLoginActionButton();
            _btnLogin.Enabled = true;
            _btnDisconnect.Enabled = loginSucceeded && _session.Stage is MirSessionStage.RunGate or MirSessionStage.InGame;
        }
    }

    private MirMessageDispatcher CreateDispatcher(CancellationToken cancellationToken)
    {
        var dispatcher = new MirMessageDispatcher();

        MirActMessageHandlers.Register(
            dispatcher,
            _world,
            getTickMs: () => Environment.TickCount64,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            playSfxById: id => _soundManager.PlaySfxById(id),
            playSoundFile: file => _soundManager.PlaySoundFile(file, loop: false),
            log: AppendLog);

        MirLoginMessageHandlers.Register(
            dispatcher,
            world: _world,
            getTimestamp: () => Stopwatch.GetTimestamp(),
            getTickMs: () => Environment.TickCount64,
            onLogon: (recog, x, y, dir, wl) =>
            {
                InvalidateMapTilePrefetch();

                AppendLog($"[logon] recog={recog} x={x} y={y} dir={dir} feature={wl.Param1} status={wl.Param2}");
                _session.MarkInGame();
                if (_sceneManager.CurrentId != MirSceneId.Play)
                    _sceneManager.Switch(MirSceneId.Play);
                HideLoginUi();
            },
            sendInitialQueries: () =>
            {
                int width = _startup is { ScreenWidth: > 0 } ? _startup.ScreenWidth : _lastLogicalSize.Width;
                int height = _startup is { ScreenHeight: > 0 } ? _startup.ScreenHeight : _lastLogicalSize.Height;

                if (width <= 0)
                    width = 800;
                if (height <= 0)
                    height = 600;

                int viewX = (int)Math.Round((width / 2.0) / Grobal2.UNITX) + 1;
                int viewY = (int)Math.Round((height / 2.0) / Grobal2.UNITY);

                viewX = Math.Clamp(viewX, 0, ushort.MaxValue);
                viewY = Math.Clamp(viewY, 0, ushort.MaxValue);

                _viewRangeSystem.TrySend(viewX, viewY, "[net]", cancellationToken);

                _inventoryQuerySystem.TryQueryBagItems(recogMode: 1, logPrefix: "[net]", cancellationToken);

                if (_session.IsConnected && _world.MyselfRecogIdSet && _world.MyselfRecogId != 0)
                {
                    ushort value = _hideDeathBody ? (ushort)1 : (ushort)0;
                    _ = _session.SendClientMessageAsync(Grobal2.CM_HIDEDEATHBODY, _world.MyselfRecogId, value, 0, 0, cancellationToken);
                }
            },
            startReconnect: (host, port) =>
            {
                _ = StartReconnectAsync(host, port, cancellationToken, source: "SM_RECONNECT");
            },
            disconnect: () =>
            {
                _ = DisconnectAsync(DisconnectBehavior.PromptLogin);
            },
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            log: AppendLog);

        MirActorActionMessageHandlers.Register(
            dispatcher,
            isMapMoving: () => _world.MapMoving,
            onActorAction: HandleActorAction,
            onActorSimpleAction: HandleActorSimpleAction,
            log: AppendLog);

        MirActorStatusMessageHandlers.Register(
            dispatcher,
            _world,
            isMapMoving: () => _world.MapMoving,
            getTimestamp: () => Stopwatch.GetTimestamp(),
            getTickMs: () => Environment.TickCount64,
            onStruck: (actor, damage) =>
            {
                if (damage > 0)
                    TryPlayStruckSfx(actor);
            },
            onClearObjects: () =>
            {
                _nameDrawList.Clear();
                ClearCorpseMarkers();
                _moveTimingStates.Clear();
            },
            onHideActor: HandleHideActor,
            log: AppendLog);

        MirMapMessageHandlers.Register(
            dispatcher,
            onMapChange: (identName, mapName, x, y, light) => HandleServerMapChange(identName, mapName, x, y, light),
            onShowEvent: (id, msg, x, y, type) =>
            {
                if (msg.HasValue)
                {
                    int eventParam = msg.Value.Ident;
                    int eventLevel = msg.Value.Message;
                    int dir = 0;

                    if (type is Grobal2.ET_DIGOUTZOMBI or 2)
                        dir = Math.Clamp(eventParam, 0, 7);

                    _world.ApplyShowEvent(id, x, y, type, eventParam, eventLevel, dir, nowMs: Environment.TickCount64);
                    AppendLog($"[event] SM_SHOWEVENT id={id} x={x} y={y} type={type} ident={msg.Value.Ident} msg={msg.Value.Message}");
                }
            },
            onHideEvent: id =>
            {
                _world.ApplyHideEvent(id);
                AppendLog($"[event] SM_HIDEEVENT id={id}");
            },
            log: AppendLog);

        MirMapConfigMessageHandlers.Register(
            dispatcher,
            _world,
            onMapDescription: (musicId, title) =>
            {
                _world.ApplyMapDescription(musicId, title);
                LoadWayPointForCurrentMap();
                _soundManager.PlayBgmById(musicId);
                if (!string.IsNullOrEmpty(_world.MapTitle))
                    AppendLog($"[map] desc='{_world.MapTitle}' music={musicId}");
            },
            onPlayerConfig: (hero, showFashion) =>
            {
                string who = hero ? "[英雄] " : string.Empty;
                AddChatLine($"{who}{(showFashion ? "开启" : "关闭")} 时装外显！", new Color4(0.92f, 0.92f, 0.92f, 1));
            },
            onPlayerConfigTooFast: () =>
            {
                AddChatLine("切换时装外显操作太快了！", new Color4(1.0f, 0.3f, 0.3f, 1));
            },
            onItemShow: (id, x, y, looks, name) => _world.ApplyItemShow(id, x, y, looks, name, nowMs: Environment.TickCount64),
            onItemHide: id => _world.ApplyItemHide(id),
            isMapMoving: () => _world.MapMoving,
            log: AppendLog);

        MirMiniMapMessageHandlers.Register(
            dispatcher,
            _world,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            log: AppendLog);

        MirTitleMessageHandlers.Register(
            dispatcher,
            _world,
            onChat: (text, success) =>
            {
                var color = success
                    ? new Color4(0.55f, 0.95f, 0.55f, 1)
                    : new Color4(1.0f, 0.3f, 0.3f, 1);
                AddChatLine(text, color);
            },
            log: AppendLog);

        MirHeroMessageHandlers.Register(
            dispatcher,
            _world,
            onChat: text =>
            {
                
                bool success = text.StartsWith("[成功]");
                var color = success
                    ? new Color4(0.55f, 0.95f, 0.55f, 1)
                    : new Color4(1.0f, 0.3f, 0.3f, 1);
                AddChatLine(text, color);
            },
            log: AppendLog);

        MirInventoryMessageHandlers.Register(
            dispatcher,
            _world,
            onBagItemsApplied: TryRestoreBagLayoutFromCache,
            onChat: (message, channel) => AddChatLine(message, new Color4(0.92f, 0.92f, 0.92f, 1)),
            onDropResponse: _inventoryPendingSystem.ClearDropPending,
            onBagItemAdded: HandleBagItemAdded,
            getHeroBagExchangePending: _inventoryPendingSystem.GetHeroBagExchangePending,
            clearHeroBagExchangePending: _inventoryPendingSystem.ClearHeroBagExchangePending,
            getItemSumCountPending: _inventoryPendingSystem.GetItemSumCountPending,
            clearItemSumCountPending: _inventoryPendingSystem.ClearItemSumCountPending,
            log: AppendLog);

        MirEquipmentMessageHandlers.Register(
            dispatcher,
            _world,
            getEatPending: _inventoryPendingSystem.GetEatPending,
            clearEatPending: _inventoryPendingSystem.ClearEatPending,
            restoreEatPendingToBag: _inventoryPendingSystem.RestoreEatPendingToBag,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            getUseItemPending: _inventoryPendingSystem.GetUseItemPending,
            clearUseItemPending: _inventoryPendingSystem.ClearUseItemPending,
            onRefineOpen: HandleRefineOpen,
            log: AppendLog);

        MirMerchantMessageHandlers.Register(
            dispatcher,
            _world,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            getItemSumCountPending: _inventoryPendingSystem.GetItemSumCountPending,
            clearItemSumCountPending: _inventoryPendingSystem.ClearItemSumCountPending,
            log: AppendLog);

        MirGroupGuildMessageHandlers.Register(
            dispatcher,
            _world,
            log: AppendLog);

        MirChatGroupGuildMessageHandlers.Register(
            dispatcher,
            _world,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            log: AppendLog);

        MirMiscMessageHandlers.Register(
            dispatcher,
            _world,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            onPlayDiceSelect: async (merchantId, command) =>
            {
                if (merchantId <= 0)
                    return;

                string text = command?.Trim() ?? string.Empty;
                if (text.Length == 0)
                    return;

                try
                {
                    CancellationToken token = _loginCts?.Token ?? CancellationToken.None;
                    await _session.SendClientStringAsync(Grobal2.CM_MERCHANTDLGSELECT, merchantId, 0, 0, 0, text, token);
                    AppendLog($"[dice] CM_MERCHANTDLGSELECT merchant={merchantId} '{text}'");
                }
                catch (Exception ex)
                {
                    AppendLog($"[dice] CM_MERCHANTDLGSELECT send failed: {ex.GetType().Name}: {ex.Message}");
                }
            },
            log: AppendLog);

        MirMovementMessageHandlers.Register(
            dispatcher,
            _world,
            getTimestamp: () => Stopwatch.GetTimestamp(),
            getTickCount64: () => Environment.TickCount64,
            playSoundFile: file => _soundManager.PlaySoundFile(file, loop: false),
            log: AppendLog);

        MirMarketMessageHandlers.Register(
            dispatcher,
            _world,
            onMarketList: first =>
            {
                _marketSystem.HandleServerList(first);
                _bagWindowVisible = true;
                _heroBagView = false;
            },
            log: AppendLog);

        MirStallMessageHandlers.Register(
            dispatcher,
            _world,
            onUserStall: (actorId, itemCount, _) =>
            {
                bool open = itemCount > 0;
                _userStallSystem.HandleServerUserStall(itemCount);

                if (open)
                {
                    _bagWindowVisible = true;
                    _heroBagView = false;
                }
            },
            onOpenStall: (actorId, stall) =>
            {
                _stallSystem.HandleServerOpenStall(actorId, stall);
            },
            onUpdateStallItem: _stallSystem.HandleUpdateStallItemResult,
            onBuyStallItem: _stallSystem.HandleBuyStallItemResult,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            log: AppendLog);

        MirSoundMessageHandlers.Register(
            dispatcher,
            silenceSound: _soundManager.SilenceSound,
            playSoundFile: (file, loop) => _soundManager.PlaySoundFile(file, loop: loop),
            log: AppendLog);

        MirCollectExpMessageHandlers.Register(
            dispatcher,
            _world,
            log: AppendLog);

        MirQueryValueMessageHandlers.Register(
            dispatcher,
            _world,
            onPrompt: request =>
            {
                try
                {
                    BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            if (!_world.QueryValuePending)
                                return;

                            UiTextPromptButtons buttons = request.Mode is 0 or 1 ? UiTextPromptButtons.OkCancelAbort : UiTextPromptButtons.OkCancel;
                            (UiTextPromptResult result, string value) = await PromptTextAsync(
                                title: "Input",
                                prompt: request.Prompt,
                                buttons: buttons).ConfigureAwait(true);

                            if (result != UiTextPromptResult.Ok)
                            {
                                AppendLog($"[queryval] dismissed ({result})");
                                return;
                            }

                            string trimmed = value.Trim();
                            int merchantId = _world.CurrentMerchantId;
                            _queryValueSendSystem.TrySend(merchantId, trimmed, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"[queryval] prompt error: {ex.Message}");
                        }
                    }));
                }
                catch (Exception ex)
                {
                    AppendLog($"[queryval] prompt invoke error: {ex.Message}");
                }
            },
            log: AppendLog);

        MirSeriesSkillMessageHandlers.Register(
            dispatcher,
            _world,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            playSoundFile: (file, loop) => _soundManager.PlaySoundFile(file, loop: loop),
            log: AppendLog);

        MirMissionMessageHandlers.Register(
            dispatcher,
            _world,
            showMissionDialog: missionClass =>
            {
                _missionSystem.Open((int)missionClass);
                _world.ClearNewMissionPending();
  
                 _levelRankSystem.Close(logUi: false);
                 _seriesSkillSystem.Close(logUi: false);
                 _guildSystem.CloseAll(logUi: false);
  
                 AppendLog($"[ui] missions opened (server prompt class={_missionSystem.MissionClass})");
            },
            showItemDialog: (merchantId, prompt) =>
            {
                _treasureDialogSystem.Close(logUi: false);
                _bindDialogSystem.Close(restoreSelectedItem: true, logUi: false);
                _itemDialogSystem.Open(merchantId, prompt, logUi: true);

                _bagWindowVisible = true;
                _heroBagView = false;
            },
            onItemDialogSelect: (recog, param) =>
            {
                _itemDialogSystem.HandleServerSelect(recog, param, logUi: true);
            },
            openUrl: url =>
            {
                try
                {
                    if (!IsHandleCreated || IsDisposed)
                    {
                        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                        return;
                    }

                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            Forms.EmbeddedBrowserForm.ShowUrl(this, url);
                        }
                        catch
                        {
                            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                        }
                    }));
                }
                catch
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
            },
            log: AppendLog);

        MirServerConfigMessageHandlers.Register(
            dispatcher,
            _world,
            log: AppendLog);

        MirDoorMessageHandlers.Register(
            dispatcher,
            _world,
            onOpenDoor: (x, y) =>
            {
                MirMapFile? map = _map;
                if (map == null)
                    return;

                bool ok = _doorOverrideSystem.TryApplyOpenDoorOverride(
                    x,
                    y,
                    map.Width,
                    map.Height,
                    (ix, iy) => map.GetCell(ix, iy).DoorIndex);
                if (!ok)
                    return;

                _soundManager.PlaySfxById(soundId: 100, volume: 0.8f);
            },
            onCloseDoor: (x, y) =>
            {
                MirMapFile? map = _map;
                if (map == null)
                    return;

                _ = _doorOverrideSystem.TryApplyCloseDoorOverride(
                    x,
                    y,
                    map.Width,
                    map.Height,
                    (ix, iy) => map.GetCell(ix, iy).DoorIndex);
            },
            log: AppendLog);

        MirDetectItemMessageHandlers.Register(
            dispatcher,
            _world,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            log: AppendLog);

        MirTreasureMessageHandlers.Register(
            dispatcher,
            _world,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            log: AppendLog);

        MirBindItemMessageHandlers.Register(
            dispatcher,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            showBindDialog: (merchantId, unbind) =>
            {
                if (_itemDialogSystem.Visible)
                    _itemDialogSystem.Close(restoreSelectedItem: true, logUi: false);

                _treasureDialogSystem.Close(logUi: false);
                _bindDialogSystem.Open(merchantId, unbind, logUi: true);

                _bagWindowVisible = true;
                _heroBagView = false;
            },
            onBindResult: code => _bindDialogSystem.HandleServerResult(code, logUi: true),
            log: AppendLog);

        MirSuitShineMessageHandlers.Register(
            dispatcher,
            _world,
            log: AppendLog);

        MirYbDealMessageHandlers.Register(
            dispatcher,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            showDealDialog: dialog => _ybDealSystem.ShowDialog(dialog, logUi: true),
            log: AppendLog);

        MirBoxMessageHandlers.Register(
            dispatcher,
            _world,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            log: AppendLog);

        MirEffectMessageHandlers.Register(
            dispatcher,
            _world,
            playSoundFile: (file, loop) => _soundManager.PlaySoundFile(file, loop: loop),
            addNormalEffect: (type, x, y) =>
            {
                if (type == 0)
                    return;

                long nowMs = Environment.TickCount64;
                _world.AddNormalEffect(type, x, y, nowMs);

                switch (type)
                {
                    case 3:
                        TryPlaySfx(48, x, y);
                        break;
                    case 4:
                        TryPlaySfx(8301, x, y);
                        break;
                    case 5:
                        TryPlaySfx(8206, x, y);
                        break;
                    case 6:
                        TryPlaySfx(8302, x, y);
                        break;
                    case 7:
                        TryPlaySfx(8208, x, y);
                        break;
                    case 8:
                        _soundManager.PlaySoundFile(@"Wav\dare-death.wav", loop: false);
                        break;
                    case >= 41 and <= 43:
                        _soundManager.PlaySoundFile(@"Wav\Flashbox.wav", loop: false);
                        break;
                    case >= 75 and <= 83:
                        if (type >= 78)
                            _soundManager.PlaySoundFile(@"Wav\newysound-mix.wav", loop: false);
                        break;
                    case 84:
                        _soundManager.PlaySoundFile(@"Wav\HeroLogin.wav", loop: false);
                        break;
                    case 85:
                        _soundManager.PlaySoundFile(@"Wav\HeroLogout.wav", loop: false);
                        break;
                }
            },
            log: AppendLog);

        MirMallMessageHandlers.Register(
            dispatcher,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            log: AppendLog);

        MirAttackModeMessageHandlers.Register(
            dispatcher,
            _world,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            log: AppendLog);

        MirInternalPowerMessageHandlers.Register(
            dispatcher,
            _world,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            log: AppendLog);

        MirChangeFaceMessageHandlers.Register(
            dispatcher,
            _world,
            getTimestamp: () => Stopwatch.GetTimestamp(),
            log: AppendLog);

        MirPasswordMessageHandlers.Register(
            dispatcher,
            togglePasswordMode: () =>
            {
                _passwordInputMode = !_passwordInputMode;
                if (_passwordInputMode)
                    OpenChatInput(string.Empty);
                else
                    CloseChatInput(clear: true);
                return _passwordInputMode;
            },
            log: AppendLog);

        MirHealthGaugeMessageHandlers.Register(
            dispatcher,
            _world,
            getTickMs: () => Environment.TickCount64,
            log: AppendLog);

        MirSystemNoticeMessageHandlers.Register(
            dispatcher,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            addMarquee: AddSystemMarqueeMessage,
            addBottomRight: AddSystemBottomRightMessage,
            log: AppendLog);

        
        dispatcher.Register(Grobal2.SM_SENDNOTICE, packet =>
        {
            string rawText = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            string text = FormatLoginNoticeText(rawText);
            AppendLog(string.IsNullOrWhiteSpace(text) ? "[notice] SM_SENDNOTICE" : $"[notice] SM_SENDNOTICE '{text}'");

            if (_session.Stage == MirSessionStage.RunGate)
                ShowLoginNoticeModal(text, cancellationToken);
            else if (!string.IsNullOrWhiteSpace(text))
                AddChatLine($"[notice] {text}", new Color4(0.98f, 0.92f, 0.75f, 1f));

            return ValueTask.CompletedTask;
        });

        
        
        dispatcher.Register(Grobal2.SM_VERSION_FAIL, packet =>
        {
            int crc1 = packet.Header.Recog;
            int crc2 = (packet.Header.Tag << 16) | packet.Header.Param;

            static bool IsAllChars(string data, char value)
            {
                if (string.IsNullOrEmpty(data))
                    return false;

                foreach (char c in data)
                {
                    if (c != value)
                        return false;
                }

                return true;
            }

            bool fileCheck = _session.Stage is MirSessionStage.RunGate or MirSessionStage.InGame;
            if (!fileCheck)
                fileCheck = IsAllChars(packet.BodyEncoded, '<'); 

            if (fileCheck)
            {
                AppendLog($"[check] SM_VERSION_FAIL ignored (file-check) stage={_session.Stage} crc1={crc1} crc2={crc2} bodyLen={packet.BodyEncoded.Length}");
                return ValueTask.CompletedTask;
            }

            AppendLog($"[login] SM_VERSION_FAIL (disconnect) crc1={crc1} crc2={crc2} bodyLen={packet.BodyEncoded.Length}");
            AddChatLine("[login] Version mismatch, disconnecting.", new Color4(1.0f, 0.35f, 0.35f, 1f));
            _ = DisconnectAsync(DisconnectBehavior.PromptLogin);
            return ValueTask.CompletedTask;
        });

        MirUserNameMessageHandlers.Register(
            dispatcher,
            _world,
            measureHalfNameWidth: null,
            log: AppendLog);

        MirShopOfferMessageHandlers.Register(
            dispatcher,
            _world,
            log: AppendLog);

        MirAbilityExpMessageHandlers.Register(
            dispatcher,
            _world,
            addChatLine: (text, color) => AddChatLine(text, new Color4(color.R, color.G, color.B, color.A)),
            log: AppendLog);

        MirActorUpdateMessageHandlers.Register(
            dispatcher,
            _world,
            getTickMs: () => Environment.TickCount64);

        MirMagicMessageHandlers.Register(
            dispatcher,
            _world,
            log: AppendLog);

        MirDealMessageHandlers.Register(
            dispatcher,
            _world,
            getPending: _dealSystem.GetPending,
            clearPending: _dealSystem.ClearPending,
            onDealOpened: () =>
            {
                _bagWindowVisible = true;
                _heroBagView = false;
            },
            onDealClosed: _dealSystem.ClearPending,
            log: AppendLog);

        MirStorageMessageHandlers.Register(
            dispatcher,
            _world,
            log: AppendLog);

        MirHeroStateMessageHandlers.Register(
            dispatcher,
            _world,
            measureHalfNameWidth: null,
            getTimestamp: () => Stopwatch.GetTimestamp(),
            getTickMs: () => Environment.TickCount64,
            log: AppendLog);

        MirDebugMessageHandlers.Register(
            dispatcher,
            _world,
            incrementTestReceiveCount: () => ++_testReceiveCount,
            log: AppendLog);


        dispatcher.Unhandled += packet =>
            AppendLog($"[msg] unhandled Ident={packet.Header.Ident} Recog={packet.Header.Recog} Param={packet.Header.Param} Tag={packet.Header.Tag} Series={packet.Header.Series} BodyLen={packet.BodyEncoded.Length}");

        return dispatcher;
    }

    private async Task StartReconnectAsync(string host, int port, CancellationToken cancellationToken, string? source = null)
    {
        if (_reconnectInProgress)
            return;

        _reconnectInProgress = true;
        AppendLog(string.IsNullOrWhiteSpace(source)
            ? $"[net] reconnect -> {host}:{port}"
            : $"[net] {source} -> {host}:{port}");

        try
        {
            lock (_logicSync)
            {
                _lastRunGateHost = host;
                _lastRunGatePort = port;
                PrepareForReconnect(host, port);
            }
            await _session.ReconnectToRunGateAsync(host, port, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppendLog("[net] reconnect cancelled.");
        }
        catch (Exception ex)
        {
            AppendLog($"[net] reconnect failed: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _reconnectInProgress = false;
        }
    }

    private enum DisconnectBehavior
    {
        None = 0,
        PromptLogin = 1,
        ReturnToSelectCharacter = 2
    }

    private Task DisconnectAsync(DisconnectBehavior behavior = DisconnectBehavior.None) =>
        InvokeAsync(() => DisconnectCoreAsync(behavior));

    private async Task DisconnectCoreAsync(DisconnectBehavior behavior)
    {
        bool returnToSelectCharacter = behavior == DisconnectBehavior.ReturnToSelectCharacter;
        bool returned = false;

        TrySaveItemFilterOverrides();
        TrySaveWayPointForCurrentMap();
        TrySaveBagCache();

        lock (_logicSync)
        {
            _serverMessagePump.Enabled = false;
            _serverMessagePump.Dispatcher = null;
            _dispatcher = null;
            _reconnectInProgress = false;
            _autoReconnectSystem.Reset();

            PrepareForReconnect(_lastRunGateHost ?? string.Empty, _lastRunGatePort, log: false);
            _heroBagView = false;
            _miniMapSystem.Reset();
            _autoMoveSystem.Reset();
            _stallSystem.ClearState(resetBagFlags: true);
        }

        await _softCloseSystem.TrySendAsync();

        _loginCts?.Cancel();
        _loginCts?.Dispose();
        _loginCts = null;
        try
        {
            lock (_logicSync)
                _soundManager.StopBgm();

            if (returnToSelectCharacter && _session.CanReconnectToSelGate)
            {
                try
                {
                    _loginCts = new CancellationTokenSource();
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(_loginCts.Token);
                    cts.CancelAfter(TimeSpan.FromSeconds(10));

                    AddChatLine("[login] Returning to character selection...", new Color4(0.95f, 0.75f, 0.35f, 1f));
                    MirCharacterListResult chr = await _session.ReconnectToSelGateAndQueryCharactersAsync(cts.Token).ConfigureAwait(true);
                    ApplyCharacterListToUi(chr.Characters);
                    LoginUiSetCharacterList(chr.Characters);
                    ShowLoginUi(LoginUiScreen.SelectCharacter);
                    returned = true;
                    return;
                }
                catch (OperationCanceledException)
                {
                    
                }
                catch (Exception ex)
                {
                    AppendLog($"[ui] logout->select character failed: {ex.GetType().Name}: {ex.Message}");
                }
            }

            await _session.DisconnectAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"[ui] disconnect error: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            UpdateLoginActionButton();

            if (!returned && behavior == DisconnectBehavior.PromptLogin)
                BeginPromptLogin();
        }
    }

    private void OpenChatInput(string? initialText = null)
    {
        void Show()
        {
            if (IsDisposed || Disposing)
                return;

            if (LoginUiVisible)
                return;

            _chatInputActive = true;
            _uiChatInput.IsPassword = _passwordInputMode;

            if (initialText == null)
                _uiChatInput.Clear();
            else
                _uiChatInput.Set(initialText);

            _chatSendHistoryIndex = _chatSendHistory.Count;
            _chatInputFocusStartMs = Environment.TickCount64;
            try { _renderControl.ImeMode = ImeMode.On; } catch {  }
            try { _renderControl.Focus(); } catch {  }
        }

        if (InvokeRequired)
        {
            try { BeginInvoke((Action)Show); } catch {  }
            return;
        }

        Show();
    }

    private void CloseChatInput(bool clear)
    {
        void Hide()
        {
            _chatInputActive = false;
            _uiChatInput.IsPassword = _passwordInputMode;
            if (clear)
                _uiChatInput.Clear();
            _chatInputFocusStartMs = Environment.TickCount64;
            try { _renderControl.ImeMode = ImeMode.NoControl; } catch {  }
        }

        if (InvokeRequired)
        {
            try { BeginInvoke((Action)Hide); } catch {  }
            return;
        }

        Hide();
    }

    private void SubmitChatInput()
    {
        string raw = _uiChatInput.Text;
        CloseChatInput(clear: true);
        string text = string.IsNullOrWhiteSpace(raw) ? string.Empty : raw.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        const int maxHistory = 50;
        if (_chatSendHistory.Count >= maxHistory)
            _chatSendHistory.RemoveAt(0);
        _chatSendHistory.Add(text);
        _chatSendHistoryIndex = _chatSendHistory.Count;

        try
        {
            BeginInvoke(new Action(async () =>
            {
                try { await SendChatAsync(text).ConfigureAwait(true); } catch {  }
            }));
        }
        catch
        {
            
        }
    }

    private async Task SendChatAsync(string rawText)
    {
        string text = string.IsNullOrWhiteSpace(rawText) ? string.Empty : rawText.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!_world.MyselfRecogIdSet)
        {
            AppendLog("[chat] not in game yet.");
            return;
        }

        CancellationToken token = _loginCts?.Token ?? CancellationToken.None;
        if (_passwordInputMode)
        {
            _passwordInputMode = false;
            _uiChatInput.IsPassword = false;
            await _chatSendSystem.TrySendPasswordAsync(text, token).ConfigureAwait(false);
            return;
        }

        bool sent = await _chatSendSystem.TrySendSayAsync(text, token).ConfigureAwait(false);
        if (!sent)
            return;

        if (text.StartsWith('/'))
        {
            string rest = text.Length > 1 ? text[1..].TrimStart() : string.Empty;
            if (rest.Length > 0)
            {
                int split = rest.IndexOf(' ');
                string name = (split >= 0 ? rest[..split] : rest).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    _whisperName = name;
            }

            AddChatLine(text, new Color4(0.9f, 0.7f, 1.0f, 1f));
        }
    }

    private void ClearItemDrag(bool restoreRefineItemToBag = true)
    {
        if (restoreRefineItemToBag &&
            _itemDragActive &&
            _itemDragSource == ItemDragSource.Refine &&
            !_itemDragHero &&
            _itemDragItem.MakeIndex != 0)
        {
            _world.RestoreBagItem(_itemDragItem);
        }

        _itemDragActive = false;
        _itemDragSource = ItemDragSource.None;
        _itemDragSourceIndex = -1;
        _itemDragHero = false;
        _itemDragItem = default;
    }

    private bool IsStallSetupUiActive()
    {
        if (_heroBagView)
            return false;

        if (!_stallSystem.WindowVisible)
            return false;

        if (_marketSystem.Visible)
            return false;

        if (_world.DealOpen)
            return false;

        return _world.MerchantMode is not MirMerchantMode.Storage and not MirMerchantMode.GetSave;
    }

    private bool IsSpecialBagUiActive()
    {
        if (_heroBagView)
            return false;

        if (_treasureDialogSystem.Visible || _bindDialogSystem.Visible || _itemDialogSystem.Visible)
            return true;

        if (_world.MerchantMode is MirMerchantMode.Storage or MirMerchantMode.GetSave)
            return true;

        if (_world.DealOpen)
            return true;

        if (_marketSystem.Visible)
            return true;

        if (IsStallSetupUiActive())
            return true;

        return _userStallSystem.Visible && _world.UserStallOpen;
    }

    private async Task<string?> PromptStallNameAsync()
    {
        (UiTextPromptResult result, string value) = await PromptTextAsync(
            title: "Stall",
            prompt: "Stall name (GBK max 28 bytes)",
            buttons: UiTextPromptButtons.OkCancel,
            maxGbkBytes: 28).ConfigureAwait(true);

        if (result != UiTextPromptResult.Ok)
            return null;

        string trimmed = value;
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool TryParseStallPrice(string input, out byte goldType, out int price)
    {
        goldType = 4;
        price = 0;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        string s = input.Trim();

        string? typePart = null;
        string numberPart = s;

        int sep = s.IndexOfAny(new[] { ' ', ':', '\t', ',' });
        if (sep > 0)
        {
            typePart = s[..sep].Trim();
            numberPart = s[(sep + 1)..].Trim();
        }
        else if (s.StartsWith("yb", StringComparison.OrdinalIgnoreCase))
        {
            typePart = "5";
            numberPart = s[2..].TrimStart(':', ' ', '\t');
        }
        else if (s.StartsWith("y", StringComparison.OrdinalIgnoreCase))
        {
            typePart = "5";
            numberPart = s[1..].TrimStart(':', ' ', '\t');
        }
        else if (s.StartsWith("g", StringComparison.OrdinalIgnoreCase))
        {
            typePart = "4";
            numberPart = s[1..].TrimStart(':', ' ', '\t');
        }

        if (!string.IsNullOrWhiteSpace(typePart))
        {
            if (typePart.Equals("5", StringComparison.OrdinalIgnoreCase) ||
                typePart.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                typePart.Equals("yb", StringComparison.OrdinalIgnoreCase))
            {
                goldType = 5;
            }
            else if (typePart.Equals("4", StringComparison.OrdinalIgnoreCase) ||
                     typePart.Equals("g", StringComparison.OrdinalIgnoreCase))
            {
                goldType = 4;
            }
            else if (int.TryParse(typePart, out int typeInt) && typeInt is 4 or 5)
            {
                goldType = (byte)typeInt;
            }
        }

        return int.TryParse(numberPart, out price);
    }

    private Task InvokeAsync(Func<Task> action)
    {
        if (!InvokeRequired)
            return action();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            BeginInvoke(async () =>
            {
                try
                {
                    await action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
        }

        return tcs.Task;
    }

    private void AppendLog(string message)
    {
        if (IsDisposed)
            return;

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }

        _txtLog.AppendText(message);
        _txtLog.AppendText(Environment.NewLine);
    }

    private static string GetConfigDirPath() => Path.Combine(AppContext.BaseDirectory, "Config");

    private static string SanitizeFileNamePart(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);
        foreach (char c in value.Trim())
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    private string? TryGetBagCachePath()
    {
        string serverName = !string.IsNullOrWhiteSpace(_selectedServerName)
            ? _selectedServerName.Trim()
            : _startup?.ServerName?.Trim() ?? string.Empty;
        string characterName = _selectedCharacterName.Trim();
        if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(characterName))
            return null;

        string dir = GetConfigDirPath();
        string fileName = $"{SanitizeFileNamePart(serverName)}.{SanitizeFileNamePart(characterName)}.itm-plus";
        return Path.Combine(dir, fileName);
    }

    private string? TryGetClientSetIniPath()
    {
        string serverName = !string.IsNullOrWhiteSpace(_selectedServerName)
            ? _selectedServerName.Trim()
            : _startup?.ServerName?.Trim() ?? string.Empty;
        string characterName = _selectedCharacterName.Trim();
        if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(characterName))
            return null;

        string dir = GetConfigDirPath();
        string fileName = $"{SanitizeFileNamePart(serverName)}.{SanitizeFileNamePart(characterName)}.Set";
        return Path.Combine(dir, fileName);
    }

    private void LoadClientSetForCurrentCharacter()
    {
        string? path = TryGetClientSetIniPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        ClientSetIni.BasicSettings settings = ClientSetIni.LoadBasic(path);
        _showActorNames = settings.ShowActorName;
        _duraWarning = settings.DuraWarning;
        _autoAttack = settings.AutoAttack;
        _showDropItems = settings.ShowDropItems;
        _hideDeathBody = settings.HideDeathBody;

        AppendLog($"[cfg] loaded '{Path.GetFileName(path)}' (ShowActorName={_showActorNames} DuraWarning={_duraWarning} AutoAttack={_autoAttack} ShowDropItems={_showDropItems} HideDeathBody={_hideDeathBody})");
    }

    private void TrySaveClientSetForCurrentCharacter()
    {
        string? path = TryGetClientSetIniPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            ClientSetIni.SaveBasic(
                path,
                new ClientSetIni.BasicSettings(
                    ShowActorName: _showActorNames,
                    DuraWarning: _duraWarning,
                    AutoAttack: _autoAttack,
                    ShowDropItems: _showDropItems,
                    HideDeathBody: _hideDeathBody));
        }
        catch (Exception ex)
        {
            AppendLog($"[cfg] save '{Path.GetFileName(path)}' failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void TryRestoreBagLayoutFromCache()
    {
        _bagLoadedFromServer = true;

        string? path = TryGetBagCachePath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            var saved = new ClientItem[Grobal2.MAXBAGITEM];
            Span<byte> dest = MemoryMarshal.AsBytes(saved.AsSpan());
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length != dest.Length)
            {
                AppendLog($"[bag-cache] ignore '{Path.GetFileName(path)}' len={bytes.Length} expected={dest.Length}");
                return;
            }

            bytes.AsSpan().CopyTo(dest);

            if (_world.TryApplyBagLayout(saved))
                AppendLog($"[bag-cache] restored '{Path.GetFileName(path)}'");
        }
        catch (Exception ex)
        {
            AppendLog($"[bag-cache] load error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void TrySaveBagCache()
    {
        ClientItem[]? snapshot = null;
        string? path = null;

        lock (_logicSync)
        {
            if (!_bagLoadedFromServer)
                return;

            path = TryGetBagCachePath();
            if (string.IsNullOrWhiteSpace(path))
                return;

            snapshot = _world.BagSlots.ToArray();
            _bagLoadedFromServer = false;
        }

        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            fs.Write(MemoryMarshal.AsBytes(snapshot.AsSpan()));

            AppendLog($"[bag-cache] saved '{Path.GetFileName(path)}'");
        }
        catch (Exception ex)
        {
            AppendLog($"[bag-cache] save error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void LoadLocalConfigFiles()
    {
        try
        {
            string resourceRoot = GetResourceRootDir();
            string dataDir = Path.Combine(resourceRoot, "Data");

            ItemDescTable.LoadResult itemDesc = _itemDescTable.LoadFromFile(Path.Combine(dataDir, "ItemDesc.dat"));

            string mapDescFile = Path.Combine(dataDir, "MapDesc2.dat");
            if (!File.Exists(mapDescFile))
                mapDescFile = Path.Combine(dataDir, "MapDesc1.dat");

            MapDescTable.LoadResult mapDesc = _mapDescTable.LoadFromFile(mapDescFile);
            ItemFilterStore.LoadResult filter = _itemFilterStore.LoadDefaultsFromDataDir(dataDir);

            AppendLog($"[cfg] loaded: ItemDesc={itemDesc.Count} MapDesc={mapDesc.Count} ItemFilter={filter.Count}");
        }
        catch (Exception ex)
        {
            AppendLog($"[cfg] load error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private string? TryGetItemFilterOverridePath()
    {
        string serverName = !string.IsNullOrWhiteSpace(_selectedServerName)
            ? _selectedServerName.Trim()
            : _startup?.ServerName?.Trim() ?? string.Empty;
        string characterName = _selectedCharacterName.Trim();
        if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(characterName))
            return null;

        string dir = GetConfigDirPath();
        string fileName = $"{SanitizeFileNamePart(serverName)}.{SanitizeFileNamePart(characterName)}.ItemFilter.txt";
        return Path.Combine(dir, fileName);
    }

    private void LoadItemFilterOverridesForCurrentCharacter()
    {
        string? path = TryGetItemFilterOverridePath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            _itemFilterStore.ResetToDefaults();
            int applied = _itemFilterStore.LoadOverridesFromFile(path);
            if (applied > 0)
                AppendLog($"[cfg] ItemFilter overrides applied={applied}");
        }
        catch (Exception ex)
        {
            AppendLog($"[cfg] ItemFilter load error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void TrySaveItemFilterOverrides()
    {
        string? path = TryGetItemFilterOverridePath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            ItemFilterStore.SaveResult saved = _itemFilterStore.SaveOverridesToFile(path);
            if (saved.Saved)
                AppendLog($"[cfg] ItemFilter saved lines={saved.Count}");
        }
        catch (Exception ex)
        {
            AppendLog($"[cfg] ItemFilter save error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private string? TryGetWayPointIniPath()
    {
        string serverName = !string.IsNullOrWhiteSpace(_selectedServerName)
            ? _selectedServerName.Trim()
            : _startup?.ServerName?.Trim() ?? string.Empty;
        string userName = _selectedCharacterName.Trim();
        if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(userName))
            return null;

        string dir = GetConfigDirPath();
        string fileName = $"{SanitizeFileNamePart(serverName)}.{SanitizeFileNamePart(userName)}.WayPoint.txt";
        return Path.Combine(dir, fileName);
    }

    private void LoadWayPointForCurrentMap()
    {
        string mapTitle = _world.MapTitle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(mapTitle))
        {
            _wayPoints = Array.Empty<(int X, int Y)>();
            return;
        }

        string? iniPath = TryGetWayPointIniPath();
        if (string.IsNullOrWhiteSpace(iniPath))
        {
            _wayPoints = Array.Empty<(int X, int Y)>();
            return;
        }

        try
        {
            _wayPoints = WayPointIni.Load(iniPath, mapTitle);
            if (_wayPoints.Count > 0)
                AppendLog($"[cfg] WayPoint loaded map='{mapTitle}' count={_wayPoints.Count}");
        }
        catch (Exception ex)
        {
            _wayPoints = Array.Empty<(int X, int Y)>();
            AppendLog($"[cfg] WayPoint load error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void TrySaveWayPointForCurrentMap()
    {
        if (_wayPoints.Count == 0)
            return;

        string mapTitle = _world.MapTitle?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(mapTitle))
            return;

        string? iniPath = TryGetWayPointIniPath();
        if (string.IsNullOrWhiteSpace(iniPath))
            return;

        try
        {
            WayPointIni.Save(iniPath, mapTitle, _wayPoints);
            AppendLog($"[cfg] WayPoint saved map='{mapTitle}' count={_wayPoints.Count}");
        }
        catch (Exception ex)
        {
            AppendLog($"[cfg] WayPoint save error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void SetTooltip(string text, Vector2 logicalPos)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        _uiTooltipText = text;
        _uiTooltipLogicalPos = logicalPos;

        if (_textRenderer != null)
        {
            (float w, float h) = _textRenderer.MeasureText(text);
            _uiTooltipWidthBackBuffer = w;
            _uiTooltipHeightBackBuffer = h;
        }
        else
        {
            _uiTooltipWidthBackBuffer = 0f;
            _uiTooltipHeightBackBuffer = 0f;
        }
    }

    private string BuildItemTooltipText(in ClientItem item, string? headerPrefix = null)
    {
        string name = item.NameString;
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        var lines = new List<string>(18);

        string header = string.IsNullOrWhiteSpace(headerPrefix) ? name : $"{headerPrefix}: {name}";

        if (item.S.Overlap > 0)
            header = $"{header} x{item.Dura}";
        else if (item.DuraMax > 0)
            header = $"{header}  dura={item.Dura}/{item.DuraMax}";

        lines.Add(header);

        if (item.S.Binded != 0)
            lines.Add("绑定");

        AddRangeLine(lines, "AC", item.S.AC);
        AddRangeLine(lines, "MAC", item.S.MAC);
        AddRangeLine(lines, "DC", item.S.DC);
        AddRangeLine(lines, "MC", item.S.MC);
        AddRangeLine(lines, "SC", item.S.SC);

        if (item.S.NeedLevel > 0)
            lines.Add($"NeedLv: {item.S.NeedLevel}");
        if (item.S.Need != 0)
            lines.Add($"Need: {item.S.Need}");

        AppendEvaluationInfo(lines, item.S);

        if (_itemDescTable.TryGet(name, out string desc) && !string.IsNullOrWhiteSpace(desc))
            lines.Add(desc);

        return string.Join('\n', lines);

        static void AppendEvaluationInfo(List<string> lines, in ClientStdItem std)
        {
            Evaluation eva = std.Eva;

            if (eva.EvaTimesMax == 0 &&
                eva.Quality == 0 &&
                eva.BaseMax == 0 &&
                eva.AdvAbilMax == 0 &&
                eva.SpiritMax == 0 &&
                eva.AdvAbil == 0 &&
                eva.SpSkill == 0)
            {
                return;
            }

            if (eva.EvaTimesMax != 0)
            {
                if (eva.EvaTimes == 0)
                {
                    lines.Add("可鉴定");
                }
                else if (eva.EvaTimes is >= 1 and <= 8)
                {
                    string stage = GetEvaStageName(eva.EvaTimes);
                    string suffix = eva.EvaTimes < eva.EvaTimesMax ? "(仍可鉴定)" : string.Empty;
                    lines.Add($"{stage}鉴{suffix}");
                }
            }

            int stars = eva.Quality switch
            {
                >= 1 and <= 50 => 1,
                >= 51 and <= 100 => 2,
                >= 101 and <= 150 => 3,
                >= 151 and <= 200 => 4,
                >= 201 and <= 255 => 5,
                _ => 0
            };

            if (stars > 0)
                lines.Add($"品质: {new string('★', stars)} ({eva.Quality})");

            int baseMax = Math.Clamp((int)eva.BaseMax, 0, 8);
            if (baseMax > 0)
            {
                var baseLines = new List<string>(baseMax);
                for (int i = 0; i < baseMax; i++)
                {
                    string s = FormatEvaBaseAbil(eva.GetAbil(i));
                    if (!string.IsNullOrWhiteSpace(s))
                        baseLines.Add(s);
                }

                if (baseLines.Count > 0)
                {
                    lines.Add("附加基础属性:");
                    lines.AddRange(baseLines);
                }
            }

            if (eva.AdvAbilMax > 0)
            {
                var mysteryLines = new List<string>(12);
                int cnt = 0;
                for (int i = baseMax; i < 8; i++)
                {
                    EvaAbil abil = eva.GetAbil(i);
                    if (abil.Value == 0)
                        break;

                    string s = FormatEvaBaseAbil(abil);
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        mysteryLines.Add(s);
                        cnt++;
                    }
                }

                int advCount;
                foreach (string s in FormatEvaMysteryAbil(eva.AdvAbil, eva.SpSkill, std.StdMode, out advCount))
                    mysteryLines.Add(s);
                cnt += advCount;

                int missing = Math.Max(0, eva.AdvAbilMax - cnt);
                for (int i = 0; i < missing; i++)
                    mysteryLines.Add("神秘属性(待解读)");

                if (mysteryLines.Count > 0)
                {
                    lines.Add("附加神秘属性:");
                    lines.AddRange(mysteryLines);
                }
            }

            if (eva.SpiritMax > 0)
                lines.Add($"宝物灵媒: 品质{eva.SpiritQ} 灵气{eva.Spirit}/{eva.SpiritMax}");

            static string GetEvaStageName(byte evaTimes) =>
                evaTimes switch
                {
                    1 => "一",
                    2 => "二",
                    3 => "三",
                    4 => "四",
                    5 => "五",
                    6 => "六",
                    7 => "七",
                    8 => "八",
                    _ => evaTimes.ToString()
                };

            static string FormatEvaBaseAbil(EvaAbil val)
            {
                if (val.Value == 0)
                    return string.Empty;

                int v = val.Value;
                return val.Type switch
                {
                    01 => $"攻击上限 +{v}",
                    02 => $"魔法上限 +{v}",
                    03 => $"道术上限 +{v}",
                    04 => $"物防上限 +{v}",
                    05 => $"魔防上限 +{v}",
                    06 => $"准确 +{v}",
                    07 => $"敏捷 +{v}",
                    08 => $"魔法躲避 +{v * 10}",
                    09 => $"幸运 +{v}",
                    10 => $"诅咒 +{v}",
                    11 => $"攻击速度 +{v}",
                    12 => $"神圣 +{v}",
                    13 => $"魔法回复 +{v * 10}",
                    14 => $"体力回复 +{v * 10}",
                    15 => $"击杀爆率 +{v}",
                    16 => $"防爆 +{v}",
                    17 => $"吸血上限 +{v}",
                    18 => $"内力恢复 +{v}",
                    19 => $"内力上限 +{v}",
                    20 => $"内功伤害 +{v}",
                    21 => $"内功减免 +{v}",
                    22 => $"内伤等级 +{v}",
                    23 => $"暴击威力 +{v * 2}",
                    24 => $"合击威力 +{v}%",
                    25 => $"麻痹抗性 +{v}%",
                    26 => $"强身等级 +{v}",
                    27 => $"聚魔等级 +{v}",
                    28 => $"主属性 +{v}",
                    29 => $"毒物躲避 +{v}",
                    30 => $"中毒恢复 +{v}",
                    _ => $"属性[{val.Type}] +{v}"
                };
            }

            static IEnumerable<string> FormatEvaMysteryAbil(byte val, byte val2, byte stdMode, out int count)
            {
                val = (byte)(val & 0x7F);

                int localCount = 0;
                var list = new List<string>(16);

                AddFlag(val, 0x01, "八卦护身神技");
                AddFlag(val, 0x02, "战意麻痹神技");
                AddFlag(val, 0x04, "重生神技");
                AddFlag(val, 0x08, "探测神技");
                AddFlag(val, 0x10, "传送神技");
                AddFlag(val, 0x20, "麻痹神技");
                AddFlag(val, 0x40, "魔道麻痹神技");

                bool weaponLike = stdMode is 5 or 6;
                if (weaponLike)
                {
                    AddFlag(val2, 0x01, "五岳独尊特技");
                    AddFlag(val2, 0x02, "召唤巨魔特技");
                    AddFlag(val2, 0x04, "神龙附体特技");
                    AddFlag(val2, 0x08, "倚天劈地特技");
                }
                else
                {
                    AddFlag(val2, 0x01, "五岳独尊Lv+1");
                    AddFlag(val2, 0x02, "召唤巨魔Lv+1");
                    AddFlag(val2, 0x04, "神龙附体Lv+1");
                    AddFlag(val2, 0x08, "倚天劈地Lv+1");
                }

                count = localCount;
                return list;

                void AddFlag(byte flags, byte mask, string text)
                {
                    if ((flags & mask) == 0)
                        return;
                    list.Add(text);
                    localCount++;
                }
            }
        }

        static void AddRangeLine(List<string> lines, string label, int packed)
        {
            (int min, int max) = UnpackRange(packed);
            if (min == 0 && max == 0)
                return;

            if (max == 0)
                max = min;

            lines.Add(min == max ? $"{label}: {min}" : $"{label}: {min}-{max}");
        }

        static (int Min, int Max) UnpackRange(int packed)
        {
            int min = (short)(packed & 0xFFFF);
            int max = (short)((packed >> 16) & 0xFFFF);
            return (min, max);
        }
    }

    private void PrepareForReconnect(string host, int port, bool log = true)
    {
        _bagLoadedFromServer = false;
        _wayPoints = Array.Empty<(int X, int Y)>();
        _autoHitTargetRecogId = 0;
        _autoChaseNextStartMs = 0;

        _world.ResetForReconnect();
        _autoMoveSystem.Cancel();
        _nameDrawList.Clear();
        _dealSystem.ClearPending();
        ClearSystemMessages();
        ClearItemDrag(restoreRefineItemToBag: false);
        ClearRefineTakeOffPending();
        Array.Clear(_refineItems, 0, _refineItems.Length);
        _itemDialogSystem.Close(restoreSelectedItem: false, logUi: false);
        _bindDialogSystem.Reset();
        _treasureDialogSystem.Close(logUi: false);
        _ybDealSystem.Close(logUi: false);
        _marketSystem.Reset(clearWorld: false);
        _merchantDialogTopLine = 0;
        _merchantDialogLastMerchantId = 0;
        _merchantDialogLastSaying = string.Empty;
        _merchantDialogLastVisibleLines = 0;
        _merchantDialogLastTotalLines = 0;
        _guildSystem.Reset();
        _levelRankSystem.Reset();
        _commandThrottle.Reset();
        _stallSystem.CloseWindow(logUi: false);
        _userStallSystem.Close(logUi: false);
        _stallSystem.ClearState(resetBagFlags: false);

        _mapLoadAttempted = false;
        _map = null;
        _soundManager.StopBgm();

        _mapTilePrefetchQueue.Clear();
        _mapTilePrefetchSet.Clear();
        _mapWilPrefetchQueue.Clear();
        _mapWilPrefetchSet.Clear();
        _mapTilePrefetchDirty = true;

        if (log)
            AppendLog($"[net] reconnecting to {host}:{port} ...");
    }

    private bool TryHandleInGameKey(Keys keyData)
    {
        Keys keyCode = keyData & Keys.KeyCode;
        bool ctrl = (keyData & Keys.Control) != 0;
        bool alt = (keyData & Keys.Alt) != 0;
        bool shift = (keyData & Keys.Shift) != 0;
        CancellationToken token = _loginCts?.Token ?? CancellationToken.None;

        if (_chatInputActive)
        {
            if (!ctrl && !alt && keyCode == Keys.Escape)
            {
                CloseChatInput(clear: true);
                return true;
            }

            if (!ctrl && !alt && keyCode == Keys.Enter)
            {
                SubmitChatInput();
                return true;
            }

            if (!ctrl && !alt && keyCode == Keys.Back)
            {
                _uiChatInput.Backspace();
                _chatInputFocusStartMs = Environment.TickCount64;
                return true;
            }

            if (ctrl && !alt && keyCode == Keys.Up)
            {
                if (_chatSendHistory.Count == 0)
                    return true;

                if (_chatSendHistoryIndex > _chatSendHistory.Count)
                    _chatSendHistoryIndex = _chatSendHistory.Count;

                if (_chatSendHistoryIndex > 0)
                    _chatSendHistoryIndex--;

                int idx = Math.Clamp(_chatSendHistoryIndex, 0, _chatSendHistory.Count - 1);
                _uiChatInput.Set(_chatSendHistory[idx]);
                _chatInputFocusStartMs = Environment.TickCount64;
                return true;
            }

            if (ctrl && !alt && keyCode == Keys.Down)
            {
                if (_chatSendHistory.Count == 0)
                    return true;

                if (_chatSendHistoryIndex > _chatSendHistory.Count)
                    _chatSendHistoryIndex = _chatSendHistory.Count;

                if (_chatSendHistoryIndex < _chatSendHistory.Count - 1)
                    _chatSendHistoryIndex++;

                int idx = Math.Clamp(_chatSendHistoryIndex, 0, _chatSendHistory.Count - 1);
                _uiChatInput.Set(_chatSendHistory[idx]);
                _chatInputFocusStartMs = Environment.TickCount64;
                return true;
            }

            if (ctrl && !alt && keyCode == Keys.V)
            {
                try
                {
                    string clip = Clipboard.GetText();
                    if (!string.IsNullOrEmpty(clip))
                        _uiChatInput.Append(clip);
                }
                catch
                {
                    
                }

                _chatInputFocusStartMs = Environment.TickCount64;
                return true;
            }

            return false;
        }

        if (_settingsWindowVisible && !ctrl && !alt && keyCode == Keys.Escape)
        {
            _settingsWindowVisible = false;
            EndUiWindowDrag();
            AppendLog("[ui] settings closed (Esc)");
            return true;
        }

        if (!ctrl && !alt && keyCode == Keys.F12)
        {
            _settingsWindowVisible = !_settingsWindowVisible;
            EndUiWindowDrag();
            AppendLog(_settingsWindowVisible ? "[ui] settings opened (F12)" : "[ui] settings closed (F12)");
            return true;
        }

        if (ctrl && !alt && keyCode == Keys.F10)
        {
            _stateOverlayVisible = !_stateOverlayVisible;
            _stateOverlayText = string.Empty;
            _heroStateOverlayVisible = false;
            _heroStateOverlayText = string.Empty;
            AppendLog(_stateOverlayVisible ? "[ui] state overlay on (Ctrl+F10)" : "[ui] state overlay off (Ctrl+F10)");
            return true;
        }

        if (!ctrl && !alt && keyCode == Keys.F9)
        {
            ClearItemDrag();
            _heroBagView = false;
            _bagWindowVisible = !_bagWindowVisible;

            if (!_bagWindowVisible)
            {
                _treasureDialogSystem.Close(logUi: false);
                _stallSystem.CloseWindow(logUi: false);
                _userStallSystem.Close(logUi: false);

                if (_marketSystem.Visible)
                {
                    _ = _maketSystem.TrySendMarketCloseAsync(token);
                    _marketSystem.Reset(clearWorld: true);
                }
            }

            AppendLog(_bagWindowVisible ? "[ui] bag opened (F9)" : "[ui] bag closed (F9)");
            return true;
        }

        if (!ctrl && !alt && keyCode == Keys.F10)
        {
            if (_stateWindowVisible && _stateWindowPage == 0)
            {
                _stateWindowVisible = false;
                _stateMagicKeyDialogOpen = false;
                AppendLog("[ui] state closed (F10)");
                return true;
            }

            _stateWindowVisible = true;
            _stateWindowPage = 0;
            _stateMagicKeyDialogOpen = false;
            AppendLog("[ui] state opened (F10)");
            return true;
        }

        if (!ctrl && !alt && keyCode == Keys.F11)
        {
            if (_stateWindowVisible && _stateWindowPage == 3)
            {
                _stateWindowVisible = false;
                _stateMagicKeyDialogOpen = false;
                AppendLog("[ui] magic closed (F11)");
                return true;
            }

            _stateWindowVisible = true;
            _stateWindowPage = 3;
            _stateMagicPage = 0;
            _stateMagicKeyDialogOpen = false;
            AppendLog("[ui] magic opened (F11)");
            return true;
        }

        if (!ctrl && !alt && keyCode == Keys.N)
        {
            _heroStateOverlayVisible = !_heroStateOverlayVisible;
            _heroStateOverlayText = string.Empty;

            if (_heroStateOverlayVisible)
            {
                _stateOverlayVisible = false;
                _stateOverlayText = string.Empty;
            }

            AppendLog(_heroStateOverlayVisible ? "[ui] hero state overlay on (N)" : "[ui] hero state overlay off (N)");
            return true;
        }

        if (_session.Stage is MirSessionStage.RunGate or MirSessionStage.InGame)
        {
            if (_itemDialogSystem.Visible && !ctrl && !alt)
            {
                if (keyCode == Keys.Escape)
                {
                    _itemDialogSystem.Close(restoreSelectedItem: true, logUi: true);
                    return true;
                }

                if (keyCode == Keys.Enter)
                {
                    _itemDialogSystem.TrySendSelect(token);
                    return true;
                }
            }
        }

        if (_session.Stage is MirSessionStage.RunGate or MirSessionStage.InGame)
        {
            if (_bindDialogSystem.Visible && !ctrl && !alt)
            {
                if (keyCode == Keys.Escape)
                {
                    _bindDialogSystem.Close(restoreSelectedItem: true, logUi: true);
                    return true;
                }

                if (keyCode == Keys.Enter)
                {
                    _bindDialogSystem.TrySendSelect(token);
                    return true;
                }
            }

            if (_treasureDialogSystem.Visible && !ctrl && !alt)
            {
                if (keyCode == Keys.Escape)
                {
                    _treasureDialogSystem.Close(logUi: true);
                    return true;
                }

                if (keyCode is Keys.Back or Keys.Delete)
                {
                    _treasureDialogSystem.ClearSelection(logUi: true);
                    return true;
                }

                if (keyCode == Keys.Tab)
                {
                    _treasureDialogSystem.ToggleMode(logUi: true);
                    return true;
                }

                if (keyCode == Keys.Enter)
                {
                    if (shift)
                        _treasureDialogSystem.TrySendSecondary(token);
                    else
                        _treasureDialogSystem.TrySendPrimary(token);
                    return true;
                }
            }

            if (_world.MerchantDialogOpen && !ctrl && !alt)
            {
                if (keyCode == Keys.Escape)
                {
                    _world.CloseMerchantDialog();
                    _merchantDialogTopLine = 0;
                    AppendLog("[ui] merchant dialog closed");
                    return true;
                }

                int pageStep = Math.Max(1, (_merchantDialogLastVisibleLines > 0 ? _merchantDialogLastVisibleLines : 8) - 1);

                if (keyCode == Keys.PageUp)
                {
                    _merchantDialogTopLine = Math.Max(0, _merchantDialogTopLine - pageStep);
                    return true;
                }

                if (keyCode == Keys.PageDown)
                {
                    _merchantDialogTopLine += pageStep;
                    return true;
                }

                if (keyCode == Keys.Home)
                {
                    _merchantDialogTopLine = 0;
                    return true;
                }

                if (keyCode == Keys.End)
                {
                    _merchantDialogTopLine = int.MaxValue;
                    return true;
                }
            }

	            if (_ybDealSystem.Visible && !ctrl && !alt)
	            {
	                if (keyCode == Keys.Escape)
	                {
	                    _ybDealSystem.Close(logUi: true);
	                    return true;
	                }

                if (keyCode == Keys.Up && _ybDealSystem.TrySelectPrev())
                {
                    return true;
                }

                if (keyCode == Keys.Down && _ybDealSystem.TrySelectNext())
                    return true;

                if (keyCode == Keys.C)
                {
                    _ybDealSystem.TryCancelOrCancelSell(token);
                    return true;
                }

                if (keyCode == Keys.Enter)
                {
                    if (_ybDealSystem.Mode == MirYbDealDialogMode.Deal)
                        _ybDealSystem.TryBuy(token);
                    return true;
	                }
	            }

 	            if (_magicWindowVisible && !ctrl && !alt)
 	            {
 	                IReadOnlyList<ClientMagic> magics = _magicWindowHeroView ? _world.HeroMagics : _world.MyMagics;
 	                int pageSize = Math.Max(1, _magicWindowPageSize);
 	                int maxTop = Math.Max(0, magics.Count - pageSize);

	                if (keyCode is Keys.Escape or Keys.K)
	                {
	                    _magicWindowVisible = false;
	                    AppendLog("[ui] magic window closed");
	                    return true;
	                }

	                if (keyCode == Keys.H)
	                {
	                    _magicWindowHeroView = !_magicWindowHeroView;
	                    _magicWindowTopIndex = 0;
	                    AppendLog(_magicWindowHeroView ? "[ui] magic window: hero" : "[ui] magic window: self");
	                    return true;
	                }

	                if (keyCode == Keys.Up)
	                {
	                    _magicWindowTopIndex = Math.Clamp(_magicWindowTopIndex - 1, 0, maxTop);
	                    return true;
	                }

	                if (keyCode == Keys.Down)
	                {
	                    _magicWindowTopIndex = Math.Clamp(_magicWindowTopIndex + 1, 0, maxTop);
	                    return true;
	                }

	                if (keyCode == Keys.PageUp)
	                {
	                    _magicWindowTopIndex = Math.Clamp(_magicWindowTopIndex - pageSize, 0, maxTop);
	                    return true;
	                }

	                if (keyCode == Keys.PageDown)
	                {
	                    _magicWindowTopIndex = Math.Clamp(_magicWindowTopIndex + pageSize, 0, maxTop);
	                    return true;
	                }

	                if (keyCode == Keys.Home)
	                {
	                    _magicWindowTopIndex = 0;
	                    return true;
	                }

 	                if (keyCode == Keys.End)
 	                {
 	                    _magicWindowTopIndex = maxTop;
 	                    return true;
                 }
  	            }
 
                if (_stateMagicKeyDialogOpen && _stateWindowVisible && _stateWindowPage == 3 && !alt)
                {
                    if (keyCode == Keys.Escape)
                    {
                        _stateMagicKeyDialogOpen = false;
                        return true;
                    }

                    if (keyCode is Keys.Back or Keys.Delete)
                    {
                        _ = TrySetStateMagicKeyAsync(_stateMagicKeyDialogHero, _stateMagicKeyDialogMagicId, key: 0, token).AsTask();
                        _stateMagicKeyDialogOpen = false;
                        return true;
                    }

                    if (!ctrl && !shift && keyCode is >= Keys.D1 and <= Keys.D8)
                    {
                        byte key = (byte)('1' + (keyCode - Keys.D1));
                        _ = TrySetStateMagicKeyAsync(_stateMagicKeyDialogHero, _stateMagicKeyDialogMagicId, key, token).AsTask();
                        _stateMagicKeyDialogOpen = false;
                        return true;
                    }

                    if (!ctrl && !shift && keyCode is >= Keys.E and <= Keys.L)
                    {
                        byte key = (byte)('E' + (keyCode - Keys.E));
                        _ = TrySetStateMagicKeyAsync(_stateMagicKeyDialogHero, _stateMagicKeyDialogMagicId, key, token).AsTask();
                        _stateMagicKeyDialogOpen = false;
                        return true;
                    }
                }

                if (_stateWindowVisible && _stateWindowPage == 3 && !ctrl && !alt)
                {
                    IReadOnlyList<ClientMagic> magics = _heroBagView ? _world.HeroMagics : _world.MyMagics;
                    const int pageSize = 6;
                    int maxPage = magics.Count > 0 ? (magics.Count + (pageSize - 1)) / pageSize - 1 : 0;
                    maxPage = Math.Max(0, maxPage);

                    if (keyCode == Keys.PageUp)
                    {
                        _stateMagicPage = Math.Clamp(_stateMagicPage - 1, 0, maxPage);
                        return true;
                    }

                    if (keyCode == Keys.PageDown)
                    {
                        _stateMagicPage = Math.Clamp(_stateMagicPage + 1, 0, maxPage);
                        return true;
                    }

                    if (keyCode == Keys.Home)
                    {
                        _stateMagicPage = 0;
                        return true;
                    }

                    if (keyCode == Keys.End)
                    {
                        _stateMagicPage = maxPage;
                        return true;
                    }
                }

                if (keyCode == Keys.Enter && !ctrl && !alt)
                {
                    OpenChatInput();
                    return true;
                }

            if (_world.BookOpen && _world.BookPath == 1 && !ctrl && !alt)
            {
                if (keyCode == Keys.PageUp)
                {
                    _bookSystem.TryPrevPage(logUi: true);
                    return true;
                }

                if (keyCode == Keys.PageDown)
                {
                    _bookSystem.TryNextPage(logUi: true);
                    return true;
                }

                if (keyCode == Keys.Enter && _world.BookPage == 4)
                {
                    _ = _bookSystem.ConfirmAsync(token);

                    return true;
                }
            }
        }

	        if (_session.Stage is MirSessionStage.RunGate or MirSessionStage.InGame)
	        {
                if (alt && !ctrl && !shift && keyCode == Keys.X)
                {
                    if (_world.MyselfRecogIdSet)
                    {
                        _ = _softCloseSystem.TrySendAsync();
                        AppendLog("[net] CM_SOFTCLOSE (Alt+X)");
                    }

                    return true;
                }

                if (alt && !ctrl && !shift && keyCode == Keys.Q)
                {
                    if (_world.MyselfRecogIdSet)
                    {
                        AppendLog("[ui] exit (Alt+Q)");
                        BeginInvoke(new Action(Close));
                    }

                    return true;
                }

                if (!ctrl && !alt && keyCode == Keys.P)
                {
                    _groupOverlayVisible = !_groupOverlayVisible;
                    AddChatLine(_groupOverlayVisible ? "[组队面板] 显示" : "[组队面板] 隐藏", new Color4(0.92f, 0.92f, 0.92f, 1f));
                    return true;
                }

                if (!ctrl && !alt && keyCode == Keys.R)
                {
                    _stateOverlayVisible = !_stateOverlayVisible;
                    _stateOverlayText = string.Empty;
                    AppendLog(_stateOverlayVisible ? "[ui] state overlay on (R)" : "[ui] state overlay off (R)");
                    return true;
                }

	            if (ctrl && !alt && !shift && keyCode == Keys.H)
	            {
                    if (_session.IsConnected)
                        _ = _chatSendSystem.TrySendSayAsync("@AttackMode", token);
                    return true;
	            }

	            if (ctrl && !alt && !shift && keyCode == Keys.A)
	            {
                    if (_session.IsConnected)
                        _ = _chatSendSystem.TrySendSayAsync("@rest", token);
                    return true;
	            }

	            if (ctrl && !alt && !shift && keyCode == Keys.E)
	            {
                    if (_session.IsConnected)
                        _ = _chatSendSystem.TrySendSayAsync("@RestHero", token);
                    return true;
	            }

	            if (ctrl && !alt && !shift && keyCode == Keys.D)
	            {
                    long nowMs = Environment.TickCount64;
                    _seriesSkillSystem.TryFireSeriesSkill(nowMs, token);
                    return true;
	            }

                if (ctrl && !alt && !shift && keyCode == Keys.W)
                {
                    if (!_session.IsConnected)
                        return true;

                    if (!_world.HeroActorIdSet || _world.HeroActorId == 0)
                        return true;

                    if (TryGetSelectedTarget(out int targetRecogId, out ActorMarker target))
                    {
                        ushort tx = (ushort)Math.Clamp(target.X, 0, ushort.MaxValue);
                        ushort ty = (ushort)Math.Clamp(target.Y, 0, ushort.MaxValue);
                        _ = _session.SendClientMessageAsync(Grobal2.CM_HEROSETTARGET, targetRecogId, tx, ty, 0, token);
                        AppendLog($"[hero] CM_HEROSETTARGET target={targetRecogId} x={tx} y={ty}");
                    }

                    return true;
                }

                if (ctrl && !alt && !shift && keyCode == Keys.S)
                {
                    if (!_session.IsConnected)
                        return true;

                    if (!_world.HeroActorIdSet || _world.HeroActorId == 0)
                        return true;

                    _ = _session.SendClientMessageAsync(Grobal2.CM_HERORJOINTATTACK, 0, 0, 0, 0, token);
                    AppendLog("[hero] CM_HERORJOINTATTACK");
                    return true;
                }

                if (ctrl && !alt && !shift && keyCode == Keys.Q)
                {
                    if (!_session.IsConnected)
                        return true;

                    if (!_world.HeroActorIdSet || _world.HeroActorId == 0)
                        return true;

                    if (_targetingSystem.SelectedRecogId != 0)
                        return true;

                    int mx = _world.MapCenterX;
                    int my = _world.MapCenterY;
                    if (TryGetMouseMapCell(out int mapX, out int mapY))
                    {
                        mx = mapX;
                        my = mapY;
                    }

                    ushort ux = (ushort)Math.Clamp(mx, 0, ushort.MaxValue);
                    ushort uy = (ushort)Math.Clamp(my, 0, ushort.MaxValue);

                    _ = _session.SendClientMessageAsync(Grobal2.CM_HEROSETTARGET, 0, ux, uy, 0, token);
                    AppendLog($"[hero] CM_HEROSETTARGET guard x={ux} y={uy}");
                    return true;
                }

                if (keyCode == Keys.V && !ctrl && !alt)
                {
                    if (!_world.MyselfRecogIdSet)
                        return true;

                    _hideDeathBody = !_hideDeathBody;
                    AddChatLine(_hideDeathBody ? "[隐藏尸体] 开启" : "[隐藏尸体] 关闭", new Color4(0.92f, 0.92f, 0.92f, 1f));
                    TrySaveClientSetForCurrentCharacter();

                    if (_session.IsConnected)
                    {
                        ushort value = _hideDeathBody ? (ushort)1 : (ushort)0;
                        _ = _session.SendClientMessageAsync(Grobal2.CM_HIDEDEATHBODY, _world.MyselfRecogId, value, 0, 0, token);
                    }

                    return true;
                }

	            if (alt && !ctrl && !shift && keyCode == Keys.W)
	            {
                    if (!_world.MyselfRecogIdSet)
                        return true;

                    if (TryGetSelectedTarget(out int recogId, out ActorMarker target) &&
                        !target.IsMyself &&
                        !string.IsNullOrWhiteSpace(target.UserName) &&
                        (!_world.HeroActorIdSet || recogId != _world.HeroActorId))
                    {
                        string who = target.UserName.Trim();
                        if (_world.GroupMembers.Count == 0)
                            _groupSystem.TryCreateGroup(who, token);
                        else
                            _groupSystem.TryAddGroupMember(who, token);
                    }

                    return true;
	            }

	            if (alt && !ctrl && !shift && keyCode == Keys.E)
	            {
                    if (!_world.MyselfRecogIdSet)
                        return true;

                    if (TryGetSelectedTarget(out int recogId, out ActorMarker target) &&
                        !target.IsMyself &&
                        !string.IsNullOrWhiteSpace(target.UserName) &&
                        (!_world.HeroActorIdSet || recogId != _world.HeroActorId))
                    {
                        string who = target.UserName.Trim();
                        _groupSystem.TryDelGroupMember(who, token);
                    }

                    return true;
	            }

	            if (keyCode == Keys.K && !ctrl && !alt)
	            {
	                bool open = !_magicWindowVisible;
	                _magicWindowVisible = open;
	                if (open)
	                {
	                    _magicWindowHeroView = false;
	                    _magicWindowTopIndex = 0;
	                }

	                AppendLog(open ? "[ui] magic window opened" : "[ui] magic window closed");
	                return true;
	            }

	            if (keyCode == Keys.R && ctrl && !alt)
	            {
	                if (!_world.MyselfRecogIdSet)
	                    return true;

                _levelRankSystem.Toggle(token, logUi: true);

                return true;
            }

            if (keyCode == Keys.V && ctrl && !alt)
            {
                if (!_world.MyselfRecogIdSet)
                    return true;

                bool wasOpen = _seriesSkillSystem.Visible;
                _seriesSkillSystem.Toggle(logUi: true);
                if (!wasOpen && _seriesSkillSystem.Visible)
                {
                    _levelRankSystem.Close(logUi: false);
                    _guildSystem.CloseAll(logUi: false);
                }

                return true;
            }

            if ((keyCode == Keys.L && ctrl && !alt) || (keyCode == Keys.O && !ctrl && !alt))
            {
                if (!_world.MyselfRecogIdSet)
                    return true;

                if (_missionSystem.Visible)
                {
                    _missionSystem.Close();
                    AppendLog("[ui] missions closed");
                    return true;
                }

                _levelRankSystem.Close(logUi: false);
                _seriesSkillSystem.Close(logUi: false);
                _guildSystem.CloseAll(logUi: false);

                _missionSystem.Open(_missionSystem.MissionClass);
                _world.ClearNewMissionPending();
                AppendLog($"[ui] missions opened (class={_missionSystem.MissionClass})");

                return true;
            }

            if (_levelRankSystem.Visible && !ctrl && !alt)
            {
                if (keyCode == Keys.PageUp)
                {
                    if (_levelRankSystem.Page > 0)
                    {
                        _levelRankSystem.SetPage(_levelRankSystem.Page - 1);
                        _levelRankSystem.TrySendQuery(token);
                    }

                    return true;
                }

                if (keyCode == Keys.PageDown)
                {
                    _levelRankSystem.SetPage(_levelRankSystem.Page + 1);
                    _levelRankSystem.TrySendQuery(token);
                    return true;
                }

                if (keyCode == Keys.Home)
                {
                    _levelRankSystem.SetPage(0);
                    _levelRankSystem.TrySendQuery(token);
                    return true;
                }

                if (!shift && keyCode is >= Keys.D1 and <= Keys.D8)
                {
                    _levelRankSystem.SetType((int)(keyCode - Keys.D1));
                    _levelRankSystem.SetPage(0);
                    _levelRankSystem.TrySendQuery(token);
                    return true;
                }
            }

            if (_seriesSkillSystem.Visible && !ctrl && !alt)
            {
                bool heroAvail = _world.HeroActorIdSet && _world.HeroActorId != 0;

                if (keyCode == Keys.Tab && heroAvail)
                {
                    _seriesSkillSystem.TryToggleControlHero(logUi: true);
                    return true;
                }

                if (!shift && keyCode is >= Keys.D1 and <= Keys.D4)
                {
                    int idx = (int)(keyCode - Keys.D1);
                    _seriesSkillSystem.SelectVenationIndex(idx);
                    return true;
                }

                long nowMs = Environment.TickCount64;

                if (keyCode == Keys.F)
                {
                    _seriesSkillSystem.TryFireSeriesSkill(nowMs, token);
                    return true;
                }

                if (_seriesSkillSystem.IsActionCooldownActive(nowMs))
                    return true;

                if (keyCode == Keys.T)
                {
                    _seriesSkillSystem.TryTrainVenation(nowMs, token);
                    return true;
                }

                if (keyCode == Keys.B)
                {
                    string prompt = "BreakPoint 请输入穴位序号 (1-65535)";
                    BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            (UiTextPromptResult result, string value) = await PromptTextAsync(
                                title: "BreakPoint",
                                prompt: prompt,
                                buttons: UiTextPromptButtons.OkCancel).ConfigureAwait(true);

                            if (result != UiTextPromptResult.Ok)
                            {
                                AppendLog($"[venation] breakpoint dismissed ({result})");
                                return;
                            }

                            if (!int.TryParse(value.Trim(), out int point) || point <= 0)
                            {
                                AppendLog($"[venation] breakpoint ignored: invalid '{value}'");
                                return;
                            }

                            _seriesSkillSystem.TryBreakPoint(Environment.TickCount64, point, token);
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"[venation] breakpoint prompt error: {ex.Message}");
                        }
                    }));
                    return true;
                }

                if (keyCode == Keys.S)
                {
                    string prompt = "SetSeriesSkill 格式: <slotIndex 0-3> <magicId> [hero0/1]";
                    BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            (UiTextPromptResult result, string value) = await PromptTextAsync(
                                title: "SetSeriesSkill",
                                prompt: prompt,
                                buttons: UiTextPromptButtons.OkCancel).ConfigureAwait(true);

                            if (result != UiTextPromptResult.Ok)
                            {
                                AppendLog($"[series] set dismissed ({result})");
                                return;
                            }

                            string[] parts = value.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            if (parts.Length < 2 ||
                                !int.TryParse(parts[0], out int slotIndex) ||
                                !int.TryParse(parts[1], out int magicId))
                            {
                                AppendLog($"[series] set ignored: invalid '{value}'");
                                return;
                            }

                            int hero = 0;
                            if (parts.Length >= 3)
                                _ = int.TryParse(parts[2], out hero);

                            _seriesSkillSystem.TrySetSeriesSkillSlot(Environment.TickCount64, slotIndex, magicId, hero != 0, token);
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"[series] set prompt error: {ex.Message}");
                        }
                    }));
                    return true;
                }
            }

            if (_missionSystem.Visible && !ctrl && !alt)
            {
                int missionCount = _world.GetMissions((byte)_missionSystem.MissionClass).Count;

                if (keyCode == Keys.Escape)
                {
                    _missionSystem.Close();
                    AppendLog("[ui] missions closed");
                    return true;
                }

                if (!shift && keyCode is >= Keys.D1 and <= Keys.D4)
                {
                    _missionSystem.SetClass((int)(keyCode - Keys.D1) + 1);
                    _world.ClearNewMissionPending();
                    return true;
                }

                if (missionCount <= 0)
                {
                    _missionSystem.EnsureListWindow(total: 0, listLines: 1);
                    return true;
                }

                const int pageSize = 8;

                if (keyCode == Keys.Up)
                {
                    _missionSystem.Navigate(MissionSystem.MissionNavigation.Up, missionCount, pageSize);
                    return true;
                }

                if (keyCode == Keys.Down)
                {
                    _missionSystem.Navigate(MissionSystem.MissionNavigation.Down, missionCount, pageSize);
                    return true;
                }

                if (keyCode == Keys.PageUp)
                {
                    _missionSystem.Navigate(MissionSystem.MissionNavigation.PageUp, missionCount, pageSize);
                    return true;
                }

                if (keyCode == Keys.PageDown)
                {
                    _missionSystem.Navigate(MissionSystem.MissionNavigation.PageDown, missionCount, pageSize);
                    return true;
                }

                if (keyCode == Keys.Home)
                {
                    _missionSystem.Navigate(MissionSystem.MissionNavigation.Home, missionCount, pageSize);
                    return true;
                }

                if (keyCode == Keys.End)
                {
                    _missionSystem.Navigate(MissionSystem.MissionNavigation.End, missionCount, pageSize);
                    return true;
                }
            }

            if (keyCode == Keys.T && !ctrl && !alt)
            {
                if (!_session.IsConnected || !_world.MyselfRecogIdSet)
                    return true;

                long nowMs = Environment.TickCount64;
                if (nowMs < _dealTryCooldownUntilMs)
                    return true;

                _dealTryCooldownUntilMs = nowMs + 3000;

                _ = _session.SendClientStringAsync(Grobal2.CM_DEALTRY, 0, 0, 0, 0, string.Empty, token);
                AppendLog("[deal] CM_DEALTRY");
                return true;
            }

            if (keyCode == Keys.G && !ctrl && !alt)
            {
                if (!_world.MyselfRecogIdSet)
                    return true;

                _guildSystem.ToggleDialog(token, logUi: true);
                return true;
            }

            if (keyCode == Keys.G && ctrl && !alt)
            {
                if (!_world.MyselfRecogIdSet)
                    return true;

                if (shift)
                {
                    _guildSystem.ToggleMemberList(token, logUi: true);
                    return true;
                }

                _guildSystem.ToggleDialog(token, logUi: true);
                return true;
            }

            if (keyCode == Keys.M && ctrl && !alt)
            {
                if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
                    return true;

                if (!_world.MyselfRecogIdSet)
                    return true;

                long nowMs = Environment.TickCount64;
                MiniMapSystem.MiniMapToggleResult result = _miniMapSystem.Toggle(nowMs);

                if (result.Request)
                {
                    _miniMapRequestSystem.TryRequest(token);
                    return true;
                }

                if (result.ViewLevel <= 0)
                    AppendLog("[ui] minimap closed");
                else
                    AppendLog($"[ui] minimap level={result.ViewLevel}");
                return true;
            }

            if (ctrl && shift && !alt && keyCode is Keys.C or Keys.A or Keys.D)
            {
                if (!_world.MyselfRecogIdSet)
                    return true;

                string who = string.Empty;
                if (TryGetSelectedTarget(out _, out ActorMarker target) && !string.IsNullOrWhiteSpace(target.UserName))
                    who = target.UserName.Trim();

                if (string.IsNullOrWhiteSpace(who))
                {
                    Keys actionKey = keyCode;
                    string prompt = keyCode switch
                    {
                        Keys.C => "Create group with (player name)",
                        Keys.A => "Add group member (player name)",
                        Keys.D => "Del group member (player name)",
                        _ => "Player name"
                    };

                    BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            (UiTextPromptResult result, string value) = await PromptTextAsync(
                                title: "Group",
                                prompt: prompt,
                                buttons: UiTextPromptButtons.OkCancel,
                                maxGbkBytes: Grobal2.ActorNameLen).ConfigureAwait(true);

                            if (result != UiTextPromptResult.Ok)
                                return;

                            string inputWho = value.Trim();
                            if (string.IsNullOrWhiteSpace(inputWho))
                                return;

                            if (actionKey == Keys.C)
                            {
                                _groupSystem.TryCreateGroup(inputWho, token);
                                return;
                            }

                            if (actionKey == Keys.A)
                            {
                                _groupSystem.TryAddGroupMember(inputWho, token);
                                return;
                            }

                            _groupSystem.TryDelGroupMember(inputWho, token);
                        }
                        catch (Exception ex)
                        {
                            AppendLog($"[group] prompt error: {ex.Message}");
                        }
                    }));

                    return true;
                }

                if (string.IsNullOrWhiteSpace(who))
                    return true;

                if (keyCode == Keys.C)
                {
                    _groupSystem.TryCreateGroup(who, token);
                    return true;
                }

                if (keyCode == Keys.A)
                {
                    _groupSystem.TryAddGroupMember(who, token);
                    return true;
                }

                _groupSystem.TryDelGroupMember(who, token);
                return true;
            }
        }

        if (_session.Stage is MirSessionStage.RunGate or MirSessionStage.InGame)
        {
            if (keyCode == Keys.I && ctrl && !alt)
            {
                if (_treasureDialogSystem.Visible)
                {
                    _treasureDialogSystem.Close(logUi: true);
                    return true;
                }

                if (_itemDialogSystem.Visible)
                    _itemDialogSystem.Close(restoreSelectedItem: true, logUi: true);

                if (_bindDialogSystem.Visible)
                    _bindDialogSystem.Close(restoreSelectedItem: true, logUi: true);

                ClearItemDrag();
                _bagWindowVisible = true;
                _heroBagView = false;
                _treasureDialogSystem.Open(logUi: true);

                return true;
            }
        }

        if (keyCode == Keys.B)
        {
            if (ctrl)
            {
                _inventoryQuerySystem.TryQueryBagItems(recogMode: 0, logPrefix: "[bag]", token);
                return true;
            }

            if (alt)
            {
                if (_world.HeroActorIdSet)
                    _inventoryQuerySystem.TryQueryHeroBagItems("[hero-bag]", token);
                return true;
            }

            if (!_world.HeroActorIdSet)
                return true;

            ClearItemDrag();
            _bagWindowVisible = true;
            _heroBagView = !_heroBagView;
            AppendLog(_heroBagView ? "[ui] hero bag view (B)" : "[ui] bag view (B)");

            if (_heroBagView && _treasureDialogSystem.Visible)
                _treasureDialogSystem.Close(logUi: false);

            if (_heroBagView)
                _inventoryQuerySystem.TryQueryHeroBagItems("[hero-bag]", token);

            return true;
        }

        if (keyCode == Keys.M && !ctrl && !alt)
        {
            if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
                return true;

            if (!_world.MyselfRecogIdSet)
                return true;

            long nowMs = Environment.TickCount64;
            MiniMapSystem.MiniMapToggleResult result = _miniMapSystem.Toggle(nowMs);

            if (result.Request)
            {
                _miniMapRequestSystem.TryRequest(token);
                return true;
            }

            if (result.ViewLevel <= 0)
                AppendLog("[ui] minimap closed");
            else
                AppendLog($"[ui] minimap level={result.ViewLevel}");
            return true;
        }

        if (keyCode == Keys.S && alt)
        {
            _bagWindowVisible = true;
            _heroBagView = false;
            _userStallSystem.Close(logUi: false);

            _stallSystem.ToggleWindow(logUi: true);
            return true;
        }

        if (!_world.MyselfRecogIdSet || !_world.MapCenterSet)
            return false;

        if (keyCode == Keys.Z && !ctrl && !alt)
        {
            _showActorNames = !_showActorNames;
            AddChatLine(_showActorNames ? "[显示角色名] 开启" : "[显示角色名] 关闭", new Color4(0.92f, 0.92f, 0.92f, 1f));
            TrySaveClientSetForCurrentCharacter();
            return true;
        }

        if (keyCode == Keys.C && !ctrl && !alt)
        {
            _autoAttack = !_autoAttack;
            AddChatLine(_autoAttack ? "[自动Shift攻击] 开启" : "[自动Shift攻击] 关闭", new Color4(0.92f, 0.92f, 0.92f, 1f));
            TrySaveClientSetForCurrentCharacter();
            return true;
        }

        if (keyCode == Keys.X && !ctrl && !alt)
        {
            _duraWarning = !_duraWarning;
            AddChatLine(_duraWarning ? "[耐久提示] 开启" : "[耐久提示] 关闭", new Color4(0.92f, 0.92f, 0.92f, 1f));
            TrySaveClientSetForCurrentCharacter();
            return true;
        }

        if (keyCode == Keys.Escape && !ctrl && !alt)
        {
            if (_itemDragActive)
            {
                ClearItemDrag();
                AppendLog("[bag] drag canceled");
                return true;
            }

            bool didClose = false;

            if (_autoMoveSystem.Active)
            {
                _autoMoveSystem.Cancel();
                didClose = true;
            }

            if (_guildSystem.DialogVisible || _guildSystem.MemberListVisible)
            {
                _guildSystem.CloseAll(logUi: true);
                didClose = true;
            }

            if (_levelRankSystem.Visible)
            {
                _levelRankSystem.Close(logUi: true);
                didClose = true;
            }

            if (_seriesSkillSystem.Visible)
            {
                _seriesSkillSystem.Close(logUi: true);
                didClose = true;
            }

            if (_world.BoxOpen)
            {
                _boxSystem.Close(logUi: true);
                didClose = true;
            }

            if (_world.BookOpen)
            {
                _bookSystem.Close(logUi: true);
                didClose = true;
            }

            if (_world.RefineOpen)
            {
                CloseRefineUi(logUi: true);
                didClose = true;
            }

            if (didClose)
                return true;

            _showDropItems = !_showDropItems;
            AddChatLine(_showDropItems ? "[显示地面物品] 开启" : "[显示地面物品] 关闭", new Color4(0.92f, 0.92f, 0.92f, 1f));
            TrySaveClientSetForCurrentCharacter();
            return true;
        }

        if (keyCode == Keys.Oem3 && !ctrl && !alt)
        {
            int x = _world.MapCenterX;
            int y = _world.MapCenterY;

            _ = _pickupSystem.TryPickupAsync(x, y, token);
            return true;
        }

        if (!ctrl && !alt && !shift && keyCode is >= Keys.D1 and <= Keys.D6)
        {
            int idx = (int)(keyCode - Keys.D1);
            TryQuickUseBagItem(idx, token);
            return true;
        }

        if (keyCode is >= Keys.F1 and <= Keys.F8 && !alt)
        {
            int slot = (int)(keyCode - Keys.F1);

            int? mouseMapX = null;
            int? mouseMapY = null;
            if (TryGetMouseMapCell(out int mx, out int my))
            {
                mouseMapX = mx;
                mouseMapY = my;
            }

            char desiredKey = (char)((ctrl ? 'E' : '1') + slot);

            bool foundByKey = false;
            IReadOnlyList<ClientMagic> magics = _world.MyMagics;
            for (int i = 0; i < magics.Count; i++)
            {
                ClientMagic magic = magics[i];
                if (magic.Key != desiredKey)
                    continue;

                _spellCastSystem.TryCastMagic(_world, _targetingSystem, slot, magic, mouseMapX, mouseMapY, token);
                foundByKey = true;
                break;
            }

            if (!foundByKey)
                _spellCastSystem.TryCastHotbarMagic(_world, _targetingSystem, slot, mouseMapX, mouseMapY, token);
            return true;
        }

        if (keyCode == Keys.Tab && !ctrl && !alt)
        {
            bool reverse = shift;
            if (_targetingActionSystem.TryCycleTargetNearby(_world, reverse))
                return true;
        }

        if (!ctrl && !alt)
        {
            if (keyCode == Keys.Up)
            {
                ScrollChatBoard(-1);
                return true;
            }

            if (keyCode == Keys.Down)
            {
                ScrollChatBoard(1);
                return true;
            }

            if (keyCode == Keys.PageUp)
            {
                ScrollChatBoard(-ViewChatLine);
                return true;
            }

            if (keyCode == Keys.PageDown)
            {
                ScrollChatBoard(ViewChatLine);
                return true;
            }

            if (keyCode == Keys.Home)
            {
                lock (_chatLines)
                {
                    _chatBoardTop = 0;
                }
                return true;
            }

            if (keyCode == Keys.End)
            {
                lock (_chatLines)
                {
                    _chatBoardTop = _chatLines.Count > 0 ? _chatLines.Count - 1 : 0;
                }
                return true;
            }
        }

        byte? dir = keyCode switch
        {
            Keys.Up => Grobal2.DR_UP,
            Keys.Right => Grobal2.DR_RIGHT,
            Keys.Down => Grobal2.DR_DOWN,
            Keys.Left => Grobal2.DR_LEFT,
            _ => null
        };

        if (dir == null)
            return false;

        return _keyboardMoveSystem.TrySendArrowMove(
            dir: dir.Value,
            wantsRun: shift,
            mapCenterX: _world.MapCenterX,
            mapCenterY: _world.MapCenterY,
            mapLoaded: _map != null,
            isWalkable: _isCurrentMapWalkable,
            token);
    }

    private bool TryGetSelectedTarget(out int recogId, out ActorMarker actor) =>
        _targetingSystem.TryGetSelectedTarget(_world, out recogId, out actor);

    private async ValueTask TrySetStateMagicKeyAsync(bool hero, ushort magicId, byte key, CancellationToken token)
    {
        ushort ident = hero ? Grobal2.CM_HEROMAGICKEYCHANGE : Grobal2.CM_MAGICKEYCHANGE;

        if (!_world.TrySetMagicKey(hero, magicId, key, out ushort clearedMagicId))
            return;

        if (clearedMagicId != 0)
        {
            try
            {
                await _session.SendClientMessageAsync(ident, clearedMagicId, 0, 0, 0, token);
                AppendLog($"[magic] key cleared: magic={clearedMagicId} key=0");
            }
            catch (Exception ex)
            {
                AppendLog($"[magic] CM_MAGICKEYCHANGE(clear) send failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        try
        {
            await _session.SendClientMessageAsync(ident, magicId, key, 0, 0, token);
            string shown = key == 0 ? "0" : ((char)key).ToString();
            AppendLog($"[magic] key set: magic={magicId} key={shown}");
        }
        catch (Exception ex)
        {
            AppendLog($"[magic] CM_MAGICKEYCHANGE send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool TryGetMouseMapCell(out int mapX, out int mapY)
    {
        mapX = 0;
        mapY = 0;

        try
        {
            System.Drawing.Point mouseClient = _renderControl.PointToClient(Cursor.Position);
            if (!TryGetLogicalPoint(mouseClient, out Vector2 logical))
                return false;

            return TryResolveMapCell(logical, out mapX, out mapY);
        }
        catch
        {
            return false;
        }
    }

    private void TryQuickUseBagItem(int slotIndex, CancellationToken token)
    {
        if ((uint)slotIndex > 5u)
            return;

        ReadOnlySpan<ClientItem> slots = _world.BagSlots;
        if ((uint)slotIndex >= (uint)slots.Length)
            return;

        ClientItem item = slots[slotIndex];
        if (item.MakeIndex == 0 || string.IsNullOrWhiteSpace(item.NameString))
            return;

        if (item.S.NeedIdentify >= 4)
        {
            AppendLog($"[bag] quick use ignored (stall item) slot={slotIndex} makeIndex={item.MakeIndex} name='{item.NameString}'");
            return;
        }

        _ = _bagUseSystem.TryEatAsync(item, heroBag: false, logPrefix: "[bag]", slotIndex: slotIndex, token);
    }

    private async ValueTask<bool> TryHandleItemDragClickAsync(Vector2 logical, CancellationToken token)
    {
        if (!_bagWindowVisible)
            return false;

        if (_itemDragActive && _itemDragHero != _heroBagView)
            ClearItemDrag();

        if (_itemDragActive)
        {
            if (_itemDragItem.MakeIndex == 0 || string.IsNullOrWhiteSpace(_itemDragItem.NameString))
            {
                ClearItemDrag();
                return true;
            }

            if (TryHitTestUseItemSlot(logical, out int useSlot, out _))
            {
                if (_itemDragSource == ItemDragSource.Bag)
                {
                    if (!_itemDragHero && _itemDragItem.S.NeedIdentify >= 4)
                    {
                        AppendLog($"[bag] drag takeon ignored (stall makeIndex={_itemDragItem.MakeIndex} name='{_itemDragItem.NameString}')");
                        ClearItemDrag();
                        return true;
                    }

                    bool heroEquip = _itemDragHero;
                    await _equipSystem.TryTakeOnAsync(
                        _itemDragItem,
                        heroEquip,
                        whereHint: useSlot,
                        bagSlotIndex: -1,
                        logPrefix: heroEquip ? "[hero-equip]" : "[equip]",
                        actionLabel: "drag takeon",
                        successSuffix: " (drag)",
                        token);

                    ClearItemDrag();
                    return true;
                }

                ClearItemDrag();
                return true;
            }

            if (TryHitTestBagSlot(logical, out int placeSlotIndex, out ClientItem placeSlotItem))
            {
                if (_itemDragSource == ItemDragSource.Use)
                {
                    bool heroEquip = _itemDragHero;
                    int where = _itemDragSourceIndex;
                    await _equipSystem.TryTakeOffAsync(
                        _itemDragItem,
                        heroEquip,
                        where,
                        logPrefix: heroEquip ? "[hero-equip]" : "[equip]",
                        actionLabel: "drag takeoff",
                        successSuffix: " (drag)",
                        token);

                    ClearItemDrag();
                    return true;
                }

                if (_itemDragSource == ItemDragSource.Bag)
                {
                    if (!_itemDragHero && _itemDragItem.S.NeedIdentify >= 4)
                    {
                        AppendLog($"[bag] drag place ignored (stall makeIndex={_itemDragItem.MakeIndex})");
                        ClearItemDrag();
                        return true;
                    }

                    if (_itemDragHero && _world.HeroBagSize >= 10 && placeSlotIndex >= _world.HeroBagSize)
                    {
                        AppendLog($"[hero-bag] drag place ignored (out of range slot={placeSlotIndex} size={_world.HeroBagSize})");
                        return true;
                    }

                    if (placeSlotIndex == _itemDragSourceIndex)
                    {
                        ClearItemDrag();
                        return true;
                    }

                    if (placeSlotItem.MakeIndex != 0 &&
                        placeSlotItem.S.Overlap > 0 &&
                        _itemDragItem.S.Overlap > 0 &&
                        string.Equals(placeSlotItem.NameString, _itemDragItem.NameString, StringComparison.Ordinal))
                    {
                        int orgMakeIndex = placeSlotItem.MakeIndex;
                        int exMakeIndex = _itemDragItem.MakeIndex;
                        string prefix = _itemDragHero ? "[hero-bag]" : "[bag]";
                        await _itemSumCountSystem.TryItemSumCountAsync(
                            heroBag: _itemDragHero,
                            orgMakeIndex,
                            exMakeIndex,
                            orgName: placeSlotItem.NameString,
                            exName: _itemDragItem.NameString,
                            logPrefix: prefix,
                            successSuffix: " (drag)",
                            token);

                        ClearItemDrag();
                        return true;
                    }

                    if (!_world.TrySwapBagSlots(_itemDragSourceIndex, placeSlotIndex, _itemDragHero))
                    {
                        string prefix = _itemDragHero ? "[hero-bag]" : "[bag]";
                        AppendLog($"{prefix} drag place failed: src={_itemDragSourceIndex} dst={placeSlotIndex}");
                        ClearItemDrag();
                        return true;
                    }

                    _world.TryGetBagSlot(_itemDragSourceIndex, _itemDragHero, out ClientItem nextHeld);

                    if (nextHeld.MakeIndex == 0)
                    {
                        string prefix = _itemDragHero ? "[hero-bag]" : "[bag]";
                        AppendLog($"{prefix} drag place slot={placeSlotIndex} (done)");
                        ClearItemDrag();
                        return true;
                    }

                    _itemDragItem = nextHeld;
                    {
                        string prefix = _itemDragHero ? "[hero-bag]" : "[bag]";
                        AppendLog($"{prefix} drag swap slot={placeSlotIndex} nowHold='{nextHeld.NameString}' makeIndex={nextHeld.MakeIndex}");
                    }

                    return true;
                }

                ClearItemDrag();
                return true;
            }

            if (TryHitTestUsePanel(logical) || TryHitTestBagPanel(logical))
            {
                ClearItemDrag();
                return true;
            }

            if (_itemDragSource == ItemDragSource.Bag &&
                !_itemDragHero &&
                _world.MerchantDialogOpen &&
                _world.MerchantMode is MirMerchantMode.Sell or MirMerchantMode.Repair &&
                _merchantSellSpotRect is { } sellSpotRect &&
                logical.X >= sellSpotRect.Left &&
                logical.X < sellSpotRect.Right &&
                logical.Y >= sellSpotRect.Top &&
                logical.Y < sellSpotRect.Bottom)
            {
                ClientItem item = _itemDragItem;
                ClearItemDrag();
                await TrySelectMerchantSellItemAsync(item, token);
                return true;
            }

            if (_itemDragSource == ItemDragSource.Bag)
            {
                if (!_itemDragHero && _itemDragItem.S.NeedIdentify >= 4)
                {
                    AppendLog($"[bag] drag drop ignored (stall makeIndex={_itemDragItem.MakeIndex})");
                    ClearItemDrag();
                    return true;
                }

                string logPrefix = _itemDragHero ? "[hero-bag]" : "[bag]";
                await _dropItemSystem.TryDropAsync(
                    _itemDragItem,
                    heroBag: _itemDragHero,
                    logPrefix,
                    slotIndex: -1,
                    actionLabel: "drag drop",
                    successSuffix: " (drag)",
                    token);

                ClearItemDrag();
                return true;
            }

            ClearItemDrag();
            return true;
        }

        bool specialUiActive = !_heroBagView &&
                               (_world.MerchantMode is MirMerchantMode.Storage or MirMerchantMode.GetSave ||
                                _world.DealOpen ||
                                _marketSystem.Visible ||
                                IsStallSetupUiActive() ||
                                (_userStallSystem.Visible && _world.UserStallOpen));

        if (specialUiActive)
            return false;

        if (TryHitTestBagSlot(logical, out int bagSlotIndex, out ClientItem bagItem) && bagItem.MakeIndex != 0)
        {
            if (!_heroBagView && bagItem.S.NeedIdentify >= 4)
            {
                AppendLog($"[bag] drag ignored (stall makeIndex={bagItem.MakeIndex} name='{bagItem.NameString}')");
                return true;
            }

            _itemDragActive = true;
            _itemDragSource = ItemDragSource.Bag;
            _itemDragSourceIndex = bagSlotIndex;
            _itemDragHero = _heroBagView;
            _itemDragItem = bagItem;

            string prefix = _heroBagView ? "[hero-bag]" : "[bag]";
            AppendLog($"{prefix} drag pick '{bagItem.NameString}' makeIndex={bagItem.MakeIndex} slot={bagSlotIndex}");
            return true;
        }

        if (TryHitTestUseItemSlot(logical, out int useSlotIndex, out ClientItem useItem) && useItem.MakeIndex != 0)
        {
            _itemDragActive = true;
            _itemDragSource = ItemDragSource.Use;
            _itemDragSourceIndex = useSlotIndex;
            _itemDragHero = _heroBagView;
            _itemDragItem = useItem;

            string prefix = _heroBagView ? "[hero-equip]" : "[equip]";
            AppendLog($"{prefix} drag pick '{useItem.NameString}' makeIndex={useItem.MakeIndex} where={useSlotIndex}");
            return true;
        }

        return false;
    }

    private void ClearCorpseMarkers()
    {
        lock (_corpseLock)
        {
            _corpseMarkers.Clear();
            _corpseRemoveKeys.Clear();
            _corpseDrawActors.Clear();
            _lastCorpsePruneMs = 0;
        }
    }

    private void RemoveCorpseMarker(int recogId)
    {
        if (recogId == 0)
            return;

        lock (_corpseLock)
            _corpseMarkers.Remove(recogId);
    }

    private static ActorMarker CreateCorpseActor(ActorMarker actor, ushort corpseAction, long corpseStartTimestamp)
    {
        return actor with
        {
            FromX = actor.X,
            FromY = actor.Y,
            Action = corpseAction,
            ActionStartTimestamp = corpseStartTimestamp,
            UserName = string.Empty,
            DescUserName = string.Empty,
            OpenHealth = false,
            InstanceOpenHealth = false,
            Status = 0,
            LastDamage = 0,
            LastDamageTimestampMs = 0,
            WeaponEffect = false,
            WeaponEffectStartMs = 0,
            MagicServerCode = 0,
            MagicSerial = 0,
            MagicEffectNumber = 0,
            MagicEffectType = 0,
            MagicTarget = 0,
            MagicTargetX = 0,
            MagicTargetY = 0,
            MagicSpellLevel = 0,
            MagicPoison = 0,
            MagicFireLevel = 0,
            MagicWaitStartMs = 0,
            MagicAnimStartMs = 0,
            MagicHold = false
        };
    }

    private void HandleHideActor(string source, int recogId)
    {
        if (recogId == 0)
            return;

        _moveTimingStates.Remove(recogId);

        if (_hideDeathBody)
            return;

        if (!_world.TryGetActor(recogId, out ActorMarker actor))
            return;

        if (actor.IsMyself)
            return;

        long now = Stopwatch.GetTimestamp();
        long nowMs = Environment.TickCount64;

        
        
        
        lock (_corpseLock)
        {
            if (_corpseMarkers.TryGetValue(recogId, out CorpseMarker existing))
            {
                PruneCorpseMarkersNoLock(nowMs);

                ActorMarker corpseActor = existing.Actor;
                if (corpseActor.X != actor.X || corpseActor.Y != actor.Y)
                {
                    corpseActor = corpseActor with
                    {
                        X = actor.X,
                        Y = actor.Y,
                        FromX = actor.X,
                        FromY = actor.Y
                    };
                }

                _corpseMarkers[recogId] = new CorpseMarker(corpseActor, nowMs + CorpseTtlMs);
                return;
            }
        }

        bool deadAction = actor.Action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH or Grobal2.SM_SKELETON;
        bool deadHp = actor.MaxHp > 0 && actor.Hp <= 0;
        if (!deadAction && !deadHp)
            return;

        int race = FeatureCodec.Race(actor.Feature);
        if (race == 0 || race == Grobal2.RCC_MERCHANT)
            return;

        ushort corpseAction = actor.Action == Grobal2.SM_SKELETON ? Grobal2.SM_SKELETON : Grobal2.SM_DEATH;
        long corpseStartTimestamp = deadAction && actor.ActionStartTimestamp != 0 ? actor.ActionStartTimestamp : now;
        ActorMarker corpse = CreateCorpseActor(actor, corpseAction, corpseStartTimestamp);

        lock (_corpseLock)
        {
            PruneCorpseMarkersNoLock(nowMs);
            _corpseMarkers[recogId] = new CorpseMarker(corpse, nowMs + CorpseTtlMs);
        }
    }

    private void PruneCorpseMarkersNoLock(long nowMs)
    {
        if (_corpseMarkers.Count == 0)
            return;

        _corpseRemoveKeys.Clear();
        foreach ((int id, CorpseMarker marker) in _corpseMarkers)
        {
            if (nowMs >= marker.ExpireMs)
                _corpseRemoveKeys.Add(id);
        }

        for (int i = 0; i < _corpseRemoveKeys.Count; i++)
            _corpseMarkers.Remove(_corpseRemoveKeys[i]);

        _corpseRemoveKeys.Clear();
    }

    private void HandleActorAction(
        ushort ident,
        int recogId,
        int x,
        int y,
        ushort dir,
        CharDesc desc,
        string? userName,
        string? descUserName,
        byte? nameColor)
    {
        long now = Stopwatch.GetTimestamp();
        long nowMs = Environment.TickCount64;

        RemoveCorpseMarker(recogId);

        if (!_world.TryApplyActorAction(
                ident,
                recogId,
                x,
                y,
                dir,
                desc.Feature,
                desc.Status,
                userName,
                descUserName,
                nameColor,
                nameOffset: null,
                now,
                nowMs,
                out ActorMarker marker,
                out ushort previousAction))
        {
            return;
        }

        UpdateMoveTimingState(recogId, marker, ident, now);

        if ((ident is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH) && previousAction != ident)
            TryPlayDeathSfx(marker);

        if (!_hideDeathBody && ident is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH or Grobal2.SM_SKELETON)
        {
            int race = FeatureCodec.Race(marker.Feature);
            if (race != 0 && race != Grobal2.RCC_MERCHANT)
            {
                ushort corpseAction = ident == Grobal2.SM_SKELETON ? Grobal2.SM_SKELETON : Grobal2.SM_DEATH;
                ActorMarker corpse = CreateCorpseActor(marker, corpseAction, marker.ActionStartTimestamp);

                lock (_corpseLock)
                {
                    PruneCorpseMarkersNoLock(nowMs);
                    _corpseMarkers[recogId] = new CorpseMarker(corpse, nowMs + CorpseTtlMs);
                }
            }
        }
    }

    private void HandleActorSimpleAction(ushort ident, int recogId, int x, int y, ushort dir)
    {
        long now = Stopwatch.GetTimestamp();
        long nowMs = Environment.TickCount64;

        RemoveCorpseMarker(recogId);

        if (!_world.TryApplyActorSimpleAction(ident, recogId, x, y, dir, now, nowMs, out ActorMarker marker, out ushort previousAction))
            return;

        if (previousAction != ident)
            TryPlayCombatActionSfx(ident, marker);
    }

    private void TryPlayCombatActionSfx(ushort ident, ActorMarker actor)
    {
        if (!_soundManager.Enabled)
            return;

        int soundId = ident switch
        {
            Grobal2.SM_HIT or Grobal2.SM_HEAVYHIT or Grobal2.SM_BIGHIT => SndHitSword,
            Grobal2.SM_FIREHIT => SndFireHit,
            _ => -1
        };

        if (soundId < 0)
            return;

        TryPlaySfx(soundId, actor.X, actor.Y);
    }

    private void TryPlayStruckSfx(ActorMarker actor)
    {
        if (!_soundManager.Enabled)
            return;

        int soundId = SndStruckShort;
        if (FeatureCodec.Race(actor.Feature) == 0)
        {
            int sex = FeatureCodec.Dress(actor.Feature) & 1;
            soundId = sex == 1 ? SndWomanStruck : SndManStruck;
        }

        TryPlaySfx(soundId, actor.X, actor.Y);
    }

    private void TryPlayDeathSfx(ActorMarker actor)
    {
        if (!_soundManager.Enabled)
            return;

        if (FeatureCodec.Race(actor.Feature) != 0)
            return;

        int sex = FeatureCodec.Dress(actor.Feature) & 1;
        int soundId = sex == 1 ? SndWomanDie : SndManDie;
        TryPlaySfx(soundId, actor.X, actor.Y);
    }

    private void TryPlaySfx(int soundId, int x, int y)
    {
        if (soundId < 0)
            return;

        float volume = 1.0f;
        float pan = 0.0f;

        if (_world.MapCenterSet)
        {
            SoundUtil.GainPanVolume(x, y, _world.MapCenterX, _world.MapCenterY, out volume, out pan);
        }

        _soundManager.PlaySfxById(soundId, volume, pan);
    }

    private bool TryDrawHud(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (_sceneManager.CurrentId == MirSceneId.Play)
            return false;

        if (!_world.AbilitySet)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        int hp = _world.MyHp;
        int maxHp = _world.MyMaxHp;
        int mp = _world.MyMp;
        int maxMp = _world.MyMaxMp;
        int exp = _world.MyExp;
        int maxExp = _world.MyMaxExp;

        float hpPct = maxHp > 0 ? Math.Clamp(hp / (float)maxHp, 0f, 1f) : 0f;
        float mpPct = maxMp > 0 ? Math.Clamp(mp / (float)maxMp, 0f, 1f) : 0f;
        float expPct = maxExp > 0 ? Math.Clamp(exp / (float)maxExp, 0f, 1f) : 0f;

        const int x0 = 16;
        const int y0 = 118;
        const int barW = 180;
        const int barH = 10;
        const int gap = 4;

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);

        DrawBar(y0, hpPct, new Color4(0.90f, 0.18f, 0.18f, 0.92f));
        DrawBar(y0 + barH + gap, mpPct, new Color4(0.25f, 0.55f, 0.95f, 0.92f));
        DrawBar(y0 + (barH + gap) * 2, expPct, new Color4(0.92f, 0.82f, 0.25f, 0.92f));

        _spriteBatch.End();
        stats = _spriteBatch.Stats;
        return true;

        void DrawBar(int y, float pct, Color4 fill)
        {
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(x0 - 1, y - 1, barW + 2, barH + 2), color: new Color4(0, 0, 0, 0.65f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(x0, y, barW, barH), color: new Color4(0.05f, 0.05f, 0.05f, 0.55f));

            int fw = (int)MathF.Round(barW * pct);
            if (fw > 0)
                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(x0, y, fw, barH), color: fill);
        }
    }

    private bool TryDrawBottomBarUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;
        _bottomMagicHotbarRect = null;
        _bottomMiniMapButtonRect = null;
        _bottomTradeButtonRect = null;
        _bottomGuildButtonRect = null;
        _bottomGroupButtonRect = null;
        _mallToggleButtonRect = null;

        if (_sceneManager.CurrentId != MirSceneId.Play)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        string resourceRoot = GetResourceRootDir();
        string dataDir = Path.Combine(resourceRoot, "Data");
        if (!Directory.Exists(dataDir))
            return false;

        string? opUiPath = TryResolveArchiveFilePath(dataDir, "NewopUI");
        if (opUiPath == null)
            return false;

        string? wMainPath = TryResolveArchiveFilePath(dataDir, "WMain");
        string? wMain3Path = TryResolveArchiveFilePath(dataDir, "WMain3");

        int screenW = view.LogicalSize.Width;
        int screenH = view.LogicalSize.Height;

        bool anyDrawn = false;

        const int leftX = 194;
        const int rightInset = 241;
        const int rowHeight = 48;
        const int bottomUiHeight = 251;

        int topRowY = screenH - 158;
        int bottomRowY = screenH - 48;
        int bottomTop = Math.Max(0, screenH - bottomUiHeight);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);

        DrawingRectangle chatRect = GetInGameChatBoardRect(view.LogicalSize);
        if (chatRect.Width > 0 && chatRect.Height > 0)
        {
            _spriteBatch.Draw(_whiteTexture, chatRect, color: new Color4(0, 0, 0, 1f));
            anyDrawn = true;
        }

        bool drewClassicBottom = false;
        if (!string.IsNullOrWhiteSpace(wMain3Path))
        {
            const int bottomBoard800Index = 371;
            PrefetchArchiveImage(wMain3Path, bottomBoard800Index);
            if (TryGetArchiveTexture(wMain3Path, bottomBoard800Index, out D3D11Texture2D bottomBoard))
            {
                
                
                if (bottomBoard.Width == screenW)
                {
                    int boardX = 0;
                    int boardY = Math.Max(0, screenH - bottomBoard.Height);
                    _spriteBatch.Draw(bottomBoard, new DrawingRectangle(boardX, boardY, bottomBoard.Width, bottomBoard.Height));
                    anyDrawn = true;
                    drewClassicBottom = true;
                }
            }
        }

        if (!drewClassicBottom)
        {
            if (TryDrawOpUi(0, 0, screenH, alignBottom: true))
                anyDrawn = true;

            if (TryDrawOpUi(1, screenW, screenH, alignRight: true, alignBottom: true))
                anyDrawn = true;
        }

        int hotbarX = 0;
        int hotbarY = screenH - 211;
        int drawnW = 0;
        int drawnH = 0;
        if (TryDrawOpUiWithRect(2, screenW / 2, hotbarY, false, false, true, out int drawnX, out int drawnY, out drawnW, out drawnH))
        {
            hotbarX = drawnX;
            hotbarY = drawnY;
            if (drawnW > 0 && drawnH > 0)
                _bottomMagicHotbarRect = new DrawingRectangle(hotbarX, hotbarY, drawnW, drawnH);
            anyDrawn = true;
        }

        if (!drewClassicBottom)
        {
            if (TryDrawOpUi(10, leftX, topRowY))
                anyDrawn = true;

            if (TryDrawOpUi(12, screenW - rightInset, topRowY))
                anyDrawn = true;

            if (TryDrawOpUi(13, leftX, topRowY + rowHeight))
                anyDrawn = true;

            if (TryDrawOpUi(13, leftX, topRowY + (rowHeight * 2)))
                anyDrawn = true;

            if (TryDrawOpUi(14, screenW - rightInset, topRowY + rowHeight))
                anyDrawn = true;

            if (TryDrawOpUi(14, screenW - rightInset, topRowY + (rowHeight * 2)))
                anyDrawn = true;

            if (TryDrawOpUi(15, leftX, bottomRowY))
                anyDrawn = true;

            if (TryDrawOpUi(17, screenW - rightInset, bottomRowY))
                anyDrawn = true;

            anyDrawn |= TryDrawOpUiTiled(11, leftX, topRowY, screenW - rightInset);
            anyDrawn |= TryDrawOpUiTiled(16, leftX, bottomRowY, screenW - rightInset);
        }

        if (TryDrawOpUi(20, screenW - 207, screenH - 127))
            anyDrawn = true;

        if (!string.IsNullOrWhiteSpace(wMain3Path))
        {
            const int shopButtonIndex = 297;
            PrefetchArchiveImage(wMain3Path, shopButtonIndex);
            if (TryGetArchiveTexture(wMain3Path, shopButtonIndex, out D3D11Texture2D shopTex))
            {
                int x = Math.Clamp(screenW - 47, 0, Math.Max(0, screenW - shopTex.Width));
                int y = Math.Clamp(bottomTop + 203, 0, Math.Max(0, screenH - shopTex.Height));
                var rect = new DrawingRectangle(x, y, shopTex.Width, shopTex.Height);
                _spriteBatch.Draw(shopTex, rect);
                _mallToggleButtonRect = rect;
                anyDrawn = true;
            }
        }

        DrawBottomButtons(screenW, screenH);
        DrawBottomSmallButtons(screenW, screenH);
        DrawBottomBarsAndText(screenH);
        DrawMagicHotbarIcons(dataDir, hotbarX, hotbarY, drawnW, drawnH);
        DrawLevelExpWeightHeroEnergy(screenW, screenH);

        _spriteBatch.End();
        stats = _spriteBatch.Stats;
        return anyDrawn;

        void DrawBottomBarsAndText(int h)
        {
            if (!_world.AbilitySet)
                return;

            int hp = _world.MyHp;
            int maxHp = _world.MyMaxHp;
            int mp = _world.MyMp;
            int maxMp = _world.MyMaxMp;

            bool barsDrawn = false;
            if (!string.IsNullOrWhiteSpace(wMainPath) && maxHp > 0 && maxMp > 0)
            {
                int safeHp0 = Math.Min(maxHp, hp);
                int safeMp0 = Math.Min(maxMp, mp);

                if (_world.MyJob == 0 && _world.MyLevel < 28)
                {
                    const int barBgIndex = 5;
                    const int barFillIndex = 6;

                    PrefetchArchiveImage(wMainPath, barBgIndex);
                    if (TryGetArchiveTexture(wMainPath, barBgIndex, out D3D11Texture2D hpBg))
                    {
                        int topY = h - 161;
                        var src = new DrawingRectangle(0, 0, Math.Max(0, hpBg.Width - 2), hpBg.Height);
                        var dst = new DrawingRectangle(38, topY, src.Width, src.Height);
                        _spriteBatch.Draw(hpBg, dst, source: src);
                        barsDrawn = true;
                    }

                    PrefetchArchiveImage(wMainPath, barFillIndex);
                    if (TryGetArchiveTexture(wMainPath, barFillIndex, out D3D11Texture2D hpFill))
                    {
                        int topY = h - 161;
                        int cropTop = (int)Math.Round(hpFill.Height / (double)maxHp * (maxHp - safeHp0));
                        cropTop = Math.Clamp(cropTop, 0, hpFill.Height);

                        int cropWidth = Math.Max(0, hpFill.Width - 2);
                        int cropHeight = hpFill.Height - cropTop;
                        if (cropWidth > 0 && cropHeight > 0)
                        {
                            var src = new DrawingRectangle(0, cropTop, cropWidth, cropHeight);
                            var dst = new DrawingRectangle(38, topY + cropTop, src.Width, src.Height);
                            _spriteBatch.Draw(hpFill, dst, source: src);
                            barsDrawn = true;
                        }
                    }
                }
                else
                {
                    const int barIndex = 4;
                    PrefetchArchiveImage(wMainPath, barIndex);
                    if (TryGetArchiveTexture(wMainPath, barIndex, out D3D11Texture2D hpmp))
                    {
                        int topY = h - 160;
                        int half = hpmp.Width / 2;
                        int barH = hpmp.Height;

                        int cropTopHp = (int)Math.Round(barH / (double)maxHp * (maxHp - safeHp0));
                        cropTopHp = Math.Clamp(cropTopHp, 0, barH);

                        int cropTopMp = (int)Math.Round(barH / (double)maxMp * (maxMp - safeMp0));
                        cropTopMp = Math.Clamp(cropTopMp, 0, barH);

                        int hpWidth = Math.Max(0, (half - 1) - 0);
                        int hpHeight = barH - cropTopHp;
                        if (hpWidth > 0 && hpHeight > 0)
                        {
                            var src = new DrawingRectangle(0, cropTopHp, hpWidth, hpHeight);
                            var dst = new DrawingRectangle(40, topY + cropTopHp, src.Width, src.Height);
                            _spriteBatch.Draw(hpmp, dst, source: src);
                            barsDrawn = true;
                        }

                        int mpLeft = half + 1;
                        int mpRight = hpmp.Width - 1;
                        int mpWidth = Math.Max(0, mpRight - mpLeft);
                        int mpHeight = barH - cropTopMp;
                        if (mpWidth > 0 && mpHeight > 0)
                        {
                            var src = new DrawingRectangle(mpLeft, cropTopMp, mpWidth, mpHeight);
                            var dst = new DrawingRectangle(40 + mpLeft, topY + cropTopMp, src.Width, src.Height);
                            _spriteBatch.Draw(hpmp, dst, source: src);
                            barsDrawn = true;
                        }
                    }
                }
            }

            if (!barsDrawn)
            {
                float hpPct = maxHp > 0 ? Math.Clamp(hp / (float)maxHp, 0f, 1f) : 0f;
                float mpPct = maxMp > 0 ? Math.Clamp(mp / (float)maxMp, 0f, 1f) : 0f;

                const int barW = 6;
                const int barH = 74;
                const int gap = 3;

                int xHp = 38;
                int y0 = h - 160;

                DrawVerticalBar(xHp, y0, barW, barH, hpPct, new Color4(0.90f, 0.18f, 0.18f, 0.92f));
                DrawVerticalBar(xHp + barW + gap, y0, barW, barH, mpPct, new Color4(0.25f, 0.55f, 0.95f, 0.92f));
            }

            int safeHp = Math.Min(maxHp, hp);
            int safeMp = Math.Min(maxMp, mp);

            _uiTextDrawList.Add(new NameDrawInfo($"{safeHp}/{maxHp}", 28, h - 37, new Color4(1, 1, 1, 1)));
            _uiTextDrawList.Add(new NameDrawInfo($"{safeMp}/{maxMp}", 88, h - 37, new Color4(1, 1, 1, 1)));

            if (!string.IsNullOrWhiteSpace(_world.MapTitle))
            {
                string mapTitle = _world.MapTitle.Trim();
                if (_world.TryGetMyself(out ActorMarker myself))
                    _uiTextDrawList.Add(new NameDrawInfo($"{mapTitle} {myself.X}:{myself.Y}", 20, h - 17, new Color4(1, 1, 1, 1)));
                else
                    _uiTextDrawList.Add(new NameDrawInfo(mapTitle, 20, h - 17, new Color4(1, 1, 1, 1)));
            }
        }

        void DrawBottomButtons(int w, int h)
        {
            if (string.IsNullOrWhiteSpace(wMainPath))
                return;

            const int bottomUiHeight = 251;
            int bottomTop = Math.Max(0, h - bottomUiHeight);

            DrawButton(imageIndex: 8, w - 157, bottomTop + 62); 
            DrawButton(imageIndex: 9, w - 118, bottomTop + 42); 
            DrawButton(imageIndex: 10, w - 78, bottomTop + 22); 
            DrawButton(imageIndex: 11, w - 36, bottomTop + 12); 

            void DrawButton(int imageIndex, int x, int y)
            {
                PrefetchArchiveImage(wMainPath, imageIndex);
                if (!TryGetArchiveTexture(wMainPath, imageIndex, out D3D11Texture2D tex))
                    return;

                _spriteBatch.Draw(tex, new DrawingRectangle(x, y, tex.Width, tex.Height));
                anyDrawn = true;
            }
        }

        void DrawBottomSmallButtons(int w, int h)
        {
            if (string.IsNullOrWhiteSpace(wMainPath))
                return;

            const int bottomUiHeight = 251;
            int bottomTop = Math.Max(0, h - bottomUiHeight);

            
            DrawSmallButton(imageIndex: 130, 219, bottomTop + 104, assign: r => _bottomMiniMapButtonRect = r);
            DrawSmallButton(imageIndex: 132, 219 + 30, bottomTop + 104, assign: r => _bottomTradeButtonRect = r);
            DrawSmallButton(imageIndex: 134, 219 + (30 * 2), bottomTop + 104, assign: r => _bottomGuildButtonRect = r);
            DrawSmallButton(imageIndex: 128, 219 + (30 * 3), bottomTop + 104, assign: r => _bottomGroupButtonRect = r);

            void DrawSmallButton(int imageIndex, int x, int y, Action<DrawingRectangle> assign)
            {
                PrefetchArchiveImage(wMainPath, imageIndex);
                if (!TryGetArchiveTexture(wMainPath, imageIndex, out D3D11Texture2D tex))
                    return;

                var rect = new DrawingRectangle(x, y, tex.Width, tex.Height);
                _spriteBatch.Draw(tex, rect);
                assign(rect);
                anyDrawn = true;
            }
        }

        void DrawLevelExpWeightHeroEnergy(int w, int h)
        {
            if (!_world.AbilitySet)
                return;

            _uiTextDrawList.Add(new NameDrawInfo(_world.MyLevel.ToString(), w - 140, h - 104, new Color4(1, 1, 1, 1)));

            int exp = _world.MyExp;
            int maxExp = _world.MyMaxExp;
            float expPct = maxExp > 0 ? Math.Clamp(exp / (float)maxExp, 0f, 1f) : 0f;

            int weight = _world.MyWeight;
            int maxWeight = _world.MyMaxWeight;
            float weightPct = maxWeight > 0 ? Math.Clamp(weight / (float)maxWeight, 0f, 1f) : 0f;

            DrawMeter(w - 134, h - 73, expPct, fallbackW: 72, fallbackH: 14, fallbackFill: new Color4(0.92f, 0.82f, 0.25f, 0.92f));
            DrawMeter(w - 134, h - 40, weightPct, fallbackW: 72, fallbackH: 14, fallbackFill: new Color4(0.55f, 0.95f, 0.55f, 0.92f));

            if (_world.HeroMaxEnergy > 0 && _world.HeroEnergy > 0)
            {
                long nowMs = Environment.TickCount64;
                int interval = _world.HeroEnergyType switch
                {
                    1 => 400,
                    2 => 150,
                    _ => 0
                };

                if (interval <= 0)
                {
                    _heroEnergyFlashOn = true;
                }
                else if (nowMs - _heroEnergyFlashTickMs >= interval)
                {
                    _heroEnergyFlashTickMs = nowMs;
                    _heroEnergyFlashOn = !_heroEnergyFlashOn;
                }

                int heroEnergyX = w - 195;
                int heroEnergyBgY = h - 150;
                int heroEnergyFillY = h - 145;

                if (!string.IsNullOrWhiteSpace(wMain3Path))
                {
                    PrefetchArchiveImage(wMain3Path, 410);
                    if (TryGetArchiveTexture(wMain3Path, 410, out D3D11Texture2D bg))
                        _spriteBatch.Draw(bg, new DrawingRectangle(heroEnergyX, heroEnergyBgY, bg.Width, bg.Height));

                    int fillIndex = _heroEnergyFlashOn ? 411 : 412;
                    PrefetchArchiveImage(wMain3Path, fillIndex);
                    if (TryGetArchiveTexture(wMain3Path, fillIndex, out D3D11Texture2D fill))
                    {
                        float pct = Math.Clamp(_world.HeroEnergy / (float)Math.Max(1, _world.HeroMaxEnergy), 0f, 1f);
                        int srcTop = (int)MathF.Round(fill.Height * (1f - pct));
                        srcTop = Math.Clamp(srcTop, 0, fill.Height);
                        int drawH = Math.Max(0, fill.Height - srcTop);
                        if (drawH > 0)
                        {
                            var src = new DrawingRectangle(0, srcTop, fill.Width, drawH);
                            var dst = new DrawingRectangle(heroEnergyX, heroEnergyFillY + srcTop, fill.Width, drawH);
                            _spriteBatch.Draw(fill, dst, src);
                        }
                    }
                }
                else
                {
                    float pct = Math.Clamp(_world.HeroEnergy / (float)Math.Max(1, _world.HeroMaxEnergy), 0f, 1f);
                    const int barW = 14;
                    const int barH = 95;

                    _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(heroEnergyX, heroEnergyBgY, barW, barH), color: new Color4(0, 0, 0, 0.55f));
                    int fh = (int)MathF.Round(barH * pct);
                    if (fh > 0)
                        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(heroEnergyX, heroEnergyBgY + (barH - fh), barW, fh), color: new Color4(0.55f, 0.95f, 0.55f, 0.92f));
                }

                if (_world.HeroGloryPoint > 0)
                    _uiTextDrawList.Add(new NameDrawInfo(_world.HeroGloryPoint.ToString(), w - 39, h - 95, new Color4(1, 1, 1, 1)));
            }

            _uiTextDrawList.Add(new NameDrawInfo(DateTime.Now.ToString("HH:mm:ss"), w - 128, h - 21, new Color4(1, 1, 1, 1)));
        }

        void DrawVerticalBar(int x, int y, int w, int h, float pct, Color4 fill)
        {
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(x - 1, y - 1, w + 2, h + 2), color: new Color4(0, 0, 0, 0.65f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(x, y, w, h), color: new Color4(0.05f, 0.05f, 0.05f, 0.55f));

            int fh = (int)MathF.Round(h * pct);
            if (fh > 0)
            {
                int fy = y + (h - fh);
                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(x, fy, w, fh), color: fill);
            }
        }

        void DrawMagicHotbarIcons(string dataDir, int barX, int barY, int barW, int barH)
        {
            if (barW <= 0 || barH <= 0)
                return;

            string? magIconPath = TryResolveArchiveFilePath(dataDir, "MagIcon");
            if (magIconPath == null)
                return;

            IReadOnlyList<ClientMagic> magics = _world.MyMagics;
            if (magics.Count <= 0)
                return;

            string? magIcon2Path = TryResolveArchiveFilePath(dataDir, "MagIcon2");
            string? ui1Path = TryResolveArchiveFilePath(dataDir, "ui1");

            const int slots = 8;
            int slotW = barW / slots;
            if (slotW <= 0)
                slotW = 1;

            for (int i = 0; i < slots; i++)
            {
                char desiredKey = (char)('1' + i);

                bool has = false;
                ClientMagic magic = default;
                for (int j = 0; j < magics.Count; j++)
                {
                    ClientMagic m = magics[j];
                    if (m.Key != desiredKey)
                        continue;

                    magic = m;
                    has = true;
                    break;
                }

                if (!has)
                {
                    if (i >= magics.Count)
                        continue;

                    magic = magics[i];
                }

                int effect = magic.Def.Effect;
                int iconIndex = effect * 2;
                if (iconIndex < 0)
                    continue;

                string? iconArchive = magIconPath;
                int resolvedIndex = iconIndex;

                if (effect is >= 124 and <= 128 && magIcon2Path != null)
                {
                    iconArchive = magIcon2Path;
                    resolvedIndex = (effect - 124) * 2 + 580;
                }
                else if (effect is >= 120 and <= 123 && ui1Path != null)
                {
                    iconArchive = ui1Path;
                    resolvedIndex = (effect - 120) * 2 + 761;
                }
                else if (effect is >= 115 and <= 117 && magIcon2Path != null)
                {
                    iconArchive = magIcon2Path;
                    resolvedIndex = (effect - 115) * 2 + 170;
                }
                else if (effect == 118 && magIcon2Path != null)
                {
                    iconArchive = magIcon2Path;
                    resolvedIndex = 620;
                }

                if (iconArchive == null)
                    continue;

                PrefetchArchiveImage(iconArchive, resolvedIndex);
                if (!TryGetArchiveTexture(iconArchive, resolvedIndex, out D3D11Texture2D iconTex))
                    continue;

                int x = barX + (i * slotW) + (slotW - iconTex.Width) / 2;
                int y = barY + (barH - iconTex.Height) / 2;

                _spriteBatch.Draw(iconTex, new DrawingRectangle(x, y, iconTex.Width, iconTex.Height));
            }
        }

        void DrawMeter(int x, int y, float pct, int fallbackW, int fallbackH, Color4 fallbackFill)
        {
            pct = Math.Clamp(pct, 0f, 1f);
            if (pct <= 0f)
                return;

            if (!string.IsNullOrWhiteSpace(wMainPath))
            {
                const int barIndex = 7;
                PrefetchArchiveImage(wMainPath, barIndex);
                if (TryGetArchiveTexture(wMainPath, barIndex, out D3D11Texture2D bar))
                {
                    int wFill = (int)MathF.Round(bar.Width * pct);
                    wFill = Math.Clamp(wFill, 0, bar.Width);
                    if (wFill > 0)
                    {
                        var src = new DrawingRectangle(0, 0, wFill, bar.Height);
                        var dst = new DrawingRectangle(x, y, wFill, bar.Height);
                        _spriteBatch.Draw(bar, dst, src);
                    }

                    return;
                }
            }

            int fw = (int)MathF.Round(fallbackW * pct);
            if (fw > 0)
                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(x, y, fw, fallbackH), color: fallbackFill);
        }

        bool TryDrawOpUi(int imageIndex, int x, int y, bool alignRight = false, bool alignBottom = false, bool centerX = false)
            => TryDrawOpUiWithRect(imageIndex, x, y, alignRight, alignBottom, centerX, out _, out _, out _, out _);

        bool TryDrawOpUiWithRect(
            int imageIndex,
            int x,
            int y,
            bool alignRight,
            bool alignBottom,
            bool centerX,
            out int outX,
            out int outY,
            out int outW,
            out int outH)
        {
            outX = x;
            outY = y;
            outW = 0;
            outH = 0;

            PrefetchArchiveImage(opUiPath, imageIndex);
            if (!TryGetArchiveTexture(opUiPath, imageIndex, out D3D11Texture2D tex))
                return false;

            int dx = alignRight ? x - tex.Width : x;
            if (centerX)
                dx = x - (tex.Width / 2) + 11;

            int dy = alignBottom ? y - tex.Height : y;

            _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, tex.Width, tex.Height));

            outX = dx;
            outY = dy;
            outW = tex.Width;
            outH = tex.Height;
            return true;
        }

        bool TryDrawOpUiTiled(int imageIndex, int x, int y, int rightX)
        {
            PrefetchArchiveImage(opUiPath, imageIndex);
            if (!TryGetArchiveTexture(opUiPath, imageIndex, out D3D11Texture2D tex))
                return false;

            int tileW = tex.Width;
            if (tileW <= 0)
                return false;

            int span = rightX - x;
            int count = span / tileW;
            if (count <= 0)
                return true;

            for (int i = 1; i <= count; i++)
            {
                int dx = x + (tileW * i);
                _spriteBatch.Draw(tex, new DrawingRectangle(dx, y, tex.Width, tex.Height));
            }

            return true;
        }

        bool TryGetArchiveTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
        {
            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
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

        void PrefetchArchiveImage(string archivePath, int imageIndex)
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
    }

    private bool TryDrawMiniMapUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        int viewLevel = _miniMapSystem.ViewLevel;
        if (viewLevel <= 0)
            return false;

        if (!_world.MiniMapVisible || _world.MiniMapIndex < 0)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        string resourceRoot = GetResourceRootDir();
        string dataDir = Path.Combine(resourceRoot, "Data");
        if (!Directory.Exists(dataDir))
            return false;

        int miniMapIndex = _world.MiniMapIndex;
        bool isOpUi = miniMapIndex >= 300;

        string? archivePath = TryResolveArchiveFilePath(dataDir, isOpUi ? "NewopUI" : "mmap");
        if (archivePath == null)
            return false;

        int imageIndex = isOpUi ? (miniMapIndex + 1) : miniMapIndex;
        PrefetchArchiveImage(archivePath, imageIndex);

        if (!TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
            return false;

        int size = viewLevel == 1 ? 120 : 200;
        int destX = Math.Max(0, view.LogicalSize.Width - size);
        int destY = 0;

        var destRect = new DrawingRectangle(destX, destY, size, size);

        DrawingRectangle? sourceRect = null;
        if (viewLevel == 1 && _world.MapCenterSet)
        {
            int mx = (_world.MapCenterX * 48) / 32;
            int my = _world.MapCenterY;

            int srcLeft = Math.Max(0, mx - 60);
            int srcTop = Math.Max(0, my - 60);
            int srcRight = Math.Min(tex.Width, srcLeft + 120);
            int srcBottom = Math.Min(tex.Height, srcTop + 120);
            sourceRect = new DrawingRectangle(srcLeft, srcTop, Math.Max(0, srcRight - srcLeft), Math.Max(0, srcBottom - srcTop));
        }

        SpriteSampler sampler = viewLevel == 1 ? SpriteSampler.Point : SpriteSampler.Linear;
        _spriteBatch.Begin(frame.Context, view, sampler, SpriteBlendMode.AlphaBlend);

        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(destX - 1, destY - 1, size + 2, size + 2), color: new Color4(0, 0, 0, 0.65f));
        _spriteBatch.Draw(_whiteTexture, destRect, color: new Color4(0.05f, 0.05f, 0.05f, 0.35f));
        DrawingRectangle drawRect = destRect;
        if (viewLevel == 1 && sourceRect is { } crop)
            drawRect = new DrawingRectangle(destX, destY, crop.Width, crop.Height);

        _spriteBatch.Draw(tex, drawRect, sourceRect, color: new Color4(1, 1, 1, 0.92f));

        float fullRx = 0f;
        float fullRy = 0f;
        if (viewLevel != 1)
        {
            fullRx = tex.Width / (size * 1.5f);
            fullRy = tex.Height / (float)size;
        }

        if (_miniMapSystem.BlinkOn && _world.MapCenterSet)
        {
            int px;
            int py;

            if (viewLevel == 1 && sourceRect is { } src)
            {
                int mx = (_world.MapCenterX * 48) / 32;
                int my = _world.MapCenterY;
                px = destX + (mx - src.Left);
                py = destY + (my - src.Top);
            }
            else
            {
                px = destX + (int)MathF.Round(_world.MapCenterX / Math.Max(0.0001f, fullRx));
                py = destY + (int)MathF.Round(_world.MapCenterY / Math.Max(0.0001f, fullRy));
            }

            if (px >= destX && px < destX + size && py >= destY && py < destY + size)
                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(px - 1, py - 1, 3, 3), color: new Color4(1, 1, 1, 1));
        }

        if (!string.IsNullOrWhiteSpace(_world.MapTitle))
        {
            string mapTitle = _world.MapTitle.Trim();
            IReadOnlyList<MapDescTable.MapDescInfo> descEntries = _mapDescTable.Entries;
            for (int i = 0; i < descEntries.Count; i++)
            {
                MapDescTable.MapDescInfo info = descEntries[i];
                if (!string.Equals(info.MapTitle, mapTitle, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrWhiteSpace(info.PlaceName))
                    continue;

                if (viewLevel == 1)
                {
                    if (info.FullMap != 1 || sourceRect is not { } src)
                        continue;

                    int mx = destX + ((info.X * 48) / 32) - src.Left;
                    int my = destY + info.Y - src.Top;
                    if (mx < destX || mx >= destX + size || my < destY || my > destY + size)
                        continue;

                    _nameDrawList.Add(new NameDrawInfo(info.PlaceName, mx, my, ToColor4FromTColor(info.Color)));
                }
                else
                {
                    if (info.FullMap != 0)
                        continue;

                    int mx = destX + (int)MathF.Round(info.X / Math.Max(0.0001f, fullRx));
                    int my = destY + (int)MathF.Round(info.Y / Math.Max(0.0001f, fullRy));
                    if (mx < destX || mx >= destX + size || my < destY || my >= destY + size)
                        continue;

                    _nameDrawList.Add(new NameDrawInfo(info.PlaceName, mx, my, ToColor4FromTColor(info.Color)));
                }
            }
        }

        Vector2 mouseLogical = new(float.NegativeInfinity, float.NegativeInfinity);
        try
        {
            System.Drawing.Point mouseClient = _renderControl.PointToClient(Cursor.Position);
            if (view.ViewportRect.Contains(mouseClient))
            {
                mouseLogical = new Vector2(
                    (mouseClient.X - view.Offset.X) / Math.Max(0.0001f, view.Scale.X),
                    (mouseClient.Y - view.Offset.Y) / Math.Max(0.0001f, view.Scale.Y));
            }
        }
        catch
        {
        }

        if (_world.MapCenterSet &&
            mouseLogical.X >= destX && mouseLogical.X < destX + size &&
            mouseLogical.Y >= destY && mouseLogical.Y < destY + size)
        {
            int mapX;
            int mapY;
            float textY;

            if (viewLevel == 1 && sourceRect is { } src)
            {
                float w = Math.Max(1f, src.Width);
                float h = Math.Max(1f, src.Height);

                float rx = _world.MapCenterX + ((mouseLogical.X - (view.LogicalSize.Width - w) - (w * 0.5f)) * (2f / 3f));
                float ry = _world.MapCenterY + (mouseLogical.Y - (h * 0.5f));

                if (rx >= 0 && ry >= 0)
                {
                    mapX = (int)MathF.Round(rx);
                    mapY = (int)MathF.Round(ry);
                    textY = h - 14f;
                }
                else
                {
                    mapX = -1;
                    mapY = -1;
                    textY = 0;
                }
            }
            else
            {
                float rx = (mouseLogical.X - destX) * fullRx;
                float ry = (mouseLogical.Y - destY) * fullRy;

                if (rx >= 0 && ry >= 0)
                {
                    mapX = (int)MathF.Round(rx);
                    mapY = (int)MathF.Round(ry);
                    textY = size - 14f;
                }
                else
                {
                    mapX = -1;
                    mapY = -1;
                    textY = 0;
                }
            }

            if (mapX >= 0 && mapY >= 0)
            {
                string label = $"{mapX}:{mapY}";
                float textX = view.LogicalSize.Width - (MeasureHalfTextWidth(label) * 2f) - 2f;
                _nameDrawList.Add(new NameDrawInfo(label, textX, textY, new Color4(1, 1, 1, 1)));
            }
        }

        _spriteBatch.End();
        stats = _spriteBatch.Stats;
        return true;

        static Color4 ToColor4FromTColor(int color)
        {
            uint bgr = unchecked((uint)color);
            byte r = (byte)(bgr & 0xFF);
            byte g = (byte)((bgr >> 8) & 0xFF);
            byte b = (byte)((bgr >> 16) & 0xFF);
            uint argb = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            return ToColor4(argb);
        }

        bool TryGetArchiveTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
        {
            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
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

	        void PrefetchArchiveImage(string archivePath, int imageIndex)
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
    }

    private bool TryDrawStateWindowUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;
        _statePanelRect = null;
        _stateCloseRect = null;
        _stateMagicPageUpRect = null;
        _stateMagicPageDownRect = null;
        _stateMagicClickPoints.Clear();
        _stateMagicKeyClickPoints.Clear();
        _stateMagicKeyPanelRect = null;
        _stateMagicKeyCloseRect = null;

        if (!_stateWindowVisible)
            return false;

        if (_spriteBatch == null)
            return false;

        string resourceRoot = GetResourceRootDir();
        string dataDir = Path.Combine(resourceRoot, "Data");
        if (!Directory.Exists(dataDir))
            return false;

        string? wMainPath = TryResolveArchiveFilePath(dataDir, "WMain");
        if (wMainPath == null)
            return false;

        const int bgIndex = 800;
        const int closeIndex = 371;
        const int magicPageUpIndex = 398;
        const int magicPageDownIndex = 396;

        PrefetchArchiveImage(wMainPath, bgIndex);
        PrefetchArchiveImage(wMainPath, closeIndex);
        PrefetchArchiveImage(wMainPath, magicPageUpIndex);
        PrefetchArchiveImage(wMainPath, magicPageDownIndex);

        D3D11Texture2D? bgTex = null;
        if (TryGetArchiveTexture(wMainPath, imageIndex: bgIndex, out D3D11Texture2D bg))
            bgTex = bg;

        int panelW = bgTex?.Width ?? 232;
        int panelH = bgTex?.Height ?? 362;

        if (!_stateWindowPosSet)
        {
            _stateWindowPosSet = true;
            _stateWindowPosX = Math.Max(0, view.LogicalSize.Width - panelW);
            _stateWindowPosY = 0;
        }

        int x0 = Math.Clamp(_stateWindowPosX, 0, Math.Max(0, view.LogicalSize.Width - panelW));
        int y0 = Math.Clamp(_stateWindowPosY, 0, Math.Max(0, view.LogicalSize.Height - panelH));
        _stateWindowPosX = x0;
        _stateWindowPosY = y0;
        DrawingRectangle panelRect = new(x0, y0, panelW, panelH);
        _statePanelRect = panelRect;

        Vector2 mouseLogical = new(float.NegativeInfinity, float.NegativeInfinity);
        try
        {
            System.Drawing.Point mouseClient = _renderControl.PointToClient(Cursor.Position);
            if (view.ViewportRect.Contains(mouseClient))
            {
                mouseLogical = new Vector2(
                    (mouseClient.X - view.Offset.X) / Math.Max(0.0001f, view.Scale.X),
                    (mouseClient.Y - view.Offset.Y) / Math.Max(0.0001f, view.Scale.Y));
            }
        }
        catch
        {
            
        }

        
        string? stateItemPath = TryResolveArchiveFilePath(dataDir, "stateitem");
        string? stateItem2Path = TryResolveArchiveFilePath(dataDir, "stateitem2");
        string? stateEffectPath = TryResolveArchiveFilePath(dataDir, "StateEffect");
        string? shineEffectPath = TryResolveArchiveFilePath(dataDir, "ShineEffect");

        IReadOnlyDictionary<int, ClientItem> useItems = _heroBagView ? _world.HeroUseItems : _world.UseItems;

        int prefetchBudget = 8;

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        if (bgTex != null)
        {
            _spriteBatch.Draw(bgTex, panelRect);
        }
        else if (_whiteTexture != null)
        {
            _spriteBatch.Draw(_whiteTexture, panelRect, color: new Color4(0.12f, 0.12f, 0.16f, 0.92f));
        }

        if (TryGetArchiveTexture(wMainPath, imageIndex: 371, out D3D11Texture2D closeTex))
        {
            DrawingRectangle closeRect = new(panelRect.Left + 8, panelRect.Top + 39, closeTex.Width, closeTex.Height);
            _spriteBatch.Draw(closeTex, closeRect);
            _stateCloseRect = closeRect;
        }

        _stateMagicPageUpRect = null;
        _stateMagicPageDownRect = null;

        if (_stateWindowPage == 3)
            DrawMagicPage();
        else
            DrawEquipPage();

        _spriteBatch.End();
        stats = _spriteBatch.Stats;
        return true;

        void DrawEquipPage()
        {
            for (int slotIndex = Grobal2.U_DRESS; slotIndex <= Grobal2.U_CHARM; slotIndex++)
            {
                if (!TryGetUseSlotRect(panelRect.Left, panelRect.Top, slotIndex, out DrawingRectangle rect))
                    continue;

                if (!useItems.TryGetValue(slotIndex, out ClientItem item) || item.MakeIndex == 0)
                    continue;

                if (_itemDragActive &&
                    _itemDragHero == _heroBagView &&
                    _itemDragSource == ItemDragSource.Use &&
                    _itemDragSourceIndex == slotIndex &&
                    item.MakeIndex == _itemDragItem.MakeIndex)
                {
                    continue;
                }

                if (TryResolveItemIcon(item.S.Looks, out string? archivePath, out int imageIndex) && archivePath != null)
                {
                    if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                    {
                        int drawW = Math.Min(rect.Width, tex.Width);
                        int drawH = Math.Min(rect.Height, tex.Height);
                        int dx = rect.Left + (rect.Width - drawW) / 2;
                        int dy = rect.Top + (rect.Height - drawH) / 2;
                        _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, drawW, drawH));
                    }
                    else if (prefetchBudget > 0)
                    {
                        PrefetchArchiveImage(archivePath, imageIndex);
                        prefetchBudget--;
                    }
                }

                if (float.IsFinite(mouseLogical.X) &&
                    float.IsFinite(mouseLogical.Y) &&
                    mouseLogical.X >= rect.Left &&
                    mouseLogical.X < rect.Right &&
                    mouseLogical.Y >= rect.Top &&
                    mouseLogical.Y < rect.Bottom)
                {
                    SetTooltip(BuildItemTooltipText(item, _heroBagView ? "Hero" : null), new Vector2(rect.Right + 6, rect.Top));
                }
            }

            if (_heroBagView)
            {
                if (!_world.HeroAbilitySet)
                    return;

                DrawAbilityText(_world.HeroAbility);
            }
            else
            {
                if (!_world.AbilitySet)
                    return;

                DrawAbilityText(_world.MyAbility);
            }

            void DrawAbilityText(Ability ability)
            {
                int l = panelRect.Left + 110;
                int m = panelRect.Top + 96;
                var color = new Color4(0.95f, 0.95f, 0.95f, 1f);

                _uiTextDrawList.Add(new NameDrawInfo(FormatMinMax(ability.AC), l, m + 2, color));
                _uiTextDrawList.Add(new NameDrawInfo(FormatMinMax(ability.MAC), l, m + 22, color));
                _uiTextDrawList.Add(new NameDrawInfo(FormatMinMax(ability.DC), l, m + 42, color));
                _uiTextDrawList.Add(new NameDrawInfo(FormatMinMax(ability.MC), l, m + 62, color));
                _uiTextDrawList.Add(new NameDrawInfo(FormatMinMax(ability.SC), l, m + 82, color));
                _uiTextDrawList.Add(new NameDrawInfo($"{ability.HP}/{ability.MaxHP}", l, m + 102, color));
                _uiTextDrawList.Add(new NameDrawInfo($"{ability.MP}/{ability.MaxMP}", l, m + 122, color));
            }

            static string FormatMinMax(int packed)
            {
                int lo = packed & 0xFFFF;
                int hi = (packed >> 16) & 0xFFFF;
                return $"{lo}-{hi}";
            }
        }

        void DrawMagicPage()
        {
            bool hero = _heroBagView;
            IReadOnlyList<ClientMagic> magics = hero ? _world.HeroMagics : _world.MyMagics;

            const int pageSize = 6;
            int maxPage = magics.Count > 0 ? (magics.Count + (pageSize - 1)) / pageSize - 1 : 0;
            maxPage = Math.Max(0, maxPage);
            _stateMagicPage = Math.Clamp(_stateMagicPage, 0, maxPage);
            int topIndex = _stateMagicPage * pageSize;

            if (TryGetArchiveTexture(wMainPath, imageIndex: 398, out D3D11Texture2D upTex))
            {
                DrawingRectangle r = new(panelRect.Left + 213, panelRect.Top + 113, upTex.Width, upTex.Height);
                _spriteBatch.Draw(upTex, r);
                _stateMagicPageUpRect = r;
            }

            if (TryGetArchiveTexture(wMainPath, imageIndex: 396, out D3D11Texture2D downTex))
            {
                DrawingRectangle r = new(panelRect.Left + 213, panelRect.Top + 143, downTex.Width, downTex.Height);
                _spriteBatch.Draw(downTex, r);
                _stateMagicPageDownRect = r;
            }

            string? wMain2Path = TryResolveArchiveFilePath(dataDir, "WMain2");
            string? wMain3Path = TryResolveArchiveFilePath(dataDir, "WMain3");
            string? magIconPath = TryResolveArchiveFilePath(dataDir, "MagIcon");
            string? magIcon2Path = TryResolveArchiveFilePath(dataDir, "MagIcon2");
            string? ui1Path = TryResolveArchiveFilePath(dataDir, "ui1");

            int bbx = panelRect.Left + 38;
            int bby = panelRect.Top + 50;

            if (wMain2Path != null)
            {
                PrefetchArchiveImage(wMain2Path, 743);
                if (TryGetArchiveTexture(wMain2Path, 743, out D3D11Texture2D listBg))
                    _spriteBatch.Draw(listBg, new DrawingRectangle(bbx, bby, listBg.Width, listBg.Height));
            }

            int[] iconYs = { 57, 94, 132, 169, 206, 243 };
            const int lineH = 37;

            for (int i = 0; i < pageSize; i++)
            {
                int idx = topIndex + i;
                if ((uint)idx >= (uint)magics.Count)
                    break;

                ClientMagic magic = magics[idx];

                int magicNameY = bby + 8 + (i * lineH);
                int magicLvY = bby + 8 + 15 + (i * lineH);

                DrawingRectangle iconRect = new(panelRect.Left + 46, panelRect.Top + iconYs[i], 31, 33);
                _stateMagicClickPoints.Add(new StateMagicClickPoint(iconRect, hero, magic.Def.MagicId));

                if (TryResolveMagicIcon(magic.Def.Effect, out string? iconArchive, out int iconIndex) && iconArchive != null)
                {
                    if (TryGetArchiveTexture(iconArchive, iconIndex, out D3D11Texture2D iconTex))
                    {
                        int drawW = Math.Min(iconRect.Width, iconTex.Width);
                        int drawH = Math.Min(iconRect.Height, iconTex.Height);
                        int dx = iconRect.Left + (iconRect.Width - drawW) / 2;
                        int dy = iconRect.Top + (iconRect.Height - drawH) / 2;
                        _spriteBatch.Draw(iconTex, new DrawingRectangle(dx, dy, drawW, drawH));
                    }
                    else if (prefetchBudget > 0)
                    {
                        PrefetchArchiveImage(iconArchive, iconIndex);
                        prefetchBudget--;
                    }
                }

                int keyImg = GetMagicKeyIconIndex(magic.Key);
                if (keyImg > 0 && wMain3Path != null)
                {
                    if (TryGetArchiveTexture(wMain3Path, keyImg, out D3D11Texture2D keyTex))
                        _spriteBatch.Draw(keyTex, new DrawingRectangle(bbx + 145, bby + 8 + (i * lineH), keyTex.Width, keyTex.Height));
                    else if (prefetchBudget > 0)
                    {
                        PrefetchArchiveImage(wMain3Path, keyImg);
                        prefetchBudget--;
                    }
                }

                if (TryGetArchiveTexture(wMainPath, 112, out D3D11Texture2D lvTex))
                    _spriteBatch.Draw(lvTex, new DrawingRectangle(bbx + 48, magicLvY, lvTex.Width, lvTex.Height));

                if (TryGetArchiveTexture(wMainPath, 111, out D3D11Texture2D expTex))
                    _spriteBatch.Draw(expTex, new DrawingRectangle(bbx + 48 + 26, magicLvY, expTex.Width, expTex.Height));

                string name = magic.Def.MagicNameString;
                if (string.IsNullOrWhiteSpace(name))
                    name = $"#{magic.Def.MagicId}";

                _uiTextDrawList.Add(new NameDrawInfo(name, bbx + 48, magicNameY, new Color4(0.92f, 0.92f, 0.92f, 1f)));
                _uiTextDrawList.Add(new NameDrawInfo(magic.Level.ToString(), bbx + 48 + 16, magicLvY, new Color4(0.75f, 0.75f, 0.75f, 1f)));

                int trainlv = Math.Min(3, (int)magic.Level);
                uint maxTrain = magic.Def.GetMaxTrain(trainlv);
                if (maxTrain > 0)
                {
                    string trainText = trainlv < magic.Def.TrainLv ? $"{magic.CurTrain}/{maxTrain}" : "-";
                    _uiTextDrawList.Add(new NameDrawInfo(trainText, bbx + 48 + 46, magicLvY, new Color4(0.75f, 0.75f, 0.75f, 1f)));
                }

                DrawingRectangle hoverRect = new(bbx, magicNameY, 210, lineH);
                bool hovered = float.IsFinite(mouseLogical.X) && float.IsFinite(mouseLogical.Y) &&
                               mouseLogical.X >= hoverRect.Left && mouseLogical.X < hoverRect.Right &&
                               mouseLogical.Y >= hoverRect.Top && mouseLogical.Y < hoverRect.Bottom;
                if (hovered)
                {
                    char keyChar = magic.KeyChar;
                    string keyText = keyChar is >= '!' and <= '~' ? keyChar.ToString() : ((ushort)keyChar).ToString(CultureInfo.InvariantCulture);
                    SetTooltip($"{name}\nLv={magic.Level}  Key={keyText}\nTrain={magic.CurTrain}", new Vector2(hoverRect.Right + 6, hoverRect.Top));
                }
            }

            _uiTextDrawList.Add(new NameDrawInfo($"{_stateMagicPage + 1}/{Math.Max(1, maxPage + 1)}", bbx + 180, bby + 273, new Color4(0.75f, 0.85f, 0.95f, 1f)));

            if (_stateMagicKeyDialogOpen)
                DrawMagicKeyDialog();

            bool TryResolveMagicIcon(int effect, out string? archivePath, out int imageIndex)
            {
                archivePath = null;
                imageIndex = 0;

                int icon = effect * 2;
                if (icon < 0)
                    return false;

                if (effect is >= 124 and <= 128 && magIcon2Path != null)
                {
                    archivePath = magIcon2Path;
                    imageIndex = (effect - 124) * 2 + 580;
                    return true;
                }

                if (effect is >= 120 and <= 123 && ui1Path != null)
                {
                    archivePath = ui1Path;
                    imageIndex = (effect - 120) * 2 + 761;
                    return true;
                }

                if (effect is >= 115 and <= 117 && magIcon2Path != null)
                {
                    archivePath = magIcon2Path;
                    imageIndex = (effect - 115) * 2 + 170;
                    return true;
                }

                if (effect == 118 && magIcon2Path != null)
                {
                    archivePath = magIcon2Path;
                    imageIndex = 620;
                    return true;
                }

                if (magIconPath == null)
                    return false;

                archivePath = magIconPath;
                imageIndex = icon;
                return true;
            }

            void DrawMagicKeyDialog()
            {
                if (_whiteTexture == null)
                    return;

                IReadOnlyList<ClientMagic> list = _stateMagicKeyDialogHero ? _world.HeroMagics : _world.MyMagics;
                ClientMagic? found = null;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Def.MagicId == _stateMagicKeyDialogMagicId)
                    {
                        found = list[i];
                        break;
                    }
                }

                if (found is not { } magic)
                {
                    _stateMagicKeyDialogOpen = false;
                    return;
                }

                if (wMain3Path == null)
                    return;

                int pad = 8;
                int headerH = 34;
                int gap = 4;

                int keyW = 16;
                int keyH = 16;
                if (TryGetArchiveTexture(wMain3Path, 156, out D3D11Texture2D sampleKeyTex))
                {
                    keyW = sampleKeyTex.Width;
                    keyH = sampleKeyTex.Height;
                }

                int gridW = (keyW + gap) * 8 - gap;
                int gridH = (keyH + gap) * 2 - gap;
                int noneH = 18;

                int panelW = Math.Min(panelRect.Width - 6, Math.Max(200, (pad * 2) + gridW));
                int panelH = headerH + pad + gridH + pad + noneH + pad;
                int x0 = panelRect.Left + (panelRect.Width - panelW) / 2;
                int y0 = panelRect.Top + (panelRect.Height - panelH) / 2;

                if (panelW <= 0 || panelH <= 0)
                    return;

                DrawingRectangle dlgRect = new(x0, y0, panelW, panelH);
                _stateMagicKeyPanelRect = dlgRect;

                _spriteBatch.Draw(_whiteTexture, dlgRect, color: new Color4(0, 0, 0, 0.72f));
                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(dlgRect.Left + 1, dlgRect.Top + 1, dlgRect.Width - 2, dlgRect.Height - 2), color: new Color4(0.14f, 0.14f, 0.18f, 0.90f));
                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(dlgRect.Left + 1, dlgRect.Top + 1, dlgRect.Width - 2, headerH), color: new Color4(0.18f, 0.18f, 0.23f, 0.92f));

                DrawingRectangle closeRect = new(dlgRect.Right - 16, dlgRect.Top + 6, 10, 10);
                _stateMagicKeyCloseRect = closeRect;
                _spriteBatch.Draw(_whiteTexture, closeRect, color: new Color4(0.85f, 0.35f, 0.35f, 0.95f));

                string magName = magic.Def.MagicNameString;
                if (string.IsNullOrWhiteSpace(magName))
                    magName = $"#{magic.Def.MagicId}";

                _uiTextDrawList.Add(new NameDrawInfo("Set Magic Key", dlgRect.Left + pad, dlgRect.Top + 6, new Color4(0.95f, 0.95f, 0.95f, 1f)));
                _uiTextDrawList.Add(new NameDrawInfo(magName, dlgRect.Left + pad, dlgRect.Top + 18, new Color4(0.75f, 0.85f, 0.95f, 1f)));

                DrawingRectangle iconRect = new(dlgRect.Left + pad, dlgRect.Top + 6, 32, 32);
                if (TryResolveMagicIcon(magic.Def.Effect, out string? iconArchive, out int iconIndex) && iconArchive != null)
                {
                    if (TryGetArchiveTexture(iconArchive, iconIndex, out D3D11Texture2D iconTex))
                        _spriteBatch.Draw(iconTex, new DrawingRectangle(iconRect.Left, iconRect.Top, iconTex.Width, iconTex.Height));
                }

                byte curKey = magic.KeyChar is >= (char)byte.MinValue and <= (char)byte.MaxValue ? (byte)magic.KeyChar : (byte)0;
                int gridX0 = dlgRect.Left + pad;
                int gridY0 = dlgRect.Top + headerH + pad;

                _stateMagicKeyClickPoints.Clear();

                for (int col = 0; col < 8; col++)
                {
                    byte key = (byte)('1' + col);
                    int img = 156 + col;
                    DrawKeyIcon(key, img, row: 0, col);
                }

                for (int col = 0; col < 8; col++)
                {
                    byte key = (byte)('E' + col);
                    int img = 148 + col;
                    DrawKeyIcon(key, img, row: 1, col);
                }

                DrawingRectangle noneRect = new(dlgRect.Left + pad, dlgRect.Bottom - pad - noneH, 64, noneH);
                _stateMagicKeyClickPoints.Add(new StateMagicKeyClickPoint(noneRect, Key: 0));

                Color4 noneBg = curKey == 0 ? new Color4(0.22f, 0.34f, 0.65f, 0.75f) : new Color4(0.08f, 0.08f, 0.10f, 0.65f);
                _spriteBatch.Draw(_whiteTexture, noneRect, color: noneBg);
                _uiTextDrawList.Add(new NameDrawInfo("None", noneRect.Left + 8, noneRect.Top + 2, new Color4(0.95f, 0.95f, 0.95f, 1f)));

                void DrawKeyIcon(byte key, int imgIndex, int row, int col)
                {
                    int x = gridX0 + col * (keyW + gap);
                    int y = gridY0 + row * (keyH + gap);
                    DrawingRectangle r = new(x, y, keyW, keyH);

                    if (curKey == key)
                        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(r.Left - 1, r.Top - 1, r.Width + 2, r.Height + 2), color: new Color4(0.22f, 0.34f, 0.65f, 0.85f));

                    if (TryGetArchiveTexture(wMain3Path, imgIndex, out D3D11Texture2D keyTex))
                        _spriteBatch.Draw(keyTex, new DrawingRectangle(r.Left, r.Top, keyTex.Width, keyTex.Height));
                    else if (prefetchBudget > 0)
                    {
                        PrefetchArchiveImage(wMain3Path, imgIndex);
                        prefetchBudget--;
                    }

                    _stateMagicKeyClickPoints.Add(new StateMagicKeyClickPoint(r, key));
                }
            }

            static int GetMagicKeyIconIndex(char key) => key switch
            {
                'E' => 148,
                'F' => 149,
                'G' => 150,
                'H' => 151,
                'I' => 152,
                'J' => 153,
                'K' => 154,
                'L' => 155,
                '1' => 156,
                '2' => 157,
                '3' => 158,
                '4' => 159,
                '5' => 160,
                '6' => 161,
                '7' => 162,
                '8' => 163,
                _ => 0
            };
        }

        static bool TryGetUseSlotRect(int baseX, int baseY, int slotIndex, out DrawingRectangle rect)
        {
            rect = slotIndex switch
            {
                Grobal2.U_NECKLACE => new DrawingRectangle(baseX + 168, baseY + 87, 34, 31),
                Grobal2.U_HELMET => new DrawingRectangle(baseX + 115, baseY + 93, 18, 18),
                Grobal2.U_RIGHTHAND => new DrawingRectangle(baseX + 168, baseY + 125, 34, 31),
                Grobal2.U_ARMRINGR => new DrawingRectangle(baseX + 42, baseY + 176, 34, 31),
                Grobal2.U_ARMRINGL => new DrawingRectangle(baseX + 168, baseY + 176, 34, 31),
                Grobal2.U_RINGR => new DrawingRectangle(baseX + 42, baseY + 215, 34, 31),
                Grobal2.U_RINGL => new DrawingRectangle(baseX + 168, baseY + 215, 34, 31),
                Grobal2.U_WEAPON => new DrawingRectangle(baseX + 47, baseY + 80, 47, 87),
                Grobal2.U_DRESS => new DrawingRectangle(baseX + 96, baseY + 122, 53, 112),
                Grobal2.U_BUJUK => new DrawingRectangle(baseX + 42, baseY + 254, 34, 31),
                Grobal2.U_BELT => new DrawingRectangle(baseX + 84, baseY + 254, 34, 31),
                Grobal2.U_BOOTS => new DrawingRectangle(baseX + 126, baseY + 254, 34, 31),
                Grobal2.U_CHARM => new DrawingRectangle(baseX + 168, baseY + 254, 34, 31),
                _ => default
            };

            return rect.Width > 0 && rect.Height > 0;
        }

        bool TryResolveItemIcon(int looks, out string? archivePath, out int imageIndex)
        {
            archivePath = null;
            imageIndex = 0;

            if (looks < 0)
                return false;

            if (looks < 10_000)
            {
                archivePath = stateItemPath;
                imageIndex = looks;
                return archivePath != null;
            }

            if (looks < 20_000)
            {
                archivePath = stateItem2Path ?? stateItemPath;
                imageIndex = looks - 10_000;
                return archivePath != null;
            }

            if (looks < 30_000)
            {
                archivePath = stateEffectPath ?? stateItemPath;
                imageIndex = looks - 20_000;
                return archivePath != null;
            }

            if (looks < 40_000)
            {
                archivePath = shineEffectPath ?? stateItemPath;
                imageIndex = looks - 30_000;
                return archivePath != null;
            }

            archivePath = stateItemPath;
            imageIndex = looks % 10_000;
            return archivePath != null;
        }

        bool TryGetArchiveTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
        {
            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
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

        void PrefetchArchiveImage(string archivePath, int imageIndex)
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
    }

    private bool TryDrawInGameUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;
        _bagPanelRect = null;
        _bagCloseRect = null;
        _heroBagPanelRect = null;
        _heroBagCloseRect = null;

        if (!_bagWindowVisible)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        string resourceRoot = GetResourceRootDir();
        string dataDir = Path.Combine(resourceRoot, "Data");
        if (!Directory.Exists(dataDir))
            return false;

        Vector2 mouseLogical = new(float.NegativeInfinity, float.NegativeInfinity);
        try
        {
            System.Drawing.Point mouseClient = _renderControl.PointToClient(Cursor.Position);
            if (view.ViewportRect.Contains(mouseClient))
            {
                mouseLogical = new Vector2(
                    (mouseClient.X - view.Offset.X) / Math.Max(0.0001f, view.Scale.X),
                    (mouseClient.Y - view.Offset.Y) / Math.Max(0.0001f, view.Scale.Y));
            }
        }
        catch
        {
            
        }

        if (TryDrawClassicBagUi(out SpriteBatchStats classicStats))
        {
            stats = classicStats;
            return true;
        }

        const int cols = 8;
        const int rows = 7;
        const int slot = 36;
        const int pad = 8;
        const int header = 22;
        const int useCols = 2;
        const int useRows = 7;

        int panelW = (pad * 2) + (cols * slot);
        int panelH = (pad * 2) + header + (rows * slot);

        int panelX = Math.Max(8, view.LogicalSize.Width - panelW - 16);
        int panelY = Math.Max(8, view.LogicalSize.Height - panelH - 16);

        int slotX0 = panelX + pad;
        int slotY0 = panelY + pad + header;

        int usePanelW = (pad * 2) + (useCols * slot);
        int usePanelH = (pad * 2) + header + (useRows * slot);

        int usePanelX = Math.Max(8, panelX - usePanelW - 12);
        int usePanelY = panelY;

        int useSlotX0 = usePanelX + pad;
        int useSlotY0 = usePanelY + pad + header;

        bool storageModeActive = !_heroBagView &&
                                 (_world.MerchantMode is MirMerchantMode.Storage or MirMerchantMode.GetSave);

        bool merchantSellModeActive = !_heroBagView && _world.MerchantMode == MirMerchantMode.Sell;
        bool merchantRepairModeActive = !_heroBagView && _world.MerchantMode == MirMerchantMode.Repair;

        bool dealModeActive = !_heroBagView && _world.DealOpen;
        bool marketModeActive = !_heroBagView && _marketSystem.Visible;
        bool stallModeActive = IsStallSetupUiActive();
        bool userStallModeActive = !_heroBagView && _userStallSystem.Visible && _world.UserStallOpen;

        const int dealCols = 5;
        const int dealRemoteRows = 4;
        const int dealMyRows = 3;
        const int dealGapY = 12;
        const int dealButtonGapY = 10;
        const int dealButtonH = 22;

        int dealPanelW = (pad * 2) + (dealCols * slot);
        int dealRemotePanelH = (pad * 2) + header + (dealRemoteRows * slot);
        int dealMyPanelH = (pad * 2) + header + (dealMyRows * slot) + dealButtonGapY + dealButtonH;
        int dealTotalH = dealRemotePanelH + dealGapY + dealMyPanelH;

        int dealPanelX = Math.Max(8, usePanelX - dealPanelW - 12);
        int dealRemotePanelY = Math.Max(8, (panelY + panelH) - dealTotalH);
        int dealMyPanelY = dealRemotePanelY + dealRemotePanelH + dealGapY;

        int dealRemoteSlotX0 = dealPanelX + pad;
        int dealRemoteSlotY0 = dealRemotePanelY + pad + header;

        int dealMySlotX0 = dealPanelX + pad;
        int dealMySlotY0 = dealMyPanelY + pad + header;

        int dealButtonsY = dealMySlotY0 + (dealMyRows * slot) + dealButtonGapY;

        const int marketRows = 10;
        const int marketRowH = 18;
        const int marketDetailH = 48;
        const int marketButtonH = 22;
        const int marketGap = 8;
        const int marketBtnGap = 6;
        const int marketBtnCount = 6;

        int marketPanelW = Math.Min(520, Math.Max(0, view.LogicalSize.Width - 16));
        int marketListH = marketRows * marketRowH;
        int marketPanelH = (pad * 2) + header + marketListH + marketGap + marketDetailH + marketGap + marketButtonH;
        marketPanelH = Math.Min(marketPanelH, Math.Max(0, view.LogicalSize.Height - 16));

        int marketPanelX = 8;
        int marketPanelY = Math.Max(8, view.LogicalSize.Height - marketPanelH - 16);

        int marketListX0 = marketPanelX + pad;
        int marketListY0 = marketPanelY + pad + header;
        int marketListW = Math.Max(0, marketPanelW - (pad * 2));

        int marketDetailY = marketListY0 + marketListH + marketGap;
        int marketButtonsY = marketDetailY + marketDetailH + marketGap;

        int marketBtnW = (marketPanelW - (pad * 2) - (marketBtnGap * (marketBtnCount - 1))) / marketBtnCount;
        int marketBtnX0 = marketPanelX + pad;

        if (marketPanelW <= 0 || marketBtnW <= 0)
            marketModeActive = false;

        const int stallCols = 5;
        const int stallRows = 2;
        const int stallButtonGapY = 10;
        const int stallButtonH = 22;
        const int stallBtnGap = 6;
        const int stallBtnCount = 4;

        int stallPanelW = Math.Max(260, (pad * 2) + (stallCols * slot));
        int stallPanelH = (pad * 2) + header + (stallRows * slot) + stallButtonGapY + stallButtonH;

        int stallPanelX = Math.Max(8, usePanelX - stallPanelW - 12);
        int stallPanelY = Math.Max(8, (panelY + panelH) - stallPanelH);

        int stallContentW = Math.Max(0, stallPanelW - (pad * 2));
        int stallGridW = stallCols * slot;
        int stallSlotX0 = stallPanelX + pad + Math.Max(0, (stallContentW - stallGridW) / 2);
        int stallSlotY0 = stallPanelY + pad + header;
        int stallButtonsY = stallSlotY0 + (stallRows * slot) + stallButtonGapY;
        int stallBtnW = (stallContentW - (stallBtnGap * (stallBtnCount - 1))) / stallBtnCount;
        int stallBtnX0 = stallPanelX + pad;

        if (stallPanelW <= 0 || stallBtnW <= 0)
            stallModeActive = false;

        const int userStallCols = 5;
        const int userStallRows = 2;
        const int userStallButtonGapY = 10;
        const int userStallButtonH = 22;
        const int userStallBtnGap = 6;
        const int userStallBtnCount = 2;

        int userStallPanelW = stallPanelW;
        int userStallPanelH = (pad * 2) + header + (userStallRows * slot) + userStallButtonGapY + userStallButtonH;
        int userStallPanelX = 8;
        int userStallPanelY = 8;

        int userStallContentW = Math.Max(0, userStallPanelW - (pad * 2));
        int userStallGridW = userStallCols * slot;
        int userStallSlotX0 = userStallPanelX + pad + Math.Max(0, (userStallContentW - userStallGridW) / 2);
        int userStallSlotY0 = userStallPanelY + pad + header;
        int userStallButtonsY = userStallSlotY0 + (userStallRows * slot) + userStallButtonGapY;
        int userStallBtnW = (userStallContentW - (userStallBtnGap * (userStallBtnCount - 1))) / userStallBtnCount;
        int userStallBtnX0 = userStallPanelX + pad;

        if (userStallPanelW <= 0 || userStallBtnW <= 0)
            userStallModeActive = false;

        int storagePanelX = panelX;
        int storagePanelY = Math.Max(8, panelY - panelH - 12);

        int storageSlotX0 = storagePanelX + pad;
        int storageSlotY0 = storagePanelY + pad + header;

        int storageTop = Math.Max(0, _world.MerchantMenuTopLine);
        IReadOnlyList<ClientItem> storageItems = _world.StorageItems;

        int prefetchBudget = 16;

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(usePanelX, usePanelY, usePanelW, usePanelH), color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(usePanelX + 1, usePanelY + 1, usePanelW - 2, usePanelH - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(usePanelX + 1, usePanelY + 1, usePanelW - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));

        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panelX, panelY, panelW, panelH), color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panelX + 1, panelY + 1, panelW - 2, panelH - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panelX + 1, panelY + 1, panelW - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));

        if (storageModeActive)
        {
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(storagePanelX, storagePanelY, panelW, panelH), color: new Color4(0, 0, 0, 0.55f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(storagePanelX + 1, storagePanelY + 1, panelW - 2, panelH - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(storagePanelX + 1, storagePanelY + 1, panelW - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        }

        if (dealModeActive)
        {
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(dealPanelX, dealRemotePanelY, dealPanelW, dealRemotePanelH), color: new Color4(0, 0, 0, 0.55f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(dealPanelX + 1, dealRemotePanelY + 1, dealPanelW - 2, dealRemotePanelH - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(dealPanelX + 1, dealRemotePanelY + 1, dealPanelW - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));

            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(dealPanelX, dealMyPanelY, dealPanelW, dealMyPanelH), color: new Color4(0, 0, 0, 0.55f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(dealPanelX + 1, dealMyPanelY + 1, dealPanelW - 2, dealMyPanelH - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(dealPanelX + 1, dealMyPanelY + 1, dealPanelW - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        }

        if (marketModeActive)
        {
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(marketPanelX, marketPanelY, marketPanelW, marketPanelH), color: new Color4(0, 0, 0, 0.55f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(marketPanelX + 1, marketPanelY + 1, marketPanelW - 2, marketPanelH - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(marketPanelX + 1, marketPanelY + 1, marketPanelW - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        }

        if (stallModeActive)
        {
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(stallPanelX, stallPanelY, stallPanelW, stallPanelH), color: new Color4(0, 0, 0, 0.55f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(stallPanelX + 1, stallPanelY + 1, stallPanelW - 2, stallPanelH - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(stallPanelX + 1, stallPanelY + 1, stallPanelW - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        }

        if (userStallModeActive)
        {
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(userStallPanelX, userStallPanelY, userStallPanelW, userStallPanelH), color: new Color4(0, 0, 0, 0.55f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(userStallPanelX + 1, userStallPanelY + 1, userStallPanelW - 2, userStallPanelH - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(userStallPanelX + 1, userStallPanelY + 1, userStallPanelW - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        }

        string equipHeader = _heroBagView ? "HeroEquip  Alt+Click:TakeOff" : "Equip  Alt+Click:TakeOff";
        _uiTextDrawList.Add(new NameDrawInfo(equipHeader, usePanelX + pad, usePanelY + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));
        string bagHeader = _heroBagView
            ? "HeroBag (Alt+B)  Alt+Click:Equip  Alt+Shift+Click:ToBag  Alt+RClick:Drop"
            : dealModeActive
                ? "Bag (B)  Alt+B:HeroBag  Ctrl+Click:Use  Alt+Click:Deal  Alt+Shift+Click:ToHero  Alt+RClick:Drop  G:Pickup"
                : storageModeActive
                    ? "Bag (B)  Alt+B:HeroBag  Ctrl+Click:Use  Alt+Click:Store  Alt+Shift+Click:ToHero  Alt+RClick:Drop  G:Pickup"
                    : merchantSellModeActive
                        ? "Bag (B)  Alt+B:HeroBag  Ctrl+Click:Use  Alt+Click:Sell  Alt+Shift+Click:ToHero  Alt+RClick:Drop  G:Pickup"
                        : merchantRepairModeActive
                            ? "Bag (B)  Alt+B:HeroBag  Ctrl+Click:Use  Alt+Click:Repair  Alt+Shift+Click:ToHero  Alt+RClick:Drop  G:Pickup"
                            : marketModeActive
                                ? "Bag (B)  Alt+B:HeroBag  Ctrl+Click:Use  Alt+Click:MarketSell  Alt+Shift+Click:ToHero  Alt+RClick:Drop  G:Pickup"
                    : stallModeActive
                        ? "Bag (B)  Alt+B:HeroBag  Ctrl+Click:Use  Alt+Click:Stall  Alt+S:StallUI  Alt+Shift+Click:ToHero  Alt+RClick:Drop  G:Pickup"
                    : "Bag (B)  Alt+B:HeroBag  Ctrl+Click:Use  Alt+Click:Equip  Alt+Shift+Click:ToHero  Alt+RClick:Drop  G:Pickup";
        _uiTextDrawList.Add(new NameDrawInfo(bagHeader, panelX + pad, panelY + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));

        if (storageModeActive)
            _uiTextDrawList.Add(new NameDrawInfo("Storage  Alt+Click:TakeBack", storagePanelX + pad, storagePanelY + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));

        if (dealModeActive)
        {
            string who = string.IsNullOrWhiteSpace(_world.DealWho) ? "?" : _world.DealWho.Trim();
            string remoteHeader = $"Deal '{who}'  Gold:{_world.DealRemoteGold}";
            _uiTextDrawList.Add(new NameDrawInfo(remoteHeader, dealPanelX + pad, dealRemotePanelY + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));

            string myHeader = $"Me  Gold:{_world.DealMyGold}";
            if (_dealSystem.HasPending)
                myHeader += " (pending)";

            _uiTextDrawList.Add(new NameDrawInfo(myHeader, dealPanelX + pad, dealMyPanelY + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));
        }

        if (marketModeActive)
        {
            string mode = _world.MarketUserMode switch
            {
                1 => "Buy",
                2 => "My",
                _ => _world.MarketUserMode.ToString()
            };

            int cur = Math.Max(0, _world.MarketCurrentPage);
            int max = Math.Max(1, _world.MarketMaxPage);

            string headerText = string.IsNullOrWhiteSpace(_marketSystem.FindText)
                ? $"Market {mode}  Page:{cur}/{max}  Items:{_world.MarketItems.Count}"
                : $"Market {mode}  Page:{cur}/{max}  Items:{_world.MarketItems.Count}  Find:'{_marketSystem.FindText}'";

            _uiTextDrawList.Add(new NameDrawInfo(headerText, marketPanelX + pad, marketPanelY + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));
        }

        if (stallModeActive)
        {
            bool onSale = !_heroBagView && _stallSystem.IsMyStallOnSale();
            int stallCount = _stallSystem.CountSlots();
            string status = onSale ? "OnSale" : "Setup";
            string name = string.IsNullOrWhiteSpace(_stallSystem.Name) ? string.Empty : $" '{_stallSystem.Name.Trim()}'";
            string headerText = $"Stall {status}{name}  Items:{stallCount}/{ClientStallItems.MaxStallItemCount}";
            _uiTextDrawList.Add(new NameDrawInfo(headerText, stallPanelX + pad, stallPanelY + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));
        }

        if (userStallModeActive)
        {
            string name = string.IsNullOrWhiteSpace(_world.UserStallName) ? "?" : _world.UserStallName.Trim();
            string headerText = $"UserStall '{name}'  Items:{_world.UserStallItemCount}";
            _uiTextDrawList.Add(new NameDrawInfo(headerText, userStallPanelX + pad, userStallPanelY + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));
        }

        string? itemsPath = TryResolveArchiveFilePath(dataDir, "Items");
        string? items2Path = TryResolveArchiveFilePath(dataDir, "Items2");
        string? shineItemsPath = TryResolveArchiveFilePath(dataDir, "ShineItems");

        IReadOnlyDictionary<int, ClientItem> useItems = _heroBagView ? _world.HeroUseItems : _world.UseItems;

        for (int slotIndex = Grobal2.U_DRESS; slotIndex <= Grobal2.U_CHARM; slotIndex++)
        {
            int row = slotIndex / useCols;
            int col = slotIndex % useCols;
            if (row >= useRows)
                break;

            int x = useSlotX0 + (col * slot);
            int y = useSlotY0 + (row * slot);
            var rect = new DrawingRectangle(x, y, slot - 2, slot - 2);

            bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                           mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

            Color4 border = hovered ? new Color4(0.95f, 0.85f, 0.25f, 0.85f) : new Color4(0, 0, 0, 0.55f);
            Color4 fill = hovered ? new Color4(0.22f, 0.22f, 0.28f, 0.75f) : new Color4(0.10f, 0.10f, 0.12f, 0.65f);

            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2), color: border);
            _spriteBatch.Draw(_whiteTexture, rect, color: fill);

            if (!useItems.TryGetValue(slotIndex, out ClientItem item) || item.MakeIndex == 0)
                continue;

            if (_itemDragActive &&
                _itemDragHero == _heroBagView &&
                _itemDragSource == ItemDragSource.Use &&
                _itemDragSourceIndex == slotIndex &&
                item.MakeIndex == _itemDragItem.MakeIndex)
            {
                continue;
            }

            int looks = item.S.Looks;
            if (!TryResolveBagItemIconArchive(looks, out string? archivePath, out int imageIndex))
                continue;

            if (archivePath == null)
                continue;

            if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
            {
                int maxW = rect.Width - 6;
                int maxH = rect.Height - 6;

                int drawW = Math.Min(maxW, tex.Width);
                int drawH = Math.Min(maxH, tex.Height);

                int dx = rect.Left + (rect.Width - drawW) / 2;
                int dy = rect.Top + (rect.Height - drawH) / 2;

                _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, drawW, drawH));
            }
            else if (prefetchBudget > 0)
            {
                PrefetchArchiveImage(archivePath, imageIndex);
                prefetchBudget--;
            }

            if (hovered)
            {
                string slotName = GetUseItemSlotName(slotIndex);
                string headerPrefix = _heroBagView ? $"Hero {slotName}" : slotName;
                SetTooltip(BuildItemTooltipText(item, headerPrefix), new Vector2(rect.Right + 6, rect.Top));
            }
        }

        ReadOnlySpan<ClientItem> bagSlots = _heroBagView ? _world.HeroBagSlots : _world.BagSlots;

        for (int i = 0; i < bagSlots.Length; i++)
        {
            int row = i / cols;
            int col = i % cols;
            if (row >= rows)
                break;

            int x = slotX0 + (col * slot);
            int y = slotY0 + (row * slot);
            var rect = new DrawingRectangle(x, y, slot - 2, slot - 2);

            bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                           mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

            Color4 border = hovered ? new Color4(0.95f, 0.85f, 0.25f, 0.85f) : new Color4(0, 0, 0, 0.55f);
            Color4 fill = hovered ? new Color4(0.22f, 0.22f, 0.28f, 0.75f) : new Color4(0.10f, 0.10f, 0.12f, 0.65f);

            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2), color: border);
            _spriteBatch.Draw(_whiteTexture, rect, color: fill);

            ClientItem item = bagSlots[i];
            if (_itemDragActive &&
                _itemDragHero == _heroBagView &&
                _itemDragSource == ItemDragSource.Bag &&
                _itemDragSourceIndex == i &&
                item.MakeIndex == _itemDragItem.MakeIndex)
            {
                continue;
            }

            if (item.MakeIndex == 0)
                continue;

            int looks = item.S.Looks;
            if (!TryResolveBagItemIconArchive(looks, out string? archivePath, out int imageIndex))
                continue;

            if (archivePath == null)
                continue;

            if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
            {
                int maxW = rect.Width - 6;
                int maxH = rect.Height - 6;

                int drawW = Math.Min(maxW, tex.Width);
                int drawH = Math.Min(maxH, tex.Height);

                int dx = rect.Left + (rect.Width - drawW) / 2;
                int dy = rect.Top + (rect.Height - drawH) / 2;

                _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, drawW, drawH));
            }
            else if (prefetchBudget > 0)
            {
                PrefetchArchiveImage(archivePath, imageIndex);
                prefetchBudget--;
            }

            if (item.S.Overlap > 0 && item.Dura > 1)
                _uiTextDrawList.Add(new NameDrawInfo(item.Dura.ToString(), rect.Left + 3, rect.Top + rect.Height - 18, new Color4(0.95f, 0.95f, 0.95f, 1)));

            if (hovered)
            {
                SetTooltip(BuildItemTooltipText(item, _heroBagView ? "Hero" : null), new Vector2(rect.Right + 6, rect.Top));
            }
        }

        if (storageModeActive)
        {
            int visibleSlots = cols * rows;

            for (int i = 0; i < visibleSlots; i++)
            {
                int row = i / cols;
                int col = i % cols;
                if (row >= rows)
                    break;

                int x = storageSlotX0 + (col * slot);
                int y = storageSlotY0 + (row * slot);
                var rect = new DrawingRectangle(x, y, slot - 2, slot - 2);

                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                               mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                Color4 border = hovered ? new Color4(0.95f, 0.85f, 0.25f, 0.85f) : new Color4(0, 0, 0, 0.55f);
                Color4 fill = hovered ? new Color4(0.22f, 0.22f, 0.28f, 0.75f) : new Color4(0.10f, 0.10f, 0.12f, 0.65f);

                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2), color: border);
                _spriteBatch.Draw(_whiteTexture, rect, color: fill);

                int itemIndex = storageTop + i;
                if ((uint)itemIndex >= (uint)storageItems.Count)
                    continue;

                ClientItem item = storageItems[itemIndex];
                if (item.MakeIndex == 0)
                    continue;

                int looks = item.S.Looks;
                if (!TryResolveBagItemIconArchive(looks, out string? archivePath, out int imageIndex))
                    continue;

                if (archivePath == null)
                    continue;

                if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                {
                    int maxW = rect.Width - 6;
                    int maxH = rect.Height - 6;

                    int drawW = Math.Min(maxW, tex.Width);
                    int drawH = Math.Min(maxH, tex.Height);

                    int dx = rect.Left + (rect.Width - drawW) / 2;
                    int dy = rect.Top + (rect.Height - drawH) / 2;

                    _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, drawW, drawH));
                }
                else if (prefetchBudget > 0)
                {
                    PrefetchArchiveImage(archivePath, imageIndex);
                    prefetchBudget--;
                }

                if (item.S.Overlap > 0 && item.Dura > 1)
                    _uiTextDrawList.Add(new NameDrawInfo(item.Dura.ToString(), rect.Left + 3, rect.Top + rect.Height - 18, new Color4(0.95f, 0.95f, 0.95f, 1)));

                if (hovered)
                {
                    SetTooltip(BuildItemTooltipText(item, "Storage"), new Vector2(rect.Right + 6, rect.Top));
                }
            }
        }

        if (dealModeActive)
        {
            IReadOnlyList<ClientItem> dealRemoteItems = _world.DealRemoteItems;
            int visibleRemoteSlots = dealCols * dealRemoteRows;

            for (int i = 0; i < visibleRemoteSlots; i++)
            {
                int row = i / dealCols;
                int col = i % dealCols;
                if (row >= dealRemoteRows)
                    break;

                int x = dealRemoteSlotX0 + (col * slot);
                int y = dealRemoteSlotY0 + (row * slot);
                var rect = new DrawingRectangle(x, y, slot - 2, slot - 2);

                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                               mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                Color4 border = hovered ? new Color4(0.95f, 0.85f, 0.25f, 0.85f) : new Color4(0, 0, 0, 0.55f);
                Color4 fill = hovered ? new Color4(0.22f, 0.22f, 0.28f, 0.75f) : new Color4(0.10f, 0.10f, 0.12f, 0.65f);

                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2), color: border);
                _spriteBatch.Draw(_whiteTexture, rect, color: fill);

                if ((uint)i >= (uint)dealRemoteItems.Count)
                    continue;

                ClientItem item = dealRemoteItems[i];
                if (item.MakeIndex == 0)
                    continue;

                int looks = item.S.Looks;
                if (!TryResolveBagItemIconArchive(looks, out string? archivePath, out int imageIndex))
                    continue;

                if (archivePath == null)
                    continue;

                if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                {
                    int maxW = rect.Width - 6;
                    int maxH = rect.Height - 6;

                    int drawW = Math.Min(maxW, tex.Width);
                    int drawH = Math.Min(maxH, tex.Height);

                    int dx = rect.Left + (rect.Width - drawW) / 2;
                    int dy = rect.Top + (rect.Height - drawH) / 2;

                    _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, drawW, drawH));
                }
                else if (prefetchBudget > 0)
                {
                    PrefetchArchiveImage(archivePath, imageIndex);
                    prefetchBudget--;
                }

                if (item.S.Overlap > 0 && item.Dura > 1)
                    _uiTextDrawList.Add(new NameDrawInfo(item.Dura.ToString(), rect.Left + 3, rect.Top + rect.Height - 18, new Color4(0.95f, 0.95f, 0.95f, 1)));

                if (hovered)
                {
                    SetTooltip(BuildItemTooltipText(item, "Deal(Remote)"), new Vector2(rect.Right + 6, rect.Top));
                }
            }

            IReadOnlyList<ClientItem> dealMyItems = _world.DealMyItems;
            int visibleMySlots = dealCols * dealMyRows;

            for (int i = 0; i < visibleMySlots; i++)
            {
                int row = i / dealCols;
                int col = i % dealCols;
                if (row >= dealMyRows)
                    break;

                int x = dealMySlotX0 + (col * slot);
                int y = dealMySlotY0 + (row * slot);
                var rect = new DrawingRectangle(x, y, slot - 2, slot - 2);

                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                               mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                Color4 border = hovered ? new Color4(0.95f, 0.85f, 0.25f, 0.85f) : new Color4(0, 0, 0, 0.55f);
                Color4 fill = hovered ? new Color4(0.22f, 0.22f, 0.28f, 0.75f) : new Color4(0.10f, 0.10f, 0.12f, 0.65f);

                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2), color: border);
                _spriteBatch.Draw(_whiteTexture, rect, color: fill);

                if ((uint)i >= (uint)dealMyItems.Count)
                    continue;

                ClientItem item = dealMyItems[i];
                if (item.MakeIndex == 0)
                    continue;

                int looks = item.S.Looks;
                if (!TryResolveBagItemIconArchive(looks, out string? archivePath, out int imageIndex))
                    continue;

                if (archivePath == null)
                    continue;

                if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                {
                    int maxW = rect.Width - 6;
                    int maxH = rect.Height - 6;

                    int drawW = Math.Min(maxW, tex.Width);
                    int drawH = Math.Min(maxH, tex.Height);

                    int dx = rect.Left + (rect.Width - drawW) / 2;
                    int dy = rect.Top + (rect.Height - drawH) / 2;

                    _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, drawW, drawH));
                }
                else if (prefetchBudget > 0)
                {
                    PrefetchArchiveImage(archivePath, imageIndex);
                    prefetchBudget--;
                }

                if (item.S.Overlap > 0 && item.Dura > 1)
                    _uiTextDrawList.Add(new NameDrawInfo(item.Dura.ToString(), rect.Left + 3, rect.Top + rect.Height - 18, new Color4(0.95f, 0.95f, 0.95f, 1)));

                if (hovered)
                {
                    SetTooltip(BuildItemTooltipText(item, "Deal(My)"), new Vector2(rect.Right + 6, rect.Top));
                }
            }

            const int btnGap = 6;
            int btnW = (dealPanelW - (pad * 2) - (btnGap * 2)) / 3;
            int btnX0 = dealPanelX + pad;

            var goldRect = new DrawingRectangle(btnX0, dealButtonsY, btnW, dealButtonH);
            var endRect = new DrawingRectangle(btnX0 + btnW + btnGap, dealButtonsY, btnW, dealButtonH);
            var cancelRect = new DrawingRectangle(btnX0 + (btnW + btnGap) * 2, dealButtonsY, btnW, dealButtonH);

            DrawButton(goldRect, "Gold");
            DrawButton(endRect, "End");
            DrawButton(cancelRect, "Cancel");

            void DrawButton(DrawingRectangle rect, string text)
            {
                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                               mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                Color4 border = hovered ? new Color4(0.95f, 0.85f, 0.25f, 0.85f) : new Color4(0, 0, 0, 0.55f);
                Color4 fill = hovered ? new Color4(0.28f, 0.28f, 0.34f, 0.85f) : new Color4(0.16f, 0.16f, 0.20f, 0.75f);

                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2), color: border);
                _spriteBatch.Draw(_whiteTexture, rect, color: fill);
                _uiTextDrawList.Add(new NameDrawInfo(text, rect.Left + 10, rect.Top + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));

                if (hovered && text == "Gold")
                {
                    SetTooltip("Deal: Click 'Gold' to change", new Vector2(rect.Right + 6, rect.Top));
                }
            }
        }

        if (userStallModeActive)
        {
            ReadOnlySpan<ClientItem> userStallItems = _world.UserStallItems;
            int userStallSelectedIndex = _userStallSystem.SelectedIndex;

            for (int i = 0; i < ClientStallItems.MaxStallItemCount; i++)
            {
                int row = i / userStallCols;
                int col = i % userStallCols;
                if (row >= userStallRows)
                    break;

                int x = userStallSlotX0 + (col * slot);
                int y = userStallSlotY0 + (row * slot);
                var rect = new DrawingRectangle(x, y, slot - 2, slot - 2);

                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                               mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                bool selected = i == userStallSelectedIndex;

                Color4 border = selected || hovered ? new Color4(0.95f, 0.85f, 0.25f, 0.85f) : new Color4(0, 0, 0, 0.55f);
                Color4 fill = selected
                    ? new Color4(0.16f, 0.22f, 0.16f, 0.85f)
                    : hovered
                        ? new Color4(0.22f, 0.22f, 0.28f, 0.75f)
                        : new Color4(0.10f, 0.10f, 0.12f, 0.65f);

                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2), color: border);
                _spriteBatch.Draw(_whiteTexture, rect, color: fill);

                if ((uint)i >= (uint)userStallItems.Length)
                    continue;

                ClientItem item = userStallItems[i];
                if (item.MakeIndex == 0)
                    continue;

                int looks = item.S.Looks;
                if (!TryResolveBagItemIconArchive(looks, out string? archivePath, out int imageIndex))
                    continue;

                if (archivePath == null)
                    continue;

                if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                {
                    int maxW = rect.Width - 6;
                    int maxH = rect.Height - 6;

                    int drawW = Math.Min(maxW, tex.Width);
                    int drawH = Math.Min(maxH, tex.Height);

                    int dx = rect.Left + (rect.Width - drawW) / 2;
                    int dy = rect.Top + (rect.Height - drawH) / 2;

                    _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, drawW, drawH));
                }
                else if (prefetchBudget > 0)
                {
                    PrefetchArchiveImage(archivePath, imageIndex);
                    prefetchBudget--;
                }

                if (item.S.Overlap > 0 && item.Dura > 1)
                    _uiTextDrawList.Add(new NameDrawInfo(item.Dura.ToString(), rect.Left + 3, rect.Top + rect.Height - 18, new Color4(0.95f, 0.95f, 0.95f, 1)));

                string priceLabel = item.S.NeedIdentify switch
                {
                    5 => $"Y:{item.S.Price}",
                    4 => $"G:{item.S.Price}",
                    _ => item.S.Price.ToString()
                };
                _uiTextDrawList.Add(new NameDrawInfo(priceLabel, rect.Left + 3, rect.Top + 2, new Color4(0.92f, 0.92f, 0.35f, 1)));

                if (hovered)
                {
                    string tip = BuildItemTooltipText(item, "UserStall");
                    if (!string.IsNullOrWhiteSpace(priceLabel))
                        tip = $"{tip}\nPrice: {priceLabel}";
                    SetTooltip(tip, new Vector2(rect.Right + 6, rect.Top));
                }
            }

            var buyRect = new DrawingRectangle(userStallBtnX0 + (userStallBtnW + userStallBtnGap) * 0, userStallButtonsY, userStallBtnW, userStallButtonH);
            var closeRect = new DrawingRectangle(userStallBtnX0 + (userStallBtnW + userStallBtnGap) * 1, userStallButtonsY, userStallBtnW, userStallButtonH);

            bool canBuy = (uint)userStallSelectedIndex < (uint)userStallItems.Length &&
                          userStallItems[userStallSelectedIndex].MakeIndex != 0;

            DrawUserStallButton(buyRect, "Buy", enabled: canBuy);
            DrawUserStallButton(closeRect, "Close", enabled: true);

            void DrawUserStallButton(DrawingRectangle rect, string label, bool enabled)
            {
                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                               mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                Color4 border = hovered ? new Color4(0.95f, 0.85f, 0.25f, 0.85f) : new Color4(0, 0, 0, 0.55f);
                Color4 fill = enabled
                    ? hovered ? new Color4(0.28f, 0.28f, 0.34f, 0.85f) : new Color4(0.16f, 0.16f, 0.20f, 0.75f)
                    : new Color4(0.12f, 0.12f, 0.14f, 0.55f);

                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2), color: border);
                _spriteBatch.Draw(_whiteTexture, rect, color: fill);

                Color4 textColor = enabled ? new Color4(0.95f, 0.95f, 0.95f, 1) : new Color4(0.65f, 0.65f, 0.65f, 1);
                _uiTextDrawList.Add(new NameDrawInfo(label, rect.Left + 10, rect.Top + 4, textColor));
            }
        }

        if (stallModeActive)
        {
            int stallSelectedIndex = _stallSystem.SelectedIndex;
            StallSlot[] stallSlots = _stallSystem.Slots;

            for (int i = 0; i < ClientStallItems.MaxStallItemCount; i++)
            {
                int row = i / stallCols;
                int col = i % stallCols;
                if (row >= stallRows)
                    break;

                int x = stallSlotX0 + (col * slot);
                int y = stallSlotY0 + (row * slot);
                var rect = new DrawingRectangle(x, y, slot - 2, slot - 2);

                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                               mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                bool selected = i == stallSelectedIndex;

                Color4 border = selected || hovered ? new Color4(0.95f, 0.85f, 0.25f, 0.85f) : new Color4(0, 0, 0, 0.55f);
                Color4 fill = selected
                    ? new Color4(0.20f, 0.16f, 0.25f, 0.85f)
                    : hovered
                        ? new Color4(0.22f, 0.22f, 0.28f, 0.75f)
                        : new Color4(0.10f, 0.10f, 0.12f, 0.65f);

                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2), color: border);
                _spriteBatch.Draw(_whiteTexture, rect, color: fill);

                StallSlot stallSlot = stallSlots[i];
                if (!stallSlot.HasItem)
                    continue;

                ClientItem item = stallSlot.Item;
                if (item.MakeIndex == 0)
                    continue;

                int looks = item.S.Looks;
                if (!TryResolveBagItemIconArchive(looks, out string? archivePath, out int imageIndex))
                    continue;

                if (archivePath == null)
                    continue;

                if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                {
                    int maxW = rect.Width - 6;
                    int maxH = rect.Height - 6;

                    int drawW = Math.Min(maxW, tex.Width);
                    int drawH = Math.Min(maxH, tex.Height);

                    int dx = rect.Left + (rect.Width - drawW) / 2;
                    int dy = rect.Top + (rect.Height - drawH) / 2;

                    _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, drawW, drawH));
                }
                else if (prefetchBudget > 0)
                {
                    PrefetchArchiveImage(archivePath, imageIndex);
                    prefetchBudget--;
                }

                if (item.S.Overlap > 0 && item.Dura > 1)
                    _uiTextDrawList.Add(new NameDrawInfo(item.Dura.ToString(), rect.Left + 3, rect.Top + rect.Height - 18, new Color4(0.95f, 0.95f, 0.95f, 1)));

                string priceLabel = stallSlot.GoldType switch
                {
                    5 => $"Y:{stallSlot.Price}",
                    4 => $"G:{stallSlot.Price}",
                    _ => stallSlot.Price.ToString()
                };
                _uiTextDrawList.Add(new NameDrawInfo(priceLabel, rect.Left + 3, rect.Top + 2, new Color4(0.92f, 0.92f, 0.35f, 1)));

                if (hovered)
                {
                    string tip = BuildItemTooltipText(item, "Stall");
                    if (!string.IsNullOrWhiteSpace(priceLabel))
                        tip = $"{tip}\nPrice: {priceLabel}";
                    SetTooltip(tip, new Vector2(rect.Right + 6, rect.Top));
                }
            }

            var nameRect = new DrawingRectangle(stallBtnX0 + (stallBtnW + stallBtnGap) * 0, stallButtonsY, stallBtnW, stallButtonH);
            var openRect = new DrawingRectangle(stallBtnX0 + (stallBtnW + stallBtnGap) * 1, stallButtonsY, stallBtnW, stallButtonH);
            var cancelRect = new DrawingRectangle(stallBtnX0 + (stallBtnW + stallBtnGap) * 2, stallButtonsY, stallBtnW, stallButtonH);
            var removeRect = new DrawingRectangle(stallBtnX0 + (stallBtnW + stallBtnGap) * 3, stallButtonsY, stallBtnW, stallButtonH);

            bool onSale = !_heroBagView && _stallSystem.IsMyStallOnSale();
            int stallCount = _stallSystem.CountSlots();
            bool hasSelection = (uint)stallSelectedIndex < (uint)stallSlots.Length && stallSlots[stallSelectedIndex].HasItem;

            DrawStallButton(nameRect, "Name", enabled: true);
            DrawStallButton(openRect, "Open", enabled: !onSale && stallCount > 0 && _world.MyselfRecogIdSet);
            DrawStallButton(cancelRect, "Stop", enabled: onSale);
            DrawStallButton(removeRect, "Del", enabled: hasSelection);

            void DrawStallButton(DrawingRectangle rect, string label, bool enabled)
            {
                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                               mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                Color4 border = hovered ? new Color4(0.95f, 0.85f, 0.25f, 0.85f) : new Color4(0, 0, 0, 0.55f);
                Color4 fill = enabled
                    ? hovered ? new Color4(0.28f, 0.28f, 0.34f, 0.85f) : new Color4(0.16f, 0.16f, 0.20f, 0.75f)
                    : new Color4(0.12f, 0.12f, 0.14f, 0.55f);

                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2), color: border);
                _spriteBatch.Draw(_whiteTexture, rect, color: fill);

                Color4 textColor = enabled ? new Color4(0.95f, 0.95f, 0.95f, 1) : new Color4(0.65f, 0.65f, 0.65f, 1);
                _uiTextDrawList.Add(new NameDrawInfo(label, rect.Left + 10, rect.Top + 4, textColor));
            }
        }

        if (marketModeActive)
        {
            IReadOnlyList<MarketItem> marketItems = _world.MarketItems;
            int top = Math.Max(0, _marketSystem.TopIndex);
            int selectedIndex = _marketSystem.SelectedIndex;

            int mode = _world.MarketUserMode;
            string whoHeader = mode == 2 ? "State" : "Who";

            int priceW = 120;
            int whoW = 160;
            int nameW = marketListW - priceW - whoW;
            if (nameW < 140)
            {
                whoW = Math.Max(80, whoW - (140 - nameW));
                nameW = marketListW - priceW - whoW;
            }

            int nameX = marketListX0 + 6;
            int priceX = marketListX0 + Math.Max(0, nameW) + 12;
            int whoX = priceX + priceW + 12;

            _uiTextDrawList.Add(new NameDrawInfo("Name", nameX, marketListY0 - 18, new Color4(0.92f, 0.92f, 0.92f, 1)));
            _uiTextDrawList.Add(new NameDrawInfo("Price", priceX, marketListY0 - 18, new Color4(0.92f, 0.92f, 0.92f, 1)));
            _uiTextDrawList.Add(new NameDrawInfo(whoHeader, whoX, marketListY0 - 18, new Color4(0.92f, 0.92f, 0.92f, 1)));

            for (int row = 0; row < marketRows; row++)
            {
                int index = top + row;
                int y = marketListY0 + (row * marketRowH);
                var rect = new DrawingRectangle(marketListX0, y, marketListW, marketRowH - 2);

                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                               mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                bool selected = index == selectedIndex;

                Color4 border = selected || hovered ? new Color4(0.95f, 0.85f, 0.25f, 0.85f) : new Color4(0, 0, 0, 0.45f);
                Color4 fill = selected
                    ? new Color4(0.25f, 0.16f, 0.16f, 0.85f)
                    : hovered
                        ? new Color4(0.22f, 0.22f, 0.28f, 0.75f)
                        : new Color4(0.10f, 0.10f, 0.12f, 0.65f);

                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2), color: border);
                _spriteBatch.Draw(_whiteTexture, rect, color: fill);

                if ((uint)index >= (uint)marketItems.Count)
                    continue;

                MarketItem item = marketItems[index];
                string name = item.Item.NameString;
                string who = item.SellWhoString;
                string whoText = mode == 2
                    ? item.SellState switch
                    {
                        1 => "Selling",
                        2 => "Sold",
                        _ => item.SellState.ToString()
                    }
                    : item.SellState == 2 ? $"{who} (sold)" : who;

                Color4 textColor = selected
                    ? new Color4(1.0f, 0.35f, 0.35f, 1)
                    : item.SellState == 2
                        ? new Color4(1.0f, 0.90f, 0.25f, 1)
                        : item.UpgCount > 0
                            ? new Color4(0.25f, 0.95f, 0.95f, 1)
                            : new Color4(0.95f, 0.95f, 0.95f, 1);

                _uiTextDrawList.Add(new NameDrawInfo(name, nameX, y, textColor));
                _uiTextDrawList.Add(new NameDrawInfo(item.SellPrice.ToString(), priceX, y, textColor));
                _uiTextDrawList.Add(new NameDrawInfo(whoText, whoX, y, textColor));

                if (hovered)
                {
                    string tip = BuildItemTooltipText(item.Item, "Market");
                    tip = $"{tip}\nPrice: {item.SellPrice}";
                    tip = mode == 2 ? $"{tip}\nState: {whoText}" : $"{tip}\nWho: {whoText}";
                    SetTooltip(tip, new Vector2(rect.Right + 6, rect.Top));
                }
            }

            var detailRect = new DrawingRectangle(marketListX0, marketDetailY, marketListW, marketDetailH);
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(detailRect.Left - 1, detailRect.Top - 1, detailRect.Width + 2, detailRect.Height + 2), color: new Color4(0, 0, 0, 0.55f));
            _spriteBatch.Draw(_whiteTexture, detailRect, color: new Color4(0.10f, 0.10f, 0.12f, 0.75f));

            if ((uint)selectedIndex < (uint)marketItems.Count)
            {
                MarketItem selected = marketItems[selectedIndex];
                ClientItem ci = selected.Item;
                string name = ci.NameString;
                string who = selected.SellWhoString;

                var iconRect = new DrawingRectangle(detailRect.Left + 4, detailRect.Top + 6, slot - 2, slot - 2);

                int looks = ci.S.Looks;
                if (TryResolveBagItemIconArchive(looks, out string? archivePath, out int imageIndex) && archivePath != null)
                {
                    if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                    {
                        int maxW = iconRect.Width;
                        int maxH = iconRect.Height;

                        int drawW = Math.Min(maxW, tex.Width);
                        int drawH = Math.Min(maxH, tex.Height);

                        int dx = iconRect.Left + (iconRect.Width - drawW) / 2;
                        int dy = iconRect.Top + (iconRect.Height - drawH) / 2;

                        _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, drawW, drawH));
                    }
                    else if (prefetchBudget > 0)
                    {
                        PrefetchArchiveImage(archivePath, imageIndex);
                        prefetchBudget--;
                    }
                }

                int tx = iconRect.Right + 10;
                _uiTextDrawList.Add(new NameDrawInfo($"Selected: {name}", tx, detailRect.Top + 6, new Color4(0.95f, 0.95f, 0.95f, 1)));
                _uiTextDrawList.Add(new NameDrawInfo($"Price:{selected.SellPrice}  {whoHeader}:{who}", tx, detailRect.Top + 24, new Color4(0.92f, 0.92f, 0.92f, 1)));
            }
            else
            {
                _uiTextDrawList.Add(new NameDrawInfo("Select an item.", detailRect.Left + 10, detailRect.Top + 14, new Color4(0.92f, 0.92f, 0.92f, 1)));
            }

            var prevRect = new DrawingRectangle(marketBtnX0 + (marketBtnW + marketBtnGap) * 0, marketButtonsY, marketBtnW, marketButtonH);
            var nextRect = new DrawingRectangle(marketBtnX0 + (marketBtnW + marketBtnGap) * 1, marketButtonsY, marketBtnW, marketButtonH);
            var refreshRect = new DrawingRectangle(marketBtnX0 + (marketBtnW + marketBtnGap) * 2, marketButtonsY, marketBtnW, marketButtonH);
            var findRect = new DrawingRectangle(marketBtnX0 + (marketBtnW + marketBtnGap) * 3, marketButtonsY, marketBtnW, marketButtonH);
            var actionRect = new DrawingRectangle(marketBtnX0 + (marketBtnW + marketBtnGap) * 4, marketButtonsY, marketBtnW, marketButtonH);
            var closeRect = new DrawingRectangle(marketBtnX0 + (marketBtnW + marketBtnGap) * 5, marketButtonsY, marketBtnW, marketButtonH);

            int merchantId = _world.CurrentMerchantId;
            bool canSend = merchantId > 0;

            bool canPrev = top > 0;
            bool canNext = top + marketRows < marketItems.Count || _world.MarketCurrentPage < _world.MarketMaxPage;
            bool canFind = canSend && mode == 1;

            bool hasSelection = (uint)selectedIndex < (uint)marketItems.Count;
            string actionLabel = mode == 2 && hasSelection
                ? marketItems[selectedIndex].SellState == 2 ? "GetPay" : "Cancel"
                : mode == 1 ? "Buy" : "Action";

            bool canAction = canSend && hasSelection;

            DrawMarketButton(prevRect, "Prev", canPrev);
            DrawMarketButton(nextRect, "Next", canNext);
            DrawMarketButton(refreshRect, "Refresh", canSend);
            DrawMarketButton(findRect, "Find", canFind);
            DrawMarketButton(actionRect, actionLabel, canAction);
            DrawMarketButton(closeRect, "Close", enabled: true);

            void DrawMarketButton(DrawingRectangle rect, string label, bool enabled)
            {
                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                               mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                Color4 border = hovered ? new Color4(0.95f, 0.85f, 0.25f, 0.85f) : new Color4(0, 0, 0, 0.55f);
                Color4 fill = enabled
                    ? hovered ? new Color4(0.28f, 0.28f, 0.34f, 0.85f) : new Color4(0.16f, 0.16f, 0.20f, 0.75f)
                    : new Color4(0.12f, 0.12f, 0.14f, 0.55f);

                _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2), color: border);
                _spriteBatch.Draw(_whiteTexture, rect, color: fill);

                Color4 textColor = enabled ? new Color4(0.95f, 0.95f, 0.95f, 1) : new Color4(0.65f, 0.65f, 0.65f, 1);
                _uiTextDrawList.Add(new NameDrawInfo(label, rect.Left + 10, rect.Top + 4, textColor));
            }
        }

        if (_itemDragActive &&
            _itemDragHero == _heroBagView &&
            _itemDragItem.MakeIndex != 0 &&
            float.IsFinite(mouseLogical.X) &&
            float.IsFinite(mouseLogical.Y))
        {
            int looks = _itemDragItem.S.Looks;
            if (TryResolveBagItemIconArchive(looks, out string? archivePath, out int imageIndex) && archivePath != null)
            {
                if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                {
                    int dx = (int)MathF.Round(mouseLogical.X - (tex.Width / 2f));
                    int dy = (int)MathF.Round(mouseLogical.Y - (tex.Height / 2f));
                    var rect = new DrawingRectangle(dx, dy, tex.Width, tex.Height);

                    _spriteBatch.Draw(
                        _whiteTexture,
                        new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2),
                        color: new Color4(1f, 1f, 1f, 0.35f));
                    _spriteBatch.Draw(tex, rect, color: new Color4(1f, 1f, 1f, 0.85f));
                }
                else if (prefetchBudget > 0)
                {
                    PrefetchArchiveImage(archivePath, imageIndex);
                    prefetchBudget--;
                }
            }
        }

        _spriteBatch.End();
        stats = _spriteBatch.Stats;
        return true;

        bool TryDrawClassicBagUi(out SpriteBatchStats classicStats)
        {
            classicStats = default;

            string? wMainPath = TryResolveArchiveFilePath(dataDir, "WMain");
            if (wMainPath != null)
                PrefetchArchiveImage(wMainPath, imageIndex: 371);

            string? itemsPath = TryResolveArchiveFilePath(dataDir, "Items");
            string? items2Path = TryResolveArchiveFilePath(dataDir, "Items2");
            string? shineItemsPath = TryResolveArchiveFilePath(dataDir, "ShineItems");

            int prefetchBudget = 16;

            _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);

            if (_heroBagView)
            {
                string? wMain3Path = TryResolveArchiveFilePath(dataDir, "WMain3");
                const int heroBgIndex = 375;
                if (wMain3Path != null)
                    PrefetchArchiveImage(wMain3Path, heroBgIndex);

                D3D11Texture2D? heroBgTex = null;
                if (wMain3Path != null && TryGetArchiveTexture(wMain3Path, imageIndex: heroBgIndex, out D3D11Texture2D heroBg))
                    heroBgTex = heroBg;

                int heroW = heroBgTex?.Width ?? 228;
                int heroH = heroBgTex?.Height ?? 158;

                if (!_heroBagWindowPosSet)
                {
                    _heroBagWindowPosSet = true;
                    _heroBagWindowPosX = 28;
                    _heroBagWindowPosY = 100;
                }

                int heroX = Math.Clamp(_heroBagWindowPosX, 0, Math.Max(0, view.LogicalSize.Width - heroW));
                int heroY = Math.Clamp(_heroBagWindowPosY, 0, Math.Max(0, view.LogicalSize.Height - heroH));
                _heroBagWindowPosX = heroX;
                _heroBagWindowPosY = heroY;
                DrawingRectangle heroRect = new(heroX, heroY, heroW, heroH);
                _heroBagPanelRect = heroRect;
                if (heroBgTex != null)
                    _spriteBatch.Draw(heroBgTex, heroRect);
                else if (_whiteTexture != null)
                    _spriteBatch.Draw(_whiteTexture, heroRect, color: new Color4(0.12f, 0.12f, 0.16f, 0.92f));

                if (wMainPath != null && TryGetArchiveTexture(wMainPath, imageIndex: 371, out D3D11Texture2D closeTex))
                {
                    DrawingRectangle closeRect = new(heroRect.Left + 208, heroRect.Top + 0, closeTex.Width, closeTex.Height);
                    _heroBagCloseRect = closeRect;
                    _spriteBatch.Draw(closeTex, closeRect);
                }

                const int cols = 5;
                const int rows = 2;
                const int slotW = 36;
                const int slotH = 32;

                int slotX0 = heroRect.Left + 17;
                int slotY0 = heroRect.Top + 14;

                ReadOnlySpan<ClientItem> heroSlots = _world.HeroBagSlots;
                int max = cols * rows;
                for (int i = 0; i < max; i++)
                {
                    int row = i / cols;
                    int col = i % cols;

                    int slotIndex = i;
                    if ((uint)slotIndex >= (uint)heroSlots.Length)
                        continue;

                    DrawingRectangle rect = new(slotX0 + (col * slotW), slotY0 + (row * slotH), slotW, slotH);

                    ClientItem item = heroSlots[slotIndex];
                    if (item.MakeIndex == 0)
                        continue;

                    if (_itemDragActive &&
                        _itemDragHero == _heroBagView &&
                        _itemDragSource == ItemDragSource.Bag &&
                        _itemDragSourceIndex == slotIndex &&
                        item.MakeIndex == _itemDragItem.MakeIndex)
                    {
                        continue;
                    }

                    if (TryResolveItemIcon(item.S.Looks, out string? archivePath, out int imageIndex) && archivePath != null)
                    {
                        if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                        {
                            int drawW = Math.Min(slotW, tex.Width);
                            int drawH = Math.Min(slotH, tex.Height);
                            int dx = rect.Left + (slotW - drawW) / 2;
                            int dy = rect.Top + (slotH - drawH) / 2;
                            _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, drawW, drawH));
                        }
                        else if (prefetchBudget > 0)
                        {
                            PrefetchArchiveImage(archivePath, imageIndex);
                            prefetchBudget--;
                        }
                    }

                    if (item.S.Overlap > 0 && item.Dura > 0)
                        _uiTextDrawList.Add(new NameDrawInfo(item.Dura.ToString(), rect.Left + 3, rect.Top + rect.Height - 18, new Color4(0.95f, 0.95f, 0.95f, 1)));

                    if (float.IsFinite(mouseLogical.X) &&
                        float.IsFinite(mouseLogical.Y) &&
                        mouseLogical.X >= rect.Left &&
                        mouseLogical.X < rect.Right &&
                        mouseLogical.Y >= rect.Top &&
                        mouseLogical.Y < rect.Bottom)
                    {
                        SetTooltip(BuildItemTooltipText(item, "Hero"), new Vector2(rect.Right + 6, rect.Top));
                    }
                }

                DrawDragOverlay();
                _spriteBatch.End();
                classicStats = _spriteBatch.Stats;
                return true;
            }

            string? wMain2Path = TryResolveArchiveFilePath(dataDir, "WMain2");
            const int bagBgIndex = 180;
            if (wMain2Path != null)
                PrefetchArchiveImage(wMain2Path, bagBgIndex);

            D3D11Texture2D? bagBgTex = null;
            if (wMain2Path != null && TryGetArchiveTexture(wMain2Path, imageIndex: bagBgIndex, out D3D11Texture2D bagBg))
                bagBgTex = bagBg;

            int bagW = bagBgTex?.Width ?? 352;
            int bagH = bagBgTex?.Height ?? 295;

            if (!_bagWindowPosSet)
            {
                _bagWindowPosSet = true;
                _bagWindowPosX = 0;
                _bagWindowPosY = 60;
            }

            int bagX = Math.Clamp(_bagWindowPosX, 0, Math.Max(0, view.LogicalSize.Width - bagW));
            int bagY = Math.Clamp(_bagWindowPosY, 0, Math.Max(0, view.LogicalSize.Height - bagH));
            _bagWindowPosX = bagX;
            _bagWindowPosY = bagY;
            DrawingRectangle bagRect = new(bagX, bagY, bagW, bagH);
            _bagPanelRect = bagRect;
            if (bagBgTex != null)
                _spriteBatch.Draw(bagBgTex, bagRect);
            else if (_whiteTexture != null)
                _spriteBatch.Draw(_whiteTexture, bagRect, color: new Color4(0.12f, 0.12f, 0.16f, 0.92f));

            if (wMainPath != null && TryGetArchiveTexture(wMainPath, imageIndex: 371, out D3D11Texture2D closeTex2))
            {
                DrawingRectangle closeRect = new(bagRect.Left + 336, Math.Max(0, bagRect.Top - 1), closeTex2.Width, closeTex2.Height);
                _bagCloseRect = closeRect;
                _spriteBatch.Draw(closeTex2, closeRect);
            }

            if (!_heroBagView && _world.AbilitySet)
                _uiTextDrawList.Add(new NameDrawInfo(_world.MyGold.ToString(), bagRect.Left + 72, bagRect.Top + 212, new Color4(0.95f, 0.95f, 0.95f, 1f)));

            const int bagCols = 8;
            const int bagRows = 5;
            const int slotW2 = 36;
            const int slotH2 = 32;

            int bagSlotX0 = bagRect.Left + 29;
            int bagSlotY0 = bagRect.Top + 41;

            ReadOnlySpan<ClientItem> slots = _world.BagSlots;
            int maxSlots = bagCols * bagRows;
            for (int i = 0; i < maxSlots; i++)
            {
                int row = i / bagCols;
                int col = i % bagCols;

                int slotIndex = 6 + i;
                if ((uint)slotIndex >= (uint)slots.Length)
                    continue;

                DrawingRectangle rect = new(bagSlotX0 + (col * slotW2), bagSlotY0 + (row * slotH2), slotW2, slotH2);

                ClientItem item = slots[slotIndex];
                if (item.MakeIndex == 0)
                    continue;

                if (_itemDragActive &&
                    _itemDragHero == _heroBagView &&
                    _itemDragSource == ItemDragSource.Bag &&
                    _itemDragSourceIndex == slotIndex &&
                    item.MakeIndex == _itemDragItem.MakeIndex)
                {
                    continue;
                }

                if (TryResolveItemIcon(item.S.Looks, out string? archivePath, out int imageIndex) && archivePath != null)
                {
                    if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                    {
                        int drawW = Math.Min(slotW2, tex.Width);
                        int drawH = Math.Min(slotH2, tex.Height);
                        int dx = rect.Left + (slotW2 - drawW) / 2;
                        int dy = rect.Top + (slotH2 - drawH) / 2;
                        _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, drawW, drawH));
                    }
                    else if (prefetchBudget > 0)
                    {
                        PrefetchArchiveImage(archivePath, imageIndex);
                        prefetchBudget--;
                    }
                }

                if (item.S.Overlap > 0 && item.Dura > 0)
                    _uiTextDrawList.Add(new NameDrawInfo(item.Dura.ToString(), rect.Left + 3, rect.Top + rect.Height - 18, new Color4(0.95f, 0.95f, 0.95f, 1)));

                if (float.IsFinite(mouseLogical.X) &&
                    float.IsFinite(mouseLogical.Y) &&
                    mouseLogical.X >= rect.Left &&
                    mouseLogical.X < rect.Right &&
                    mouseLogical.Y >= rect.Top &&
                    mouseLogical.Y < rect.Bottom)
                {
                    SetTooltip(BuildItemTooltipText(item, null), new Vector2(rect.Right + 6, rect.Top));
                }
            }

            DrawDragOverlay();
            _spriteBatch.End();
            classicStats = _spriteBatch.Stats;
            return true;

            void DrawDragOverlay()
            {
                if (_whiteTexture == null)
                    return;

                if (!_itemDragActive ||
                    _itemDragHero != _heroBagView ||
                    _itemDragItem.MakeIndex == 0 ||
                    !float.IsFinite(mouseLogical.X) ||
                    !float.IsFinite(mouseLogical.Y))
                {
                    return;
                }

                if (!TryResolveItemIcon(_itemDragItem.S.Looks, out string? archivePath, out int imageIndex) || archivePath == null)
                    return;

                if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                {
                    int dx = (int)MathF.Round(mouseLogical.X - (tex.Width / 2f));
                    int dy = (int)MathF.Round(mouseLogical.Y - (tex.Height / 2f));
                    var rect = new DrawingRectangle(dx, dy, tex.Width, tex.Height);

                    _spriteBatch.Draw(
                        _whiteTexture,
                        new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2),
                        color: new Color4(1f, 1f, 1f, 0.35f));
                    _spriteBatch.Draw(tex, rect, color: new Color4(1f, 1f, 1f, 0.85f));
                }
                else if (prefetchBudget > 0)
                {
                    PrefetchArchiveImage(archivePath, imageIndex);
                    prefetchBudget--;
                }
            }

            bool TryResolveItemIcon(int looks, out string? archivePath, out int imageIndex)
            {
                archivePath = null;
                imageIndex = 0;

                if (looks < 0)
                    return false;

                if (looks < 10_000)
                {
                    archivePath = itemsPath;
                    imageIndex = looks;
                    return archivePath != null;
                }

                if (looks < 20_000)
                {
                    archivePath = items2Path ?? itemsPath;
                    imageIndex = looks - 10_000;
                    return archivePath != null;
                }

                if (looks < 30_000)
                {
                    archivePath = shineItemsPath ?? itemsPath;
                    imageIndex = looks - 20_000;
                    return archivePath != null;
                }

                archivePath = itemsPath;
                imageIndex = looks % 10_000;
                return archivePath != null;
            }
        }

        static string GetUseItemSlotName(int slot) => slot switch
        {
            Grobal2.U_DRESS => "Dress",
            Grobal2.U_WEAPON => "Weapon",
            Grobal2.U_RIGHTHAND => "RightHand",
            Grobal2.U_NECKLACE => "Necklace",
            Grobal2.U_HELMET => "Helmet",
            Grobal2.U_ARMRINGL => "ArmRingL",
            Grobal2.U_ARMRINGR => "ArmRingR",
            Grobal2.U_RINGL => "RingL",
            Grobal2.U_RINGR => "RingR",
            Grobal2.U_BUJUK => "Bujuk",
            Grobal2.U_BELT => "Belt",
            Grobal2.U_BOOTS => "Boots",
            Grobal2.U_CHARM => "Charm",
            _ => $"Slot{slot}"
        };

        bool TryResolveBagItemIconArchive(int looks, out string? archivePath, out int imageIndex)
        {
            archivePath = null;
            imageIndex = 0;

            if (looks < 0)
                return false;

            if (looks < 10_000)
            {
                archivePath = itemsPath;
                imageIndex = looks;
                return archivePath != null;
            }

            if (looks < 20_000)
            {
                archivePath = items2Path ?? itemsPath;
                imageIndex = looks - 10_000;
                return archivePath != null;
            }

            if (looks < 30_000)
            {
                archivePath = shineItemsPath ?? itemsPath;
                imageIndex = looks - 20_000;
                return archivePath != null;
            }

            archivePath = itemsPath;
            imageIndex = looks % 10_000;
            return archivePath != null;
        }

        bool TryGetArchiveTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
        {
            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
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

        void PrefetchArchiveImage(string archivePath, int imageIndex)
        {
            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
            {
                _ = _wilImageCache.GetImageAsyncFullPath(archivePath, imageIndex);
                return;
            }

            _ = _packDataImageCache.GetImageAsyncFullPath(archivePath, imageIndex);
        }
    }

    private bool TryDrawSettingsWindowUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;
        _settingsPanelRect = null;
        _settingsCloseRect = null;
        _settingsClickPoints.Clear();

        if (!_settingsWindowVisible)
            return false;

        if (_sceneManager.CurrentId != MirSceneId.Play)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        string resourceRoot = GetResourceRootDir();
        string dataDir = Path.Combine(resourceRoot, "Data");
        if (!Directory.Exists(dataDir))
            return false;

        string? opUiPath = TryResolveArchiveFilePath(dataDir, "NewopUI");
        if (opUiPath == null)
            return false;

        const int bgIndex = 40; 
        const int closeIndex = 46; 
        const int cbOffIndex = 48; 
        const int cbOnIndex = 49; 

        PrefetchArchiveImage(opUiPath, bgIndex);
        PrefetchArchiveImage(opUiPath, closeIndex);
        PrefetchArchiveImage(opUiPath, cbOffIndex);
        PrefetchArchiveImage(opUiPath, cbOnIndex);

        D3D11Texture2D? bgTex = null;
        if (TryGetArchiveTexture(opUiPath, bgIndex, out D3D11Texture2D bg))
            bgTex = bg;

        int panelW = bgTex?.Width ?? 416;
        int panelH = bgTex?.Height ?? 261;

        if (!_settingsWindowPosSet)
        {
            _settingsWindowPosSet = true;
            _settingsWindowPosX = Math.Max(0, (view.LogicalSize.Width - panelW) / 2);
            _settingsWindowPosY = Math.Max(0, (view.LogicalSize.Height - panelH) / 2);
        }

        int x0 = Math.Clamp(_settingsWindowPosX, 0, Math.Max(0, view.LogicalSize.Width - panelW));
        int y0 = Math.Clamp(_settingsWindowPosY, 0, Math.Max(0, view.LogicalSize.Height - panelH));
        _settingsWindowPosX = x0;
        _settingsWindowPosY = y0;

        DrawingRectangle panelRect = new(x0, y0, panelW, panelH);
        _settingsPanelRect = panelRect;

        D3D11Texture2D? closeTex = null;
        if (TryGetArchiveTexture(opUiPath, closeIndex, out D3D11Texture2D close))
            closeTex = close;

        D3D11Texture2D? cbOffTex = null;
        if (TryGetArchiveTexture(opUiPath, cbOffIndex, out D3D11Texture2D cbOff))
            cbOffTex = cbOff;

        D3D11Texture2D? cbOnTex = null;
        if (TryGetArchiveTexture(opUiPath, cbOnIndex, out D3D11Texture2D cbOn))
            cbOnTex = cbOn;

        int cbW = cbOffTex?.Width ?? cbOnTex?.Width ?? 16;
        int cbH = cbOffTex?.Height ?? cbOnTex?.Height ?? 16;

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        if (bgTex != null)
            _spriteBatch.Draw(bgTex, panelRect);
        else
            _spriteBatch.Draw(_whiteTexture, panelRect, color: new Color4(0.12f, 0.12f, 0.16f, 0.92f));

        if (closeTex != null)
        {
            DrawingRectangle closeRect = new(panelRect.Left + 394, panelRect.Top + 1, closeTex.Width, closeTex.Height);
            _settingsCloseRect = closeRect;
            _spriteBatch.Draw(closeTex, closeRect);
        }
        else
        {
            DrawingRectangle closeRect = new(panelRect.Right - 24, panelRect.Top + 6, 16, 16);
            _settingsCloseRect = closeRect;
            _spriteBatch.Draw(_whiteTexture, closeRect, color: new Color4(0.85f, 0.35f, 0.35f, 0.95f));
        }

        int listX = panelRect.Left + 36;
        int listY = panelRect.Top + 60;
        const int rowGapY = 24;

        AddToggle(0, SettingsClickKind.ShowActorName, "Show Actor Name", _showActorNames);
        AddToggle(1, SettingsClickKind.DuraWarning, "Dura Warning", _duraWarning);
        AddToggle(2, SettingsClickKind.AutoAttack, "Auto Attack", _autoAttack);
        AddToggle(3, SettingsClickKind.ShowDropItems, "Show Drop Items", _showDropItems);
        AddToggle(4, SettingsClickKind.HideDeathBody, "Hide Death Body", _hideDeathBody);

        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        _uiTextDrawList.Add(new NameDrawInfo("Settings (F12)", panelRect.Left + 18, panelRect.Top + 6, new Color4(0.95f, 0.95f, 0.95f, 1f)));
        _uiTextDrawList.Add(new NameDrawInfo("Click: toggle    Drag: top bar    Right-click/Esc: close", panelRect.Left + 18, panelRect.Bottom - 22, new Color4(0.85f, 0.85f, 0.85f, 1f)));

        return true;

        void AddToggle(int row, SettingsClickKind kind, string label, bool enabled)
        {
            int y = listY + (row * rowGapY);
            DrawingRectangle cbRect = new(listX, y, cbW, cbH);
            _settingsClickPoints.Add(new SettingsClickPoint(cbRect, kind));

            D3D11Texture2D? tex = enabled ? cbOnTex : cbOffTex;
            if (tex != null)
            {
                _spriteBatch.Draw(tex, new DrawingRectangle(cbRect.Left, cbRect.Top, tex.Width, tex.Height));
            }
            else
            {
                _spriteBatch.Draw(
                    _whiteTexture,
                    cbRect,
                    color: enabled ? new Color4(0.55f, 0.95f, 0.55f, 0.85f) : new Color4(0.35f, 0.35f, 0.35f, 0.85f));
            }

            _uiTextDrawList.Add(new NameDrawInfo(label, cbRect.Right + 8, cbRect.Top - 2, new Color4(0.92f, 0.92f, 0.92f, 1f)));
        }

        bool TryGetArchiveTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
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

        void PrefetchArchiveImage(string archivePath, int imageIndex)
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
    }

    private bool TryDrawMerchantUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!_world.MerchantDialogOpen)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        Vector2 mouseLogical = new(float.NegativeInfinity, float.NegativeInfinity);
        try
        {
            System.Drawing.Point mouseClient = _renderControl.PointToClient(Cursor.Position);
            if (view.ViewportRect.Contains(mouseClient))
            {
                mouseLogical = new Vector2(
                    (mouseClient.X - view.Offset.X) / Math.Max(0.0001f, view.Scale.X),
                    (mouseClient.Y - view.Offset.Y) / Math.Max(0.0001f, view.Scale.Y));
            }
        }
        catch
        {
            
        }

        const int pad = 10;
        const int header = 22;
        const int lineH = 18;

        string npcName = _world.MerchantNpcName.Trim();
        string raw = _world.MerchantSaying ?? string.Empty;

        int merchantId = _world.CurrentMerchantId;
        if (merchantId != _merchantDialogLastMerchantId ||
            !string.Equals(_merchantDialogLastSaying, raw, StringComparison.Ordinal))
        {
            _merchantDialogTopLine = 0;
            _merchantDialogLastMerchantId = merchantId;
            _merchantDialogLastSaying = raw;
        }

        var dialogLines = new List<string>(32);
        foreach (string segment in raw.Split('\\'))
        {
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            dialogLines.Add(segment.Trim());
        }

        if (dialogLines.Count == 0)
            dialogLines.Add(string.Empty);

        string resourceRoot = GetResourceRootDir();
        string dataDir = Path.Combine(resourceRoot, "Data");

        string? wMainPath = Directory.Exists(dataDir) ? TryResolveArchiveFilePath(dataDir, "WMain") : null;

        bool classicDlg = false;
        D3D11Texture2D? merchantBg = null;
        if (wMainPath != null)
        {
            int backslashCount = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i] == '\\')
                    backslashCount++;
            }

            int bgIndex = backslashCount < 15 ? 384 : 402;
            PrefetchArchiveImage(wMainPath, bgIndex);
            if (TryGetArchiveTexture(wMainPath, bgIndex, out D3D11Texture2D bg))
            {
                classicDlg = true;
                merchantBg = bg;
            }
        }

        int panelX;
        int panelY;
        int panelW;
        int panelH;
        int textX0;
        int textY0;
        int lineHActual;
        bool drawTitle = true;

        if (classicDlg && merchantBg != null)
        {
            panelW = merchantBg.Width;
            panelH = merchantBg.Height;
        }
        else
        {
            panelW = Math.Clamp(view.LogicalSize.Width / 2, 360, 520);
            int maxPanelH = Math.Max(180, view.LogicalSize.Height - 160);
            int maxVisibleLinesTmp = Math.Clamp((maxPanelH - (pad * 2) - header) / lineH, 6, 24);
            int visibleLinesTmp = Math.Min(dialogLines.Count, maxVisibleLinesTmp);
            panelH = (pad * 2) + header + (visibleLinesTmp * lineH);
        }

        if (!_merchantDialogPosSet)
        {
            _merchantDialogPosSet = true;
            _merchantDialogPosX = 0;
            _merchantDialogPosY = 0;
        }

        panelX = Math.Clamp(_merchantDialogPosX, 0, Math.Max(0, view.LogicalSize.Width - panelW));
        panelY = Math.Clamp(_merchantDialogPosY, 0, Math.Max(0, view.LogicalSize.Height - panelH));
        _merchantDialogPosX = panelX;
        _merchantDialogPosY = panelY;

        if (classicDlg && merchantBg != null)
        {
            textX0 = panelX + 30;
            textY0 = panelY + 20;
            lineHActual = 16;
            drawTitle = false;
        }
        else
        {
            textX0 = panelX + pad;
            textY0 = panelY + pad + header;
            lineHActual = lineH;
        }

        int maxVisibleLines = Math.Max(1, (panelH - (textY0 - panelY) - 8) / Math.Max(1, lineHActual));
        int visibleLines = Math.Min(dialogLines.Count, maxVisibleLines);
        int maxTop = Math.Max(0, dialogLines.Count - visibleLines);
        _merchantDialogTopLine = Math.Clamp(_merchantDialogTopLine, 0, maxTop);
        _merchantDialogLastVisibleLines = visibleLines;
        _merchantDialogLastTotalLines = dialogLines.Count;

        _merchantDialogPanelRect = new DrawingRectangle(panelX, panelY, panelW, panelH);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);

        if (classicDlg && merchantBg != null)
        {
            _spriteBatch.Draw(merchantBg, new DrawingRectangle(panelX, panelY, panelW, panelH));

            _merchantDialogCloseRect = null;
            if (wMainPath != null)
            {
                PrefetchArchiveImage(wMainPath, 64);
                if (TryGetArchiveTexture(wMainPath, 64, out D3D11Texture2D closeTex))
                {
                    DrawingRectangle closeRect = new(panelX + 399, panelY + 1, closeTex.Width, closeTex.Height);
                    _merchantDialogCloseRect = closeRect;
                    _spriteBatch.Draw(closeTex, closeRect);
                }
            }
        }
        else
        {
            _merchantDialogCloseRect = null;
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panelX, panelY, panelW, panelH), color: new Color4(0, 0, 0, 0.55f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panelX + 1, panelY + 1, panelW - 2, panelH - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panelX + 1, panelY + 1, panelW - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        }

        int x0 = textX0;
        int clickH = Math.Max(1, classicDlg ? 14 : lineHActual);

        int resolvedTop = _merchantDialogTopLine;
        if (resolvedTop > maxTop)
            resolvedTop = maxTop;

        for (int i = 0; i < visibleLines; i++)
        {
            int idx = resolvedTop + i;
            if ((uint)idx >= (uint)dialogLines.Count)
                break;

            int y = textY0 + (i * lineHActual);
            DrawMerchantDialogLine(dialogLines[idx], x0, y);
        }

        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        if (drawTitle)
        {
            string title = string.IsNullOrWhiteSpace(npcName) ? $"NPC {merchantId}" : npcName;
            string page = dialogLines.Count > visibleLines
                ? $" ({resolvedTop + 1}-{resolvedTop + visibleLines}/{dialogLines.Count})"
                : string.Empty;
            _uiTextDrawList.Add(new NameDrawInfo(title + page, panelX + pad, panelY + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));
        }

        if (TryDrawMerchantMenuUi(frame, view, mouseLogical, out SpriteBatchStats menuStats))
        {
            stats = new SpriteBatchStats(
                stats.DrawCalls + menuStats.DrawCalls,
                stats.TextureBinds + menuStats.TextureBinds,
                stats.Sprites + menuStats.Sprites,
                stats.ScissorChanges + menuStats.ScissorChanges);
        }

        if (TryDrawMerchantSellUi(frame, view, mouseLogical, out SpriteBatchStats sellStats))
        {
            stats = new SpriteBatchStats(
                stats.DrawCalls + sellStats.DrawCalls,
                stats.TextureBinds + sellStats.TextureBinds,
                stats.Sprites + sellStats.Sprites,
                stats.ScissorChanges + sellStats.ScissorChanges);
        }

        return true;

        void DrawMerchantDialogLine(string line, int baseX, int baseY)
        {
            int sx = 0;
            string data = line ?? string.Empty;

            while (data.Length > 0)
            {
                int lt = data.IndexOf('<');
                if (lt < 0)
                {
                    DrawPlain(data);
                    break;
                }

                int gt = data.IndexOf('>', lt + 1);
                if (gt < 0)
                {
                    DrawPlain(data);
                    break;
                }

                if (lt > 0)
                    DrawPlain(data[..lt]);

                string tag = data.Substring(lt + 1, gt - lt - 1).Trim();
                data = gt + 1 < data.Length ? data[(gt + 1)..] : string.Empty;

                if (tag.Length == 0)
                    continue;

                if (tag.Contains("COLOR=", StringComparison.OrdinalIgnoreCase))
                {
                    (Color4 color, string text) = ParseColorTag(tag);
                    if (!string.IsNullOrEmpty(text))
                        DrawText(text, color);
                    continue;
                }

                int slash = tag.IndexOf('/');
                if (slash <= 0 || slash + 1 >= tag.Length)
                    continue;

                string label = tag[..slash].Trim();
                string cmd = tag[(slash + 1)..].Trim();
                if (label.Length == 0 || cmd.Length == 0)
                    continue;

                int w = MeasureUiTextWidth(label);
                if (w <= 0)
                    continue;

                var rect = new DrawingRectangle(baseX + sx, baseY, w, clickH);
                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                               mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                _merchantClickPoints.Add(new MerchantClickPoint(rect, cmd));

                Color4 cmdColor = hovered ? new Color4(1.0f, 0.85f, 0.25f, 1f) : new Color4(1.0f, 1.0f, 0.0f, 1f);
                _uiTextDrawList.Add(new NameDrawInfo(label, baseX + sx, baseY, cmdColor));

                int underlineY = baseY + clickH - 1;
                if (w > 0 && underlineY >= 0)
                    _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(baseX + sx, underlineY, w, 1), color: cmdColor);

                if (hovered && string.IsNullOrWhiteSpace(_uiTooltipText))
                    SetTooltip(cmd, new Vector2(rect.Left, rect.Bottom + 2));

                sx += w;
            }

            void DrawPlain(string text)
            {
                if (string.IsNullOrEmpty(text))
                    return;

                _uiTextDrawList.Add(new NameDrawInfo(text, baseX + sx, baseY, new Color4(0.92f, 0.92f, 0.92f, 1f)));
                sx += MeasureUiTextWidth(text);
            }

            void DrawText(string text, Color4 color)
            {
                if (string.IsNullOrEmpty(text))
                    return;

                _uiTextDrawList.Add(new NameDrawInfo(text, baseX + sx, baseY, color));
                sx += MeasureUiTextWidth(text);
            }
        }

        int MeasureUiTextWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            if (_textRenderer == null)
                return text.Length * 8;

            return Math.Max(0, (int)MathF.Round(_textRenderer.MeasureTextWidth(text)));
        }

        static (Color4 Color, string Text) ParseColorTag(string tag)
        {
            int idx = tag.IndexOf("COLOR=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return (new Color4(1, 1, 1, 1), string.Empty);

            string rest = tag[idx..].Trim();
            int split = rest.IndexOfAny([' ', '\t', ',']);
            if (split < 0)
                return (new Color4(1, 1, 1, 1), string.Empty);

            string token = rest[..split].Trim();
            string text = split + 1 < rest.Length ? rest[(split + 1)..].Trim() : string.Empty;

            string colorSpec = token.Length > 6 ? token[6..].Trim() : string.Empty;
            Color4 color = ParseDelphiColor(colorSpec);
            return (color, text);
        }

        static Color4 ParseDelphiColor(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec))
                return new Color4(1, 1, 1, 1);

            spec = spec.Trim();

            if (spec.StartsWith('#'))
            {
                if (int.TryParse(spec.AsSpan(1), out int value))
                    return ColorFromDelphiColorRef(value);
            }

            if (spec.StartsWith('$'))
            {
                if (int.TryParse(spec.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int value))
                    return ColorFromDelphiColorRef(value);
            }

            string key = spec.Replace(" ", string.Empty).Trim();
            return key.ToLowerInvariant() switch
            {
                "clwhite" => new Color4(1, 1, 1, 1),
                "clblack" => new Color4(0, 0, 0, 1),
                "clred" => new Color4(1, 0, 0, 1),
                "clyellow" => new Color4(1, 1, 0, 1),
                "clltgray" => new Color4(0.78f, 0.78f, 0.78f, 1),
                "cldkgray" => new Color4(0.35f, 0.35f, 0.35f, 1),
                _ => new Color4(1, 1, 1, 1)
            };
        }

        static Color4 ColorFromDelphiColorRef(int value)
        {
            int r = value & 0xFF;
            int g = (value >> 8) & 0xFF;
            int b = (value >> 16) & 0xFF;
            return new Color4(r / 255f, g / 255f, b / 255f, 1f);
        }

        bool TryGetArchiveTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
        {
            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
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

        void PrefetchArchiveImage(string archivePath, int imageIndex)
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
    }

    private bool TryDrawMerchantMenuUi(D3D11Frame frame, D3D11ViewTransform view, Vector2 mouseLogical, out SpriteBatchStats stats)
    {
        stats = default;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        MirMerchantMode mode = _world.MerchantMode;
        if (mode is not MirMerchantMode.Buy and not MirMerchantMode.MakeDrug and not MirMerchantMode.DetailMenu)
            return false;

        int merchantId = _world.CurrentMerchantId;
        if (merchantId <= 0)
            return false;

        IReadOnlyList<MirMerchantGoods> goods = _world.MerchantGoods;
        const int maxLines = 10;
        _merchantMenuSystem.Sync(merchantId, mode, goods.Count, maxLines);

        if (TryDrawClassicMerchantMenuUi(out SpriteBatchStats classicStats))
        {
            stats = classicStats;
            return true;
        }

        const int pad = 10;
        const int header = 22;
        const int lineH = 18;
        const int buttonGapY = 10;
        const int buttonH = 22;
        const int buttonGapX = 6;
        const int btnCount = 3;

        DrawingRectangle baseRect = _merchantDialogPanelRect ?? new DrawingRectangle(16, view.LogicalSize.Height - 200, Math.Clamp(view.LogicalSize.Width / 2, 360, 520), 200);
        int panelW = baseRect.Width;
        int panelH = (pad * 2) + header + (maxLines * lineH) + buttonGapY + buttonH;
        int panelX = baseRect.Left;
        int panelY = Math.Max(8, baseRect.Top - panelH - 12);

        _merchantMenuPanelRect = new DrawingRectangle(panelX, panelY, panelW, panelH);

        int topIndex = _merchantMenuSystem.TopIndex;
        int selectedIndex = _merchantMenuSystem.SelectedIndex;

        int contentW = Math.Max(0, panelW - (pad * 2));
        int listX0 = panelX + pad;
        int listY0 = panelY + pad + header;

        int buttonsY = listY0 + (maxLines * lineH) + buttonGapY;
        int btnW = btnCount > 0 ? (contentW - (buttonGapX * (btnCount - 1))) / btnCount : 0;

        var prevRect = new DrawingRectangle(listX0, buttonsY, btnW, buttonH);
        var actionRect = new DrawingRectangle(listX0 + (btnW + buttonGapX), buttonsY, btnW, buttonH);
        var nextRect = new DrawingRectangle(listX0 + (btnW + buttonGapX) * 2, buttonsY, btnW, buttonH);

        _merchantMenuClickPoints.Clear();
        if (btnW > 0)
        {
            _merchantMenuClickPoints.Add(new MerchantMenuClickPoint(prevRect, MerchantMenuClickKind.Prev, Index: -1));
            _merchantMenuClickPoints.Add(new MerchantMenuClickPoint(actionRect, MerchantMenuClickKind.Action, Index: -1));
            _merchantMenuClickPoints.Add(new MerchantMenuClickPoint(nextRect, MerchantMenuClickKind.Next, Index: -1));
        }

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panelX, panelY, panelW, panelH), color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panelX + 1, panelY + 1, panelW - 2, panelH - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panelX + 1, panelY + 1, panelW - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));

        int hoveredIndex = -1;
        for (int i = 0; i < maxLines; i++)
        {
            int idx = topIndex + i;
            int y = listY0 + (i * lineH);
            var rect = new DrawingRectangle(listX0, y - 2, contentW, lineH);

            bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                           mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

            if (idx >= 0 && idx < goods.Count)
            {
                _merchantMenuClickPoints.Add(new MerchantMenuClickPoint(rect, MerchantMenuClickKind.Item, idx));

                bool selected = idx == selectedIndex;
                if (selected)
                    _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0.22f, 0.34f, 0.65f, 0.55f));
                else if (hovered)
                    _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(1.0f, 0.85f, 0.25f, 0.20f));

                if (hovered)
                    hoveredIndex = idx;
            }
            else if (hovered)
            {
                hoveredIndex = -1;
            }
        }

        if (btnW > 0)
        {
            DrawButton(prevRect);
            DrawButton(actionRect);
            DrawButton(nextRect);
        }

        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        string modeLabel = mode switch
        {
            MirMerchantMode.Buy => "Buy",
            MirMerchantMode.MakeDrug => "MakeDrug",
            MirMerchantMode.DetailMenu => "Detail",
            _ => mode.ToString()
        };

        string title = mode == MirMerchantMode.DetailMenu
            ? $"Shop: {modeLabel} (Top={Math.Max(0, _world.MerchantMenuTopLine)})"
            : $"Shop: {modeLabel} ({goods.Count})";

        _uiTextDrawList.Add(new NameDrawInfo(title, panelX + pad, panelY + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));

        int nameX = listX0 + 4;
        int priceX = nameX + Math.Max(0, contentW - 140);

        for (int i = 0; i < maxLines; i++)
        {
            int idx = topIndex + i;
            if (idx < 0 || idx >= goods.Count)
                continue;

            MirMerchantGoods g = goods[idx];
            if (string.IsNullOrWhiteSpace(g.Name))
                continue;

            int y = listY0 + (i * lineH);

            bool selected = idx == selectedIndex;
            bool hovered = idx == hoveredIndex;

            Color4 color;
            if (selected)
                color = new Color4(1.0f, 0.45f, 0.45f, 1);
            else if (hovered)
                color = new Color4(1.0f, 0.85f, 0.25f, 1);
            else if (g.SubMenu > 0 && g.SubMenu != 2)
                color = new Color4(0.98f, 0.88f, 0.35f, 1);
            else
                color = new Color4(0.92f, 0.92f, 0.92f, 1);

            string name = g.SubMenu > 0 && g.SubMenu != 2 ? $"> {g.Name}" : g.Name;
            string price = g.Price > 0 ? g.Price.ToString() : string.Empty;

            _uiTextDrawList.Add(new NameDrawInfo(name, nameX, y, color));
            if (!string.IsNullOrWhiteSpace(price))
                _uiTextDrawList.Add(new NameDrawInfo(price, priceX, y, color));

            if (hovered && string.IsNullOrWhiteSpace(_uiTooltipText))
            {
                string tip = g.SubMenu > 0 && g.SubMenu != 2
                    ? $"Open: {g.Name}"
                    : $"Buy: {g.Name}  Price:{g.Price}";

                SetTooltip(tip, new Vector2(panelX + pad, y + lineH + 2));
            }
        }

        if (btnW > 0)
        {
            Color4 btnColor = new(0.95f, 0.95f, 0.95f, 1);
            _uiTextDrawList.Add(new NameDrawInfo("Prev", prevRect.Left + 10, prevRect.Top + 4, btnColor));
            _uiTextDrawList.Add(new NameDrawInfo(mode == MirMerchantMode.MakeDrug ? "Make" : "Action", actionRect.Left + 10, actionRect.Top + 4, btnColor));
            _uiTextDrawList.Add(new NameDrawInfo("Next", nextRect.Left + 10, nextRect.Top + 4, btnColor));
        }

        return true;

        bool TryDrawClassicMerchantMenuUi(out SpriteBatchStats classicStats)
        {
            classicStats = default;

            string resourceRoot = GetResourceRootDir();
            string dataDir = Path.Combine(resourceRoot, "Data");
            if (!Directory.Exists(dataDir))
                return false;

            string? wMainPath = TryResolveArchiveFilePath(dataDir, "WMain");
            if (wMainPath == null)
                return false;

            PrefetchArchiveImage(wMainPath, 385);
            PrefetchArchiveImage(wMainPath, 388);
            PrefetchArchiveImage(wMainPath, 387);
            PrefetchArchiveImage(wMainPath, 386);
            PrefetchArchiveImage(wMainPath, 64);

            if (!TryGetArchiveTexture(wMainPath, imageIndex: 385, out D3D11Texture2D bg))
                return false;

            D3D11Texture2D? prevTex = null;
            if (TryGetArchiveTexture(wMainPath, imageIndex: 388, out D3D11Texture2D tmpPrev))
                prevTex = tmpPrev;

            D3D11Texture2D? nextTex = null;
            if (TryGetArchiveTexture(wMainPath, imageIndex: 387, out D3D11Texture2D tmpNext))
                nextTex = tmpNext;

            D3D11Texture2D? buyTex = null;
            if (TryGetArchiveTexture(wMainPath, imageIndex: 386, out D3D11Texture2D tmpBuy))
                buyTex = tmpBuy;

            D3D11Texture2D? closeTex = null;
            if (TryGetArchiveTexture(wMainPath, imageIndex: 64, out D3D11Texture2D tmpClose))
                closeTex = tmpClose;

            int panelX = 0;
            int panelY = 176;

            if (panelX + bg.Width > view.LogicalSize.Width)
                panelX = Math.Max(0, view.LogicalSize.Width - bg.Width);
            if (panelY + bg.Height > view.LogicalSize.Height)
                panelY = Math.Max(0, view.LogicalSize.Height - bg.Height);

            _merchantMenuPanelRect = new DrawingRectangle(panelX, panelY, bg.Width, bg.Height);

            int topIndex = _merchantMenuSystem.TopIndex;
            int selectedIndex = _merchantMenuSystem.SelectedIndex;

            const int listX0 = 14;
            const int listY0 = 32;
            const int listW = 266;
            const int lineHClassic = 13;

            _merchantMenuClickPoints.Clear();

            if (prevTex != null)
            {
                var prevRect = new DrawingRectangle(panelX + 43, panelY + 175, prevTex.Width, prevTex.Height);
                _merchantMenuClickPoints.Add(new MerchantMenuClickPoint(prevRect, MerchantMenuClickKind.Prev, Index: -1));
            }

            if (nextTex != null)
            {
                var nextRect = new DrawingRectangle(panelX + 90, panelY + 175, nextTex.Width, nextTex.Height);
                _merchantMenuClickPoints.Add(new MerchantMenuClickPoint(nextRect, MerchantMenuClickKind.Next, Index: -1));
            }

            if (buyTex != null)
            {
                var buyRect = new DrawingRectangle(panelX + 215, panelY + 171, buyTex.Width, buyTex.Height);
                _merchantMenuClickPoints.Add(new MerchantMenuClickPoint(buyRect, MerchantMenuClickKind.Action, Index: -1));
            }

            if (closeTex != null)
            {
                var closeRect = new DrawingRectangle(panelX + 291, panelY + 0, closeTex.Width, closeTex.Height);
                _merchantMenuClickPoints.Add(new MerchantMenuClickPoint(closeRect, MerchantMenuClickKind.Close, Index: -1));
            }

            int hoveredIndex = -1;
            for (int i = 0; i < maxLines; i++)
            {
                int idx = topIndex + i;
                var rect = new DrawingRectangle(panelX + listX0, panelY + listY0 + (i * lineHClassic), listW, lineHClassic);

                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right &&
                               mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;

                if (idx >= 0 && idx < goods.Count)
                {
                    _merchantMenuClickPoints.Add(new MerchantMenuClickPoint(rect, MerchantMenuClickKind.Item, idx));
                    if (hovered)
                        hoveredIndex = idx;
                }
                else if (hovered)
                {
                    hoveredIndex = -1;
                }
            }

            _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
            _spriteBatch.Draw(bg, new DrawingRectangle(panelX, panelY, bg.Width, bg.Height));

            if (prevTex != null)
                _spriteBatch.Draw(prevTex, new DrawingRectangle(panelX + 43, panelY + 175, prevTex.Width, prevTex.Height));
            if (nextTex != null)
                _spriteBatch.Draw(nextTex, new DrawingRectangle(panelX + 90, panelY + 175, nextTex.Width, nextTex.Height));
            if (buyTex != null)
                _spriteBatch.Draw(buyTex, new DrawingRectangle(panelX + 215, panelY + 171, buyTex.Width, buyTex.Height));
            if (closeTex != null)
                _spriteBatch.Draw(closeTex, new DrawingRectangle(panelX + 291, panelY + 0, closeTex.Width, closeTex.Height));

            _spriteBatch.End();
            classicStats = _spriteBatch.Stats;

            Color4 fontColor = new(0.92f, 0.92f, 0.92f, 1f);
            Color4 selectedColor = new(1.0f, 0.35f, 0.35f, 1f);

            _uiTextDrawList.Add(new NameDrawInfo("物品列表", panelX + 19, panelY + 11, fontColor));
            _uiTextDrawList.Add(new NameDrawInfo("费用", panelX + 156, panelY + 11, fontColor));
            _uiTextDrawList.Add(new NameDrawInfo("持久", panelX + 245, panelY + 11, fontColor));

            for (int i = 0; i < maxLines; i++)
            {
                int idx = topIndex + i;
                if (idx < 0 || idx >= goods.Count)
                    continue;

                MirMerchantGoods g = goods[idx];
                if (string.IsNullOrWhiteSpace(g.Name))
                    continue;

                int y = panelY + listY0 + (i * lineHClassic);
                Color4 color = idx == selectedIndex ? selectedColor : fontColor;

                _uiTextDrawList.Add(new NameDrawInfo(g.Name, panelX + 19, y, color));

                if (g.Price > 0)
                    _uiTextDrawList.Add(new NameDrawInfo(g.Price.ToString(), panelX + 156, y, color));

                if (g.Grade == -1)
                    _uiTextDrawList.Add(new NameDrawInfo("-->>", panelX + 245, y, color));
                else if (g.Grade > 0)
                    _uiTextDrawList.Add(new NameDrawInfo(g.Grade.ToString(), panelX + 245, y, color));
            }

            if (hoveredIndex >= 0 &&
                hoveredIndex < goods.Count &&
                string.IsNullOrWhiteSpace(_uiTooltipText))
            {
                MirMerchantGoods hovered = goods[hoveredIndex];
                string tip = hovered.SubMenu > 0 && hovered.SubMenu != 2
                    ? $"Open: {hovered.Name}"
                    : $"Buy: {hovered.Name}  Price:{hovered.Price}";

                SetTooltip(tip, new Vector2(panelX + 19, panelY + listY0 + ((hoveredIndex - topIndex) * lineHClassic) + lineHClassic + 2));
            }

            return true;

            void PrefetchArchiveImage(string archivePath, int imageIndex)
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

            bool TryGetArchiveTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
            {
                if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                    archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
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
        }

        void DrawButton(DrawingRectangle rect)
        {
            _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0.08f, 0.08f, 0.10f, 0.65f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, Math.Max(0, rect.Width - 2), Math.Max(0, rect.Height - 2)), color: new Color4(0.16f, 0.16f, 0.20f, 0.85f));
        }
    }

    private bool TryDrawMerchantSellUi(D3D11Frame frame, D3D11ViewTransform view, Vector2 mouseLogical, out SpriteBatchStats stats)
    {
        stats = default;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        MirMerchantMode mode = _world.MerchantMode;
        if (mode is not MirMerchantMode.Sell and not MirMerchantMode.Repair)
        {
            _merchantSellItem = default;
            _merchantSellLastMerchantId = 0;
            _merchantSellLastMode = MirMerchantMode.None;
            return false;
        }

        int merchantId = _world.CurrentMerchantId;
        if (merchantId <= 0)
            return false;

        if (!_bagWindowVisible)
        {
            _bagWindowVisible = true;
            _heroBagView = false;
        }

        if (merchantId != _merchantSellLastMerchantId || mode != _merchantSellLastMode)
        {
            _merchantSellItem = default;
            _merchantSellLastMerchantId = merchantId;
            _merchantSellLastMode = mode;

            _world.ApplySellPriceQuote(0);
            _world.ApplyRepairCostQuote(0);
        }

        string resourceRoot = GetResourceRootDir();
        string dataDir = Path.Combine(resourceRoot, "Data");
        if (!Directory.Exists(dataDir))
            return false;

        string? wMainPath = TryResolveArchiveFilePath(dataDir, "WMain");
        if (wMainPath != null)
        {
            PrefetchArchiveImage(wMainPath, 392);
            PrefetchArchiveImage(wMainPath, 393);
            PrefetchArchiveImage(wMainPath, 64);
        }

        bool classic = false;
        D3D11Texture2D? bg = null;
        D3D11Texture2D? okTex = null;
        D3D11Texture2D? closeTex = null;

        if (wMainPath != null && TryGetArchiveTexture(wMainPath, imageIndex: 392, out D3D11Texture2D bgTmp))
        {
            classic = true;
            bg = bgTmp;

            if (TryGetArchiveTexture(wMainPath, imageIndex: 393, out D3D11Texture2D okTmp))
                okTex = okTmp;
            if (TryGetArchiveTexture(wMainPath, imageIndex: 64, out D3D11Texture2D closeTmp))
                closeTex = closeTmp;
        }

        int panelW = classic && bg != null ? bg.Width : 170;
        int panelH = classic && bg != null ? bg.Height : 200;

        int panelX = 260;
        int panelY = 176;

        if (panelX + panelW > view.LogicalSize.Width)
            panelX = Math.Max(0, view.LogicalSize.Width - panelW);
        if (panelY + panelH > view.LogicalSize.Height)
            panelY = Math.Max(0, view.LogicalSize.Height - panelH);

        _merchantSellPanelRect = new DrawingRectangle(panelX, panelY, panelW, panelH);

        _merchantSellOkRect = okTex != null
            ? new DrawingRectangle(panelX + 85, panelY + 150, okTex.Width, okTex.Height)
            : null;
        _merchantSellCloseRect = closeTex != null
            ? new DrawingRectangle(panelX + 115, panelY + 0, closeTex.Width, closeTex.Height)
            : null;
        _merchantSellSpotRect = new DrawingRectangle(panelX + 27, panelY + 67, 61, 52);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);

        if (classic && bg != null)
        {
            _spriteBatch.Draw(bg, new DrawingRectangle(panelX, panelY, bg.Width, bg.Height));

            if (okTex != null && _merchantSellOkRect is { } okRect)
                _spriteBatch.Draw(okTex, okRect);
            if (closeTex != null && _merchantSellCloseRect is { } closeRect)
                _spriteBatch.Draw(closeTex, closeRect);
        }
        else
        {
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panelX, panelY, panelW, panelH), color: new Color4(0, 0, 0, 0.55f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(panelX + 1, panelY + 1, Math.Max(0, panelW - 2), Math.Max(0, panelH - 2)), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        }

        if (_merchantSellItem.MakeIndex != 0 &&
            _merchantSellSpotRect is { } spotRect &&
            TryResolveItemIcon(_merchantSellItem.S.Looks, out string? iconArchive, out int iconIndex) &&
            iconArchive != null)
        {
            if (TryGetArchiveTexture(iconArchive, iconIndex, out D3D11Texture2D iconTex))
            {
                int dx = spotRect.Left + ((spotRect.Width - iconTex.Width) / 2);
                int dy = spotRect.Top + ((spotRect.Height - iconTex.Height) / 2);
                _spriteBatch.Draw(iconTex, new DrawingRectangle(dx, dy, iconTex.Width, iconTex.Height));
            }
        }

        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        string actionLabel = mode == MirMerchantMode.Sell ? "出售: " : "修理: ";
        int quote = mode == MirMerchantMode.Sell ? _world.LastSellPriceQuote : _world.LastRepairCostQuote;
        string quoteText = quote > 0 ? quote.ToString() : string.Empty;
        _uiTextDrawList.Add(new NameDrawInfo(actionLabel + quoteText, panelX + 8, panelY + 6, new Color4(0.92f, 0.92f, 0.92f, 1f)));

        if (_merchantSellItem.MakeIndex != 0 &&
            _merchantSellItem.S.Overlap > 0 &&
            _merchantSellItem.Dura > 0 &&
            _merchantSellSpotRect is { } spotRect2)
        {
            _uiTextDrawList.Add(new NameDrawInfo(_merchantSellItem.Dura.ToString(), spotRect2.Left + 21, spotRect2.Top + 15, new Color4(0.82f, 0.82f, 0.82f, 1f)));
        }

        if (_merchantSellItem.MakeIndex != 0 &&
            _merchantSellSpotRect is { } spotRect3 &&
            string.IsNullOrWhiteSpace(_uiTooltipText) &&
            mouseLogical.X >= spotRect3.Left &&
            mouseLogical.X < spotRect3.Right &&
            mouseLogical.Y >= spotRect3.Top &&
            mouseLogical.Y < spotRect3.Bottom)
        {
            SetTooltip(BuildItemTooltipText(_merchantSellItem, null), new Vector2(spotRect3.Right + 6, spotRect3.Top));
        }

        return true;

        bool TryResolveItemIcon(int looks, out string? archivePath, out int imageIndex)
        {
            archivePath = null;
            imageIndex = 0;

            if (looks < 0)
                return false;

            string? itemsPath = TryResolveArchiveFilePath(dataDir, "Items");
            if (itemsPath == null)
                return false;

            string? items2Path = TryResolveArchiveFilePath(dataDir, "Items2");
            string? shineItemsPath = TryResolveArchiveFilePath(dataDir, "ShineItems");

            if (looks < 10_000)
            {
                archivePath = itemsPath;
                imageIndex = looks;
                return true;
            }

            if (looks < 20_000)
            {
                archivePath = items2Path ?? itemsPath;
                imageIndex = looks - 10_000;
                return archivePath != null;
            }

            if (looks < 30_000)
            {
                archivePath = shineItemsPath ?? itemsPath;
                imageIndex = looks - 20_000;
                return archivePath != null;
            }

            archivePath = itemsPath;
            imageIndex = looks % 10_000;
            return true;
        }

        bool TryGetArchiveTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
        {
            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
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

        void PrefetchArchiveImage(string archivePath, int imageIndex)
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
    }

    private void ClearMerchantSellSelection(bool clearQuotes)
    {
        _merchantSellItem = default;

        if (!clearQuotes)
            return;

        _world.ApplySellPriceQuote(0);
        _world.ApplyRepairCostQuote(0);
    }

    private async ValueTask TrySelectMerchantSellItemAsync(ClientItem item, CancellationToken token)
    {
        MirMerchantMode mode = _world.MerchantMode;
        if (mode is not MirMerchantMode.Sell and not MirMerchantMode.Repair)
            return;

        if (item.MakeIndex == 0 || string.IsNullOrWhiteSpace(item.NameString))
            return;

        if (_heroBagView)
            return;

        if (_merchantSellItem.MakeIndex == item.MakeIndex && mode == _merchantSellLastMode)
            return;

        _merchantSellItem = item;
        _world.ApplySellPriceQuote(0);
        _world.ApplyRepairCostQuote(0);

        int merchantId = _world.CurrentMerchantId;
        if (merchantId <= 0)
            return;

        ushort lo = unchecked((ushort)(item.MakeIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((item.MakeIndex >> 16) & 0xFFFF));

        ushort ident = mode == MirMerchantMode.Sell
            ? Grobal2.CM_MERCHANTQUERYSELLPRICE
            : Grobal2.CM_MERCHANTQUERYREPAIRCOST;

        try
        {
            await _session.SendClientStringAsync(ident, merchantId, lo, hi, 0, item.NameString, token);
            AppendLog(mode == MirMerchantMode.Sell
                ? $"[merchant] CM_MERCHANTQUERYSELLPRICE '{item.NameString}' makeIndex={item.MakeIndex}"
                : $"[merchant] CM_MERCHANTQUERYREPAIRCOST '{item.NameString}' makeIndex={item.MakeIndex}");
        }
        catch (Exception ex)
        {
            AppendLog($"[merchant] query send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async ValueTask TryMerchantSellDialogOkAsync(CancellationToken token)
    {
        MirMerchantMode mode = _world.MerchantMode;
        if (mode is not MirMerchantMode.Sell and not MirMerchantMode.Repair)
            return;

        ClientItem item = _merchantSellItem;
        if (item.MakeIndex == 0 || string.IsNullOrWhiteSpace(item.NameString))
        {
            AppendLog("[merchant] sell/repair ignored: no item");
            return;
        }

        ClearMerchantSellSelection(clearQuotes: true);

        if (mode == MirMerchantMode.Sell)
            await _merchantTradeSystem.TrySellAsync(item, bagSlotIndex: -1, token);
        else
            await _merchantTradeSystem.TryRepairAsync(item, bagSlotIndex: -1, token);
    }

    private bool TryDrawGroupGuildUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!_guildSystem.DialogVisible && !_guildSystem.MemberListVisible)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        const int pad = 10;
        const int header = 22;
        const int lineH = 18;
        const int gapY = 12;

        int panelW = Math.Clamp(view.LogicalSize.Width / 3, 340, 520);
        int x0 = 16;
        int y0 = 16;

        List<string>? guildLines = null;
        DrawingRectangle guildRect = default;

        if (_guildSystem.DialogVisible)
        {
            guildLines = new List<string>(24);

            if (_world.GuildNoticeLines.Count > 0)
            {
                guildLines.Add("Notice:");
                foreach (string line in _world.GuildNoticeLines)
                {
                    if (guildLines.Count >= 1 + 12)
                        break;
                    if (!string.IsNullOrWhiteSpace(line))
                        guildLines.Add(line.Trim());
                }
            }
            else
            {
                foreach (string line in _world.GuildDialogLines)
                {
                    if (guildLines.Count >= 14)
                        break;
                    if (!string.IsNullOrWhiteSpace(line))
                        guildLines.Add(line.Trim());
                }

                if (guildLines.Count == 0)
                    guildLines.Add("(no guild dialog data yet)");
            }

            int guildH = (pad * 2) + header + (guildLines.Count * lineH);
            guildRect = new DrawingRectangle(x0, y0, panelW, guildH);
        }

        List<string>? memberLines = null;
        DrawingRectangle membersRect = default;
        if (_guildSystem.MemberListVisible)
        {
            memberLines = new List<string>(24);
            int show = Math.Min(16, _world.GuildMembers.Count);
            for (int i = 0; i < show; i++)
            {
                MirGuildMember m = _world.GuildMembers[i];
                string name = m.Name.Trim();
                if (name.Length == 0)
                    continue;

                string rank = string.IsNullOrWhiteSpace(m.RankName) ? m.Rank.ToString() : m.RankName.Trim();
                memberLines.Add($"{i + 1}. {name} ({rank})");
            }

            if (memberLines.Count == 0)
                memberLines.Add("(no member list yet)");

            int h = (pad * 2) + header + (memberLines.Count * lineH);
            int y = _guildSystem.DialogVisible ? guildRect.Bottom + gapY : y0;
            membersRect = new DrawingRectangle(x0, y, panelW, h);
        }

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);

        if (_guildSystem.DialogVisible)
        {
            _spriteBatch.Draw(_whiteTexture, guildRect, color: new Color4(0, 0, 0, 0.55f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(guildRect.Left + 1, guildRect.Top + 1, guildRect.Width - 2, guildRect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(guildRect.Left + 1, guildRect.Top + 1, guildRect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        }

        if (_guildSystem.MemberListVisible)
        {
            _spriteBatch.Draw(_whiteTexture, membersRect, color: new Color4(0, 0, 0, 0.55f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(membersRect.Left + 1, membersRect.Top + 1, membersRect.Width - 2, membersRect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(membersRect.Left + 1, membersRect.Top + 1, membersRect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        }

        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        if (_guildSystem.DialogVisible && guildLines != null)
        {
            string name = !string.IsNullOrWhiteSpace(_world.GuildDialogName) ? _world.GuildDialogName.Trim() : _world.MyGuildName.Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = "Guild";

            string commander = _world.GuildCommanderMode ? " commander" : string.Empty;
            _uiTextDrawList.Add(new NameDrawInfo($"{name}{commander}  (Ctrl+G close)", guildRect.Left + pad, guildRect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));

            int y = guildRect.Top + pad + header;
            foreach (string line in guildLines)
            {
                _uiTextDrawList.Add(new NameDrawInfo(line, guildRect.Left + pad, y, new Color4(0.92f, 0.92f, 0.92f, 1)));
                y += lineH;
            }
        }

        if (_guildSystem.MemberListVisible && memberLines != null)
        {
            _uiTextDrawList.Add(new NameDrawInfo($"Members ({_world.GuildMembers.Count})  (Ctrl+Shift+G close)", membersRect.Left + pad, membersRect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));

            int y = membersRect.Top + pad + header;
            foreach (string line in memberLines)
            {
                _uiTextDrawList.Add(new NameDrawInfo(line, membersRect.Left + pad, y, new Color4(0.92f, 0.92f, 0.92f, 1)));
                y += lineH;
            }
        }

        return true;
    }

	    private bool TryDrawLevelRankUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
	    {
	        stats = default;

        if (!_levelRankSystem.Visible)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        const int pad = 10;
        const int header = 22;
        const int lineH = 18;

        int panelW = Math.Clamp(view.LogicalSize.Width / 3, 360, 560);
        int x0 = Math.Max(16, view.LogicalSize.Width - panelW - 16);
        int y0 = 16;

        int type = _world.LevelRankType;
        int page = _world.LevelRankPage;

        var lines = new List<string>(16);

        if (type is >= 4 and <= 7)
        {
            if (_world.HeroLevelRanks.Count == 0)
            {
                lines.Add("(waiting hero rank data...)");
            }
            else
            {
                int show = Math.Min(10, _world.HeroLevelRanks.Count);
                for (int i = 0; i < show; i++)
                {
                    MirHeroLevelRank r = _world.HeroLevelRanks[i];
                    string heroName = r.HeroName.Trim();
                    string masterName = r.MasterName.Trim();
                    if (heroName.Length == 0)
                        heroName = "(hero)";
                    if (masterName.Length == 0)
                        masterName = "(master)";

                    string idx = r.Index > 0 ? $" #{r.Index}" : string.Empty;
                    lines.Add($"{i + 1}. {heroName} Lv{r.Level}{idx}  {masterName}");
                }
            }
        }
        else
        {
            if (_world.HumanLevelRanks.Count == 0)
            {
                lines.Add("(waiting rank data...)");
            }
            else
            {
                int show = Math.Min(10, _world.HumanLevelRanks.Count);
                for (int i = 0; i < show; i++)
                {
                    MirHumanLevelRank r = _world.HumanLevelRanks[i];
                    string name = r.Name.Trim();
                    if (name.Length == 0)
                        continue;

                    string idx = r.Index > 0 ? $" #{r.Index}" : string.Empty;
                    lines.Add($"{i + 1}. {name} Lv{r.Level}{idx}");
                }
            }
        }

        lines.Add("PageUp/PageDown: page   Home: first   1-8: type   Ctrl+R: close");

        int panelH = (pad * 2) + header + (lines.Count * lineH);
        DrawingRectangle rect = new(x0, y0, panelW, panelH);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, rect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        string title = type is >= 4 and <= 7 ? "Hero Level Rank" : "Level Rank";
        _uiTextDrawList.Add(new NameDrawInfo($"{title}  type={type} page={page}", rect.Left + pad, rect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));

        int y = rect.Top + pad + header;
        foreach (string line in lines)
        {
            _uiTextDrawList.Add(new NameDrawInfo(line, rect.Left + pad, y, new Color4(0.92f, 0.92f, 0.92f, 1)));
            y += lineH;
        }

	        return true;
	    }

	    private bool TryDrawMagicWindowUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
	    {
	        stats = default;

	        if (!_magicWindowVisible)
	            return false;

	        if (_spriteBatch == null || _whiteTexture == null)
	            return false;

	        const int pad = 10;
	        const int header = 22;
	        const int lineH = 18;
	        const int helpLines = 1;

	        IReadOnlyList<ClientMagic> magics = _magicWindowHeroView ? _world.HeroMagics : _world.MyMagics;
	        int maxPanelH = Math.Max(0, view.LogicalSize.Height - 16);
	        int maxLinesTotal = (maxPanelH - ((pad * 2) + header)) / lineH;
	        int itemLines = Math.Clamp(maxLinesTotal - helpLines, 1, 26);
	        _magicWindowPageSize = itemLines;

	        int total = magics.Count;
	        int maxTop = Math.Max(0, total - itemLines);
	        _magicWindowTopIndex = Math.Clamp(_magicWindowTopIndex, 0, maxTop);

	        int panelW = Math.Clamp(view.LogicalSize.Width / 3, 360, 580);
	        panelW = Math.Min(panelW, Math.Max(0, view.LogicalSize.Width - 16));
	        if (panelW <= 0)
	            return false;

	        string title = _magicWindowHeroView ? "Hero Magic" : "Magic";
	        int page = itemLines > 0 ? (_magicWindowTopIndex / itemLines) : 0;
	        int pages = itemLines > 0 ? (int)Math.Ceiling(total / (double)itemLines) : 0;

	        int lines = helpLines + itemLines;
	        int panelH = (pad * 2) + header + (lines * lineH);

	        int x0 = 16;
	        int y0 = 16;
	        DrawingRectangle rect = new(x0, y0, panelW, panelH);

	        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
	        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.55f));
	        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, rect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
	        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
	        _spriteBatch.End();
	        stats = _spriteBatch.Stats;

	        _uiTextDrawList.Add(new NameDrawInfo($"{title}  count={total} page={page + 1}/{Math.Max(1, pages)}  (K close, H toggle)", rect.Left + pad, rect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));

	        int y = rect.Top + pad + header;

	        int shown = Math.Min(itemLines, Math.Max(0, total - _magicWindowTopIndex));
	        for (int i = 0; i < shown; i++)
	        {
	            int idx = _magicWindowTopIndex + i;
	            ClientMagic magic = magics[idx];

	            string name = magic.Def.MagicNameString;
	            if (string.IsNullOrWhiteSpace(name))
	                name = $"#{magic.Def.MagicId}";

	            string slot = idx is >= 0 and < 8 ? $"F{idx + 1}" : "  ";
	            char keyChar = magic.KeyChar;
	            string key = keyChar is >= '!' and <= '~' ? keyChar.ToString() : ((ushort)keyChar).ToString(CultureInfo.InvariantCulture);

	            _uiTextDrawList.Add(new NameDrawInfo($"{slot} {idx + 1,3}. {name}  Lv={magic.Level}  Key={key}  Tr={magic.CurTrain}", rect.Left + pad, y, new Color4(0.92f, 0.92f, 0.92f, 1f)));
	            y += lineH;
	        }

	        if (shown < itemLines)
	            y += (itemLines - shown) * lineH;

	        _uiTextDrawList.Add(new NameDrawInfo("Up/Down/PageUp/PageDown/Home/End: scroll", rect.Left + pad, y, new Color4(0.75f, 0.75f, 0.75f, 1f)));
	        return true;
	    }

	    private bool TryDrawSeriesSkillUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
	    {
	        stats = default;

        if (!_seriesSkillSystem.Visible)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        const int pad = 10;
        const int header = 22;
        const int lineH = 18;

        int panelW = Math.Clamp(view.LogicalSize.Width / 2, 420, 760);
        int x0 = 16;
        int y0 = 16;

        int selfRecog = _world.MyselfRecogId;
        bool heroAvail = _world.HeroActorIdSet && _world.HeroActorId != 0;
        int heroRecog = _world.HeroActorId;

        _seriesSkillSystem.EnsureUiState();

        bool controlHero = _seriesSkillSystem.ControlHero;
        int selfVenationSelectedIndex = _seriesSkillSystem.SelfVenationSelectedIndex;
        int heroVenationSelectedIndex = _seriesSkillSystem.HeroVenationSelectedIndex;
        string control = controlHero ? "Hero" : "Self";

        var lines = new List<string>(20)
        {
            $"Control={control}  SelfRecog={selfRecog}  HeroRecog={(heroAvail ? heroRecog : 0)}",
            $"SeriesReady={(_world.SeriesSkillReady ? 1 : 0)}  Step={_world.SeriesSkillStep}  Arr={FormatByteArr(_world.SeriesSkillArr)}",
            $"SelfTemp={FormatByteArr(_world.TempSeriesSkillArr)}  HeroTemp={FormatByteArr(_world.HeroTempSeriesSkillArr)}"
        };

        lines.Add("Self Venation:");
        for (int i = 0; i < _world.VenationInfos.Length; i++)
        {
            VenationInfo v = _world.VenationInfos[i];
            string mark = !controlHero && selfVenationSelectedIndex == i ? ">" : " ";
            lines.Add($"{mark} V{i + 1}: Lv={v.Level} Pt={v.Point}");
        }

        lines.Add(heroAvail ? "Hero Venation:" : "Hero Venation: (none)");
        if (heroAvail)
        {
            for (int i = 0; i < _world.HeroVenationInfos.Length; i++)
            {
                VenationInfo v = _world.HeroVenationInfos[i];
                string mark = controlHero && heroVenationSelectedIndex == i ? ">" : " ";
                lines.Add($"{mark} V{i + 1}: Lv={v.Level} Pt={v.Point}");
            }
        }

        lines.Add("Ctrl+V: close   Tab: toggle self/hero   1-4: select venation");
        lines.Add("T: train venation   B: break point   S: set series slot   F: fire series skill");

        int panelH = (pad * 2) + header + (lines.Count * lineH);
        DrawingRectangle rect = new(x0, y0, panelW, panelH);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, rect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        _uiTextDrawList.Add(new NameDrawInfo("Venation / SeriesSkill", rect.Left + pad, rect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));

        int y = rect.Top + pad + header;
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            Color4 color = new(0.92f, 0.92f, 0.92f, 1);
            if (line.StartsWith(">", StringComparison.Ordinal))
                color = new Color4(0.95f, 0.85f, 0.25f, 1f);
            _uiTextDrawList.Add(new NameDrawInfo(line, rect.Left + pad, y, color));
            y += lineH;
        }

        return true;

        static string FormatByteArr(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
                return "[]";

            var sb = new StringBuilder(capacity: 2 + (bytes.Length * 4));
            sb.Append('[');
            for (int i = 0; i < bytes.Length; i++)
            {
                if (i != 0)
                    sb.Append(' ');
                sb.Append(bytes[i]);
            }
            sb.Append(']');
            return sb.ToString();
        }
    }

    private bool TryDrawTreasureDialogUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!_treasureDialogSystem.Visible)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        const int pad = 10;
        const int header = 22;
        const int lineH = 18;

        const int bagCols = 8;
        const int bagRows = 7;
        const int bagSlot = 36;
        const int bagPad = 8;
        const int bagHeader = 22;

        int bagPanelW = (bagPad * 2) + (bagCols * bagSlot);
        int bagPanelH = (bagPad * 2) + bagHeader + (bagRows * bagSlot);
        int bagPanelX = Math.Max(8, view.LogicalSize.Width - bagPanelW - 16);
        int bagPanelY = Math.Max(8, view.LogicalSize.Height - bagPanelH - 16);

        int panelW = Math.Clamp(view.LogicalSize.Width / 3, 380, 580);
        int x0 = Math.Max(16, bagPanelX - panelW - 12);

        MirTreasureDialogMode mode = _treasureDialogSystem.Mode;

        var lines = new List<(string Text, Color4 Color)>(14)
        {
            ($"Mode: {mode}", new Color4(0.92f, 0.92f, 0.92f, 1f)),
            ($"Next: slot{_treasureDialogSystem.NextSelectSlot}", new Color4(0.75f, 0.85f, 0.95f, 1f))
        };

        lines.Add(FormatSlotLine(0, _treasureDialogSystem.SelectedMakeIndex0, mode));
        lines.Add(FormatSlotLine(1, _treasureDialogSystem.SelectedMakeIndex1, mode));

        if (!string.IsNullOrWhiteSpace(_treasureDialogSystem.Status))
            lines.Add(($"Status: {_treasureDialogSystem.Status}", new Color4(0.95f, 0.85f, 0.25f, 1f)));

        long nowMs = Environment.TickCount64;
        if (_treasureDialogSystem.LastSendMs != 0 && nowMs - _treasureDialogSystem.LastSendMs < 8000)
            lines.Add(("Waiting server...", new Color4(0.75f, 0.75f, 0.8f, 1f)));

        lines.Add(("Click bag item: fill slot0/slot1", new Color4(0.92f, 0.92f, 0.92f, 1f)));
        lines.Add(("Tab: switch mode   Backspace: clear", new Color4(0.92f, 0.92f, 0.92f, 1f)));

        if (mode == MirTreasureDialogMode.Identify)
            lines.Add(("Enter: normal   Shift+Enter: special", new Color4(0.92f, 0.92f, 0.92f, 1f)));
        else
            lines.Add(("Enter: exchange", new Color4(0.92f, 0.92f, 0.92f, 1f)));

        lines.Add(("Esc: close   Ctrl+I: toggle", new Color4(0.92f, 0.92f, 0.92f, 1f)));

        int panelH = (pad * 2) + header + (lines.Count * lineH);
        int y0 = bagPanelY + 40;
        if (y0 + panelH > view.LogicalSize.Height - 16)
            y0 = Math.Max(16, (view.LogicalSize.Height - 16) - panelH);
        y0 = Math.Max(16, y0);

        DrawingRectangle rect = new(x0, y0, panelW, panelH);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, rect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        _uiTextDrawList.Add(new NameDrawInfo("Treasure", rect.Left + pad, rect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));

        int y = rect.Top + pad + header;
        foreach ((string line, Color4 color) in lines)
        {
            _uiTextDrawList.Add(new NameDrawInfo(line, rect.Left + pad, y, color));
            y += lineH;
        }

        return true;

        (string Text, Color4 Color) FormatSlotLine(int slot, int makeIndex, MirTreasureDialogMode mode)
        {
            if (makeIndex == 0)
                return ($"Slot{slot}: (none)", new Color4(0.92f, 0.92f, 0.92f, 1f));

            if (!TryFindBagItem(makeIndex, out ClientItem item))
                return ($"Slot{slot}: (missing) makeIndex={makeIndex}", new Color4(1.0f, 0.3f, 0.3f, 1f));

            string name = string.IsNullOrWhiteSpace(item.NameString) ? "(unknown)" : item.NameString.Trim();
            string extra = (mode, slot) switch
            {
                (MirTreasureDialogMode.Identify, 1) => $" StdMode={item.S.StdMode}",
                (MirTreasureDialogMode.Exchange, 0) => $" EvaTimes={item.S.Eva.EvaTimes}",
                (MirTreasureDialogMode.Exchange, 1) => $" StdMode={item.S.StdMode} Shape={item.S.Shape}",
                _ => string.Empty
            };

            return ($"Slot{slot}: {name}  makeIndex={makeIndex}{extra}", new Color4(0.95f, 0.85f, 0.25f, 1f));
        }

        bool TryFindBagItem(int makeIndex, out ClientItem item)
        {
            item = default;
            foreach (ClientItem slot in _world.BagSlots)
            {
                if (slot.MakeIndex != makeIndex)
                    continue;
                item = slot;
                return true;
            }
            return false;
        }
    }

    private bool TryDrawItemDialogUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!_itemDialogSystem.Visible)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        const int pad = 10;
        const int header = 22;
        const int lineH = 18;

        const int bagCols = 8;
        const int bagRows = 7;
        const int bagSlot = 36;
        const int bagPad = 8;
        const int bagHeader = 22;

        int bagPanelW = (bagPad * 2) + (bagCols * bagSlot);
        int bagPanelH = (bagPad * 2) + bagHeader + (bagRows * bagSlot);
        int bagPanelX = Math.Max(8, view.LogicalSize.Width - bagPanelW - 16);
        int bagPanelY = Math.Max(8, view.LogicalSize.Height - bagPanelH - 16);

        int panelW = Math.Clamp(view.LogicalSize.Width / 3, 340, 520);
        int x0 = Math.Max(16, bagPanelX - panelW - 12);

        int maxChars = Math.Max(16, (panelW - (pad * 2)) / 8);
        string prompt = string.IsNullOrWhiteSpace(_itemDialogSystem.Prompt) ? "(no prompt)" : _itemDialogSystem.Prompt.Trim();

        var lines = new List<(string Text, Color4 Color)>(16);
        foreach (string line in WrapLines(prompt, maxChars, maxLines: 6))
            lines.Add((line, new Color4(0.92f, 0.92f, 0.92f, 1f)));

        lines.Add((string.Empty, new Color4(0.92f, 0.92f, 0.92f, 1f)));

        ClientItem selected = _itemDialogSystem.SelectedItem;
        bool hasSelected = selected.MakeIndex != 0;
        string selectedName = hasSelected ? selected.NameString.Trim() : "(none)";
        int selectedIndex = hasSelected ? selected.MakeIndex : 0;
        lines.Add(($"Selected: {selectedName}  makeIndex={selectedIndex}", hasSelected
            ? new Color4(0.95f, 0.85f, 0.25f, 1f)
            : new Color4(0.92f, 0.92f, 0.92f, 1f)));

        long nowMs = Environment.TickCount64;
        if (_itemDialogSystem.LastSendMs != 0 && nowMs - _itemDialogSystem.LastSendMs < 8000)
            lines.Add(("Waiting server...", new Color4(0.75f, 0.75f, 0.8f, 1f)));

        lines.Add(("Click bag item: select", new Color4(0.92f, 0.92f, 0.92f, 1f)));
        lines.Add(("Enter: send   Esc: close", new Color4(0.92f, 0.92f, 0.92f, 1f)));

        int panelH = (pad * 2) + header + (lines.Count * lineH);
        int y0 = bagPanelY + 100;
        if (y0 + panelH > view.LogicalSize.Height - 16)
            y0 = Math.Max(16, (view.LogicalSize.Height - 16) - panelH);
        y0 = Math.Max(16, y0);

        DrawingRectangle rect = new(x0, y0, panelW, panelH);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, rect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        _uiTextDrawList.Add(new NameDrawInfo($"Item Dialog  merchant={_itemDialogSystem.MerchantId}", rect.Left + pad, rect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));

        int y = rect.Top + pad + header;
        foreach ((string line, Color4 color) in lines)
        {
            _uiTextDrawList.Add(new NameDrawInfo(line, rect.Left + pad, y, color));
            y += lineH;
        }

        return true;

        static IEnumerable<string> WrapLines(string text, int maxChars, int maxLines)
        {
            if (maxLines <= 0)
                yield break;

            if (string.IsNullOrWhiteSpace(text))
            {
                yield return string.Empty;
                yield break;
            }

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (string raw in normalized.Split('\n'))
            {
                if (maxLines <= 0)
                    yield break;

                string line = raw.TrimEnd();
                if (line.Length == 0)
                {
                    yield return string.Empty;
                    maxLines--;
                    continue;
                }

                int idx = 0;
                while (idx < line.Length && maxLines > 0)
                {
                    int take = Math.Min(maxChars, line.Length - idx);
                    yield return line.Substring(idx, take);
                    idx += take;
                    maxLines--;
                }
            }
        }
    }

    private bool TryDrawBindDialogUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!_bindDialogSystem.Visible)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        const int pad = 10;
        const int header = 22;
        const int lineH = 18;

        const int bagCols = 8;
        const int bagRows = 7;
        const int bagSlot = 36;
        const int bagPad = 8;
        const int bagHeader = 22;

        int bagPanelW = (bagPad * 2) + (bagCols * bagSlot);
        int bagPanelH = (bagPad * 2) + bagHeader + (bagRows * bagSlot);
        int bagPanelX = Math.Max(8, view.LogicalSize.Width - bagPanelW - 16);
        int bagPanelY = Math.Max(8, view.LogicalSize.Height - bagPanelH - 16);

        int panelW = Math.Clamp(view.LogicalSize.Width / 3, 340, 520);
        int x0 = Math.Max(16, bagPanelX - panelW - 12);

        string title = _bindDialogSystem.Unbind ? "Unbind Item" : "Bind Item";

        var lines = new List<(string Text, Color4 Color)>(10);

        if (_bindDialogSystem.Waiting && _bindDialogSystem.WaitingItem.MakeIndex != 0)
        {
            ClientItem waitingItem = _bindDialogSystem.WaitingItem;
            string name = string.IsNullOrWhiteSpace(waitingItem.NameString) ? "(unknown)" : waitingItem.NameString.Trim();
            lines.Add(($"Pending: {name}  makeIndex={waitingItem.MakeIndex}", new Color4(0.95f, 0.85f, 0.25f, 1f)));
        }
        else
        {
            ClientItem selected = _bindDialogSystem.SelectedItem;
            bool hasSelected = selected.MakeIndex != 0;
            string selectedName = hasSelected ? selected.NameString.Trim() : "(none)";
            int selectedIndex = hasSelected ? selected.MakeIndex : 0;
            lines.Add(($"Selected: {selectedName}  makeIndex={selectedIndex}", hasSelected
                ? new Color4(0.95f, 0.85f, 0.25f, 1f)
                : new Color4(0.92f, 0.92f, 0.92f, 1f)));
        }

        long nowMs = Environment.TickCount64;
        if (_bindDialogSystem.LastSendMs != 0 && nowMs - _bindDialogSystem.LastSendMs < 8000)
            lines.Add(("Waiting server...", new Color4(0.75f, 0.75f, 0.8f, 1f)));

        lines.Add(("Click bag item: select", new Color4(0.92f, 0.92f, 0.92f, 1f)));
        lines.Add((_bindDialogSystem.Unbind ? "Enter: unbind   Esc: close" : "Enter: bind     Esc: close", new Color4(0.92f, 0.92f, 0.92f, 1f)));

        int panelH = (pad * 2) + header + (lines.Count * lineH);
        int y0 = bagPanelY + 60;
        if (y0 + panelH > view.LogicalSize.Height - 16)
            y0 = Math.Max(16, (view.LogicalSize.Height - 16) - panelH);
        y0 = Math.Max(16, y0);

        DrawingRectangle rect = new(x0, y0, panelW, panelH);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, rect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        _uiTextDrawList.Add(new NameDrawInfo($"{title}  merchant={_bindDialogSystem.MerchantId}", rect.Left + pad, rect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));

        int y = rect.Top + pad + header;
        foreach ((string line, Color4 color) in lines)
        {
            _uiTextDrawList.Add(new NameDrawInfo(line, rect.Left + pad, y, color));
            y += lineH;
        }

        return true;
    }

    private bool TryDrawMallUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;
        _mallClickPoints.Clear();

        if (!_mallWindowVisible)
        {
            _mallPanelRect = null;
            _mallCloseRect = null;
            return false;
        }

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        Vector2 mouseLogical = new(float.NegativeInfinity, float.NegativeInfinity);
        try
        {
            System.Drawing.Point mouseClient = _renderControl.PointToClient(Cursor.Position);
            if (view.ViewportRect.Contains(mouseClient))
            {
                mouseLogical = new Vector2(
                    (mouseClient.X - view.Offset.X) / Math.Max(0.0001f, view.Scale.X),
                    (mouseClient.Y - view.Offset.Y) / Math.Max(0.0001f, view.Scale.Y));
            }
        }
        catch
        {
            
        }

        string resourceRoot = GetResourceRootDir();
        string dataDir = Path.Combine(resourceRoot, "Data");
        if (!Directory.Exists(dataDir))
            return false;

        string? wMain3Path = TryResolveArchiveFilePath(dataDir, "WMain3");
        string? wMainPath = TryResolveArchiveFilePath(dataDir, "WMain");

        
        D3D11Texture2D? bgTex = null;
        if (!string.IsNullOrWhiteSpace(wMain3Path))
        {
            const int bgIndex = 298;
            PrefetchArchiveImage(wMain3Path, bgIndex);
            if (TryGetArchiveTexture(wMain3Path, bgIndex, out D3D11Texture2D tex))
                bgTex = tex;
        }

        int panelW = bgTex?.Width ?? Math.Clamp(view.LogicalSize.Width / 2, 640, 800);
        int panelH = bgTex?.Height ?? Math.Clamp(view.LogicalSize.Height / 2, 420, 600);

        if (!_mallWindowPosSet)
        {
            _mallWindowPosSet = true;
            _mallWindowPosX = 0;
            _mallWindowPosY = 0;
        }

        int panelX = Math.Clamp(_mallWindowPosX, 0, Math.Max(0, view.LogicalSize.Width - panelW));
        int panelY = Math.Clamp(_mallWindowPosY, 0, Math.Max(0, view.LogicalSize.Height - panelH));
        _mallWindowPosX = panelX;
        _mallWindowPosY = panelY;

        var panelRect = new DrawingRectangle(panelX, panelY, panelW, panelH);
        _mallPanelRect = panelRect;

        
        int selectedClass = Math.Clamp(_mallSelectedClass, 0, 4);
        _mallSelectedClass = selectedClass;

        List<ShopItem>? mainItems = null;
        if (_world.ShopItemsByClass.TryGetValue(unchecked((byte)selectedClass), out List<ShopItem>? bucket))
            mainItems = bucket;

        int mainCount = mainItems?.Count ?? 0;
        int maxPage = Math.Max(1, (mainCount + 9) / 10);
        _mallPage = Math.Clamp(_mallPage, 0, maxPage - 1);

        List<ShopItem>? hotItems = null;
        if (_world.ShopItemsByClass.TryGetValue(5, out List<ShopItem>? hotBucket))
            hotItems = hotBucket;

        string? stateItemPath = TryResolveArchiveFilePath(dataDir, "stateitem");
        string? stateItem2Path = TryResolveArchiveFilePath(dataDir, "stateitem2");
        string? stateEffectPath = TryResolveArchiveFilePath(dataDir, "StateEffect");
        string? shineEffectPath = TryResolveArchiveFilePath(dataDir, "ShineEffect");

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);

        if (bgTex != null)
        {
            _spriteBatch.Draw(bgTex, panelRect);
        }
        else
        {
            _spriteBatch.Draw(_whiteTexture, panelRect, color: new Color4(0.10f, 0.10f, 0.14f, 0.92f));
        }

        
        _mallCloseRect = null;
        if (!string.IsNullOrWhiteSpace(wMainPath))
        {
            const int closeIndex = 64;
            PrefetchArchiveImage(wMainPath, closeIndex);
            if (TryGetArchiveTexture(wMainPath, closeIndex, out D3D11Texture2D closeTex))
            {
                int cx = panelX + 606;
                int cy = panelY + 5;
                cx = Math.Clamp(cx, panelX, Math.Max(panelX, (panelX + panelW) - closeTex.Width));
                cy = Math.Clamp(cy, panelY, Math.Max(panelY, (panelY + panelH) - closeTex.Height));
                var rect = new DrawingRectangle(cx, cy, closeTex.Width, closeTex.Height);
                _spriteBatch.Draw(closeTex, rect);
                _mallCloseRect = rect;
            }
        }

        
        if (!string.IsNullOrWhiteSpace(wMain3Path))
        {
            const int categoryStart = 299;
            const int categoryX0 = 177;
            const int categoryY = 14;
            const int categoryStepX = 58;

            for (int i = 0; i < 5; i++)
            {
                int img = categoryStart + i;
                PrefetchArchiveImage(wMain3Path, img);
                if (!TryGetArchiveTexture(wMain3Path, img, out D3D11Texture2D tex))
                    continue;

                int x = panelX + categoryX0 + (i * categoryStepX);
                int y = panelY + categoryY;
                var rect = new DrawingRectangle(x, y, tex.Width, tex.Height);
                _spriteBatch.Draw(tex, rect);
                _mallClickPoints.Add(new MallClickPoint(rect, MallClickKind.Category, (byte)i, Index: 0));

                if (i == selectedClass)
                {
                    _spriteBatch.Draw(
                        _whiteTexture,
                        new DrawingRectangle(rect.Left - 1, rect.Top - 1, rect.Width + 2, rect.Height + 2),
                        color: new Color4(1f, 1f, 1f, 0.25f));
                }
            }
        }

        
        if (!string.IsNullOrWhiteSpace(wMainPath))
        {
            DrawPageButton(wMainPath, imageIndex: 388, panelX + 197, panelY + 349, MallClickKind.PrevPage);
            DrawPageButton(wMainPath, imageIndex: 387, panelX + 287, panelY + 349, MallClickKind.NextPage);
        }

        
        if (!string.IsNullOrWhiteSpace(wMain3Path))
        {
            DrawActionButton(wMain3Path, imageIndex: 304, panelX + 329, panelY + 365, MallClickKind.Buy);
            DrawActionButton(wMain3Path, imageIndex: 305, panelX + 329 + 58, panelY + 365, MallClickKind.Gift);
            DrawActionButton(wMain3Path, imageIndex: 306, panelX + 329 + (58 * 2), panelY + 365, MallClickKind.Close);
        }

        
        const int grid1Cols = 5;
        const int grid1Rows = 2;
        const int grid1X = 180;
        const int grid1Y = 58;
        const int grid1W = 328;
        const int grid1H = 266;

        int gridCellW = Math.Max(1, grid1W / grid1Cols);
        int gridCellH = Math.Max(1, grid1H / grid1Rows);
        int startIndex = _mallPage * 10;

        for (int i = 0; i < 10; i++)
        {
            int idx = startIndex + i;
            if (idx < 0 || idx >= mainCount || mainItems == null)
                break;

            int col = i % grid1Cols;
            int row = i / grid1Cols;
            int x = panelX + grid1X + (col * gridCellW);
            int y = panelY + grid1Y + (row * gridCellH);
            var rect = new DrawingRectangle(x, y, gridCellW, gridCellH);

            ShopItem item = mainItems[idx];

            _mallClickPoints.Add(new MallClickPoint(rect, MallClickKind.Item, (byte)selectedClass, idx));

            bool selected = _mallSelectedItemClass == (byte)selectedClass && _mallSelectedItemIndex == idx;
            bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right && mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;
            if (selected || hovered)
            {
                _spriteBatch.Draw(
                    _whiteTexture,
                    new DrawingRectangle(rect.Left, rect.Top, rect.Width, rect.Height),
                    color: selected ? new Color4(1f, 0.35f, 0.35f, 0.20f) : new Color4(1f, 1f, 1f, 0.12f));
            }

            if (TryResolveItemIcon(item.Looks, out string? iconArchive, out int iconIndex) && iconArchive != null)
            {
                if (TryGetArchiveTexture(iconArchive, iconIndex, out D3D11Texture2D iconTex))
                {
                    int drawW = Math.Min(iconTex.Width, rect.Width - 4);
                    int drawH = Math.Min(iconTex.Height, 32);
                    int dx = rect.Left + (rect.Width - drawW) / 2;
                    int dy = rect.Top + 6;
                    _spriteBatch.Draw(iconTex, new DrawingRectangle(dx, dy, drawW, drawH));
                }
                else
                {
                    PrefetchArchiveImage(iconArchive, iconIndex);
                }
            }

            string name = item.ItemNameString.Trim();
            if (!string.IsNullOrWhiteSpace(name))
                _uiTextDrawList.Add(new NameDrawInfo(name, rect.Left + 4, rect.Top + 46, new Color4(0.92f, 0.92f, 0.92f, 1f)));

            if (item.Price > 0)
                _uiTextDrawList.Add(new NameDrawInfo(item.Price.ToString(CultureInfo.InvariantCulture), rect.Left + 4, rect.Top + 62, new Color4(0.92f, 0.82f, 0.25f, 1f)));

            if (hovered && string.IsNullOrWhiteSpace(_uiTooltipText))
            {
                string tip = item.ExplainString.Trim();
                if (string.IsNullOrWhiteSpace(tip))
                    tip = name;
                if (!string.IsNullOrWhiteSpace(tip))
                    SetTooltip(tip, new Vector2(rect.Right + 6, rect.Top));
            }
        }

        
        if (hotItems is { Count: > 0 })
        {
            const int hotX = 518;
            const int hotY = 66;
            const int hotW = 88;
            const int hotH = 320;
            const int hotRows = 5;
            int hotCellH = Math.Max(1, hotH / hotRows);

            int max = Math.Min(hotItems.Count, hotRows);
            for (int i = 0; i < max; i++)
            {
                int x = panelX + hotX;
                int y = panelY + hotY + (i * hotCellH);
                var rect = new DrawingRectangle(x, y, hotW, hotCellH);
                ShopItem item = hotItems[i];

                _mallClickPoints.Add(new MallClickPoint(rect, MallClickKind.Item, Class: 5, i));

                bool selected = _mallSelectedItemClass == 5 && _mallSelectedItemIndex == i;
                bool hovered = mouseLogical.X >= rect.Left && mouseLogical.X < rect.Right && mouseLogical.Y >= rect.Top && mouseLogical.Y < rect.Bottom;
                if (selected || hovered)
                {
                    _spriteBatch.Draw(
                        _whiteTexture,
                        new DrawingRectangle(rect.Left, rect.Top, rect.Width, rect.Height),
                        color: selected ? new Color4(1f, 0.35f, 0.35f, 0.20f) : new Color4(1f, 1f, 1f, 0.12f));
                }

                if (TryResolveItemIcon(item.Looks, out string? iconArchive, out int iconIndex) && iconArchive != null)
                {
                    if (TryGetArchiveTexture(iconArchive, iconIndex, out D3D11Texture2D iconTex))
                    {
                        int drawW = Math.Min(iconTex.Width, rect.Width - 4);
                        int drawH = Math.Min(iconTex.Height, rect.Height - 4);
                        int dx = rect.Left + (rect.Width - drawW) / 2;
                        int dy = rect.Top + (rect.Height - drawH) / 2;
                        _spriteBatch.Draw(iconTex, new DrawingRectangle(dx, dy, drawW, drawH));
                    }
                    else
                    {
                        PrefetchArchiveImage(iconArchive, iconIndex);
                    }
                }

                if (hovered && string.IsNullOrWhiteSpace(_uiTooltipText))
                {
                    string name = item.ItemNameString.Trim();
                    string tip = item.ExplainString.Trim();
                    if (string.IsNullOrWhiteSpace(tip))
                        tip = name;
                    if (!string.IsNullOrWhiteSpace(tip))
                        SetTooltip(tip, new Vector2(rect.Right + 6, rect.Top));
                }
            }
        }

        _spriteBatch.End();
        stats = _spriteBatch.Stats;
        return true;

        void DrawPageButton(string archive, int imageIndex, int x, int y, MallClickKind kind)
        {
            PrefetchArchiveImage(archive, imageIndex);
            if (!TryGetArchiveTexture(archive, imageIndex, out D3D11Texture2D tex))
                return;

            var rect = new DrawingRectangle(x, y, tex.Width, tex.Height);
            _spriteBatch.Draw(tex, rect);
            _mallClickPoints.Add(new MallClickPoint(rect, kind, Class: 0, Index: 0));
        }

        void DrawActionButton(string archive, int imageIndex, int x, int y, MallClickKind kind)
        {
            PrefetchArchiveImage(archive, imageIndex);
            if (!TryGetArchiveTexture(archive, imageIndex, out D3D11Texture2D tex))
                return;

            var rect = new DrawingRectangle(x, y, tex.Width, tex.Height);
            _spriteBatch.Draw(tex, rect);
            _mallClickPoints.Add(new MallClickPoint(rect, kind, Class: 0, Index: 0));
        }

        bool TryResolveItemIcon(int looks, out string? archivePath, out int imageIndex)
        {
            archivePath = null;
            imageIndex = 0;

            if (looks < 0)
                return false;

            if (looks < 10_000)
            {
                archivePath = stateItemPath;
                imageIndex = looks;
                return archivePath != null;
            }

            if (looks < 20_000)
            {
                archivePath = stateItem2Path ?? stateItemPath;
                imageIndex = looks - 10_000;
                return archivePath != null;
            }

            if (looks < 30_000)
            {
                archivePath = stateEffectPath ?? stateItemPath;
                imageIndex = looks - 20_000;
                return archivePath != null;
            }

            if (looks < 40_000)
            {
                archivePath = shineEffectPath ?? stateItemPath;
                imageIndex = looks - 30_000;
                return archivePath != null;
            }

            archivePath = stateItemPath;
            imageIndex = looks % 10_000;
            return archivePath != null;
        }

        void PrefetchArchiveImage(string archivePath, int imageIndex)
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

        bool TryGetArchiveTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
        {
            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
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
    }

    private int GetMallMaxPage(int shopClass)
    {
        byte cls = unchecked((byte)Math.Clamp(shopClass, 0, 5));
        if (_world.ShopItemsByClass.TryGetValue(cls, out List<ShopItem>? bucket))
        {
            int count = bucket?.Count ?? 0;
            return Math.Max(1, (count + 9) / 10);
        }

        return 1;
    }

    private bool TryGetSelectedMallItem(out ShopItem item)
    {
        item = default;

        byte cls = _mallSelectedItemClass;
        int index = _mallSelectedItemIndex;
        if (cls == byte.MaxValue || index < 0)
            return false;

        if (!_world.ShopItemsByClass.TryGetValue(cls, out List<ShopItem>? bucket) || bucket == null)
            return false;

        if ((uint)index >= (uint)bucket.Count)
            return false;

        item = bucket[index];
        return true;
    }

    private void TryRequestMallItems(int shopClass, CancellationToken token, bool force)
    {
        if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
            return;

        long nowMs = Environment.TickCount64;
        if (!force && nowMs - _mallLastRequestMs < 800)
            return;

        _mallLastRequestMs = nowMs;

        int cls = Math.Clamp(shopClass, 0, 5);
        _ = _session.SendClientMessageAsync(Grobal2.CM_GETSHOPITEM, 0, 0, 0, 0, token);
        _ = _session.SendClientMessageAsync(Grobal2.CM_GETSHOPITEM, cls, 0, 0, 0, token);
        AppendLog($"[mall] CM_GETSHOPITEM class={cls}");
    }

    private bool TryDrawYbDealUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!_ybDealSystem.Visible)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        const int pad = 10;
        const int header = 22;
        const int lineH = 18;
        const int buttonH = 22;
        const int buttonW = 120;
        const int buttonGap = 8;

        MirYbDealDialogMode mode = _ybDealSystem.Mode;
        ClientItem[] items = _ybDealSystem.Items;

        int shownItems = Math.Min(10, items.Length);
        _ybDealSystem.ClampSelectionToShownItems(shownItems);
        int selectedIndex = _ybDealSystem.SelectedIndex;

        int contentLines = 6 + shownItems + 2;
        int panelW = Math.Clamp(view.LogicalSize.Width / 2, 520, 820);
        int panelH = Math.Clamp((pad * 2) + header + (contentLines * lineH) + pad + buttonH + pad, 260, view.LogicalSize.Height - 32);
        int x0 = Math.Max(16, (view.LogicalSize.Width - panelW) / 2);
        int y0 = Math.Max(16, (view.LogicalSize.Height - panelH) / 2);

        DrawingRectangle rect = new(x0, y0, panelW, panelH);
        _ybDealPanelRect = rect;

        DrawingRectangle closeRect = new(rect.Right - 26, rect.Top + 3, 20, header - 6);

        int buttonsY = rect.Bottom - pad - buttonH;
        DrawingRectangle buyRect = default;
        DrawingRectangle cancelRect = default;
        DrawingRectangle cancelSellRect = default;
        DrawingRectangle closeBottomRect = default;

        int buttonX = rect.Left + pad;
        if (mode == MirYbDealDialogMode.Deal)
        {
            buyRect = new DrawingRectangle(buttonX, buttonsY, buttonW, buttonH);
            buttonX += buttonW + buttonGap;
            cancelRect = new DrawingRectangle(buttonX, buttonsY, buttonW, buttonH);
            buttonX += buttonW + buttonGap;
        }
        else
        {
            cancelSellRect = new DrawingRectangle(buttonX, buttonsY, buttonW, buttonH);
            buttonX += buttonW + buttonGap;
        }

        closeBottomRect = new DrawingRectangle(buttonX, buttonsY, buttonW, buttonH);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, rect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));

        DrawButton(closeRect, enabled: true);
        if (mode == MirYbDealDialogMode.Deal)
        {
            DrawButton(buyRect, enabled: true);
            DrawButton(cancelRect, enabled: true);
        }
        else
        {
            DrawButton(cancelSellRect, enabled: true);
        }
        DrawButton(closeBottomRect, enabled: true);

        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        _ybDealClickPoints.Add(new YbDealClickPoint(closeRect, YbDealClickKind.Close, Index: -1));
        if (mode == MirYbDealDialogMode.Deal)
        {
            _ybDealClickPoints.Add(new YbDealClickPoint(buyRect, YbDealClickKind.Buy, Index: -1));
            _ybDealClickPoints.Add(new YbDealClickPoint(cancelRect, YbDealClickKind.Cancel, Index: -1));
        }
        else
        {
            _ybDealClickPoints.Add(new YbDealClickPoint(cancelSellRect, YbDealClickKind.CancelSell, Index: -1));
        }
        _ybDealClickPoints.Add(new YbDealClickPoint(closeBottomRect, YbDealClickKind.Close, Index: -1));

        string title = mode == MirYbDealDialogMode.Deal ? "YB Deal" : "YB Sell";
        _uiTextDrawList.Add(new NameDrawInfo(title, rect.Left + pad, rect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));
        _uiTextDrawList.Add(new NameDrawInfo("X", closeRect.Left + 7, closeRect.Top + 2, new Color4(0.95f, 0.95f, 0.95f, 1)));

        int y = rect.Top + pad + header;
        _uiTextDrawList.Add(new NameDrawInfo($"From: {_ybDealSystem.CharName}".TrimEnd(), rect.Left + pad, y, new Color4(0.92f, 0.92f, 0.92f, 1))); y += lineH;
        _uiTextDrawList.Add(new NameDrawInfo($"To: {_ybDealSystem.TargetName}".TrimEnd(), rect.Left + pad, y, new Color4(0.92f, 0.92f, 0.92f, 1))); y += lineH;
        _uiTextDrawList.Add(new NameDrawInfo($"Price: {_ybDealSystem.PostPrice}", rect.Left + pad, y, new Color4(0.95f, 0.85f, 0.25f, 1f))); y += lineH;
        if (!string.IsNullOrWhiteSpace(_ybDealSystem.PostTime))
        {
            _uiTextDrawList.Add(new NameDrawInfo($"Time: {_ybDealSystem.PostTime}".TrimEnd(), rect.Left + pad, y, new Color4(0.92f, 0.92f, 0.92f, 1)));
            y += lineH;
        }

        _uiTextDrawList.Add(new NameDrawInfo("Items:", rect.Left + pad, y, new Color4(0.92f, 0.92f, 0.92f, 1))); y += lineH;

        for (int i = 0; i < shownItems; i++)
        {
            ClientItem item = items[i];
            string name = string.IsNullOrWhiteSpace(item.NameString) ? "(unknown)" : item.NameString.Trim();
            string suffix = item.MakeIndex == 1 && item.Dura > 1 ? $" x{item.Dura}" : string.Empty;
            Color4 color = i == selectedIndex ? new Color4(0.95f, 0.85f, 0.25f, 1f) : new Color4(0.92f, 0.92f, 0.92f, 1f);

            _uiTextDrawList.Add(new NameDrawInfo($"{i + 1}. {name}{suffix}", rect.Left + pad, y, color));

            DrawingRectangle itemRect = new(rect.Left + pad, y - 2, rect.Width - (pad * 2), lineH);
            _ybDealClickPoints.Add(new YbDealClickPoint(itemRect, YbDealClickKind.Item, Index: i));

            y += lineH;
        }

        _uiTextDrawList.Add(new NameDrawInfo(
            mode == MirYbDealDialogMode.Deal
                ? "Enter: buy  C: cancel  Esc/RightClick: close"
                : "C: cancel sell  Esc/RightClick: close",
            rect.Left + pad,
            buttonsY - lineH - 2,
            new Color4(0.75f, 0.75f, 0.8f, 1f)));

        if (mode == MirYbDealDialogMode.Deal)
        {
            _uiTextDrawList.Add(new NameDrawInfo("Buy", buyRect.Left + 12, buyRect.Top + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));
            _uiTextDrawList.Add(new NameDrawInfo("Cancel", cancelRect.Left + 12, cancelRect.Top + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));
        }
        else
        {
            _uiTextDrawList.Add(new NameDrawInfo("Cancel Sell", cancelSellRect.Left + 12, cancelSellRect.Top + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));
        }
        _uiTextDrawList.Add(new NameDrawInfo("Close", closeBottomRect.Left + 12, closeBottomRect.Top + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));

        return true;

        void DrawButton(DrawingRectangle buttonRect, bool enabled)
        {
            Color4 border = enabled ? new Color4(0, 0, 0, 0.55f) : new Color4(0, 0, 0, 0.45f);
            Color4 fill = enabled ? new Color4(0.18f, 0.18f, 0.23f, 0.85f) : new Color4(0.12f, 0.12f, 0.16f, 0.75f);

            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(buttonRect.Left - 1, buttonRect.Top - 1, buttonRect.Width + 2, buttonRect.Height + 2), color: border);
            _spriteBatch.Draw(_whiteTexture, buttonRect, color: fill);
        }
    }

    private bool TryDrawMissionUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!_missionSystem.Visible)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        const int pad = 10;
        const int header = 22;
        const int lineH = 18;

        int panelW = Math.Clamp(view.LogicalSize.Width / 2, 520, 900);
        int panelH = Math.Clamp(view.LogicalSize.Height - 32, 260, 560);
        int x0 = 16;
        int y0 = 16;

        int missionClass = _missionSystem.MissionClass;

        var missions = _world.GetMissions((byte)missionClass).Values
            .OrderBy(static m => m.Index)
            .ToList();

        int total = missions.Count;

        DrawingRectangle rect = new(x0, y0, panelW, panelH);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, rect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        int listW = Math.Clamp(panelW / 3, 180, 300);
        int listX = rect.Left + pad;
        int detailX = rect.Left + pad + listW + pad;

        int yBodyTop = rect.Top + pad + header;
        int yHelp = rect.Bottom - pad - lineH;
        int listStartY = yBodyTop + lineH;
        int listLines = Math.Max(1, (yHelp - listStartY) / lineH);

        _missionSystem.EnsureListWindow(total, listLines);
        int selectedIndex = _missionSystem.SelectedIndex;
        int topIndex = _missionSystem.TopIndex;

        _uiTextDrawList.Add(new NameDrawInfo("Missions", rect.Left + pad, rect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));

        _uiTextDrawList.Add(new NameDrawInfo(
            $"Class={missionClass}  Count={total}  New={(_world.NewMissionPending ? 1 : 0)}",
            listX,
            yBodyTop,
            new Color4(0.92f, 0.92f, 0.92f, 1f)));

        if (total == 0)
        {
            _uiTextDrawList.Add(new NameDrawInfo("(no missions)", listX, listStartY, new Color4(0.92f, 0.92f, 0.92f, 1f)));
        }
        else
        {
            int maxListChars = Math.Max(10, (listW - 8) / 8);

            int y = listStartY;
            int end = Math.Min(total, topIndex + listLines);
            for (int i = topIndex; i < end; i++)
            {
                MirMission m = missions[i];
                string title = string.IsNullOrWhiteSpace(m.Title) ? "(untitled)" : m.Title.Trim();
                string line = $"{m.Index}: {Trunc(title, maxListChars)}";

                bool selected = i == selectedIndex;
                _uiTextDrawList.Add(new NameDrawInfo(
                    (selected ? "> " : "  ") + line,
                    listX,
                    y,
                    selected ? new Color4(0.95f, 0.85f, 0.25f, 1f) : new Color4(0.92f, 0.92f, 0.92f, 1f)));

                y += lineH;
            }

            MirMission selectedMission = missions[selectedIndex];
            string selectedTitle = string.IsNullOrWhiteSpace(selectedMission.Title) ? "(untitled)" : selectedMission.Title.Trim();
            _uiTextDrawList.Add(new NameDrawInfo($"#{selectedMission.Index} {selectedTitle}", detailX, yBodyTop, new Color4(0.92f, 0.92f, 0.92f, 1f)));

            int detailW = rect.Right - pad - detailX;
            int maxDetailChars = Math.Max(16, detailW / 8);
            int detailLines = Math.Max(1, listLines - 1);
            int dy = listStartY;
            foreach (string line in WrapLines(selectedMission.Description, maxDetailChars, detailLines))
            {
                _uiTextDrawList.Add(new NameDrawInfo(line, detailX, dy, new Color4(0.92f, 0.92f, 0.92f, 1f)));
                dy += lineH;
            }
        }

        _uiTextDrawList.Add(new NameDrawInfo(
            "Ctrl+L: close   1-4: class   Up/Down: select   PgUp/PgDn/Home/End: scroll",
            rect.Left + pad,
            yHelp,
            new Color4(0.92f, 0.92f, 0.92f, 1f)));

        return true;

        static string Trunc(string text, int maxChars)
        {
            if (string.IsNullOrEmpty(text) || maxChars <= 0)
                return string.Empty;

            if (text.Length <= maxChars)
                return text;

            int keep = Math.Max(0, maxChars - 1);
            return keep > 0 ? text[..keep] + "…" : "…";
        }

        static IEnumerable<string> WrapLines(string text, int maxChars, int maxLines)
        {
            if (maxLines <= 0)
                yield break;

            if (string.IsNullOrWhiteSpace(text))
            {
                yield return "(no description)";
                yield break;
            }

            string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
            foreach (string raw in normalized.Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0)
                {
                    yield return string.Empty;
                    maxLines--;
                    if (maxLines <= 0)
                        yield break;
                    continue;
                }

                int idx = 0;
                while (idx < line.Length && maxLines > 0)
                {
                    int take = Math.Min(maxChars, line.Length - idx);
                    yield return line.Substring(idx, take);
                    idx += take;
                    maxLines--;
                }

                if (maxLines <= 0)
                    yield break;
            }
        }
    }

    private bool TryDrawOpenBoxUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!_world.BoxOpen)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        const int cols = 3;
        const int rows = 3;
        const int pad = 10;
        const int header = 22;
        const int slot = 54;
        const int lineH = 18;
        const int btnH = 24;
        const int btnW = 90;
        const int btnGap = 10;

        Vector2 mouseLogical = new(float.NegativeInfinity, float.NegativeInfinity);
        try
        {
            System.Drawing.Point mouseClient = _renderControl.PointToClient(Cursor.Position);
            if (view.ViewportRect.Contains(mouseClient))
            {
                mouseLogical = new Vector2(
                    (mouseClient.X - view.Offset.X) / Math.Max(0.0001f, view.Scale.X),
                    (mouseClient.Y - view.Offset.Y) / Math.Max(0.0001f, view.Scale.Y));
            }
        }
        catch
        {
            
        }

        int gridW = cols * slot;
        int gridH = rows * slot;

        int panelW = Math.Clamp(view.LogicalSize.Width / 3, 360, 520);
        int x0 = Math.Max(16, (view.LogicalSize.Width - panelW) / 2);
        int y0 = 16;

        int selectedIndex = _boxSystem.GetSelectedIndexForUi();

        MirBoxItem selected = _world.BoxItems[selectedIndex];
        string selectedName = selected.Name.Trim();
        string selectedInfo = selectedName.Length > 0
            ? $"{selectedName}  x{selected.Number}"
            : $"Slot {selectedIndex + 1}";

        int svrIdx = _world.BoxServerItemIndex;
        string serverInfo = svrIdx is >= 1 and <= 9 ? $"Server: {svrIdx}" : "Server: (none)";

        bool flashEnabled = svrIdx == 0 && !_boxSystem.FlashRequested && !_boxSystem.GetRequested;
        bool getEnabled = svrIdx != 0 && !_boxSystem.GetRequested;

        string status = _boxSystem.FlashRequested ? "(waiting server...)" : string.Empty;

        var lines = new List<string>(6)
        {
            $"Param={_world.BoxParam}  {serverInfo}  {status}".TrimEnd(),
            $"Selected: {selectedIndex + 1}  {selectedInfo}"
        };

        lines.Add(svrIdx == 0 ? "Flash: click 'Flash' to roll" : "Get: double click highlighted slot or click 'Get'");
        lines.Add("Esc/Close: close");

        int panelH = (pad * 2) + header + gridH + (lines.Count * lineH) + btnGap + btnH;
        DrawingRectangle rect = new(x0, y0, panelW, panelH);
        _boxPanelRect = rect;

        int gridX = rect.Left + (rect.Width - gridW) / 2;
        int gridY = rect.Top + pad + header;

        int btnY = rect.Bottom - pad - btnH;
        DrawingRectangle flashRect = new(rect.Left + pad, btnY, btnW, btnH);
        DrawingRectangle getRect = new(flashRect.Right + btnGap, btnY, btnW, btnH);
        DrawingRectangle closeRect = new(rect.Right - pad - btnW, btnY, btnW, btnH);

        _boxClickPoints.Add(new BoxClickPoint(flashRect, BoxClickKind.Flash, Index: -1));
        _boxClickPoints.Add(new BoxClickPoint(getRect, BoxClickKind.Get, Index: -1));
        _boxClickPoints.Add(new BoxClickPoint(closeRect, BoxClickKind.Close, Index: -1));

        string resourceRoot = GetResourceRootDir();
        string dataDir = Path.Combine(resourceRoot, "Data");
        string? itemsPath = TryResolveArchiveFilePath(dataDir, "Items");
        string? items2Path = TryResolveArchiveFilePath(dataDir, "Items2");
        string? shineItemsPath = TryResolveArchiveFilePath(dataDir, "ShineItems");

        int prefetchBudget = 6;

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, rect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));

        DrawButton(flashRect, flashEnabled);
        DrawButton(getRect, getEnabled);
        DrawButton(closeRect, enabled: true);

        for (int i = 0; i < 9; i++)
        {
            int row = i / cols;
            int col = i % cols;
            int x = gridX + (col * slot);
            int y = gridY + (row * slot);
            var slotRect = new DrawingRectangle(x, y, slot - 2, slot - 2);

            bool hovered = mouseLogical.X >= slotRect.Left && mouseLogical.X < slotRect.Right &&
                           mouseLogical.Y >= slotRect.Top && mouseLogical.Y < slotRect.Bottom;

            bool isServer = (i + 1) == svrIdx;
            bool isSelected = i == selectedIndex;

            Color4 border = isServer
                ? new Color4(0.95f, 0.75f, 0.25f, 0.90f)
                : hovered
                    ? new Color4(0.95f, 0.85f, 0.25f, 0.85f)
                    : isSelected
                        ? new Color4(0.60f, 0.88f, 1.0f, 0.85f)
                        : new Color4(0, 0, 0, 0.55f);

            Color4 fill = isServer
                ? new Color4(0.22f, 0.20f, 0.12f, 0.85f)
                : isSelected
                    ? new Color4(0.16f, 0.18f, 0.24f, 0.75f)
                    : hovered
                        ? new Color4(0.22f, 0.22f, 0.28f, 0.75f)
                        : new Color4(0.10f, 0.10f, 0.12f, 0.65f);

            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(slotRect.Left - 1, slotRect.Top - 1, slotRect.Width + 2, slotRect.Height + 2), color: border);
            _spriteBatch.Draw(_whiteTexture, slotRect, color: fill);

            _boxClickPoints.Add(new BoxClickPoint(slotRect, BoxClickKind.Slot, Index: i));

            MirBoxItem item = _world.BoxItems[i];
            int looks = item.Looks;
            if (looks >= 0 && TryResolveBagItemIconArchive(looks, out string? archivePath, out int imageIndex) && archivePath != null)
            {
                if (TryGetArchiveTexture(archivePath, imageIndex, out D3D11Texture2D tex))
                {
                    int maxW = slotRect.Width - 6;
                    int maxH = slotRect.Height - 6;
                    int drawW = Math.Min(maxW, tex.Width);
                    int drawH = Math.Min(maxH, tex.Height);
                    int dx = slotRect.Left + (slotRect.Width - drawW) / 2;
                    int dy = slotRect.Top + (slotRect.Height - drawH) / 2;
                    _spriteBatch.Draw(tex, new DrawingRectangle(dx, dy, drawW, drawH));
                }
                else if (prefetchBudget > 0)
                {
                    PrefetchArchiveImage(archivePath, imageIndex);
                    prefetchBudget--;
                }
            }

            if (item.Number > 1)
                _uiTextDrawList.Add(new NameDrawInfo(item.Number.ToString(), slotRect.Right - 14, slotRect.Bottom - 16, new Color4(0.98f, 0.98f, 0.98f, 1)));

            _uiTextDrawList.Add(new NameDrawInfo((i + 1).ToString(), slotRect.Left + 3, slotRect.Top + 2, new Color4(0.85f, 0.85f, 0.85f, 1)));

            if (hovered && string.IsNullOrWhiteSpace(_uiTooltipText) && !string.IsNullOrWhiteSpace(item.Name))
            {
                string tip = item.Number > 1 ? $"{item.Name} x{item.Number}" : item.Name;
                SetTooltip(tip, new Vector2(slotRect.Right + 6, slotRect.Top));
            }
        }

        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        _uiTextDrawList.Add(new NameDrawInfo("RareBox", rect.Left + pad, rect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));

        int textY = gridY + gridH + pad;
        foreach (string line in lines)
        {
            _uiTextDrawList.Add(new NameDrawInfo(line, rect.Left + pad, textY, new Color4(0.92f, 0.92f, 0.92f, 1)));
            textY += lineH;
        }

        _uiTextDrawList.Add(new NameDrawInfo("Flash", flashRect.Left + 10, flashRect.Top + 4, flashEnabled ? new Color4(0.95f, 0.95f, 0.95f, 1) : new Color4(0.55f, 0.55f, 0.55f, 1)));
        _uiTextDrawList.Add(new NameDrawInfo("Get", getRect.Left + 10, getRect.Top + 4, getEnabled ? new Color4(0.95f, 0.95f, 0.95f, 1) : new Color4(0.55f, 0.55f, 0.55f, 1)));
        _uiTextDrawList.Add(new NameDrawInfo("Close", closeRect.Left + 10, closeRect.Top + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));

        return true;

        void DrawButton(DrawingRectangle buttonRect, bool enabled)
        {
            Color4 border = enabled ? new Color4(0, 0, 0, 0.55f) : new Color4(0, 0, 0, 0.45f);
            Color4 fill = enabled ? new Color4(0.18f, 0.18f, 0.23f, 0.85f) : new Color4(0.12f, 0.12f, 0.16f, 0.75f);

            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(buttonRect.Left - 1, buttonRect.Top - 1, buttonRect.Width + 2, buttonRect.Height + 2), color: border);
            _spriteBatch.Draw(_whiteTexture, buttonRect, color: fill);
        }

        bool TryResolveBagItemIconArchive(int looks, out string? archivePath, out int imageIndex)
        {
            archivePath = null;
            imageIndex = 0;

            if (looks < 0)
                return false;

            if (looks < 10_000)
            {
                archivePath = itemsPath;
                imageIndex = looks;
                return archivePath != null;
            }

            if (looks < 20_000)
            {
                archivePath = items2Path ?? itemsPath;
                imageIndex = looks - 10_000;
                return archivePath != null;
            }

            if (looks < 30_000)
            {
                archivePath = shineItemsPath ?? itemsPath;
                imageIndex = looks - 20_000;
                return archivePath != null;
            }

            archivePath = itemsPath;
            imageIndex = looks % 10_000;
            return archivePath != null;
        }

        bool TryGetArchiveTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
        {
            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
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

        void PrefetchArchiveImage(string archivePath, int imageIndex)
        {
            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
            {
                _ = _wilImageCache.GetImageAsyncFullPath(archivePath, imageIndex);
                return;
            }

            _ = _packDataImageCache.GetImageAsyncFullPath(archivePath, imageIndex);
        }
    }

    private bool TryDrawBookUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!_world.BookOpen || _world.BookPath <= 0)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        const int pad = 10;
        const int header = 22;
        const int lineH = 18;
        const int btnH = 24;
        const int btnW = 90;
        const int btnGap = 10;

        int panelW = Math.Clamp(view.LogicalSize.Width / 2, 360, 620);
        int x0 = Math.Max(16, (view.LogicalSize.Width - panelW) / 2);
        int y0 = 16;

        int path = _world.BookPath;
        int page = _world.BookPage;
        int merchantId = _world.BookMerchantId;
        string label = _world.BookLabel.Trim();

        if (path != 1)
        {
            string resourceRoot = GetResourceRootDir();
            string dataDir = Path.Combine(resourceRoot, "Data");
            string? opUiPath = TryResolveArchiveFilePath(dataDir, "NewopUI");

            if (!string.IsNullOrWhiteSpace(opUiPath))
            {
                int imageIndex = (path * 10 + page) + 100;
                if (TryGetArchiveTexture(opUiPath, imageIndex, out D3D11Texture2D bookTexture))
                {
                    int imgW = bookTexture.Width;
                    int imgH = bookTexture.Height;

                    int availW = Math.Max(0, view.LogicalSize.Width - 32 - (pad * 2));
                    int availH = Math.Max(0, view.LogicalSize.Height - 32 - header - btnH - btnGap - (pad * 2));

                    float scale = 1f;
                    if (imgW > 0 && imgH > 0 && availW > 0 && availH > 0)
                    {
                        float sx = (float)availW / imgW;
                        float sy = (float)availH / imgH;
                        scale = Math.Clamp(Math.Min(1f, Math.Min(sx, sy)), 0.25f, 1f);
                    }

                    int drawW = Math.Max(1, (int)Math.Round(imgW * scale));
                    int drawH = Math.Max(1, (int)Math.Round(imgH * scale));

                    int imagePanelW = drawW + (pad * 2);
                    int imagePanelH = (pad * 2) + header + drawH + btnGap + btnH;

                    int imgPanelX = Math.Max(16, (view.LogicalSize.Width - imagePanelW) / 2);
                    int imgPanelY = 16;

                    DrawingRectangle imagePanelRect = new(imgPanelX, imgPanelY, imagePanelW, imagePanelH);
                    _bookPanelRect = imagePanelRect;

                    int imageBtnY = imagePanelRect.Bottom - pad - btnH;
                    DrawingRectangle imageCloseRect = new(imagePanelRect.Right - pad - btnW, imageBtnY, btnW, btnH);
                    _bookClickPoints.Add(new BookClickPoint(imageCloseRect, BookClickKind.Close));

                    DrawingRectangle imageRect = new(imagePanelRect.Left + pad, imagePanelRect.Top + pad + header, drawW, drawH);

                    _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
                    _spriteBatch.Draw(_whiteTexture, imagePanelRect, color: new Color4(0, 0, 0, 0.55f));
                    _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(imagePanelRect.Left + 1, imagePanelRect.Top + 1, imagePanelRect.Width - 2, imagePanelRect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
                    _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(imagePanelRect.Left + 1, imagePanelRect.Top + 1, imagePanelRect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));
                    _spriteBatch.Draw(bookTexture, imageRect, color: new Color4(1f, 1f, 1f, 1f));
                    DrawButton(imageCloseRect);
                    _spriteBatch.End();

                    stats = _spriteBatch.Stats;
                    _uiTextDrawList.Add(new NameDrawInfo("Book", imagePanelRect.Left + pad, imagePanelRect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));
                    _uiTextDrawList.Add(new NameDrawInfo("Close", imageCloseRect.Left + 10, imageCloseRect.Top + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));

                    return true;
                }
            }
        }

        var lines = new List<string>(8);
        lines.Add($"Path={path}  Page={page}  Merchant={merchantId}");
        if (!string.IsNullOrWhiteSpace(label))
            lines.Add(label.Length <= 80 ? $"Label: {label}" : $"Label: {label[..80]}...");

        if (path == 1)
        {
            lines.Add(page == 4 ? "Enter/OK: confirm" : "PageUp/PageDown: prev/next");
        }

        lines.Add("Esc/Close: close");

        int panelH = (pad * 2) + header + (lines.Count * lineH) + btnGap + btnH;
        DrawingRectangle rect = new(x0, y0, panelW, panelH);
        _bookPanelRect = rect;

        int btnY = rect.Bottom - pad - btnH;

        DrawingRectangle closeRect = new(rect.Right - pad - btnW, btnY, btnW, btnH);
        _bookClickPoints.Add(new BookClickPoint(closeRect, BookClickKind.Close));

        if (path == 1)
        {
            if (page == 4)
            {
                DrawingRectangle okRect = new(rect.Left + pad, btnY, btnW, btnH);
                _bookClickPoints.Add(new BookClickPoint(okRect, BookClickKind.Confirm));
            }
            else
            {
                DrawingRectangle prevRect = new(rect.Left + pad, btnY, btnW, btnH);
                DrawingRectangle nextRect = new(prevRect.Right + btnGap, btnY, btnW, btnH);
                _bookClickPoints.Add(new BookClickPoint(prevRect, BookClickKind.Prev));
                _bookClickPoints.Add(new BookClickPoint(nextRect, BookClickKind.Next));
            }
        }

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, rect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));

        foreach (BookClickPoint point in _bookClickPoints)
            DrawButton(point.Rect);

        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        _uiTextDrawList.Add(new NameDrawInfo("Book", rect.Left + pad, rect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));

        int y = rect.Top + pad + header;
        foreach (string line in lines)
        {
            _uiTextDrawList.Add(new NameDrawInfo(line, rect.Left + pad, y, new Color4(0.92f, 0.92f, 0.92f, 1)));
            y += lineH;
        }

        Color4 btnColor = new(0.95f, 0.95f, 0.95f, 1);
        foreach (BookClickPoint point in _bookClickPoints)
        {
            string caption = point.Kind switch
            {
                BookClickKind.Close => "Close",
                BookClickKind.Prev => "Prev",
                BookClickKind.Next => "Next",
                BookClickKind.Confirm => "OK",
                _ => string.Empty
            };

            if (caption.Length > 0)
                _uiTextDrawList.Add(new NameDrawInfo(caption, point.Rect.Left + 10, point.Rect.Top + 4, btnColor));
        }

        return true;

        void DrawButton(DrawingRectangle buttonRect)
        {
            _spriteBatch.Draw(_whiteTexture, buttonRect, color: new Color4(0.08f, 0.08f, 0.10f, 0.65f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(buttonRect.Left + 1, buttonRect.Top + 1, Math.Max(0, buttonRect.Width - 2), Math.Max(0, buttonRect.Height - 2)), color: new Color4(0.16f, 0.16f, 0.20f, 0.85f));
        }

        bool TryGetArchiveTexture(string archivePath, int imageIndex, out D3D11Texture2D texture)
        {
            if (archivePath.EndsWith(".wil", StringComparison.OrdinalIgnoreCase) ||
                archivePath.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
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
    }

    private void HandleRefineOpen()
    {
        _bagWindowVisible = true;
        _heroBagView = false;
        ClearItemDrag();

        RestoreAndClearRefineItems();
        ClearRefineTakeOffPending();

        AppendLog("[refine] opened");
    }

    private void HandleBagItemAdded(ClientItem item)
    {
        if (_refinePendingTakeOffSlot < 0)
            return;

        if (!_world.RefineOpen)
        {
            ClearRefineTakeOffPending();
            return;
        }

        if (item.MakeIndex == 0 || item.MakeIndex != _refinePendingTakeOffMakeIndex)
            return;

        if (!_world.TryRemoveBagItemByMakeIndex(item.MakeIndex, out ClientItem removed))
            return;

        int slotIndex = _refinePendingTakeOffSlot;
        if ((uint)slotIndex >= (uint)_refineItems.Length)
        {
            _world.RestoreBagItem(removed);
            ClearRefineTakeOffPending();
            return;
        }

        if (_refineItems[slotIndex].MakeIndex != 0)
            _world.RestoreBagItem(_refineItems[slotIndex]);

        _refineItems[slotIndex] = removed;
        ClearRefineTakeOffPending();

        AppendLog($"[refine] takeoff -> slot={slotIndex + 1} '{removed.NameString}' makeIndex={removed.MakeIndex}");
    }

    private void TickRefine(long nowMs)
    {
        if (_refinePendingTakeOffSlot < 0)
            return;

        if (!_world.RefineOpen)
        {
            ClearRefineTakeOffPending();
            return;
        }

        const long timeoutMs = 5_000;
        if (nowMs - _refinePendingTakeOffSinceMs < timeoutMs)
            return;

        AppendLog($"[refine] takeoff timeout slot={_refinePendingTakeOffSlot + 1} makeIndex={_refinePendingTakeOffMakeIndex}");
        ClearRefineTakeOffPending();
    }

    private void CloseRefineUi(bool logUi)
    {
        if (!_world.RefineOpen)
            return;

        RestoreAndClearRefineItems();

        if (_itemDragActive && _itemDragSource == ItemDragSource.Refine)
            ClearItemDrag();

        ClearRefineTakeOffPending();
        _world.CloseRefine();

        if (logUi)
            AppendLog("[refine] closed");
    }

    private void RestoreAndClearRefineItems()
    {
        for (int i = 0; i < _refineItems.Length; i++)
        {
            if (_refineItems[i].MakeIndex != 0)
                _world.RestoreBagItem(_refineItems[i]);
            _refineItems[i] = default;
        }
    }

    private void ClearRefineTakeOffPending()
    {
        _refinePendingTakeOffSlot = -1;
        _refinePendingTakeOffMakeIndex = 0;
        _refinePendingTakeOffSinceMs = 0;
    }

    private async Task TrySendRefineOkAsync(CancellationToken token)
    {
        if (!_world.RefineOpen)
            return;

        if (_refinePendingTakeOffSlot >= 0)
        {
            AppendLog("[refine] busy (waiting for takeoff).");
            return;
        }

        long nowMs = Environment.TickCount64;
        if (nowMs - _refineLastSendMs < 800)
            return;
        _refineLastSendMs = nowMs;

        int count = 0;
        for (int i = 0; i < _refineItems.Length; i++)
        {
            if (_refineItems[i].MakeIndex != 0 && !string.IsNullOrWhiteSpace(_refineItems[i].NameString))
                count++;
        }

        if (count < 3)
        {
            AppendLog("[refine] need 3 items.");
            return;
        }

        ClientItem[] snapshot = (ClientItem[])_refineItems.Clone();
        Array.Clear(_refineItems, 0, _refineItems.Length);

        try
        {
            byte[] body = BuildRefineItemsBuffer(snapshot);
            await _session.SendClientBufferAsync(Grobal2.CM_REFINEITEM, 0, 0, 0, 0, body, token).ConfigureAwait(true);
            AppendLog("[refine] -> CM_REFINEITEM");
        }
        catch (Exception ex)
        {
            Array.Copy(snapshot, _refineItems, _refineItems.Length);
            AppendLog($"[refine] CM_REFINEITEM send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task HandleRefineSlotClickAsync(int slotIndex, CancellationToken token)
    {
        if ((uint)slotIndex >= (uint)_refineItems.Length)
            return;

        if (_heroBagView)
            _heroBagView = false;

        if (_refinePendingTakeOffSlot >= 0)
        {
            AppendLog("[refine] busy (waiting for takeoff).");
            return;
        }

        if (_itemDragActive)
        {
            if (_itemDragItem.MakeIndex == 0 || string.IsNullOrWhiteSpace(_itemDragItem.NameString))
            {
                ClearItemDrag();
                return;
            }

            if (_itemDragSource == ItemDragSource.Bag)
            {
                if (_itemDragHero)
                {
                    AppendLog("[refine] hero bag not supported.");
                    ClearItemDrag();
                    return;
                }

                if (_itemDragItem.S.NeedIdentify >= 4)
                {
                    AppendLog($"[refine] ignored (stall makeIndex={_itemDragItem.MakeIndex})");
                    ClearItemDrag();
                    return;
                }

                if (!_world.TryRemoveBagItemByMakeIndex(_itemDragItem.MakeIndex, out ClientItem removed))
                {
                    ClearItemDrag();
                    return;
                }

                ClientItem previous = _refineItems[slotIndex];
                _refineItems[slotIndex] = removed;

                if (previous.MakeIndex != 0)
                {
                    _itemDragSource = ItemDragSource.Refine;
                    _itemDragSourceIndex = slotIndex;
                    _itemDragHero = false;
                    _itemDragItem = previous;
                    AppendLog($"[refine] slot {slotIndex + 1} swap");
                    return;
                }

                ClearItemDrag();
                AppendLog($"[refine] slot {slotIndex + 1} set '{removed.NameString}'");
                return;
            }

            if (_itemDragSource == ItemDragSource.Refine)
            {
                ClientItem held = _itemDragItem;
                ClientItem previous = _refineItems[slotIndex];
                _refineItems[slotIndex] = held;

                if (previous.MakeIndex != 0)
                {
                    _itemDragSourceIndex = slotIndex;
                    _itemDragItem = previous;
                    AppendLog($"[refine] slot swap -> {slotIndex + 1}");
                    return;
                }

                ClearItemDrag(restoreRefineItemToBag: false);
                AppendLog($"[refine] slot {slotIndex + 1} moved");
                return;
            }

            if (_itemDragSource == ItemDragSource.Use)
            {
                if (_itemDragHero)
                {
                    AppendLog("[refine] hero equip not supported.");
                    ClearItemDrag();
                    return;
                }

                int where = _itemDragSourceIndex;
                ClientItem item = _itemDragItem;

                if (_refineItems[slotIndex].MakeIndex != 0)
                {
                    _world.RestoreBagItem(_refineItems[slotIndex]);
                    _refineItems[slotIndex] = default;
                }

                _refinePendingTakeOffSlot = slotIndex;
                _refinePendingTakeOffMakeIndex = item.MakeIndex;
                _refinePendingTakeOffSinceMs = Environment.TickCount64;

                await _equipSystem.TryTakeOffAsync(
                    item,
                    heroEquip: false,
                    where,
                    logPrefix: "[equip]",
                    actionLabel: "refine takeoff",
                    successSuffix: " (refine)",
                    token).ConfigureAwait(true);

                long nowMs = Environment.TickCount64;
                if (!_inventoryPendingSystem.IsUseItemPendingActive(nowMs))
                    ClearRefineTakeOffPending();

                ClearItemDrag();
                return;
            }

            ClearItemDrag();
            return;
        }

        ClientItem existing = _refineItems[slotIndex];
        if (existing.MakeIndex == 0)
            return;

        _refineItems[slotIndex] = default;
        _itemDragActive = true;
        _itemDragSource = ItemDragSource.Refine;
        _itemDragSourceIndex = slotIndex;
        _itemDragHero = false;
        _itemDragItem = existing;
        AppendLog($"[refine] pick slot={slotIndex + 1} '{existing.NameString}' makeIndex={existing.MakeIndex}");
    }

    private static byte[] BuildRefineItemsBuffer(ReadOnlySpan<ClientItem> items)
    {
        using var ms = new MemoryStream(128);
        using var writer = new BinaryWriter(ms);

        int count = Math.Min(items.Length, 3);
        for (int i = 0; i < count; i++)
        {
            writer.Write(items[i].MakeIndex);
            WritePascalString(writer, TrimToMaxGbkBytes(items[i].NameString, Grobal2.ItemNameLen), Grobal2.ItemNameLen);
        }

        for (int i = count; i < 3; i++)
        {
            writer.Write(0);
            WritePascalString(writer, string.Empty, Grobal2.ItemNameLen);
        }

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

    private bool TryDrawRefineUi(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!_world.RefineOpen)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        const int pad = 10;
        const int header = 22;
        const int slot = 52;
        const int slotGap = 14;
        const int btnH = 24;
        const int btnW = 90;

        int panelW = Math.Clamp(slot * 5, 320, 520);
        int panelH = header + (pad * 3) + (slot * 2) + slotGap + btnH + 22;

        int x0 = Math.Max(16, (view.LogicalSize.Width - panelW) / 2);
        int y0 = Math.Max(16, (view.LogicalSize.Height - panelH) / 2);

        DrawingRectangle rect = new(x0, y0, panelW, panelH);
        _refinePanelRect = rect;

        int slot0X = rect.Left + (rect.Width - slot) / 2;
        int slot0Y = rect.Top + header + pad;
        DrawingRectangle slot0Rect = new(slot0X, slot0Y, slot, slot);

        int slot1Y = slot0Rect.Bottom + slotGap;
        DrawingRectangle slot1Rect = new(rect.Left + pad, slot1Y, slot, slot);
        DrawingRectangle slot2Rect = new(rect.Right - pad - slot, slot1Y, slot, slot);

        int btnY = rect.Bottom - pad - btnH;
        DrawingRectangle okRect = new(rect.Left + pad, btnY, btnW, btnH);
        DrawingRectangle closeRect = new(rect.Right - pad - btnW, btnY, btnW, btnH);

        _refineClickPoints.Add(new RefineClickPoint(slot0Rect, RefineClickKind.Slot, 0));
        _refineClickPoints.Add(new RefineClickPoint(slot1Rect, RefineClickKind.Slot, 1));
        _refineClickPoints.Add(new RefineClickPoint(slot2Rect, RefineClickKind.Slot, 2));
        _refineClickPoints.Add(new RefineClickPoint(okRect, RefineClickKind.Ok, -1));
        _refineClickPoints.Add(new RefineClickPoint(closeRect, RefineClickKind.Close, -1));

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, 0.55f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, rect.Height - 2), color: new Color4(0.12f, 0.12f, 0.16f, 0.85f));
        _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(rect.Left + 1, rect.Top + 1, rect.Width - 2, header), color: new Color4(0.18f, 0.18f, 0.23f, 0.85f));

        DrawSlot(slot0Rect);
        DrawSlot(slot1Rect);
        DrawSlot(slot2Rect);

        DrawButton(okRect);
        DrawButton(closeRect);

        _spriteBatch.End();
        stats = _spriteBatch.Stats;

        _uiTextDrawList.Add(new NameDrawInfo("Refine", rect.Left + pad, rect.Top + 4, new Color4(0.6f, 0.88f, 1.0f, 1f)));

        DrawSlotText(slot0Rect, _refineItems[0], slotLabel: "1");
        DrawSlotText(slot1Rect, _refineItems[1], slotLabel: "2");
        DrawSlotText(slot2Rect, _refineItems[2], slotLabel: "3");

        if (_refinePendingTakeOffSlot >= 0)
        {
            string info = $"Waiting takeoff... (slot {_refinePendingTakeOffSlot + 1})";
            _uiTextDrawList.Add(new NameDrawInfo(info, rect.Left + pad, okRect.Top - 20, new Color4(0.95f, 0.85f, 0.25f, 1)));
        }
        else
        {
            _uiTextDrawList.Add(new NameDrawInfo("Drag 3 items, then OK.", rect.Left + pad, okRect.Top - 20, new Color4(0.85f, 0.85f, 0.85f, 1)));
        }

        _uiTextDrawList.Add(new NameDrawInfo("OK", okRect.Left + 12, okRect.Top + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));
        _uiTextDrawList.Add(new NameDrawInfo("Close", closeRect.Left + 10, closeRect.Top + 4, new Color4(0.95f, 0.95f, 0.95f, 1)));

        return true;

        void DrawButton(DrawingRectangle buttonRect)
        {
            _spriteBatch.Draw(_whiteTexture, buttonRect, color: new Color4(0.08f, 0.08f, 0.10f, 0.65f));
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(buttonRect.Left + 1, buttonRect.Top + 1, Math.Max(0, buttonRect.Width - 2), Math.Max(0, buttonRect.Height - 2)), color: new Color4(0.16f, 0.16f, 0.20f, 0.85f));
        }

        void DrawSlot(DrawingRectangle slotRect)
        {
            _spriteBatch.Draw(_whiteTexture, new DrawingRectangle(slotRect.Left - 1, slotRect.Top - 1, slotRect.Width + 2, slotRect.Height + 2), color: new Color4(0, 0, 0, 0.55f));
            _spriteBatch.Draw(_whiteTexture, slotRect, color: new Color4(0.10f, 0.10f, 0.12f, 0.65f));
        }

        void DrawSlotText(DrawingRectangle slotRect, ClientItem item, string slotLabel)
        {
            string name = item.MakeIndex != 0 ? item.NameString : "(empty)";
            if (name.Length > 12)
                name = name[..12];

            _uiTextDrawList.Add(new NameDrawInfo($"Slot {slotLabel}: {name}", slotRect.Left, slotRect.Bottom + 2, new Color4(0.92f, 0.92f, 0.92f, 1)));
        }
    }

    private bool TryDrawIntroSplash(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (!_introSplashActive)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        int w = view.LogicalSize.Width;
        int h = view.LogicalSize.Height;
        if (w <= 0 || h <= 0)
            return false;

        DrawingRectangle full = new(0, 0, w, h);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, full, color: new Color4(0, 0, 0, 1f));

        D3D11Texture2D? logo = EnsureIntroLogoTexture(frame);
        if (logo != null)
        {
            float scale = MathF.Min(w / (float)logo.Width, h / (float)logo.Height);
            if (scale > 1.0f)
                scale = 1.0f;

            int drawW = Math.Max(1, (int)MathF.Round(logo.Width * scale));
            int drawH = Math.Max(1, (int)MathF.Round(logo.Height * scale));
            int x = (w - drawW) / 2;
            int y = (h - drawH) / 2;
            _spriteBatch.Draw(logo, new DrawingRectangle(x, y, drawW, drawH));
        }

        _spriteBatch.End();
        stats = _spriteBatch.Stats;
        return true;
    }

    private D3D11Texture2D? EnsureIntroLogoTexture(D3D11Frame frame)
    {
        if (_introLogoTexture != null)
            return _introLogoTexture;

        if (_introLogoTextureLoadFailed)
            return null;

        string? path = TryResolveIntroLogoPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            _introLogoTextureLoadFailed = true;
            return null;
        }

        try
        {
            _introLogoTexture = D3D11Texture2D.LoadFromFile(frame.Device, path);
            AppendLog($"[intro] logo loaded: {Path.GetFileName(path)} {_introLogoTexture.Width}x{_introLogoTexture.Height}");
            return _introLogoTexture;
        }
        catch (Exception ex)
        {
            _introLogoTextureLoadFailed = true;
            AppendLog($"[intro] logo load error: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private string? TryResolveIntroLogoPath()
    {
        string baseDir = AppContext.BaseDirectory;
        string resourceRoot = GetResourceRootDir();

        if (_startup is { Logo: { Length: > 0 } logo })
        {
            string? resolved = TryResolveFilePath(baseDir, resourceRoot, logo);
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;
        }

        string? fallback = TryResolveFilePath(baseDir, resourceRoot, "logo.png");
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;

        try
        {
            var current = new DirectoryInfo(baseDir);
            for (int depth = 0; depth < 12 && current != null; depth++)
            {
                string candidate = Path.Combine(current.FullName, "DlpProj", "Source", "MirClient", "logo.png");
                string full = Path.GetFullPath(candidate);
                if (FileExistsCached(full))
                    return full;
                current = current.Parent;
            }
        }
        catch
        {
            
        }

        return null;
    }

    private bool TryDrawLoginNoticeBackground(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (_sceneManager.CurrentId != MirSceneId.LoginNotice)
            return false;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        int w = view.LogicalSize.Width;
        int h = view.LogicalSize.Height;
        if (w <= 0 || h <= 0)
            return false;

        string? chrSelPath = TryEnsureLoginUiArchives() ? _loginUiChrSelPath : null;
        (int baseX, int baseY) = GetClassicUiBaseOrigin(view.LogicalSize);

        DrawingRectangle full = new(0, 0, w, h);

        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, full, color: new Color4(0, 0, 0, 1f));

        if (!string.IsNullOrWhiteSpace(chrSelPath))
            DrawLoginBackground(frame, chrSelPath, baseX, baseY);

        _spriteBatch.End();
        stats = _spriteBatch.Stats;
        return true;
    }

    private bool TryDrawStageTransitionMask(D3D11Frame frame, D3D11ViewTransform view, out SpriteBatchStats stats)
    {
        stats = default;

        if (_spriteBatch == null || _whiteTexture == null)
            return false;

        bool fadeOutInGame = _transitionStage == MirSessionStage.InGame &&
                             _transitionStagePrev is MirSessionStage.SelectGate or MirSessionStage.RunGate;

        if (_sceneManager.CurrentId == MirSceneId.Play && !fadeOutInGame)
            return false;

        if (_transitionStage is not (MirSessionStage.SelectGate or MirSessionStage.RunGate) && !fadeOutInGame)
            return false;

        long nowMs = Environment.TickCount64;
        long age = nowMs - _transitionStageEnterMs;
        if (age < 0)
            age = 0;

        float alpha;
        if (fadeOutInGame)
        {
            const float fadeOutMs = 350f;
            float t = Math.Clamp(age / fadeOutMs, 0f, 1f);
            alpha = 0.55f * (1f - t);
        }
        else
        {
            const float fadeInMs = 250f;
            float t = Math.Clamp(age / fadeInMs, 0f, 1f);
            alpha = 0.55f * t;
        }

        if (alpha <= 0.001f)
            return false;

        DrawingRectangle rect = new(0, 0, view.LogicalSize.Width, view.LogicalSize.Height);
        _spriteBatch.Begin(frame.Context, view, SpriteSampler.Point, SpriteBlendMode.AlphaBlend);
        _spriteBatch.Draw(_whiteTexture, rect, color: new Color4(0, 0, 0, alpha));
        _spriteBatch.End();

        stats = _spriteBatch.Stats;
        return true;
    }

    private static void ParseMerchantLine(string line, List<string> lines, List<(string Label, string Command)> commands)
    {
        string data = line;
        var plain = new StringBuilder(data.Length);

        while (data.Length > 0)
        {
            int lt = data.IndexOf('<');
            if (lt < 0)
            {
                plain.Append(data);
                break;
            }

            int gt = data.IndexOf('>', lt + 1);
            if (gt < 0)
            {
                plain.Append(data);
                break;
            }

            if (lt > 0)
                plain.Append(data.AsSpan(0, lt));

            string tag = data.Substring(lt + 1, gt - lt - 1).Trim();
            data = gt + 1 < data.Length ? data[(gt + 1)..] : string.Empty;

            if (tag.Length == 0)
                continue;

            if (tag.StartsWith("COLOR=", StringComparison.OrdinalIgnoreCase))
            {
                int split = tag.IndexOfAny([' ', '\t', ',']);
                if (split >= 0 && split + 1 < tag.Length)
                {
                    string coloredText = tag[(split + 1)..].Trim();
                    if (coloredText.Length > 0)
                        plain.Append(coloredText);
                }

                continue;
            }

            int slash = tag.IndexOf('/');
            if (slash <= 0 || slash + 1 >= tag.Length)
                continue;

            string label = tag[..slash].Trim();
            string command = tag[(slash + 1)..].Trim();
            if (label.Length == 0 || command.Length == 0)
                continue;

            commands.Add((label, command));
        }

        string plainText = plain.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(plainText))
            lines.Add(plainText);
    }

    private void HandleRenderControlMouseWheel(MouseEventArgs e)
    {
        if (LoginUiVisible)
            return;

        if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
            return;

        int delta = e.Delta;
        if (delta == 0)
            return;

        bool ctrl = (Control.ModifierKeys & Keys.Control) != 0;

        int step = ctrl ? GetInGameChatVisibleLines() : (_chatStatusLarge ? 3 : 1);
        if (step <= 0)
            step = 1;

        ScrollChatBoard(delta > 0 ? -step : step);
    }

    private void HandleRenderControlMouseMove(MouseEventArgs e)
    {
        _lastMouseClientX = e.Location.X;
        _lastMouseClientY = e.Location.Y;

        if (_uiWindowDragTarget == UiWindowDragTarget.None)
            return;

        if ((Control.MouseButtons & MouseButtons.Left) == 0)
        {
            EndUiWindowDrag();
            return;
        }

        if (!TryGetLogicalPoint(e.Location, out Vector2 logical))
            return;

        UpdateUiWindowDrag(logical);
    }

    private void HandleRenderControlMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            EndUiWindowDrag();

        if (e.Button is MouseButtons.Left or MouseButtons.Right)
            _holdMoveActive = false;
    }

    private void EndUiWindowDrag()
    {
        if (_uiWindowDragTarget == UiWindowDragTarget.None)
            return;

        _uiWindowDragTarget = UiWindowDragTarget.None;
        _uiWindowDragOffsetX = 0;
        _uiWindowDragOffsetY = 0;
        _uiWindowDragW = 0;
        _uiWindowDragH = 0;

        try
        {
            _renderControl.Capture = false;
        }
        catch
        {
            
        }
    }

    private bool TryBeginUiWindowDrag(Vector2 logical)
    {
        if (_sceneManager.CurrentId != MirSceneId.Play)
            return false;

        if (_mallWindowVisible && _mallPanelRect is { } mallRect)
        {
            bool overInteractive = false;
            foreach (MallClickPoint point in _mallClickPoints)
            {
                if (RectContains(point.Rect, logical))
                {
                    overInteractive = true;
                    break;
                }
            }

            if (!overInteractive && TryBeginDrag(mallRect, _mallCloseRect, UiWindowDragTarget.Mall, headerHeight: 40))
                return true;
        }

        if (_settingsWindowVisible && _settingsPanelRect is { } settingsRect)
        {
            if (TryBeginDrag(settingsRect, _settingsCloseRect, UiWindowDragTarget.Settings, headerHeight: 28))
                return true;
        }

        if (_world.MerchantDialogOpen && _merchantDialogPanelRect is { } merchantRect)
        {
            if (TryBeginDrag(merchantRect, _merchantDialogCloseRect, UiWindowDragTarget.Merchant, headerHeight: 18))
                return true;
        }

        if (_bagWindowVisible)
        {
            if (_heroBagView)
            {
                if (_heroBagPanelRect is { } heroBagRect &&
                    TryBeginDrag(heroBagRect, _heroBagCloseRect, UiWindowDragTarget.HeroBag, headerHeight: 24))
                {
                    return true;
                }
            }
            else if (_bagPanelRect is { } bagRect &&
                     TryBeginDrag(bagRect, _bagCloseRect, UiWindowDragTarget.Bag, headerHeight: 24))
            {
                return true;
            }
        }

        if (_stateWindowVisible && _statePanelRect is { } stateRect)
        {
            if (TryBeginDrag(stateRect, _stateCloseRect, UiWindowDragTarget.State, headerHeight: 32))
                return true;
        }

        return false;

        bool TryBeginDrag(DrawingRectangle panelRect, DrawingRectangle? closeRect, UiWindowDragTarget target, int headerHeight)
        {
            if (panelRect.Width <= 0 || panelRect.Height <= 0)
                return false;

            int hh = Math.Clamp(headerHeight, 8, panelRect.Height);
            var headerRect = new DrawingRectangle(panelRect.Left, panelRect.Top, panelRect.Width, hh);

            if (!RectContains(headerRect, logical))
                return false;

            if (closeRect is { } c && RectContains(c, logical))
                return false;

            _uiWindowDragTarget = target;
            _uiWindowDragOffsetX = (int)MathF.Round(logical.X) - panelRect.Left;
            _uiWindowDragOffsetY = (int)MathF.Round(logical.Y) - panelRect.Top;
            _uiWindowDragW = panelRect.Width;
            _uiWindowDragH = panelRect.Height;

            try
            {
                _renderControl.Capture = true;
            }
            catch
            {
                
            }

            return true;
        }

        static bool RectContains(DrawingRectangle rect, Vector2 p)
            => p.X >= rect.Left && p.X < rect.Right && p.Y >= rect.Top && p.Y < rect.Bottom;
    }

    private void UpdateUiWindowDrag(Vector2 logical)
    {
        int w = _lastLogicalSize.Width;
        int h = _lastLogicalSize.Height;
        if (w <= 0 || h <= 0 || _uiWindowDragW <= 0 || _uiWindowDragH <= 0)
            return;

        int x = (int)MathF.Round(logical.X) - _uiWindowDragOffsetX;
        int y = (int)MathF.Round(logical.Y) - _uiWindowDragOffsetY;

        x = Math.Clamp(x, 0, Math.Max(0, w - _uiWindowDragW));
        y = Math.Clamp(y, 0, Math.Max(0, h - _uiWindowDragH));

        switch (_uiWindowDragTarget)
        {
            case UiWindowDragTarget.Bag:
                _bagWindowPosSet = true;
                _bagWindowPosX = x;
                _bagWindowPosY = y;
                break;
            case UiWindowDragTarget.HeroBag:
                _heroBagWindowPosSet = true;
                _heroBagWindowPosX = x;
                _heroBagWindowPosY = y;
                break;
            case UiWindowDragTarget.State:
                _stateWindowPosSet = true;
                _stateWindowPosX = x;
                _stateWindowPosY = y;
                break;
            case UiWindowDragTarget.Merchant:
                _merchantDialogPosSet = true;
                _merchantDialogPosX = x;
                _merchantDialogPosY = y;
                break;
            case UiWindowDragTarget.Settings:
                _settingsWindowPosSet = true;
                _settingsWindowPosX = x;
                _settingsWindowPosY = y;
                break;
            case UiWindowDragTarget.Mall:
                _mallWindowPosSet = true;
                _mallWindowPosX = x;
                _mallWindowPosY = y;
                break;
        }
    }

    private async Task HandleRenderControlMouseDownAsync(MouseEventArgs e)
    {
        bool left = e.Button == MouseButtons.Left;
        bool right = e.Button == MouseButtons.Right;

        if (!left && !right)
            return;

        if (_introSplashActive)
        {
            RequestIntroSplashSkip();
            return;
        }

        bool bagUiActive = _bagWindowVisible || _stateWindowVisible || _settingsWindowVisible;
        bool merchantUiActive = _world.MerchantDialogOpen;

        bool ctrl = (Control.ModifierKeys & Keys.Control) != 0;
        bool alt = (Control.ModifierKeys & Keys.Alt) != 0;
        bool shift = (Control.ModifierKeys & Keys.Shift) != 0;

        if (!TryGetLogicalPoint(e.Location, out Vector2 logical))
            return;

        if (LoginUiVisible)
        {
            bool consumed = await TryHandleLoginUiMouseDownAsync(logical, left, right).ConfigureAwait(true);
            if (consumed)
                return;
        }

        if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
            return;

        CancellationToken token = _loginCts?.Token ?? CancellationToken.None;

        if (left && !ctrl && !alt && _sceneManager.CurrentId == MirSceneId.Play && _mallToggleButtonRect is { } mallToggle &&
            logical.X >= mallToggle.Left && logical.X < mallToggle.Right &&
            logical.Y >= mallToggle.Top && logical.Y < mallToggle.Bottom)
        {
            _mallWindowVisible = !_mallWindowVisible;
            if (_mallWindowVisible)
            {
                _mallSelectedItemClass = byte.MaxValue;
                _mallSelectedItemIndex = -1;
                _mallPage = 0;
                AppendLog("[ui] mall opened");
                TryRequestMallItems(_mallSelectedClass, token, force: false);
            }
            else
            {
                _mallPanelRect = null;
                _mallCloseRect = null;
                AppendLog("[ui] mall closed");
            }

            return;
        }

        if (_mallWindowVisible)
        {
            if (right && !ctrl && !alt)
            {
                _mallWindowVisible = false;
                EndUiWindowDrag();
                _mallPanelRect = null;
                _mallCloseRect = null;
                AppendLog("[ui] mall closed (right click)");
                return;
            }

            if (left && !ctrl && !alt)
            {
                if (_mallCloseRect is { } close &&
                    logical.X >= close.Left && logical.X < close.Right &&
                    logical.Y >= close.Top && logical.Y < close.Bottom)
                {
                    _mallWindowVisible = false;
                    EndUiWindowDrag();
                    _mallPanelRect = null;
                    _mallCloseRect = null;
                    AppendLog("[ui] mall closed (close btn)");
                    return;
                }

                if (TryBeginUiWindowDrag(logical))
                    return;

                foreach (MallClickPoint point in _mallClickPoints)
                {
                    DrawingRectangle rect = point.Rect;
                    if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                        continue;

                    switch (point.Kind)
                    {
                        case MallClickKind.Close:
                            _mallWindowVisible = false;
                            EndUiWindowDrag();
                            _mallPanelRect = null;
                            _mallCloseRect = null;
                            AppendLog("[ui] mall closed");
                            return;
                        case MallClickKind.Category:
                            _mallSelectedClass = point.Class;
                            _mallPage = 0;
                            _mallSelectedItemClass = byte.MaxValue;
                            _mallSelectedItemIndex = -1;
                            TryRequestMallItems(_mallSelectedClass, token, force: true);
                            return;
                        case MallClickKind.PrevPage:
                            if (_mallPage > 0)
                                _mallPage--;
                            return;
                        case MallClickKind.NextPage:
                        {
                            int max = GetMallMaxPage(_mallSelectedClass);
                            if (_mallPage + 1 < max)
                                _mallPage++;
                            return;
                        }
                        case MallClickKind.Item:
                            _mallSelectedItemClass = point.Class;
                            _mallSelectedItemIndex = point.Index;
                            return;
                        case MallClickKind.Buy:
                        {
                            if (!TryGetSelectedMallItem(out ShopItem item))
                                return;

                            string name = item.ItemNameString.Trim();
                            if (string.IsNullOrWhiteSpace(name))
                                return;

                            if (MessageBox.Show(this, $"Buy '{name}'?", "Mall", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                                return;

                            _ = _session.SendClientStringAsync(Grobal2.CM_BUYSHOPITEM, 0, 0, 0, 0, name, token);
                            AppendLog($"[mall] CM_BUYSHOPITEM '{name}'");
                            return;
                        }
                        case MallClickKind.Gift:
                        {
                            if (!TryGetSelectedMallItem(out ShopItem item))
                                return;

                            string name = item.ItemNameString.Trim();
                            if (string.IsNullOrWhiteSpace(name))
                                return;

                            (UiTextPromptResult result, string value) = await PromptTextAsync(
                                title: "Mall Gift",
                                prompt: $"Gift '{name}' to (player name)",
                                buttons: UiTextPromptButtons.OkCancel,
                                maxGbkBytes: Grobal2.ActorNameLen).ConfigureAwait(true);

                            if (result != UiTextPromptResult.Ok)
                                return;

                            string who = value.Trim();
                            if (string.IsNullOrWhiteSpace(who))
                                return;

                            int recog = _world.MyselfRecogIdSet ? _world.MyselfRecogId : 0;
                            _ = _session.SendClientStringAsync(Grobal2.CM_SHOPPRESEND, recog, 0, 0, 0, $"{who}/{name}", token);
                            AppendLog($"[mall] CM_SHOPPRESEND '{who}' '{name}'");
                            return;
                        }
                    }
                }

                return;
            }
        }

        if (_settingsWindowVisible && !alt)
        {
            if (right && !ctrl)
            {
                _settingsWindowVisible = false;
                EndUiWindowDrag();
                AppendLog("[ui] settings closed (right click)");
                return;
            }

            if (left && !ctrl)
            {
                if (_settingsCloseRect is { } close &&
                    logical.X >= close.Left && logical.X < close.Right &&
                    logical.Y >= close.Top && logical.Y < close.Bottom)
                {
                    _settingsWindowVisible = false;
                    EndUiWindowDrag();
                    AppendLog("[ui] settings closed (close btn)");
                    return;
                }

                if (TryBeginUiWindowDrag(logical))
                    return;

                foreach (SettingsClickPoint point in _settingsClickPoints)
                {
                    DrawingRectangle rect = point.Rect;
                    if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                        continue;

                    switch (point.Kind)
                    {
                        case SettingsClickKind.ShowActorName:
                            _showActorNames = !_showActorNames;
                            TrySaveClientSetForCurrentCharacter();
                            AppendLog(_showActorNames ? "[cfg] ShowActorName on" : "[cfg] ShowActorName off");
                            return;
                        case SettingsClickKind.DuraWarning:
                            _duraWarning = !_duraWarning;
                            TrySaveClientSetForCurrentCharacter();
                            AppendLog(_duraWarning ? "[cfg] DuraWarning on" : "[cfg] DuraWarning off");
                            return;
                        case SettingsClickKind.AutoAttack:
                            _autoAttack = !_autoAttack;
                            TrySaveClientSetForCurrentCharacter();
                            AppendLog(_autoAttack ? "[cfg] AutoAttack on" : "[cfg] AutoAttack off");
                            return;
                        case SettingsClickKind.ShowDropItems:
                            _showDropItems = !_showDropItems;
                            TrySaveClientSetForCurrentCharacter();
                            AppendLog(_showDropItems ? "[cfg] ShowDropItems on" : "[cfg] ShowDropItems off");
                            return;
                        case SettingsClickKind.HideDeathBody:
                            _hideDeathBody = !_hideDeathBody;
                            TrySaveClientSetForCurrentCharacter();
                            AppendLog(_hideDeathBody ? "[cfg] HideDeathBody on" : "[cfg] HideDeathBody off");
                            return;
                    }
                }

                
                return;
            }

            return;
        }

        if (left && !ctrl && !alt)
        {
            if (_stateCloseRect is { } stateClose &&
                logical.X >= stateClose.Left && logical.X < stateClose.Right &&
                logical.Y >= stateClose.Top && logical.Y < stateClose.Bottom)
            {
                _stateWindowVisible = false;
                _stateMagicKeyDialogOpen = false;
                AppendLog("[ui] state closed (close btn)");
                return;
            }

            if (_stateMagicKeyDialogOpen)
            {
                if (_stateMagicKeyCloseRect is { } close &&
                    logical.X >= close.Left && logical.X < close.Right &&
                    logical.Y >= close.Top && logical.Y < close.Bottom)
                {
                    _stateMagicKeyDialogOpen = false;
                    return;
                }

                if (_stateMagicKeyPanelRect is { } panel &&
                    (logical.X < panel.Left || logical.X >= panel.Right || logical.Y < panel.Top || logical.Y >= panel.Bottom))
                {
                    _stateMagicKeyDialogOpen = false;
                    return;
                }

                foreach (StateMagicKeyClickPoint point in _stateMagicKeyClickPoints)
                {
                    DrawingRectangle rect = point.Rect;
                    if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                        continue;

                    await TrySetStateMagicKeyAsync(_stateMagicKeyDialogHero, _stateMagicKeyDialogMagicId, point.Key, token);
                    _stateMagicKeyDialogOpen = false;
                    return;
                }
            }

            if (_stateWindowVisible && _stateWindowPage == 3)
            {
                IReadOnlyList<ClientMagic> magics = _heroBagView ? _world.HeroMagics : _world.MyMagics;
                const int pageSize = 6;
                int maxPage = magics.Count > 0 ? (magics.Count + (pageSize - 1)) / pageSize - 1 : 0;
                maxPage = Math.Max(0, maxPage);

                if (_stateMagicPageUpRect is { } prev &&
                    logical.X >= prev.Left && logical.X < prev.Right &&
                    logical.Y >= prev.Top && logical.Y < prev.Bottom)
                {
                    _stateMagicPage = Math.Clamp(_stateMagicPage - 1, 0, maxPage);
                    return;
                }

                if (_stateMagicPageDownRect is { } next &&
                    logical.X >= next.Left && logical.X < next.Right &&
                    logical.Y >= next.Top && logical.Y < next.Bottom)
                {
                    _stateMagicPage = Math.Clamp(_stateMagicPage + 1, 0, maxPage);
                    return;
                }

                foreach (StateMagicClickPoint point in _stateMagicClickPoints)
                {
                    DrawingRectangle rect = point.Rect;
                    if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                        continue;

                    _stateMagicKeyDialogOpen = true;
                    _stateMagicKeyDialogHero = point.Hero;
                    _stateMagicKeyDialogMagicId = point.MagicId;
                    return;
                }
            }

            if (_heroBagView && _heroBagCloseRect is { } heroBagClose &&
                logical.X >= heroBagClose.Left && logical.X < heroBagClose.Right &&
                logical.Y >= heroBagClose.Top && logical.Y < heroBagClose.Bottom)
            {
                _bagWindowVisible = false;
                _heroBagView = false;
                ClearItemDrag();
                _treasureDialogSystem.Close(logUi: false);
                _stallSystem.CloseWindow(logUi: false);
                _userStallSystem.Close(logUi: false);
                AppendLog("[ui] hero bag closed (close btn)");
                return;
            }

            if (!_heroBagView && _bagCloseRect is { } bagClose &&
                logical.X >= bagClose.Left && logical.X < bagClose.Right &&
                logical.Y >= bagClose.Top && logical.Y < bagClose.Bottom)
            {
                _bagWindowVisible = false;
                ClearItemDrag();
                _treasureDialogSystem.Close(logUi: false);
                _stallSystem.CloseWindow(logUi: false);
                _userStallSystem.Close(logUi: false);

                if (_marketSystem.Visible)
                {
                    _ = _maketSystem.TrySendMarketCloseAsync(token);
                    _marketSystem.Reset(clearWorld: true);
                }

                AppendLog("[ui] bag closed (close btn)");
                return;
            }

            if (TryBeginUiWindowDrag(logical))
                return;
        }

        if (_ybDealSystem.Visible)
        {
            if (right && !ctrl && !alt)
            {
                _ybDealSystem.Close(logUi: true);
                return;
            }

            if (left && !ctrl && !alt)
            {
                if (_ybDealPanelRect is { } panel &&
                    (logical.X < panel.Left || logical.X >= panel.Right || logical.Y < panel.Top || logical.Y >= panel.Bottom))
                {
                    _ybDealSystem.Close(logUi: true);
                    return;
                }

                foreach (YbDealClickPoint point in _ybDealClickPoints)
                {
                    DrawingRectangle rect = point.Rect;
                    if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                        continue;

                    switch (point.Kind)
                    {
                        case YbDealClickKind.Close:
                            _ybDealSystem.Close(logUi: true);
                            return;
                        case YbDealClickKind.Item:
                            _ybDealSystem.SetSelectedIndex(point.Index);
                            return;
                        case YbDealClickKind.Buy:
                            _ybDealSystem.TryBuy(token);
                            return;
                        case YbDealClickKind.Cancel:
                            _ybDealSystem.TryCancelDeal(token);
                            return;
                        case YbDealClickKind.CancelSell:
                            _ybDealSystem.TryCancelSell(token);
                            return;
                    }
                }

                return;
            }
        }

        if (_world.BoxOpen && left)
        {
            foreach (BoxClickPoint point in _boxClickPoints)
            {
                DrawingRectangle rect = point.Rect;
                if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                    continue;

                long nowMs = Environment.TickCount64;

                switch (point.Kind)
                {
                    case BoxClickKind.Close:
                        _boxSystem.Close(logUi: true);
                        return;
                    case BoxClickKind.Slot:
                    {
                        await _boxSystem.HandleSlotClickAsync(point.Index, nowMs, token);

                        return;
                    }
                    case BoxClickKind.Flash:
                    {
                        await _boxSystem.TryFlashAsync(nowMs, token);

                        return;
                    }
                    case BoxClickKind.Get:
                    {
                        await _boxSystem.TryGetAsync(nowMs, token);

                        return;
                    }
                }
            }
        }

        if (_world.BookOpen && left)
        {
            foreach (BookClickPoint point in _bookClickPoints)
            {
                DrawingRectangle rect = point.Rect;
                if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                    continue;

                switch (point.Kind)
                {
                    case BookClickKind.Close:
                        _bookSystem.Close(logUi: true);
                        return;
                    case BookClickKind.Prev:
                        _bookSystem.TryPrevPage(logUi: true);
                        return;
                    case BookClickKind.Next:
                        _bookSystem.TryNextPage(logUi: true);
                        return;
                    case BookClickKind.Confirm:
                    {
                        await _bookSystem.ConfirmAsync(token);

                        return;
                    }
                }
            }
        }

        if (_world.RefineOpen && left)
        {
            foreach (RefineClickPoint point in _refineClickPoints)
            {
                DrawingRectangle rect = point.Rect;
                if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                    continue;

                switch (point.Kind)
                {
                    case RefineClickKind.Close:
                        CloseRefineUi(logUi: true);
                        return;
                    case RefineClickKind.Ok:
                        await TrySendRefineOkAsync(token);
                        return;
                    case RefineClickKind.Slot:
                        await HandleRefineSlotClickAsync(point.Index, token);
                        return;
                }
            }
        }

        if (merchantUiActive && right && !ctrl && !alt)
        {
            _world.CloseMerchantDialog();
            _merchantDialogTopLine = 0;
            AppendLog("[ui] merchant dialog closed");
            return;
        }

        if (merchantUiActive && left)
        {
            if (!ctrl && !alt && _merchantDialogCloseRect is { } closeRect &&
                logical.X >= closeRect.Left && logical.X < closeRect.Right &&
                logical.Y >= closeRect.Top && logical.Y < closeRect.Bottom)
            {
                _world.CloseMerchantDialog();
                _merchantDialogTopLine = 0;
                AppendLog("[ui] merchant dialog closed (close btn)");
                return;
            }

            if (_world.MerchantMode is MirMerchantMode.Sell or MirMerchantMode.Repair)
            {
                if (!ctrl && !alt && _merchantSellCloseRect is { } sellCloseRect &&
                    logical.X >= sellCloseRect.Left && logical.X < sellCloseRect.Right &&
                    logical.Y >= sellCloseRect.Top && logical.Y < sellCloseRect.Bottom)
                {
                    ClearMerchantSellSelection(clearQuotes: true);
                    _world.ApplyMerchantMode(_world.CurrentMerchantId, MirMerchantMode.None);
                    AppendLog("[ui] merchant sell dialog closed");
                    return;
                }

                if (!ctrl && !alt && _merchantSellOkRect is { } okRect &&
                    logical.X >= okRect.Left && logical.X < okRect.Right &&
                    logical.Y >= okRect.Top && logical.Y < okRect.Bottom)
                {
                    await TryMerchantSellDialogOkAsync(token);
                    return;
                }

                if (!ctrl && !alt && _merchantSellSpotRect is { } spotRect &&
                    logical.X >= spotRect.Left && logical.X < spotRect.Right &&
                    logical.Y >= spotRect.Top && logical.Y < spotRect.Bottom)
                {
                    ClearMerchantSellSelection(clearQuotes: true);
                    AppendLog("[ui] merchant sell dialog cleared");
                    return;
                }
            }

            foreach (MerchantMenuClickPoint point in _merchantMenuClickPoints)
            {
                DrawingRectangle rect = point.Rect;
                if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                    continue;

                if (point.Kind == MerchantMenuClickKind.Close)
                {
                    _world.ApplyMerchantMode(_world.CurrentMerchantId, MirMerchantMode.None);
                    AppendLog("[ui] merchant menu closed");
                    return;
                }

                if (point.Kind == MerchantMenuClickKind.Item)
                {
                    _merchantMenuSystem.SelectItem(point.Index, _world.MerchantGoods, logUi: true);

                    return;
                }

                MirMerchantMode mode = _world.MerchantMode;
                int merchantId = _world.CurrentMerchantId;

                const int maxMenuLines = 10;

                if (point.Kind is MerchantMenuClickKind.Prev or MerchantMenuClickKind.Next)
                {
                    if (mode == MirMerchantMode.DetailMenu)
                    {
                        if (point.Kind == MerchantMenuClickKind.Prev)
                            await _merchantMenuSystem.TryDetailPrevAsync(merchantId, _world.MerchantMenuTopLine, token);
                        else
                            await _merchantMenuSystem.TryDetailNextAsync(merchantId, _world.MerchantMenuTopLine, token);

                        return;
                    }

                    if (point.Kind == MerchantMenuClickKind.Prev)
                        _merchantMenuSystem.TryScrollPrev(_world.MerchantGoods.Count, maxMenuLines);
                    else
                        _merchantMenuSystem.TryScrollNext(_world.MerchantGoods.Count, maxMenuLines);

                    return;
                }

                if (point.Kind == MerchantMenuClickKind.Action)
                {
                    int selectedIndex = _merchantMenuSystem.SelectedIndex;
                    if ((uint)selectedIndex >= (uint)_world.MerchantGoods.Count)
                    {
                        AppendLog("[shop] action ignored: no selection");
                        return;
                    }

                    MirMerchantGoods selected = _world.MerchantGoods[selectedIndex];

                    ushort count = 0;
                    const int maxOverlapItem = 9999;
                    if (selected.SubMenu == 2)
                    {
                        string prompt = $"Buy count (1-{maxOverlapItem})";
                        (UiTextPromptResult result, string value) = await PromptTextAsync(
                            title: "Buy",
                            prompt: prompt,
                            buttons: UiTextPromptButtons.OkCancel).ConfigureAwait(true);

                        if (result != UiTextPromptResult.Ok)
                        {
                            AppendLog($"[shop] buy count dismissed ({result})");
                            return;
                        }

                        if (!int.TryParse(value.Trim(), out int parsed))
                        {
                            AppendLog($"[shop] buy count invalid: '{value}'");
                            return;
                        }

                        parsed = Math.Clamp(parsed, 1, maxOverlapItem);
                        count = unchecked((ushort)parsed);
                    }

                    await _merchantMenuSystem.TryActionAsync(merchantId, mode, selected, count, token);

                    return;
                }
            }

            foreach (MerchantClickPoint point in _merchantClickPoints)
            {
                DrawingRectangle rect = point.Rect;
                if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                    continue;

                await _merchantDialogSystem.TrySelectAsync(_world.CurrentMerchantId, point.Command, token);
                return;
            }
        }

        if (left && _sceneManager.CurrentId == MirSceneId.Play && !alt)
        {
            if (TryHandleInGameChatMouseDown(logical, ctrl))
                return;

            if (TryHandleInGameBottomUiMouseDown(logical, bagUiActive, token))
                return;
        }

        if (left && alt && !ctrl && !bagUiActive && !merchantUiActive && _sceneManager.CurrentId == MirSceneId.Play)
        {
            int butchMapX = 0;
            int butchMapY = 0;
            int butchMyX = 0;
            int butchMyY = 0;
            int butchTargetRecogId = 0;
            byte butchDir = 0;
            bool doButch = false;

            lock (_logicSync)
            {
                if (_world.MapCenterSet && TryResolveMapCell(logical, out butchMapX, out butchMapY))
                {
                    butchMyX = _world.MapCenterX;
                    butchMyY = _world.MapCenterY;

                    if (butchMapX != butchMyX || butchMapY != butchMyY)
                    {
                        butchDir = MirDirection.GetFlyDirection(butchMyX, butchMyY, butchMapX, butchMapY);
                    }
                    else if (_world.TryGetMyself(out ActorMarker myself))
                    {
                        butchDir = (byte)(myself.Dir & 7);
                    }
                    else
                    {
                        butchDir = 0;
                    }

                    int bestRecogId = 0;
                    int bestDist = int.MaxValue;
                    foreach ((int recogId, ActorMarker actor) in _world.Actors)
                    {
                        if (recogId == 0 || actor.IsMyself)
                            continue;

                        if (actor.Action is not (Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH or Grobal2.SM_SKELETON))
                            continue;

                        int dx = actor.X - butchMapX;
                        int dy = actor.Y - butchMapY;
                        if (dx is < -1 or > 1 || dy is < -1 or > 1)
                            continue;

                        int dist = Math.Abs(dx) + Math.Abs(dy);
                        if (dist >= bestDist)
                            continue;

                        bestDist = dist;
                        bestRecogId = recogId;
                        if (bestDist <= 0)
                            break;
                    }

                    butchTargetRecogId = bestRecogId != 0 ? bestRecogId : _world.DetectItemMineId;
                    doButch = butchTargetRecogId != 0;
                }
            }

            if (doButch && _commandThrottle.TryCombatSend())
            {
                _autoMoveSystem.Cancel();
                _ = _session.SendClientMessageAsync(
                    Grobal2.CM_BUTCH,
                    recog: butchTargetRecogId,
                    param: unchecked((ushort)butchMapX),
                    tag: unchecked((ushort)butchMapY),
                    series: butchDir,
                    token);
                AppendLog($"[act] CM_BUTCH recog={butchTargetRecogId} x={butchMapX} y={butchMapY} dir={butchDir}");

                _ = _actSendSystem.SendAsync(Grobal2.CM_SITDOWN, butchMyX, butchMyY, butchDir, token);
            }

            return;
        }

        if (left && !ctrl && !alt && !merchantUiActive)
        {
            bool clickInsideBagUi = bagUiActive &&
                                    (TryHitTestBagPanel(logical) ||
                                     TryHitTestUsePanel(logical) ||
                                     TryHitTestBagSlot(logical, out _, out _) ||
                                     TryHitTestUseItemSlot(logical, out _, out _) ||
                                     TryHitTestStorageSlot(logical, out _, out _) ||
                                     TryHitTestDealMySlot(logical, out _, out _) ||
                                     TryHitTestDealButton(logical, out _) ||
                                     TryHitTestStallPanel(logical) ||
                                     TryHitTestStallButton(logical, out _) ||
                                     TryHitTestStallSlot(logical, out _, out _) ||
                                     TryHitTestUserStallPanel(logical) ||
                                     TryHitTestUserStallButton(logical, out _) ||
                                     TryHitTestUserStallSlot(logical, out _, out _) ||
                                     TryHitTestMarketPanel(logical) ||
                                     TryHitTestMarketButton(logical, out _));

            if (!clickInsideBagUi)
            {
                int targetId = 0;
                ActorMarker target = default;
                bool ok = false;

                lock (_logicSync)
                {
                    if (_map != null &&
                        _world.MapCenterSet &&
                        TryResolveMapCell(logical, out int mapX, out int mapY) &&
                        _merchantDialogSystem.TryPickMerchantNpcAtCell(mapX, mapY, out targetId, out target))
                    {
                        ok = true;
                    }
                }

                if (ok && await _merchantDialogSystem.TryClickNpcAsync(targetId, target.X, target.Y, token))
                    return;
            }
        }

        if (left && !bagUiActive && !merchantUiActive && !ctrl && !alt)
        {
            int mouseMapX = 0;
            int mouseMapY = 0;
            bool hasMapCell = false;
            bool selectedActor = false;
            TargetingSystem.TargetSelectResult selected = default;
            bool stallOpen = false;
            bool doPickup = false;
            int pickupMapX = 0;
            int pickupMapY = 0;

            long nowMs = Environment.TickCount64;

            lock (_logicSync)
            {
                if (TryResolveMapCell(logical, out mouseMapX, out mouseMapY))
                {
                    hasMapCell = true;
                    selected = _targetingSystem.TrySelectAt(_world, mouseMapX, mouseMapY, nowMs);
                    if (selected.Handled)
                    {
                        selectedActor = true;
                        _autoMoveSystem.Cancel();
                        _autoHitTargetRecogId = 0;

                        string name = string.IsNullOrWhiteSpace(selected.Actor.UserName) ? "(no name)" : selected.Actor.UserName;
                        AppendLog($"[target] selected recog={selected.SelectedRecogId} x={selected.Actor.X} y={selected.Actor.Y} name='{name}' feature={selected.Actor.Feature}");

                        stallOpen = _world.Stalls.TryGetValue(selected.SelectedRecogId, out StallActorMarker stallMarker) && stallMarker.Open;

                        if (stallOpen && _session.IsConnected)
                        {
                            _ = _session.SendClientMessageAsync(Grobal2.CM_CLICKNPC, selected.SelectedRecogId, 0, 0, 0, token);
                            AppendLog($"[stall] CM_CLICKNPC recog={selected.SelectedRecogId}");
                        }
                        else
                        {
                            int race = FeatureCodec.Race(selected.Actor.Feature);
                            bool isMerchant = race == Grobal2.RCC_MERCHANT;
                            bool isHero = _world.HeroActorIdSet && selected.SelectedRecogId == _world.HeroActorId;
                            bool isDead = selected.Actor.Action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH or Grobal2.SM_SKELETON;

                            bool canAttack = !isMerchant &&
                                             !selected.Actor.IsMyself &&
                                             !isHero &&
                                             !isDead &&
                                             (race != 0 || shift || _autoAttack);

                            if (canAttack)
                            {
                                _autoHitTargetRecogId = selected.SelectedRecogId;
                                _autoChaseNextStartMs = nowMs;

                                int myX = _world.MapCenterX;
                                int myY = _world.MapCenterY;
                                int dx = selected.Actor.X - myX;
                                int dy = selected.Actor.Y - myY;

                                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1)
                                {
                                    byte dir;
                                    if (dx != 0 || dy != 0)
                                    {
                                        dir = MirDirection.GetFlyDirection(myX, myY, selected.Actor.X, selected.Actor.Y);
                                    }
                                    else if (_world.TryGetMyself(out ActorMarker myself))
                                    {
                                        dir = (byte)(myself.Dir & 7);
                                    }
                                    else
                                    {
                                        dir = 0;
                                    }

                                    _basicHitSystem.TrySend(myX, myY, dir, selected.SelectedRecogId, token);
                                }
                                else if (_map is { } map)
                                {
                                    _autoMoveStartSystem.TryStartAutoMove(
                                        stage: _session.Stage,
                                        mapLoaded: true,
                                        world: _world,
                                        requestedX: selected.Actor.X,
                                        requestedY: selected.Actor.Y,
                                        mapWidth: map.Width,
                                        mapHeight: map.Height,
                                        isWalkable: _isCurrentMapWalkable,
                                        wantsRun: false,
                                        nowMs: nowMs,
                                        token: token);
                                }
                            }
                            else if (isDead && !isMerchant && !selected.Actor.IsMyself && !isHero && race != 0)
                            {
                                doPickup = true;
                                pickupMapX = selected.Actor.X;
                                pickupMapY = selected.Actor.Y;
                            }
                        }
                    }
                }
            }

            if (selectedActor)
            {
                if (doPickup)
                    _ = _pickupSystem.TryPickupAsync(pickupMapX, pickupMapY, token);
                return;
            }

            bool mineRequested = false;
            int mineMyX = 0;
            int mineMyY = 0;
            byte mineDir = 0;
            lock (_logicSync)
            {
                if (hasMapCell &&
                    _world.MapCenterSet &&
                    _world.UseItems.TryGetValue(Grobal2.U_WEAPON, out ClientItem weapon) &&
                    weapon.MakeIndex != 0 &&
                    weapon.S.Shape == 19)
                {
                    mineMyX = _world.MapCenterX;
                    mineMyY = _world.MapCenterY;

                    if (mouseMapX != mineMyX || mouseMapY != mineMyY)
                    {
                        mineDir = MirDirection.GetFlyDirection(mineMyX, mineMyY, mouseMapX, mouseMapY);
                    }
                    else if (_world.TryGetMyself(out ActorMarker myself))
                    {
                        mineDir = (byte)(myself.Dir & 7);
                    }
                    else
                    {
                        mineDir = 0;
                    }

                    (int frontX, int frontY) = MirDirection.StepByDir(mineMyX, mineMyY, mineDir, steps: 1);
                    bool frontWalkable = IsCurrentMapWalkable(frontX, frontY);
                    mineRequested = !frontWalkable || shift;
                }
            }

            if (mineRequested && _commandThrottle.TryCombatSend())
            {
                _autoMoveSystem.Cancel();
                _ = _actSendSystem.SendAsync(Grobal2.CM_HEAVYHIT, mineMyX, mineMyY, mineDir, token);
                AppendLog($"[hit] CM_HEAVYHIT x={mineMyX} y={mineMyY} dir={mineDir}");
                return;
            }

            if (shift && hasMapCell)
            {
                bool swung = false;

                lock (_logicSync)
                {
                    if (_world.MapCenterSet)
                    {
                        int myX = _world.MapCenterX;
                        int myY = _world.MapCenterY;

                        byte dir;
                        if (mouseMapX != myX || mouseMapY != myY)
                        {
                            dir = MirDirection.GetFlyDirection(myX, myY, mouseMapX, mouseMapY);
                        }
                        else if (_world.TryGetMyself(out ActorMarker myself))
                        {
                            dir = (byte)(myself.Dir & 7);
                        }
                        else
                        {
                            dir = 0;
                        }

                        _basicHitSystem.TrySend(myX, myY, dir, targetId: 0, token);
                        swung = true;
                    }
                }

                if (swung)
                    return;
            }
        }

        if (!bagUiActive && !merchantUiActive && !ctrl && !alt && (left || right))
        {
            int mapX = 0;
            int mapY = 0;
            bool hasMapCell;
            lock (_logicSync)
                hasMapCell = TryResolveMapCell(logical, out mapX, out mapY);

            if (hasMapCell)
            {
                bool wantsRun = right;
                long nowMs = Environment.TickCount64;
                _holdMoveActive = true;
                _holdMoveWantsRun = wantsRun;
                _holdMoveStartMs = nowMs;
                _holdMoveLastUpdateMs = nowMs;
                _holdMoveLastMapX = mapX;
                _holdMoveLastMapY = mapY;
                _lastMouseClientX = e.Location.X;
                _lastMouseClientY = e.Location.Y;

                lock (_logicSync)
                {
                    MirMapFile? map = _map;
                    if (map != null &&
                        _autoMoveStartSystem.TryStartAutoMove(
                            stage: _session.Stage,
                            mapLoaded: true,
                            world: _world,
                            requestedX: mapX,
                            requestedY: mapY,
                            mapWidth: map.Width,
                            mapHeight: map.Height,
                            isWalkable: _isCurrentMapWalkable,
                            wantsRun: wantsRun,
                            nowMs: nowMs,
                            token: token))
                    {
                        _autoHitTargetRecogId = 0;
                        _targetingSystem.ClearSelected();
                        return;
                    }
                }
            }
        }

        if (bagUiActive && left && !ctrl && !alt && !_heroBagView && _world.DealOpen)
        {
            if (TryHitTestDealButton(logical, out DealButton button))
            {
                long nowMs = Environment.TickCount64;

                switch (button)
                {
                    case DealButton.Cancel:
                        await _dealSystem.TrySendCancelAsync(token);

                        return;
                    case DealButton.End:
                        await _dealSystem.TrySendEndAsync(token);

                        return;
                    case DealButton.Gold:
                    {
                        int maxOffer = Math.Max(0, _world.MyGold + _world.DealMyGold);
                        string prompt = $"交易金币 (0-{maxOffer})  当前:{_world.DealMyGold}  身上:{_world.MyGold}";

                        (UiTextPromptResult result, string value) = await PromptTextAsync(
                            title: "Deal",
                            prompt: prompt,
                            buttons: UiTextPromptButtons.OkCancel).ConfigureAwait(true);

                        if (result != UiTextPromptResult.Ok)
                        {
                            AppendLog($"[deal] gold dismissed ({result})");
                            return;
                        }

                        if (!int.TryParse(value.Trim(), out int gold))
                        {
                            AppendLog($"[deal] gold invalid: '{value}'");
                            return;
                        }

                        gold = Math.Clamp(gold, 0, maxOffer);

                        await _dealSystem.TrySendChangeGoldAsync(gold, token);

                        return;
                    }
                    default:
                        return;
                }
            }
        }

        if (bagUiActive && left && !ctrl && !alt && !_heroBagView && _marketSystem.Visible)
        {
            if (TryHitTestMarketButton(logical, out MarketButton button))
            {
                int merchantId = _world.CurrentMerchantId;

                switch (button)
                {
                    case MarketButton.Close:
                        await _marketSystem.TryCloseAsync(token);

                        return;
                    case MarketButton.Refresh:
                        await _marketSystem.TryRefreshAsync(merchantId, token);

                        return;
                    case MarketButton.Find:
                        if (_world.MarketUserMode != 1)
                            return;

                        if (merchantId <= 0)
                        {
                            AppendLog("[market] find ignored: merchant not set");
                            return;
                        }

                        (UiTextPromptResult result, string value) = await PromptTextAsync(
                            title: "Market",
                            prompt: "Search item name (max 14)",
                            buttons: UiTextPromptButtons.OkCancel,
                            maxGbkBytes: 14).ConfigureAwait(true);

                        if (result != UiTextPromptResult.Ok)
                        {
                            AppendLog($"[market] find dismissed ({result})");
                            return;
                        }

                        await _marketSystem.TryFindAsync(merchantId, value, token);

                        return;
                    case MarketButton.Prev:
                        _marketSystem.TryPrev();
                        return;
                    case MarketButton.Next:
                        await _marketSystem.TryNextAsync(merchantId, token);

                        return;
                    case MarketButton.Action:
                        await _marketSystem.TryActionAsync(merchantId, token);

                        return;
                    default:
                        return;
                }
            }

            if (TryHitTestMarketRow(logical, out int index, out MarketItem item))
            {
                _marketSystem.SelectRow(index, item, logUi: true);
                return;
            }

            if (TryHitTestMarketPanel(logical))
                return;
        }

        if (bagUiActive && !_heroBagView && _userStallSystem.Visible && _world.UserStallOpen)
        {
            if (left && TryHitTestUserStallButton(logical, out UserStallButton button))
            {
                switch (button)
                {
                    case UserStallButton.Close:
                        _userStallSystem.TryCloseWithThrottle(logUi: true);
                        return;
                    case UserStallButton.Buy:
                    {
                        await _userStallSystem.TryBuyAsync(token);

                        return;
                    }
                    default:
                        return;
                }
            }

            if (left && TryHitTestUserStallSlot(logical, out int index, out ClientItem item))
            {
                _userStallSystem.SelectSlot(index, item, logUi: true);
                return;
            }

            if (TryHitTestUserStallPanel(logical))
                return;
        }

        if (bagUiActive && !_heroBagView && IsStallSetupUiActive())
        {
            if (left && TryHitTestStallButton(logical, out StallButton button))
            {
                switch (button)
                {
                    case StallButton.Name:
                    {
                        string? name = await PromptStallNameAsync().ConfigureAwait(true);
                        if (!string.IsNullOrWhiteSpace(name))
                            _stallSystem.SetNameFromUi(name, logUi: true);
                        return;
                    }
                    case StallButton.Open:
                    {
                        if (string.IsNullOrWhiteSpace(_stallSystem.Name))
                        {
                            string? prompted = await PromptStallNameAsync().ConfigureAwait(true);
                            if (string.IsNullOrWhiteSpace(prompted))
                                return;
                            _stallSystem.SetNameFromUi(prompted, logUi: false);
                        }

                        if (await _stallSystem.TrySendOpenAsync(token))
                            _bagWindowVisible = false;

                        return;
                    }
                    case StallButton.Cancel:
                    {
                        await _stallSystem.TrySendCloseAsync(token);

                        return;
                    }
                    case StallButton.Remove:
                    {
                        await _stallSystem.TryRemoveSelectedAsync(token);
                        return;
                    }
                    default:
                        return;
                }
            }

            if (TryHitTestStallSlot(logical, out int index, out StallSlot slot))
            {
                _stallSystem.SelectSlot(index, slot, logUi: true);
                return;
            }

            if (TryHitTestStallPanel(logical))
                return;
        }

        if (bagUiActive && left && !ctrl && !alt)
        {
            if (_treasureDialogSystem.Visible)
            {
                if (_heroBagView)
                    return;

                if (TryHitTestBagSlot(logical, out int slotIndex, out ClientItem clicked) && clicked.MakeIndex != 0)
                {
                    ClearItemDrag();
                    if (_treasureDialogSystem.HandleBagClick(slotIndex, clicked, logUi: true))
                        return;
                }
            }

            if (_bindDialogSystem.Visible)
            {
                if (_bindDialogSystem.Waiting)
                    return;

                if (!_heroBagView &&
                    TryHitTestBagSlot(logical, out int slotIndex, out ClientItem clicked) &&
                    clicked.MakeIndex != 0)
                {
                    if (_bindDialogSystem.SelectedMakeIndex != 0 && _bindDialogSystem.SelectedMakeIndex == clicked.MakeIndex)
                        return;

                    ClearItemDrag();
                    if (_bindDialogSystem.HandleBagClick(slotIndex, clicked, logUi: true))
                        return;
                }
            }

            if (_itemDialogSystem.Visible)
            {
                if (!_heroBagView &&
                    TryHitTestBagSlot(logical, out int slotIndex, out ClientItem clicked) &&
                    clicked.MakeIndex != 0)
                {
                    if (_itemDialogSystem.SelectedMakeIndex != 0 && _itemDialogSystem.SelectedMakeIndex == clicked.MakeIndex)
                        return;

                    ClearItemDrag();
                    if (_itemDialogSystem.HandleBagClick(slotIndex, clicked, logUi: true))
                        return;
                }
            }

            if (await TryHandleItemDragClickAsync(logical, token))
                return;
        }

        if (!bagUiActive || (!ctrl && !alt))
            return;

        if (right)
        {
            if (!alt)
                return;

            if (!TryHitTestBagSlot(logical, out int slotIndex, out ClientItem item))
                return;

            if (item.MakeIndex == 0)
                return;

            if (!_heroBagView && item.S.NeedIdentify >= 4)
            {
                AppendLog($"[bag] drop ignored (stall makeIndex={item.MakeIndex})");
                return;
            }

            string logPrefix = _heroBagView ? "[hero-bag]" : "[bag]";
            await _dropItemSystem.TryDropAsync(
                item,
                heroBag: _heroBagView,
                logPrefix,
                slotIndex,
                actionLabel: "drop",
                successSuffix: string.Empty,
                token);

            return;
        }

        if (ctrl)
        {
            if (!TryHitTestBagSlot(logical, out int slotIndex, out ClientItem item))
                return;

            if (item.MakeIndex == 0)
                return;

            bool hero = _heroBagView;
            string prefix = hero ? "[hero-bag]" : "[bag]";

            if (!hero && item.S.NeedIdentify >= 4)
            {
                AppendLog($"[bag] use ignored (stall makeIndex={item.MakeIndex} name='{item.NameString}')");
                return;
            }

            bool eatable = item.S.StdMode <= 3 || item.S.StdMode == 31;

            if (eatable)
            {
                await _bagUseSystem.TryEatAsync(item, hero, prefix, slotIndex, token);

                return;
            }

            if (item.S.Overlap > 0 && item.Dura > 1)
            {
                await _bagUseSystem.TryDismantleAsync(item, hero, prefix, slotIndex, token);

                return;
            }

            await _equipSystem.TryTakeOnAsync(
                item,
                hero,
                whereHint: -1,
                bagSlotIndex: slotIndex,
                logPrefix: prefix,
                actionLabel: "Ctrl+Click",
                successSuffix: " (Ctrl+Click)",
                token);

            return;
        }

        if (alt)
        {
            if (shift)
            {
                long exchangeNowMs = Environment.TickCount64;
                if (!_heroBagExchangeSystem.TryBeginExchange(exchangeNowMs))
                    return;

                if (!TryHitTestBagSlot(logical, out int slotIndex, out ClientItem clicked) || clicked.MakeIndex == 0)
                    return;

                if (!_heroBagView && clicked.S.NeedIdentify >= 4)
                {
                    AppendLog($"[bag] exchange ignored (stall makeIndex={clicked.MakeIndex} name='{clicked.NameString}')");
                    return;
                }

                await _heroBagExchangeSystem.TrySendExchangeAsync(
                    heroToPlayer: _heroBagView,
                    makeIndex: clicked.MakeIndex,
                    slotIndex,
                    exchangeNowMs,
                    token);

                return;
            }

            if (!_heroBagView && IsStallSetupUiActive())
            {
                if (!TryHitTestBagSlot(logical, out int stallBagSlotIndex, out ClientItem stallBagItem) || stallBagItem.MakeIndex == 0)
                    return;

                int price = 0;
                byte goldType = 4;

                const string prompt = "Stall price: '1000' (gold) / 'y:10' (yuanbao) / 'g 1000'";
                (UiTextPromptResult result, string value) = await PromptTextAsync(
                    title: "Stall",
                    prompt: prompt,
                    buttons: UiTextPromptButtons.OkCancel).ConfigureAwait(true);

                if (result != UiTextPromptResult.Ok)
                    return;

                if (!TryParseStallPrice(value, out goldType, out price))
                {
                    ShowModal("Stall", "Invalid stall price input.", UiModalButtons.Ok, __ => { });
                    return;
                }

                int max = goldType == 5 ? 8_000_000 : 150_000_000;
                if (price <= 0 || price > max)
                {
                    ShowModal("Stall", $"Price out of range (1~{max}).", UiModalButtons.Ok, __ => { });
                    return;
                }

                await _stallSystem.TryAddItemAsync(stallBagItem, price, goldType, stallBagSlotIndex, token);

                return;
            }

            if (!_heroBagView && _world.DealOpen)
            {
                if (TryHitTestDealMySlot(logical, out int dealIndex, out ClientItem dealItem) && dealItem.MakeIndex != 0)
                {
                    await _dealSystem.TrySendDelItemAsync(dealItem, dealIndex, token);

                    return;
                }

                if (TryHitTestBagSlot(logical, out int dealBagSlotIndex, out ClientItem dealBagItem) && dealBagItem.MakeIndex != 0)
                {
                    if (dealBagItem.S.NeedIdentify >= 4)
                    {
                        AppendLog($"[deal] add ignored (stall makeIndex={dealBagItem.MakeIndex} name='{dealBagItem.NameString}')");
                        return;
                    }

                    await _dealSystem.TrySendAddItemAsync(dealBagItem, dealBagSlotIndex, token);

                    return;
                }
            }

            if (!_heroBagView && _world.MerchantMode is MirMerchantMode.Storage or MirMerchantMode.GetSave)
            {
                if (TryHitTestStorageSlot(logical, out int storageIndex, out ClientItem storageItem) && storageItem.MakeIndex != 0)
                {
                    await _storageSystem.TryTakeBackAsync(storageItem, storageIndex, token);

                    return;
                }

                if (TryHitTestBagSlot(logical, out int storageBagSlotIndex, out ClientItem storageBagItem) && storageBagItem.MakeIndex != 0)
                {
                    if (storageBagItem.S.NeedIdentify >= 4)
                    {
                        AppendLog($"[storage] store ignored (stall makeIndex={storageBagItem.MakeIndex} name='{storageBagItem.NameString}')");
                        return;
                    }

                    await _storageSystem.TryStoreAsync(storageBagItem, storageBagSlotIndex, token);

                    return;
                }
            }

            if (!_heroBagView && _world.MerchantMode is MirMerchantMode.Sell or MirMerchantMode.Repair)
            {
                if (TryHitTestBagSlot(logical, out int tradeSlotIndex, out ClientItem tradeItem) && tradeItem.MakeIndex != 0)
                {
                    if (tradeItem.S.NeedIdentify >= 4)
                    {
                        AppendLog($"[merchant] trade ignored (stall makeIndex={tradeItem.MakeIndex} name='{tradeItem.NameString}')");
                        return;
                    }

                    if (_world.MerchantMode == MirMerchantMode.Sell)
                    {
                        await _merchantTradeSystem.TrySellAsync(tradeItem, tradeSlotIndex, token);

                        return;
                    }

                    await _merchantTradeSystem.TryRepairAsync(tradeItem, tradeSlotIndex, token);

                    return;
                }
            }

            if (!_heroBagView && _marketSystem.Visible)
            {
                if (TryHitTestBagSlot(logical, out int marketSellSlotIndex, out ClientItem marketSellItem) && marketSellItem.MakeIndex != 0)
                {
                    if (marketSellItem.S.NeedIdentify >= 4)
                    {
                        AppendLog($"[market] sell ignored (stall makeIndex={marketSellItem.MakeIndex} name='{marketSellItem.NameString}')");
                        return;
                    }

                    if (!_marketSystem.TryBeginAction())
                        return;

                    int merchantId = _world.CurrentMerchantId;
                    if (merchantId <= 0)
                    {
                        AppendLog("[market] sell ignored: merchant not set");
                        return;
                    }

                    ushort duraOrCount = marketSellItem.Dura;
                    if (marketSellItem.S.Overlap > 0 && marketSellItem.Dura > 1)
                    {
                        string prompt = $"Sell count (1-{marketSellItem.Dura})";
                        (UiTextPromptResult result, string value) = await PromptTextAsync(
                            title: "Market",
                            prompt: prompt,
                            buttons: UiTextPromptButtons.OkCancel).ConfigureAwait(true);

                        if (result != UiTextPromptResult.Ok)
                        {
                            AppendLog($"[market] sell count dismissed ({result})");
                            return;
                        }

                        if (!int.TryParse(value.Trim(), out int count))
                        {
                            AppendLog($"[market] sell count invalid: '{value}'");
                            return;
                        }

                        count = Math.Clamp(count, 1, marketSellItem.Dura);
                        duraOrCount = unchecked((ushort)count);
                    }

                    const int maxPrice = 100_000_000;
                    (UiTextPromptResult priceResult, string priceValue) = await PromptTextAsync(
                        title: "Market",
                        prompt: $"Sell price (1-{maxPrice})",
                        buttons: UiTextPromptButtons.OkCancel).ConfigureAwait(true);

                    if (priceResult != UiTextPromptResult.Ok)
                    {
                        AppendLog($"[market] sell price dismissed ({priceResult})");
                        return;
                    }

                    if (!int.TryParse(priceValue.Trim(), out int sellPrice))
                    {
                        AppendLog($"[market] sell price invalid: '{priceValue}'");
                        return;
                    }

                    sellPrice = Math.Clamp(sellPrice, 1, maxPrice);

                    await _maketSystem.TrySendMarketSellAsync(merchantId, marketSellItem, duraOrCount, sellPrice, marketSellSlotIndex, token);

                    return;
                }
            }

            bool heroEquip = _heroBagView;
            string equipLogPrefix = heroEquip ? "[hero-equip]" : "[equip]";

            if (TryHitTestUseItemSlot(logical, out int useSlot, out ClientItem useItem) && useItem.MakeIndex != 0)
            {
                await _equipSystem.TryTakeOffAsync(
                    useItem,
                    heroEquip,
                    where: useSlot,
                    logPrefix: equipLogPrefix,
                    actionLabel: "takeoff",
                    successSuffix: string.Empty,
                    token);

                return;
            }

            if (!TryHitTestBagSlot(logical, out int bagSlotIndex, out ClientItem bagItem) || bagItem.MakeIndex == 0)
                return;

            if (!_heroBagView && bagItem.S.NeedIdentify >= 4)
            {
                AppendLog($"{equipLogPrefix} takeon ignored (stall makeIndex={bagItem.MakeIndex} name='{bagItem.NameString}')");
                return;
            }

            await _equipSystem.TryTakeOnAsync(
                bagItem,
                heroEquip,
                whereHint: -1,
                bagSlotIndex: bagSlotIndex,
                logPrefix: equipLogPrefix,
                actionLabel: "Alt+Click",
                successSuffix: string.Empty,
                token);
        }
    }

    private bool TryResolveMapCell(Vector2 logical, out int mapX, out int mapY)
    {
        mapX = 0;
        mapY = 0;

        if (_map == null)
            return false;

        int unitX = Grobal2.UNITX;
        int unitY = Grobal2.UNITY;
        if (unitX <= 0 || unitY <= 0)
            return false;

        int w = _lastLogicalSize.Width;
        int h = _lastLogicalSize.Height;
        if (w <= 0 || h <= 0)
            return false;

        int centerX = _world.MapCenterSet ? _world.MapCenterX : _map.Width / 2;
        int centerY = _world.MapCenterSet ? _world.MapCenterY : _map.Height / 2;
        centerX = Math.Clamp(centerX, 0, Math.Max(0, _map.Width - 1));
        centerY = Math.Clamp(centerY, 0, Math.Max(0, _map.Height - 1));

        int offsetX = (int)Math.Round((w / 2.0) / unitX) + 1;
        int offsetY = (int)Math.Round((h / 2.0) / unitY);

        int left = centerX - offsetX;
        int top = centerY - offsetY;

        int aax = ((w - unitX) / 2) % unitX;

        int shiftX = 0;
        int shiftY = 0;
        if (_world.MyselfRecogIdSet && _world.TryGetActor(_world.MyselfRecogId, out ActorMarker myself))
        {
            long nowTimestamp = Stopwatch.GetTimestamp();
            float rx = myself.X;
            float ry = myself.Y;

            if (MirDirection.IsMoveAction(myself.Action))
            {
                (int moveFrames, int moveFrameTimeMs) = GetActorMoveTiming(_world.MyselfRecogId, myself);
                long totalMs = (long)moveFrames * moveFrameTimeMs;
                if (totalMs > 0)
                {
                    long elapsedMs = (nowTimestamp - myself.ActionStartTimestamp) * 1000 / Stopwatch.Frequency;
                    if (elapsedMs >= 0 && elapsedMs < totalMs)
                    {
                        float t = Math.Clamp(elapsedMs / (float)totalMs, 0f, 1f);
                        rx = myself.FromX + ((myself.X - myself.FromX) * t);
                        ry = myself.FromY + ((myself.Y - myself.FromY) * t);
                    }
                }
            }

            shiftX = (int)Math.Round((rx - centerX) * unitX);
            shiftY = (int)Math.Round((ry - centerY) * unitY);
        }

        int defx = (-unitX * 2) - shiftX + aax;
        int defy = (-unitY * 2) - shiftY;

        mapX = left + (int)Math.Floor((logical.X - defx) / unitX);
        mapY = top + 1 + (int)Math.Floor((logical.Y - defy) / unitY);

        if ((uint)mapX >= (uint)_map.Width || (uint)mapY >= (uint)_map.Height)
            return false;

        return true;
    }

    private bool TryGetLogicalPoint(System.Drawing.Point mouseClient, out Vector2 logical)
    {
        logical = default;

        if (!_lastViewportRect.Contains(mouseClient))
            return false;

        float sx = _lastViewScale.X;
        float sy = _lastViewScale.Y;
        if (sx <= 0.0001f || sy <= 0.0001f)
            return false;

        logical = new Vector2(
            (mouseClient.X - _lastViewOffset.X) / sx,
            (mouseClient.Y - _lastViewOffset.Y) / sy);
        return true;
    }

    private bool TryHitTestBagSlot(Vector2 logical, out int slotIndex, out ClientItem item)
    {
        slotIndex = -1;
        item = default;

        if (!_bagWindowVisible)
            return false;

        if (!IsSpecialBagUiActive())
        {
            if (_heroBagView)
            {
                DrawingRectangle heroRect = _heroBagPanelRect ?? default;
                int heroX = heroRect.Width > 0 ? heroRect.Left : (_heroBagWindowPosSet ? _heroBagWindowPosX : 28);
                int heroY = heroRect.Height > 0 ? heroRect.Top : (_heroBagWindowPosSet ? _heroBagWindowPosY : 100);

                const int heroCols = 5;
                const int heroRows = 2;
                const int heroSlotW = 36;
                const int heroSlotH = 32;
                int heroSlotX0 = heroX + 17;
                int heroSlotY0 = heroY + 14;

                ReadOnlySpan<ClientItem> heroSlots = _world.HeroBagSlots;
                int heroMax = heroCols * heroRows;
                for (int i = 0; i < heroMax; i++)
                {
                    int row = i / heroCols;
                    int col = i % heroCols;
                    int x = heroSlotX0 + (col * heroSlotW);
                    int y = heroSlotY0 + (row * heroSlotH);
                    var rect = new DrawingRectangle(x, y, heroSlotW, heroSlotH);

                    if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                        continue;

                    if ((uint)i >= (uint)heroSlots.Length)
                        return false;

                    slotIndex = i;
                    item = heroSlots[i];
                    return true;
                }

                return false;
            }

            DrawingRectangle bagRect = _bagPanelRect ?? default;
            int bagX = bagRect.Width > 0 ? bagRect.Left : (_bagWindowPosSet ? _bagWindowPosX : 0);
            int bagY = bagRect.Height > 0 ? bagRect.Top : (_bagWindowPosSet ? _bagWindowPosY : 60);

            const int bagCols = 8;
            const int bagRows = 5;
            const int bagSlotW = 36;
            const int bagSlotH = 32;
            int bagSlotX0 = bagX + 29;
            int bagSlotY0 = bagY + 41;

            ReadOnlySpan<ClientItem> bagSlots = _world.BagSlots;
            int bagMax = bagCols * bagRows;
            for (int i = 0; i < bagMax; i++)
            {
                int row = i / bagCols;
                int col = i % bagCols;
                int x = bagSlotX0 + (col * bagSlotW);
                int y = bagSlotY0 + (row * bagSlotH);
                var rect = new DrawingRectangle(x, y, bagSlotW, bagSlotH);

                if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                    continue;

                int realIndex = 6 + i;
                if ((uint)realIndex >= (uint)bagSlots.Length)
                    return false;

                slotIndex = realIndex;
                item = bagSlots[realIndex];
                return true;
            }

            return false;
        }

        const int cols = 8;
        const int rows = 7;
        const int slot = 36;
        const int pad = 8;
        const int header = 22;

        int panelW = (pad * 2) + (cols * slot);
        int panelH = (pad * 2) + header + (rows * slot);

        int panelX = Math.Max(8, _lastLogicalSize.Width - panelW - 16);
        int panelY = Math.Max(8, _lastLogicalSize.Height - panelH - 16);

        int slotX0 = panelX + pad;
        int slotY0 = panelY + pad + header;

        ReadOnlySpan<ClientItem> slots = _heroBagView ? _world.HeroBagSlots : _world.BagSlots;

        for (int i = 0; i < slots.Length; i++)
        {
            int row = i / cols;
            int col = i % cols;
            if (row >= rows)
                break;

            int x = slotX0 + (col * slot);
            int y = slotY0 + (row * slot);
            var rect = new DrawingRectangle(x, y, slot - 2, slot - 2);

            if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                continue;

            slotIndex = i;
            item = slots[i];
            return true;
        }

        return false;
    }

    private bool TryHitTestUseItemSlot(Vector2 logical, out int slotIndex, out ClientItem item)
    {
        slotIndex = -1;
        item = default;

        if (!IsSpecialBagUiActive())
        {
            if (!_stateWindowVisible || _statePanelRect is not { } stateRect)
                return false;

            IReadOnlyDictionary<int, ClientItem> useItems2 = _heroBagView ? _world.HeroUseItems : _world.UseItems;

            for (int i = Grobal2.U_DRESS; i <= Grobal2.U_CHARM; i++)
            {
                if (!TryGetUseSlotRect(stateRect.Left, stateRect.Top, i, out DrawingRectangle rect))
                    continue;

                if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                    continue;

                slotIndex = i;
                useItems2.TryGetValue(i, out item);
                return true;
            }

            return false;
        }

        const int cols = 8;
        const int rows = 7;
        const int slot = 36;
        const int pad = 8;
        const int header = 22;

        const int useCols = 2;
        const int useRows = 7;

        int bagPanelW = (pad * 2) + (cols * slot);
        int bagPanelH = (pad * 2) + header + (rows * slot);

        int bagPanelX = Math.Max(8, _lastLogicalSize.Width - bagPanelW - 16);
        int bagPanelY = Math.Max(8, _lastLogicalSize.Height - bagPanelH - 16);

        int usePanelW = (pad * 2) + (useCols * slot);
        int usePanelX = Math.Max(8, bagPanelX - usePanelW - 12);
        int usePanelY = bagPanelY;

        int slotX0 = usePanelX + pad;
        int slotY0 = usePanelY + pad + header;

        IReadOnlyDictionary<int, ClientItem> useItems = _heroBagView ? _world.HeroUseItems : _world.UseItems;

        for (int i = Grobal2.U_DRESS; i <= Grobal2.U_CHARM; i++)
        {
            int row = i / useCols;
            int col = i % useCols;
            if (row >= useRows)
                break;

            int x = slotX0 + (col * slot);
            int y = slotY0 + (row * slot);
            var rect = new DrawingRectangle(x, y, slot - 2, slot - 2);

            if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                continue;

            slotIndex = i;
            useItems.TryGetValue(i, out item);
            return true;
        }

        return false;

        static bool TryGetUseSlotRect(int baseX, int baseY, int slotIndex, out DrawingRectangle rect)
        {
            rect = slotIndex switch
            {
                Grobal2.U_NECKLACE => new DrawingRectangle(baseX + 168, baseY + 87, 34, 31),
                Grobal2.U_HELMET => new DrawingRectangle(baseX + 115, baseY + 93, 18, 18),
                Grobal2.U_RIGHTHAND => new DrawingRectangle(baseX + 168, baseY + 125, 34, 31),
                Grobal2.U_ARMRINGR => new DrawingRectangle(baseX + 42, baseY + 176, 34, 31),
                Grobal2.U_ARMRINGL => new DrawingRectangle(baseX + 168, baseY + 176, 34, 31),
                Grobal2.U_RINGR => new DrawingRectangle(baseX + 42, baseY + 215, 34, 31),
                Grobal2.U_RINGL => new DrawingRectangle(baseX + 168, baseY + 215, 34, 31),
                Grobal2.U_WEAPON => new DrawingRectangle(baseX + 47, baseY + 80, 47, 87),
                Grobal2.U_DRESS => new DrawingRectangle(baseX + 96, baseY + 122, 53, 112),
                Grobal2.U_BUJUK => new DrawingRectangle(baseX + 42, baseY + 254, 34, 31),
                Grobal2.U_BELT => new DrawingRectangle(baseX + 84, baseY + 254, 34, 31),
                Grobal2.U_BOOTS => new DrawingRectangle(baseX + 126, baseY + 254, 34, 31),
                Grobal2.U_CHARM => new DrawingRectangle(baseX + 168, baseY + 254, 34, 31),
                _ => default
            };

            return rect.Width > 0 && rect.Height > 0;
        }
    }

    private bool TryHitTestBagPanel(Vector2 logical)
    {
        if (!_bagWindowVisible)
            return false;

        if (!IsSpecialBagUiActive())
        {
            if (_heroBagView)
            {
                if (_heroBagPanelRect is not { } rect)
                    return false;

                return logical.X >= rect.Left &&
                       logical.X < rect.Right &&
                       logical.Y >= rect.Top &&
                       logical.Y < rect.Bottom;
            }

            if (_bagPanelRect is not { } rect2)
                return false;

            return logical.X >= rect2.Left &&
                   logical.X < rect2.Right &&
                   logical.Y >= rect2.Top &&
                   logical.Y < rect2.Bottom;
        }

        const int cols = 8;
        const int rows = 7;
        const int slot = 36;
        const int pad = 8;
        const int header = 22;

        int panelW = (pad * 2) + (cols * slot);
        int panelH = (pad * 2) + header + (rows * slot);

        int panelX = Math.Max(8, _lastLogicalSize.Width - panelW - 16);
        int panelY = Math.Max(8, _lastLogicalSize.Height - panelH - 16);

        return logical.X >= panelX &&
               logical.X < panelX + panelW &&
               logical.Y >= panelY &&
               logical.Y < panelY + panelH;
    }

    private bool TryHitTestUsePanel(Vector2 logical)
    {
        if (!IsSpecialBagUiActive())
        {
            if (_statePanelRect is not { } rect)
                return false;

            return logical.X >= rect.Left &&
                   logical.X < rect.Right &&
                   logical.Y >= rect.Top &&
                   logical.Y < rect.Bottom;
        }

        const int cols = 8;
        const int rows = 7;
        const int slot = 36;
        const int pad = 8;
        const int header = 22;

        const int useCols = 2;
        const int useRows = 7;

        int bagPanelW = (pad * 2) + (cols * slot);
        int bagPanelH = (pad * 2) + header + (rows * slot);

        int bagPanelX = Math.Max(8, _lastLogicalSize.Width - bagPanelW - 16);
        int bagPanelY = Math.Max(8, _lastLogicalSize.Height - bagPanelH - 16);

        int usePanelW = (pad * 2) + (useCols * slot);
        int usePanelH = (pad * 2) + header + (useRows * slot);
        int usePanelX = Math.Max(8, bagPanelX - usePanelW - 12);
        int usePanelY = bagPanelY;

        return logical.X >= usePanelX &&
               logical.X < usePanelX + usePanelW &&
               logical.Y >= usePanelY &&
               logical.Y < usePanelY + usePanelH;
    }

    private bool TryHitTestStorageSlot(Vector2 logical, out int storageIndex, out ClientItem item)
    {
        storageIndex = -1;
        item = default;

        if (_heroBagView)
            return false;

        if (_world.MerchantMode is not MirMerchantMode.Storage and not MirMerchantMode.GetSave)
            return false;

        const int cols = 8;
        const int rows = 7;
        const int slot = 36;
        const int pad = 8;
        const int header = 22;

        int panelW = (pad * 2) + (cols * slot);
        int panelH = (pad * 2) + header + (rows * slot);

        int bagPanelX = Math.Max(8, _lastLogicalSize.Width - panelW - 16);
        int bagPanelY = Math.Max(8, _lastLogicalSize.Height - panelH - 16);

        int storagePanelX = bagPanelX;
        int storagePanelY = Math.Max(8, bagPanelY - panelH - 12);

        int slotX0 = storagePanelX + pad;
        int slotY0 = storagePanelY + pad + header;

        int top = Math.Max(0, _world.MerchantMenuTopLine);
        IReadOnlyList<ClientItem> items = _world.StorageItems;

        int max = cols * rows;
        for (int i = 0; i < max; i++)
        {
            int row = i / cols;
            int col = i % cols;
            if (row >= rows)
                break;

            int x = slotX0 + (col * slot);
            int y = slotY0 + (row * slot);
            var rect = new DrawingRectangle(x, y, slot - 2, slot - 2);

            if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                continue;

            storageIndex = top + i;
            if ((uint)storageIndex < (uint)items.Count)
                item = items[storageIndex];
            return true;
        }

        return false;
    }

    private bool TryHitTestDealMySlot(Vector2 logical, out int dealIndex, out ClientItem item)
    {
        dealIndex = -1;
        item = default;

        if (_heroBagView)
            return false;

        if (!_world.DealOpen)
            return false;

        const int cols = 8;
        const int rows = 7;
        const int slot = 36;
        const int pad = 8;
        const int header = 22;
        const int useCols = 2;

        int panelW = (pad * 2) + (cols * slot);
        int panelH = (pad * 2) + header + (rows * slot);

        int panelX = Math.Max(8, _lastLogicalSize.Width - panelW - 16);
        int panelY = Math.Max(8, _lastLogicalSize.Height - panelH - 16);

        int usePanelW = (pad * 2) + (useCols * slot);
        int usePanelX = Math.Max(8, panelX - usePanelW - 12);

        const int dealCols = 5;
        const int dealRemoteRows = 4;
        const int dealMyRows = 3;
        const int dealGapY = 12;
        const int dealButtonGapY = 10;
        const int dealButtonH = 22;

        int dealPanelW = (pad * 2) + (dealCols * slot);
        int dealRemotePanelH = (pad * 2) + header + (dealRemoteRows * slot);
        int dealMyPanelH = (pad * 2) + header + (dealMyRows * slot) + dealButtonGapY + dealButtonH;
        int dealTotalH = dealRemotePanelH + dealGapY + dealMyPanelH;

        int dealPanelX = Math.Max(8, usePanelX - dealPanelW - 12);
        int dealRemotePanelY = Math.Max(8, (panelY + panelH) - dealTotalH);
        int dealMyPanelY = dealRemotePanelY + dealRemotePanelH + dealGapY;

        int slotX0 = dealPanelX + pad;
        int slotY0 = dealMyPanelY + pad + header;

        IReadOnlyList<ClientItem> items = _world.DealMyItems;

        int max = dealCols * dealMyRows;
        for (int i = 0; i < max; i++)
        {
            int row = i / dealCols;
            int col = i % dealCols;
            if (row >= dealMyRows)
                break;

            int x = slotX0 + (col * slot);
            int y = slotY0 + (row * slot);
            var rect = new DrawingRectangle(x, y, slot - 2, slot - 2);

            if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                continue;

            dealIndex = i;
            if ((uint)i < (uint)items.Count)
                item = items[i];
            return true;
        }

        return false;
    }

    private bool TryHitTestDealButton(Vector2 logical, out DealButton button)
    {
        button = DealButton.None;

        if (_heroBagView)
            return false;

        if (!_world.DealOpen)
            return false;

        const int cols = 8;
        const int rows = 7;
        const int slot = 36;
        const int pad = 8;
        const int header = 22;
        const int useCols = 2;

        int panelW = (pad * 2) + (cols * slot);
        int panelH = (pad * 2) + header + (rows * slot);

        int panelX = Math.Max(8, _lastLogicalSize.Width - panelW - 16);
        int panelY = Math.Max(8, _lastLogicalSize.Height - panelH - 16);

        int usePanelW = (pad * 2) + (useCols * slot);
        int usePanelX = Math.Max(8, panelX - usePanelW - 12);

        const int dealCols = 5;
        const int dealRemoteRows = 4;
        const int dealMyRows = 3;
        const int dealGapY = 12;
        const int dealButtonGapY = 10;
        const int dealButtonH = 22;

        int dealPanelW = (pad * 2) + (dealCols * slot);
        int dealRemotePanelH = (pad * 2) + header + (dealRemoteRows * slot);
        int dealMyPanelH = (pad * 2) + header + (dealMyRows * slot) + dealButtonGapY + dealButtonH;
        int dealTotalH = dealRemotePanelH + dealGapY + dealMyPanelH;

        int dealPanelX = Math.Max(8, usePanelX - dealPanelW - 12);
        int dealRemotePanelY = Math.Max(8, (panelY + panelH) - dealTotalH);
        int dealMyPanelY = dealRemotePanelY + dealRemotePanelH + dealGapY;

        int dealMySlotY0 = dealMyPanelY + pad + header;
        int dealButtonsY = dealMySlotY0 + (dealMyRows * slot) + dealButtonGapY;

        const int btnGap = 6;
        int btnW = (dealPanelW - (pad * 2) - (btnGap * 2)) / 3;
        int btnX0 = dealPanelX + pad;

        var goldRect = new DrawingRectangle(btnX0, dealButtonsY, btnW, dealButtonH);
        var endRect = new DrawingRectangle(btnX0 + btnW + btnGap, dealButtonsY, btnW, dealButtonH);
        var cancelRect = new DrawingRectangle(btnX0 + (btnW + btnGap) * 2, dealButtonsY, btnW, dealButtonH);

        if (logical.X >= goldRect.Left && logical.X < goldRect.Right && logical.Y >= goldRect.Top && logical.Y < goldRect.Bottom)
        {
            button = DealButton.Gold;
            return true;
        }

        if (logical.X >= endRect.Left && logical.X < endRect.Right && logical.Y >= endRect.Top && logical.Y < endRect.Bottom)
        {
            button = DealButton.End;
            return true;
        }

        if (logical.X >= cancelRect.Left && logical.X < cancelRect.Right && logical.Y >= cancelRect.Top && logical.Y < cancelRect.Bottom)
        {
            button = DealButton.Cancel;
            return true;
        }

        return false;
    }

    private bool TryHitTestStallSlot(Vector2 logical, out int slotIndex, out StallSlot slot)
    {
        slotIndex = -1;
        slot = default;

        if (!IsStallSetupUiActive())
            return false;

        const int cols = 8;
        const int rows = 7;
        const int slotSize = 36;
        const int pad = 8;
        const int header = 22;
        const int useCols = 2;

        int panelW = (pad * 2) + (cols * slotSize);
        int panelH = (pad * 2) + header + (rows * slotSize);

        int panelX = Math.Max(8, _lastLogicalSize.Width - panelW - 16);
        int panelY = Math.Max(8, _lastLogicalSize.Height - panelH - 16);

        int usePanelW = (pad * 2) + (useCols * slotSize);
        int usePanelX = Math.Max(8, panelX - usePanelW - 12);

        const int stallCols = 5;
        const int stallRows = 2;
        const int stallButtonGapY = 10;
        const int stallButtonH = 22;

        int stallPanelW = Math.Max(260, (pad * 2) + (stallCols * slotSize));
        int stallPanelH = (pad * 2) + header + (stallRows * slotSize) + stallButtonGapY + stallButtonH;

        int stallPanelX = Math.Max(8, usePanelX - stallPanelW - 12);
        int stallPanelY = Math.Max(8, (panelY + panelH) - stallPanelH);

        int stallContentW = Math.Max(0, stallPanelW - (pad * 2));
        int stallGridW = stallCols * slotSize;
        int slotX0 = stallPanelX + pad + Math.Max(0, (stallContentW - stallGridW) / 2);
        int slotY0 = stallPanelY + pad + header;

        for (int i = 0; i < ClientStallItems.MaxStallItemCount; i++)
        {
            int row = i / stallCols;
            int col = i % stallCols;
            if (row >= stallRows)
                break;

            int x = slotX0 + (col * slotSize);
            int y = slotY0 + (row * slotSize);
            var rect = new DrawingRectangle(x, y, slotSize - 2, slotSize - 2);

            if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                continue;

            slotIndex = i;
            slot = _stallSystem.Slots[i];
            return true;
        }

        return false;
    }

    private bool TryHitTestStallButton(Vector2 logical, out StallButton button)
    {
        button = StallButton.None;

        if (!IsStallSetupUiActive())
            return false;

        const int cols = 8;
        const int rows = 7;
        const int slotSize = 36;
        const int pad = 8;
        const int header = 22;
        const int useCols = 2;

        int panelW = (pad * 2) + (cols * slotSize);
        int panelH = (pad * 2) + header + (rows * slotSize);

        int panelX = Math.Max(8, _lastLogicalSize.Width - panelW - 16);
        int panelY = Math.Max(8, _lastLogicalSize.Height - panelH - 16);

        int usePanelW = (pad * 2) + (useCols * slotSize);
        int usePanelX = Math.Max(8, panelX - usePanelW - 12);

        const int stallCols = 5;
        const int stallRows = 2;
        const int stallButtonGapY = 10;
        const int stallButtonH = 22;
        const int stallBtnGap = 6;
        const int stallBtnCount = 4;

        int stallPanelW = Math.Max(260, (pad * 2) + (stallCols * slotSize));
        int stallPanelH = (pad * 2) + header + (stallRows * slotSize) + stallButtonGapY + stallButtonH;

        int stallPanelX = Math.Max(8, usePanelX - stallPanelW - 12);
        int stallPanelY = Math.Max(8, (panelY + panelH) - stallPanelH);

        int stallContentW = Math.Max(0, stallPanelW - (pad * 2));
        int slotY0 = stallPanelY + pad + header;
        int buttonsY = slotY0 + (stallRows * slotSize) + stallButtonGapY;

        int btnW = (stallContentW - (stallBtnGap * (stallBtnCount - 1))) / stallBtnCount;
        if (btnW <= 0)
            return false;

        int btnX0 = stallPanelX + pad;

        var nameRect = new DrawingRectangle(btnX0 + (btnW + stallBtnGap) * 0, buttonsY, btnW, stallButtonH);
        var openRect = new DrawingRectangle(btnX0 + (btnW + stallBtnGap) * 1, buttonsY, btnW, stallButtonH);
        var cancelRect = new DrawingRectangle(btnX0 + (btnW + stallBtnGap) * 2, buttonsY, btnW, stallButtonH);
        var removeRect = new DrawingRectangle(btnX0 + (btnW + stallBtnGap) * 3, buttonsY, btnW, stallButtonH);

        if (logical.X >= nameRect.Left && logical.X < nameRect.Right && logical.Y >= nameRect.Top && logical.Y < nameRect.Bottom)
        {
            button = StallButton.Name;
            return true;
        }

        if (logical.X >= openRect.Left && logical.X < openRect.Right && logical.Y >= openRect.Top && logical.Y < openRect.Bottom)
        {
            button = StallButton.Open;
            return true;
        }

        if (logical.X >= cancelRect.Left && logical.X < cancelRect.Right && logical.Y >= cancelRect.Top && logical.Y < cancelRect.Bottom)
        {
            button = StallButton.Cancel;
            return true;
        }

        if (logical.X >= removeRect.Left && logical.X < removeRect.Right && logical.Y >= removeRect.Top && logical.Y < removeRect.Bottom)
        {
            button = StallButton.Remove;
            return true;
        }

        return false;
    }

    private bool TryHitTestStallPanel(Vector2 logical)
    {
        if (!IsStallSetupUiActive())
            return false;

        const int cols = 8;
        const int rows = 7;
        const int slotSize = 36;
        const int pad = 8;
        const int header = 22;
        const int useCols = 2;

        int panelW = (pad * 2) + (cols * slotSize);
        int panelH = (pad * 2) + header + (rows * slotSize);

        int panelX = Math.Max(8, _lastLogicalSize.Width - panelW - 16);
        int panelY = Math.Max(8, _lastLogicalSize.Height - panelH - 16);

        int usePanelW = (pad * 2) + (useCols * slotSize);
        int usePanelX = Math.Max(8, panelX - usePanelW - 12);

        const int stallCols = 5;
        const int stallRows = 2;
        const int stallButtonGapY = 10;
        const int stallButtonH = 22;

        int stallPanelW = Math.Max(260, (pad * 2) + (stallCols * slotSize));
        int stallPanelH = (pad * 2) + header + (stallRows * slotSize) + stallButtonGapY + stallButtonH;

        int stallPanelX = Math.Max(8, usePanelX - stallPanelW - 12);
        int stallPanelY = Math.Max(8, (panelY + panelH) - stallPanelH);

        var rect = new DrawingRectangle(stallPanelX, stallPanelY, stallPanelW, stallPanelH);
        return logical.X >= rect.Left && logical.X < rect.Right && logical.Y >= rect.Top && logical.Y < rect.Bottom;
    }

    private bool TryHitTestUserStallSlot(Vector2 logical, out int slotIndex, out ClientItem item)
    {
        slotIndex = -1;
        item = default;

        if (_heroBagView)
            return false;
        if (!_userStallSystem.Visible || !_world.UserStallOpen)
            return false;

        const int slotSize = 36;
        const int pad = 8;
        const int header = 22;

        const int userStallCols = 5;
        const int userStallRows = 2;

        int panelW = Math.Max(260, (pad * 2) + (userStallCols * slotSize));

        int panelX = 8;
        int panelY = 8;

        int contentW = Math.Max(0, panelW - (pad * 2));
        int gridW = userStallCols * slotSize;
        int slotX0 = panelX + pad + Math.Max(0, (contentW - gridW) / 2);
        int slotY0 = panelY + pad + header;

        ReadOnlySpan<ClientItem> items = _world.UserStallItems;

        for (int i = 0; i < ClientStallItems.MaxStallItemCount; i++)
        {
            int row = i / userStallCols;
            int col = i % userStallCols;
            if (row >= userStallRows)
                break;

            int x = slotX0 + (col * slotSize);
            int y = slotY0 + (row * slotSize);
            var rect = new DrawingRectangle(x, y, slotSize - 2, slotSize - 2);

            if (logical.X < rect.Left || logical.X >= rect.Right || logical.Y < rect.Top || logical.Y >= rect.Bottom)
                continue;

            slotIndex = i;
            if ((uint)i < (uint)items.Length)
                item = items[i];
            return true;
        }

        return false;
    }

    private bool TryHitTestUserStallButton(Vector2 logical, out UserStallButton button)
    {
        button = UserStallButton.None;

        if (_heroBagView)
            return false;
        if (!_userStallSystem.Visible || !_world.UserStallOpen)
            return false;

        const int slotSize = 36;
        const int pad = 8;
        const int header = 22;

        const int userStallCols = 5;
        const int userStallRows = 2;
        const int userStallButtonGapY = 10;
        const int userStallButtonH = 22;
        const int userStallBtnGap = 6;
        const int userStallBtnCount = 2;

        int panelW = Math.Max(260, (pad * 2) + (userStallCols * slotSize));

        int panelX = 8;
        int panelY = 8;

        int contentW = Math.Max(0, panelW - (pad * 2));
        int slotY0 = panelY + pad + header;
        int buttonsY = slotY0 + (userStallRows * slotSize) + userStallButtonGapY;

        int btnW = (contentW - (userStallBtnGap * (userStallBtnCount - 1))) / userStallBtnCount;
        if (btnW <= 0)
            return false;

        int btnX0 = panelX + pad;

        var buyRect = new DrawingRectangle(btnX0 + (btnW + userStallBtnGap) * 0, buttonsY, btnW, userStallButtonH);
        var closeRect = new DrawingRectangle(btnX0 + (btnW + userStallBtnGap) * 1, buttonsY, btnW, userStallButtonH);

        if (logical.X >= buyRect.Left && logical.X < buyRect.Right && logical.Y >= buyRect.Top && logical.Y < buyRect.Bottom)
        {
            button = UserStallButton.Buy;
            return true;
        }

        if (logical.X >= closeRect.Left && logical.X < closeRect.Right && logical.Y >= closeRect.Top && logical.Y < closeRect.Bottom)
        {
            button = UserStallButton.Close;
            return true;
        }

        return false;
    }

    private bool TryHitTestUserStallPanel(Vector2 logical)
    {
        if (_heroBagView)
            return false;
        if (!_userStallSystem.Visible || !_world.UserStallOpen)
            return false;

        const int slotSize = 36;
        const int pad = 8;
        const int header = 22;

        const int userStallCols = 5;
        const int userStallRows = 2;
        const int userStallButtonGapY = 10;
        const int userStallButtonH = 22;

        int panelW = Math.Max(260, (pad * 2) + (userStallCols * slotSize));
        int panelH = (pad * 2) + header + (userStallRows * slotSize) + userStallButtonGapY + userStallButtonH;

        int panelX = 8;
        int panelY = 8;

        var rect = new DrawingRectangle(panelX, panelY, panelW, panelH);
        return logical.X >= rect.Left && logical.X < rect.Right && logical.Y >= rect.Top && logical.Y < rect.Bottom;
    }

    private bool TryHitTestMarketRow(Vector2 logical, out int marketIndex, out MarketItem item)
    {
        marketIndex = -1;
        item = default;

        if (_heroBagView)
            return false;

        if (!_marketSystem.Visible)
            return false;

        const int pad = 8;
        const int header = 22;
        const int marketRows = 10;
        const int marketRowH = 18;
        const int marketDetailH = 48;
        const int marketButtonH = 22;
        const int marketGap = 8;

        int maxW = Math.Max(0, _lastLogicalSize.Width - 16);
        int panelW = Math.Min(520, maxW);
        if (panelW <= 0)
            return false;

        int listH = marketRows * marketRowH;
        int panelH = (pad * 2) + header + listH + marketGap + marketDetailH + marketGap + marketButtonH;
        panelH = Math.Min(panelH, Math.Max(0, _lastLogicalSize.Height - 16));

        int panelX = 8;
        int panelY = Math.Max(8, _lastLogicalSize.Height - panelH - 16);

        int listX0 = panelX + pad;
        int listY0 = panelY + pad + header;
        int listW = Math.Max(0, panelW - (pad * 2));

        if (logical.X < listX0 || logical.X >= listX0 + listW || logical.Y < listY0 || logical.Y >= listY0 + listH)
            return false;

        int row = (int)((logical.Y - listY0) / marketRowH);
        if ((uint)row >= (uint)marketRows)
            return false;

        int index = Math.Max(0, _marketSystem.TopIndex) + row;
        IReadOnlyList<MarketItem> items = _world.MarketItems;
        if ((uint)index >= (uint)items.Count)
            return false;

        marketIndex = index;
        item = items[index];
        return true;
    }

    private bool TryHitTestMarketPanel(Vector2 logical)
    {
        if (_heroBagView)
            return false;

        if (!_marketSystem.Visible)
            return false;

        const int pad = 8;
        const int header = 22;
        const int marketRows = 10;
        const int marketRowH = 18;
        const int marketDetailH = 48;
        const int marketButtonH = 22;
        const int marketGap = 8;

        int maxW = Math.Max(0, _lastLogicalSize.Width - 16);
        int panelW = Math.Min(520, maxW);
        if (panelW <= 0)
            return false;

        int listH = marketRows * marketRowH;
        int panelH = (pad * 2) + header + listH + marketGap + marketDetailH + marketGap + marketButtonH;
        panelH = Math.Min(panelH, Math.Max(0, _lastLogicalSize.Height - 16));

        int panelX = 8;
        int panelY = Math.Max(8, _lastLogicalSize.Height - panelH - 16);

        if (logical.X < panelX || logical.X >= panelX + panelW || logical.Y < panelY || logical.Y >= panelY + panelH)
            return false;

        return true;
    }

    private bool TryHitTestMarketButton(Vector2 logical, out MarketButton button)
    {
        button = MarketButton.None;

        if (_heroBagView)
            return false;

        if (!_marketSystem.Visible)
            return false;

        const int pad = 8;
        const int header = 22;
        const int marketRows = 10;
        const int marketRowH = 18;
        const int marketDetailH = 48;
        const int marketButtonH = 22;
        const int marketGap = 8;
        const int btnGap = 6;
        const int btnCount = 6;

        int maxW = Math.Max(0, _lastLogicalSize.Width - 16);
        int panelW = Math.Min(520, maxW);
        if (panelW <= 0)
            return false;

        int listH = marketRows * marketRowH;
        int panelH = (pad * 2) + header + listH + marketGap + marketDetailH + marketGap + marketButtonH;
        panelH = Math.Min(panelH, Math.Max(0, _lastLogicalSize.Height - 16));

        int panelX = 8;
        int panelY = Math.Max(8, _lastLogicalSize.Height - panelH - 16);

        int listY0 = panelY + pad + header;
        int detailY = listY0 + listH + marketGap;
        int buttonsY = detailY + marketDetailH + marketGap;

        int btnW = (panelW - (pad * 2) - (btnGap * (btnCount - 1))) / btnCount;
        if (btnW <= 0)
            return false;

        int x0 = panelX + pad;

        var rectPrev = new DrawingRectangle(x0 + (btnW + btnGap) * 0, buttonsY, btnW, marketButtonH);
        var rectNext = new DrawingRectangle(x0 + (btnW + btnGap) * 1, buttonsY, btnW, marketButtonH);
        var rectRefresh = new DrawingRectangle(x0 + (btnW + btnGap) * 2, buttonsY, btnW, marketButtonH);
        var rectFind = new DrawingRectangle(x0 + (btnW + btnGap) * 3, buttonsY, btnW, marketButtonH);
        var rectAction = new DrawingRectangle(x0 + (btnW + btnGap) * 4, buttonsY, btnW, marketButtonH);
        var rectClose = new DrawingRectangle(x0 + (btnW + btnGap) * 5, buttonsY, btnW, marketButtonH);

        if (logical.X >= rectPrev.Left && logical.X < rectPrev.Right && logical.Y >= rectPrev.Top && logical.Y < rectPrev.Bottom)
        {
            button = MarketButton.Prev;
            return true;
        }

        if (logical.X >= rectNext.Left && logical.X < rectNext.Right && logical.Y >= rectNext.Top && logical.Y < rectNext.Bottom)
        {
            button = MarketButton.Next;
            return true;
        }

        if (logical.X >= rectRefresh.Left && logical.X < rectRefresh.Right && logical.Y >= rectRefresh.Top && logical.Y < rectRefresh.Bottom)
        {
            button = MarketButton.Refresh;
            return true;
        }

        if (logical.X >= rectFind.Left && logical.X < rectFind.Right && logical.Y >= rectFind.Top && logical.Y < rectFind.Bottom)
        {
            button = MarketButton.Find;
            return true;
        }

        if (logical.X >= rectAction.Left && logical.X < rectAction.Right && logical.Y >= rectAction.Top && logical.Y < rectAction.Bottom)
        {
            button = MarketButton.Action;
            return true;
        }

        if (logical.X >= rectClose.Left && logical.X < rectClose.Right && logical.Y >= rectClose.Top && logical.Y < rectClose.Bottom)
        {
            button = MarketButton.Close;
            return true;
        }

        return false;
    }

    private void HandleServerMapChange(string identName, string mapName, int x, int y, int light)
    {
        mapName = mapName.Trim();
        AppendLog($"[map] {identName} '{mapName}' x={x} y={y} light={light}");

        string? path = TryResolveMapFilePath(mapName, allowNew: true);
        if (string.IsNullOrWhiteSpace(path))
        {
            AppendLog("[map] resolve failed.");
            return;
        }

        if (!File.Exists(path))
        {
            AppendLog($"[map] file not found: {path}");
            return;
        }

        try
        {
            _map = MirMapFile.Open(path);
            _mapLoadAttempted = true;
            AppendLog($"[map] loaded {path} {_map.Width}x{_map.Height} fmt={_map.Format} cell={_map.CellSizeBytes}");

            int centerX = Math.Clamp(x, 0, Math.Max(0, _map.Width - 1));
            int centerY = Math.Clamp(y, 0, Math.Max(0, _map.Height - 1));
            InvalidateMapTilePrefetch();

            _world.ApplyMapLoaded(centerX, centerY, nowTimestamp: Stopwatch.GetTimestamp());
            _world.ApplyDayChanging(_world.DayBright, light);
            _moveTimingStates.Clear();

            if (_miniMapSystem.ViewLevel > 0 &&
                _session.Stage is MirSessionStage.RunGate or MirSessionStage.InGame &&
                _world.MyselfRecogIdSet)
            {
                _world.ApplyReadMiniMapFail();
                _miniMapRequestSystem.TryRequest(_loginCts?.Token ?? CancellationToken.None);
                AppendLog("[minimap] request after map change");
            }
        }
        catch (Exception ex)
        {
            _map = null;
            _world.SetMapMoving(false);
            AppendLog($"[map] load error: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool IsCurrentMapWalkable(int x, int y)
    {
        if (_map == null)
            return true;

        if ((uint)x >= (uint)_map.Width || (uint)y >= (uint)_map.Height)
            return false;

        int key = (x << 16) | (y & 0xFFFF);
        if (_world.DoorOpenOverrides.TryGetValue(key, out bool open))
        {
            MirMapCell cell = _map.GetCell(x, y);
            if (!cell.IsWalkable)
                return false;

            if ((cell.DoorIndex & 0x80) == 0)
                return true;

            return open;
        }

        return _map.IsWalkable(x, y);
    }

    private string? TryResolveMapFilePath(string mapName, bool allowNew)
    {
        if (string.IsNullOrWhiteSpace(mapName))
            return null;

        string file = mapName.Replace('/', '\\').Trim();
        string defaultMapDir = Path.Combine(AppContext.BaseDirectory, "Map");

        string resourceDir = GetResourceRootDir();
        string resourceMapDir = Path.Combine(resourceDir, "Map");

        if (file.StartsWith('$'))
        {
            file = file[1..];
            if (Path.GetExtension(file).Length == 0)
                file += ".map";
            return Path.GetFullPath(Path.Combine(resourceMapDir, file));
        }

        if (Path.GetExtension(file).Length == 0)
            file += ".map";

        string resCandidate = Path.GetFullPath(Path.Combine(resourceMapDir, file));
        if (File.Exists(resCandidate))
            return resCandidate;

        string newCandidate = Path.GetFullPath(Path.Combine(defaultMapDir, "n" + file));
        if (allowNew && File.Exists(newCandidate))
            return newCandidate;

        return Path.GetFullPath(Path.Combine(defaultMapDir, file));
    }

    private string GetResourceRootDir()
    {
        string baseDir = AppContext.BaseDirectory;
        string? resourceDir = _startup?.ResourceDir;
        return MirResourceRootResolver.Resolve(baseDir, resourceDir);
    }

    private static string FormatLoginNoticeText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        string normalized = rawText.Replace("\r\n", "\n").Replace('\r', '\n');
        normalized = normalized.Replace('\x1B', '\n');

        string[] lines = normalized.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
            return string.Empty;

        return string.Join("\n", lines);
    }

    private void ShowLoginNoticeModal(string noticeText, CancellationToken cancellationToken)
    {
        void SendOk()
        {
            int tick = Environment.TickCount;
            AppendLog($"[notice] -> CM_LOGINNOTICEOK tick={tick} type={Grobal2.CLIENTTYPE}");
            _ = _session.SendClientMessageAsync(Grobal2.CM_LOGINNOTICEOK, tick, 0, 0, Grobal2.CLIENTTYPE, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(noticeText))
        {
            SendOk();
            return;
        }

        void Show()
        {
            if (IsDisposed || Disposing)
                return;

            if (_loginUiModal != null || _uiTextPrompt != null)
                return;

            ShowModal("公告", noticeText, UiModalButtons.Ok, _ => SendOk(), layout: UiModalLayout.Classic2);
            try { _renderControl.Focus(); } catch {  }
        }

        if (InvokeRequired)
            BeginInvoke((Action)Show);
        else
            Show();
    }

    private static Color4 ToColor4(uint argb, float alpha = 1.0f)
    {
        float a = ((argb >> 24) & 0xFF) / 255.0f * alpha;
        float r = ((argb >> 16) & 0xFF) / 255.0f;
        float g = ((argb >> 8) & 0xFF) / 255.0f;
        float b = (argb & 0xFF) / 255.0f;
        return new Color4(r, g, b, a);
    }

    private MirActionTiming.MoveTiming GetActorMoveTiming(int recogId, ActorMarker actor)
    {
        MirActionTiming.MoveTiming baseTiming = GetBaseMoveTiming(actor, actor.Action);
        if (baseTiming.Frames <= 0 || baseTiming.FrameTimeMs <= 0)
            return baseTiming;

        int frameTimeMs = ApplyMoveSpeedRate(baseTiming.FrameTimeMs);

        if (recogId > 0 &&
            _moveTimingStates.TryGetValue(recogId, out MoveTimingState state) &&
            state.LastMoveAction == actor.Action &&
            state.Frames == baseTiming.Frames &&
            state.MoveDurationMs > 0)
        {
            int totalMs = Math.Max(1, state.MoveDurationMs);
            int effectiveFrameTime = Math.Max(1, (int)Math.Round(totalMs / (double)baseTiming.Frames));
            return new MirActionTiming.MoveTiming(baseTiming.Frames, effectiveFrameTime);
        }

        return new MirActionTiming.MoveTiming(baseTiming.Frames, frameTimeMs);
    }

    private MirActionTiming.MoveTiming GetActorMoveTiming(ActorMarker actor) => GetActorMoveTiming(recogId: 0, actor);

    private static MirActionTiming.MoveTiming GetBaseMoveTiming(ActorMarker actor, ushort action)
    {
        int race = FeatureCodec.Race(actor.Feature);
        if (race == 0 || race == Grobal2.RCC_MERCHANT)
            return MirActionTiming.GetMoveTiming(action);

        int appearance = FeatureCodec.Appearance(actor.Feature);
        MonsterActions.MonsterAction actionSet = MonsterActions.GetRaceByPm(race, appearance);
        MonsterActions.ActionInfo walk = actionSet.ActWalk;
        if (walk.Frames > 0 && walk.FrameTimeMs > 0)
            return new MirActionTiming.MoveTiming(walk.Frames, walk.FrameTimeMs);

        return MirActionTiming.GetMoveTiming(action);
    }

    private int ApplyMoveSpeedRate(int frameTimeMs)
    {
        if (frameTimeMs <= 0)
            return frameTimeMs;

        if (!_world.SpeedRateEnabled)
            return frameTimeMs;

        int adjust = Math.Min(10, (int)_world.MoveSpeedRate);
        return Math.Max(1, frameTimeMs - adjust);
    }

    private void UpdateMoveTimingState(int recogId, ActorMarker actor, ushort action, long nowTimestamp)
    {
        if (recogId <= 0 || !MirDirection.IsMoveAction(action))
            return;

        MirActionTiming.MoveTiming baseTiming = GetBaseMoveTiming(actor, action);
        if (baseTiming.Frames <= 0 || baseTiming.FrameTimeMs <= 0)
            return;

        int baseFrameTimeMs = ApplyMoveSpeedRate(baseTiming.FrameTimeMs);
        int baseTotalMs = Math.Max(1, baseTiming.Frames * baseFrameTimeMs);

        if (!_moveTimingStates.TryGetValue(recogId, out MoveTimingState state))
        {
            _moveTimingStates[recogId] = new MoveTimingState(nowTimestamp, baseTotalMs, action, baseTiming.Frames);
            return;
        }

        long lastStart = state.LastMoveStartTimestamp;
        bool canMeasure = lastStart > 0 &&
            MirDirection.IsMoveAction(state.LastMoveAction) &&
            state.LastMoveAction == action &&
            state.Frames == baseTiming.Frames;

        int nextDurationMs = baseTotalMs;
        if (canMeasure)
        {
            long deltaMs = (nowTimestamp - lastStart) * 1000 / Stopwatch.Frequency;
            if (deltaMs > 0)
            {
                int minTotalMs = Math.Max(1, (int)Math.Round(baseTotalMs * 0.25f));
                int maxTotalMs = (int)Math.Round(baseTotalMs * 2.0f);

                if (deltaMs >= minTotalMs && deltaMs <= maxTotalMs)
                {
                    int measured = (int)Math.Clamp(deltaMs, minTotalMs, maxTotalMs);
                    nextDurationMs = state.MoveDurationMs <= 0
                        ? measured
                        : (int)Math.Round((state.MoveDurationMs * 0.7) + (measured * 0.3));
                }
            }
        }

        _moveTimingStates[recogId] = new MoveTimingState(nowTimestamp, nextDurationMs, action, baseTiming.Frames);
    }

    private static byte GetHumanWeaponOrder(int sex, int frame)
    {
        if ((uint)frame >= 600u)
            return 0;

        return sex == 1 ? HumanWeaponOrder1[frame] : HumanWeaponOrder0[frame];
    }

    private static readonly byte[] HumanWeaponOrder0 =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1,
        0, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 1, 1, 1, 0, 0, 1, 1, 0, 1, 1, 0, 0, 0, 1, 1, 0, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0,
        0, 1, 1, 1, 1, 0, 1, 1, 1, 1, 0, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 0,
        1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1,
        0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1
    ];

    private static readonly byte[] HumanWeaponOrder1 =
    [
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1,
        0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1,
        1, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        1, 1, 1, 1, 0, 0, 1, 1, 0, 1, 1, 0, 0, 0, 1, 1, 0, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 0, 0,
        0, 1, 1, 1, 1, 0, 1, 1, 1, 1, 0, 1, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 0, 0, 1, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 1, 0, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 0,
        1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0, 1, 1, 1, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 1, 0, 0, 1, 1,
        0, 0, 0, 1, 0, 0, 1, 1, 0, 0, 1, 0, 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 0, 0, 0, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1,
        0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1, 0, 0, 0, 1, 1, 1, 1, 1
    ];

    private readonly record struct MoveTimingState(
        long LastMoveStartTimestamp,
        int MoveDurationMs,
        ushort LastMoveAction,
        int Frames);

    private readonly record struct MagicEffDrawInfo(int Row, int X, float Rx, float Ry, string ArchivePath, int ImageIndex, int PixelOffsetX = 0, int PixelOffsetY = 0);

    private readonly record struct CorpseMarker(ActorMarker Actor, long ExpireMs);

    private readonly record struct ActorDrawInfo(
        int RecogId,
        ActorMarker Actor,
        int Row,
        int SortX,
        int Priority,
        int X,
        int Y,
        int PixelOffsetX,
        int PixelOffsetY,
        long ElapsedMs);

    private readonly record struct NameDrawInfo(string Text, float X, float Y, Color4 Color);

    private static bool AppStillIdle => !PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(
        out Msg lpMsg,
        IntPtr hWnd,
        uint wMsgFilterMin,
        uint wMsgFilterMax,
        uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsShiftKeyDown() => (GetAsyncKeyState(0x10) & 0x8000) != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
