using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Diagnostics;
using MirClient.Core.Effects;
using MirClient.Core.Util;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;
using MirClient.Protocol.Text;

namespace MirClient.Core.World;

public sealed class MirWorldState
{
    private const long LoopEffectKeepAliveMs = 3000;
    private const int MaxLoopNormalEffects = 512;
    private const int MaxLoopScreenEffects = 64;

    private readonly record struct LoopEffectKey(int Type, int X, int Y);
    private readonly record struct PendingActorInfo(
        string? UserName,
        string? DescUserName,
        byte? NameColor,
        byte? Attribute,
        float? NameOffset,
        int? Light);

    private readonly Dictionary<int, ActorMarker> _actors = new();
    private readonly Dictionary<int, PendingActorInfo> _pendingActorInfos = new();
    private readonly Dictionary<int, DropItemMarker> _dropItems = new();
    private readonly Dictionary<int, MapEventMarker> _mapEvents = new();
    private readonly Dictionary<int, bool> _doorOpenOverrides = new();
    private readonly Dictionary<int, StallActorMarker> _stalls = new();
    private readonly Dictionary<int, ClientItem> _bagItems = new();
    private readonly ClientItem[] _bagSlots = new ClientItem[Grobal2.MAXBAGITEM];
    private readonly Dictionary<int, ClientItem> _heroBagItems = new();
    private readonly ClientItem[] _heroBagSlots = new ClientItem[Grobal2.MAXBAGITEM];
    private readonly Dictionary<int, ClientItem> _useItems = new();
    private readonly Dictionary<int, ClientItem> _heroUseItems = new();
    private readonly Dictionary<byte, List<ShopItem>> _shopItemsByClass = new();
    private readonly Dictionary<(byte Class, ushort MagicId), ClientMagic> _myMagicsById = new();
    private readonly Dictionary<(byte Class, ushort MagicId), ClientMagic> _heroMagicsById = new();
    private readonly List<ClientMagic> _myMagics = new();
    private readonly List<ClientMagic> _myIpMagics = new();
    private readonly List<ClientMagic> _heroMagics = new();
    private readonly List<ClientMagic> _heroIpMagics = new();
    private readonly List<MirMerchantGoods> _merchantGoods = new();
    private readonly List<ClientItem> _merchantDetailItems = new();
    private readonly List<ClientItem> _storageItems = new();
    private readonly List<ClientItem> _dealRemoteItems = new();
    private readonly List<ClientItem> _dealMyItems = new();
    private readonly List<string> _groupMembers = new();
    private readonly List<MarketItem> _marketItems = new();
    private readonly List<string> _guildDialogLines = new();
    private readonly List<string> _guildNoticeLines = new();
    private readonly List<MirGuildMember> _guildMembers = new();
    private readonly List<MirHumanLevelRank> _humanLevelRanks = new();
    private readonly List<MirHeroLevelRank> _heroLevelRanks = new();
    private readonly MirBoxItem[] _boxItems = new MirBoxItem[9];
    private readonly Dictionary<byte, Dictionary<int, MirMission>> _missionsByClass = new();
    private readonly Dictionary<int, PendingChangeFace> _pendingChangeFacesByActorId = new();
    private readonly HashSet<int> _changingFaceActorIds = new();
    private readonly List<NormalEffect> _normalEffects = new(32);
    private readonly List<StruckEffect> _struckEffects = new(32);
    private readonly Dictionary<LoopEffectKey, LoopNormalEffect> _loopNormalEffects = new();
    private readonly Dictionary<LoopEffectKey, LoopScreenEffect> _loopScreenEffects = new();
    private readonly List<MapMagicEffect> _mapMagicEffects = new(32);
    private readonly List<MagicEffInstance> _magicEffs = new(32);
    private ClientStdItem[] _serverTitles = Array.Empty<ClientStdItem>();
    private HumTitle[] _myTitles = Array.Empty<HumTitle>();
    private HumTitle[] _heroTitles = Array.Empty<HumTitle>();
    private HeroInfo[] _heroes = Array.Empty<HeroInfo>();
    private string _selectedHeroName = string.Empty;
    private ClientSuiteItem[] _suiteItems = Array.Empty<ClientSuiteItem>();
    private byte[] _allshineBytes = Array.Empty<byte>();
    private ClientItem _detectItem;
    private int _detectItemMineId;
    private ClientItem _waitingDetectItem;
    private int _waitingDetectItemIndex;
    private bool _waitingDetectItemSet;
    private MirUserStateSnapshot? _lastUserState;

    private string _hudOverlayText = string.Empty;
    private readonly VenationInfo[] _venationInfos = new VenationInfo[4];
    private readonly VenationInfo[] _heroVenationInfos = new VenationInfo[4];
    private readonly byte[] _tempSeriesSkillArr = new byte[4];
    private readonly byte[] _heroTempSeriesSkillArr = new byte[4];
    private readonly byte[] _seriesSkillArr = new byte[4];

    public IReadOnlyDictionary<int, ActorMarker> Actors => _actors;
    public IReadOnlyDictionary<int, DropItemMarker> DropItems => _dropItems;
    public IReadOnlyDictionary<int, MapEventMarker> MapEvents => _mapEvents;
    public IReadOnlyDictionary<int, bool> DoorOpenOverrides => _doorOpenOverrides;
    public IReadOnlyDictionary<int, StallActorMarker> Stalls => _stalls;
    public IReadOnlyDictionary<int, ClientItem> BagItems => _bagItems;
    public ReadOnlySpan<ClientItem> BagSlots => _bagSlots;
    public IReadOnlyDictionary<int, ClientItem> HeroBagItems => _heroBagItems;
    public ReadOnlySpan<ClientItem> HeroBagSlots => _heroBagSlots;
    public IReadOnlyDictionary<int, ClientItem> UseItems => _useItems;
    public IReadOnlyDictionary<int, ClientItem> HeroUseItems => _heroUseItems;
    public IReadOnlyDictionary<byte, List<ShopItem>> ShopItemsByClass => _shopItemsByClass;
    public IReadOnlyDictionary<(byte Class, ushort MagicId), ClientMagic> MyMagicsById => _myMagicsById;
    public IReadOnlyDictionary<(byte Class, ushort MagicId), ClientMagic> HeroMagicsById => _heroMagicsById;
    public IReadOnlyList<ClientMagic> MyMagics => _myMagics;
    public IReadOnlyList<ClientMagic> MyIpMagics => _myIpMagics;
    public IReadOnlyList<ClientMagic> HeroMagics => _heroMagics;
    public IReadOnlyList<ClientMagic> HeroIpMagics => _heroIpMagics;
    public IReadOnlyList<MirMerchantGoods> MerchantGoods => _merchantGoods;
    public IReadOnlyList<ClientItem> MerchantDetailItems => _merchantDetailItems;
    public IReadOnlyList<ClientItem> StorageItems => _storageItems;
    public IReadOnlyList<ClientItem> DealRemoteItems => _dealRemoteItems;
    public IReadOnlyList<ClientItem> DealMyItems => _dealMyItems;
    public IReadOnlyList<string> GroupMembers => _groupMembers;
    public IReadOnlyList<MarketItem> MarketItems => _marketItems;
    public IReadOnlyList<string> GuildDialogLines => _guildDialogLines;
    public IReadOnlyList<string> GuildNoticeLines => _guildNoticeLines;
    public IReadOnlyList<MirGuildMember> GuildMembers => _guildMembers;
    public IReadOnlyList<MirHumanLevelRank> HumanLevelRanks => _humanLevelRanks;
    public IReadOnlyList<MirHeroLevelRank> HeroLevelRanks => _heroLevelRanks;
    public IReadOnlyList<MirBoxItem> BoxItems => _boxItems;
    public IReadOnlyList<NormalEffect> NormalEffects => _normalEffects;
    public IReadOnlyList<StruckEffect> StruckEffects => _struckEffects;
    public IReadOnlyCollection<LoopNormalEffect> LoopNormalEffects => _loopNormalEffects.Values;
    public IReadOnlyCollection<LoopScreenEffect> LoopScreenEffects => _loopScreenEffects.Values;
    public IReadOnlyList<MapMagicEffect> MapMagicEffects => _mapMagicEffects;
    public IReadOnlyList<MagicEffInstance> MagicEffs => _magicEffs;
    public ReadOnlySpan<ClientStdItem> ServerTitles => _serverTitles;
    public ReadOnlySpan<HumTitle> MyTitles => _myTitles;
    public ReadOnlySpan<HumTitle> HeroTitles => _heroTitles;
    public ReadOnlySpan<HeroInfo> Heroes => _heroes;
    public string SelectedHeroName => _selectedHeroName;
    public ReadOnlySpan<ClientSuiteItem> SuiteItems => _suiteItems;
    public ReadOnlyMemory<byte> AllshineBytes => _allshineBytes;
    public ClientItem DetectItem => _detectItem;
    public bool DetectItemSet => _detectItem.MakeIndex != 0;
    public int DetectItemMineId => _detectItemMineId;

    public bool MapMoving { get; private set; }
    public int MapCenterX { get; private set; }
    public int MapCenterY { get; private set; }
    public bool MapCenterSet { get; private set; }
    public string MapTitle { get; private set; } = string.Empty;
    public int MapMusicId { get; private set; } = -1;
    public int DayBright { get; private set; }
    public int DarkLevel { get; private set; }
    public bool ViewFog { get; private set; }
    public bool MiniMapVisible { get; private set; }
    public int MiniMapIndex { get; private set; } = -1;

    public string MyGuildName { get; private set; } = string.Empty;
    public string MyGuildRankName { get; private set; } = string.Empty;
    public string GuildDialogName { get; private set; } = string.Empty;
    public string GuildDialogFlag { get; private set; } = string.Empty;
    public bool GuildCommanderMode { get; private set; }

    public int LevelRankType { get; private set; }
    public int LevelRankPage { get; private set; }
    public bool LevelRankHasData { get; private set; }

    public bool BoxOpen { get; private set; }
    public int BoxParam { get; private set; }
    public int BoxServerItemIndex { get; private set; }
    public int BoxNameMaxLen { get; private set; } = Grobal2.ItemNameLen;

    public bool BookOpen { get; private set; }
    public int BookMerchantId { get; private set; }
    public int BookPath { get; private set; }
    public int BookPage { get; private set; }
    public string BookLabel { get; private set; } = string.Empty;

    public bool RefineOpen { get; private set; }

    public MirUserStateSnapshot? LastUserState => _lastUserState;

    public string GameGoldName { get; private set; } = string.Empty;
    public string GamePointName { get; private set; } = string.Empty;
    public int GameGold { get; private set; }
    public int GamePoint { get; private set; }

    public int ShopSellType { get; private set; }

    public MirMerchantMode MerchantMode { get; private set; }
    public int CurrentMerchantId { get; private set; }
    public int MerchantDialogX { get; private set; }
    public int MerchantDialogY { get; private set; }
    public int MerchantDialogFace { get; private set; }
    public string MerchantNpcName { get; private set; } = string.Empty;
    public string MerchantSaying { get; private set; } = string.Empty;
    public bool MerchantDialogOpen { get; private set; }
    public int MerchantMenuTopLine { get; private set; }
    public int LastSellPriceQuote { get; private set; }
    public int LastBookCountQuote { get; private set; }
    public int LastRepairCostQuote { get; private set; }
    public int LastSoldOutGoodsId { get; private set; }

    public bool AllowGroup { get; private set; }

    public bool DealOpen { get; private set; }
    public string DealWho { get; private set; } = string.Empty;
    public int DealMyGold { get; private set; }
    public int DealRemoteGold { get; private set; }

    public bool IsChangingFace(int recogId) => recogId != 0 && _changingFaceActorIds.Contains(recogId);

    public int HeroBagSize { get; private set; }
    public int HeroEnergyType { get; private set; }
    public int HeroEnergy { get; private set; }
    public int HeroMaxEnergy { get; private set; }
    public bool HeroActorIdSet { get; private set; }
    public int HeroActorId { get; private set; }
    public bool HeroAbilitySet { get; private set; }
    public int HeroGold { get; private set; }
    public byte HeroJob { get; private set; }
    public byte HeroIPowerLevel { get; private set; }
    public ushort HeroGloryPoint { get; private set; }
    public Ability HeroAbility { get; private set; }
    public int HeroIPowerExp { get; private set; }
    public int HeroNimbusExp { get; private set; }
    public byte HeroHitPoint { get; private set; }
    public byte HeroSpeedPoint { get; private set; }
    public byte HeroAntiPoison { get; private set; }
    public byte HeroPoisonRecover { get; private set; }
    public byte HeroHealthRecover { get; private set; }
    public byte HeroSpellRecover { get; private set; }
    public byte HeroAntiMagic { get; private set; }
    public byte HeroIPowerRecover { get; private set; }
    public byte HeroAddDamage { get; private set; }
    public byte HeroDecDamage { get; private set; }
    public string HeroLoyalty { get; private set; } = string.Empty;

    public bool MyselfRecogIdSet { get; private set; }
    public int MyselfRecogId { get; private set; }

    public bool AbilitySet { get; private set; }
    public Ability MyAbility { get; private set; }
    public int MyGold { get; private set; }
    public byte MyJob { get; private set; }
    public byte MyIPowerLevel { get; private set; }
    public uint MyGameGold { get; private set; }
    public int MyGameDiamond { get; private set; }
    public int MyGameGird { get; private set; }
    public int MyLevel { get; private set; }
    public int MyHp { get; private set; }
    public int MyMaxHp { get; private set; }
    public int MyMp { get; private set; }
    public int MyMaxMp { get; private set; }
    public int MyExp { get; private set; }
    public int MyMaxExp { get; private set; }
    public int MyIPowerExp { get; private set; }
    public int MyNimbusExp { get; private set; }
    public int MagicRange { get; private set; }
    public int MyWeight { get; private set; }
    public int MyMaxWeight { get; private set; }
    public int MyWearWeight { get; private set; }
    public int MyMaxWearWeight { get; private set; }
    public int MyHandWeight { get; private set; }
    public int MyMaxHandWeight { get; private set; }
    public byte MyHitPoint { get; private set; }
    public byte MySpeedPoint { get; private set; }
    public byte MyAntiPoison { get; private set; }
    public byte MyPoisonRecover { get; private set; }
    public byte MyHealthRecover { get; private set; }
    public byte MySpellRecover { get; private set; }
    public byte MyAntiMagic { get; private set; }
    public byte MyIPowerRecover { get; private set; }
    public byte MyAddDamage { get; private set; }
    public byte MyDecDamage { get; private set; }

    public int MarketUserMode { get; private set; }
    public int MarketItemType { get; private set; }
    public int MarketCurrentPage { get; private set; }
    public int MarketMaxPage { get; private set; }

    public string HudOverlayText => _hudOverlayText;

    public uint ServerTime { get; private set; }
    public byte AttackMode { get; private set; }
    public string AttackModeLabel { get; private set; } = string.Empty;
    public bool OpenAutoPlay { get; private set; }
    public bool ActionLock { get; private set; }
    public bool MoveBusy { get; private set; }
    public int MoveErr { get; private set; }

    public ClientConf ClientConfig { get; private set; }
    public bool ClientConfigSet { get; private set; }
    public bool SpeedRateEnabled { get; private set; }
    public bool SpeedRateShow { get; private set; }
    public byte HitSpeedRate { get; private set; }
    public byte MagSpeedRate { get; private set; }
    public byte MoveSpeedRate { get; private set; }
    public bool CanRunHuman { get; private set; }
    public bool CanRunMon { get; private set; }
    public bool CanRunNpc { get; private set; }
    public bool CanRunAllInWarZone { get; private set; }
    public bool CanRunSafeZone { get; private set; }
    public int AreaStateValue { get; private set; }

    public bool AutoSay { get; private set; }
    public bool MultiHero { get; private set; }
    public bool Skill114Mp { get; private set; }
    public bool Skill68Mp { get; private set; }
    public int EatItemInvTime { get; private set; }
    public bool ForceNotViewFog { get; private set; }
    public bool OpenStallSystem { get; private set; }
    public bool AutoLongAttack { get; private set; }
    public bool HeroEnabled { get; private set; }

    public int BonusPoint { get; private set; }
    public NakedAbility BonusTick { get; private set; }
    public NakedAbility BonusAbility { get; private set; }
    public NakedAbility BaseNakedAbility { get; private set; }

    public bool ShowFashion { get; private set; }
    public bool HeroShowFashion { get; private set; }
    public byte MyLuck { get; private set; }
    public byte MyEnergy { get; private set; }
    public int MyHungryState { get; private set; }

    public bool MyDigFragment { get; private set; }
    public bool MyNextPowerHit { get; private set; }
    public bool MyCanLongHit { get; private set; }
    public bool MyCanWideHit { get; private set; }
    public bool MyCanStnHit { get; private set; }
    public bool MyCanCrsHit { get; private set; }
    public bool MyNextTwinHit { get; private set; }
    public long MyLatestTwinHitTickMs { get; private set; }
    public bool MyCanSquHit { get; private set; }
    public int MySquHitPoint { get; private set; }
    public int MyMaxSquHitPoint { get; private set; }
    public bool MyNextFireHit { get; private set; }
    public long MyLatestFireHitTickMs { get; private set; }
    public bool MyNextPursueHit { get; private set; }
    public long MyLatestPursueHitTickMs { get; private set; }
    public bool MyNextRushHit { get; private set; }
    public long MyLatestRushHitTickMs { get; private set; }
    public bool MyNextSmiteHit { get; private set; }
    public long MyLatestSmiteHitTickMs { get; private set; }
    public bool MyNextSmiteLongHit { get; private set; }
    public long MyLatestSmiteLongHitTickMs { get; private set; }
    public bool MyNextSmiteLongHit2 { get; private set; }
    public long MyLatestSmiteLongHitTickMs2 { get; private set; }
    public bool MyNextSmiteLongHit3 { get; private set; }
    public long MyLatestSmiteLongHitTickMs3 { get; private set; }
    public bool MyNextSmiteWideHit { get; private set; }
    public long MyLatestSmiteWideHitTickMs { get; private set; }
    public bool MyNextSmiteWideHit2 { get; private set; }
    public long MyLatestSmiteWideHitTickMs2 { get; private set; }
    public bool MyCanSLonHit { get; private set; }
    public long MyLatestSLonHitTickMs { get; private set; }

    public bool UserStallOpen { get; private set; }
    public int UserStallActorId { get; private set; }
    public string UserStallName { get; private set; } = string.Empty;
    public int UserStallItemCount { get; private set; }
    public ReadOnlySpan<ClientItem> UserStallItems => _userStallItems;

    public byte CollectExpLevel { get; private set; }
    public uint CollectExp { get; private set; }
    public uint CollectExpMax { get; private set; } = 1;
    public uint CollectIpExp { get; private set; }
    public uint CollectIpExpMax { get; private set; } = 1;
    public int CollectExpShineCount { get; private set; }

    public bool QueryValuePending { get; private set; }
    public QueryValueRequest QueryValue { get; private set; }

    private readonly ClientItem[] _userStallItems = new ClientItem[ClientStallInfo.MaxStallItemCount];

    public ReadOnlySpan<VenationInfo> VenationInfos => _venationInfos;
    public ReadOnlySpan<VenationInfo> HeroVenationInfos => _heroVenationInfos;
    public ReadOnlySpan<byte> TempSeriesSkillArr => _tempSeriesSkillArr;
    public ReadOnlySpan<byte> HeroTempSeriesSkillArr => _heroTempSeriesSkillArr;
    public ReadOnlySpan<byte> SeriesSkillArr => _seriesSkillArr;
    public bool SeriesSkillReady { get; private set; }
    public ushort SeriesSkillStep { get; private set; }

    public bool NewMissionPending { get; private set; }

    private readonly record struct PendingChangeFace(int NewRecogId, int Feature, int Status, long RequestedAtTimestamp);

    public void ResetForReconnect()
    {
        _actors.Clear();
        _pendingActorInfos.Clear();
        _dropItems.Clear();
        _mapEvents.Clear();
        _doorOpenOverrides.Clear();
        _stalls.Clear();
        _bagItems.Clear();
        _bagSlots.AsSpan().Clear();
        _heroBagItems.Clear();
        _heroBagSlots.AsSpan().Clear();
        _useItems.Clear();
        _heroUseItems.Clear();
        _shopItemsByClass.Clear();
        _myMagicsById.Clear();
        _heroMagicsById.Clear();
        _myMagics.Clear();
        _myIpMagics.Clear();
        _heroMagics.Clear();
        _heroIpMagics.Clear();
        _merchantGoods.Clear();
        _merchantDetailItems.Clear();
        _storageItems.Clear();
        _dealRemoteItems.Clear();
        _dealMyItems.Clear();
        _groupMembers.Clear();
        _marketItems.Clear();
        _guildDialogLines.Clear();
        _guildNoticeLines.Clear();
        _guildMembers.Clear();
        _humanLevelRanks.Clear();
        _heroLevelRanks.Clear();
        _missionsByClass.Clear();
        _pendingChangeFacesByActorId.Clear();
        _changingFaceActorIds.Clear();
        _normalEffects.Clear();
        _struckEffects.Clear();
        _mapMagicEffects.Clear();
        _magicEffs.Clear();
        _serverTitles = Array.Empty<ClientStdItem>();
        _myTitles = Array.Empty<HumTitle>();
        _heroTitles = Array.Empty<HumTitle>();
        _heroes = Array.Empty<HeroInfo>();
        _selectedHeroName = string.Empty;
        _suiteItems = Array.Empty<ClientSuiteItem>();
        _allshineBytes = Array.Empty<byte>();
        _detectItem = default;
        _detectItemMineId = 0;
        _waitingDetectItem = default;
        _waitingDetectItemIndex = 0;
        _waitingDetectItemSet = false;
        _lastUserState = null;

        UserStallOpen = false;
        UserStallActorId = 0;
        UserStallName = string.Empty;
        UserStallItemCount = 0;
        _userStallItems.AsSpan().Clear();

        CollectExpLevel = 0;
        CollectExp = 0;
        CollectExpMax = 1;
        CollectIpExp = 0;
        CollectIpExpMax = 1;
        CollectExpShineCount = 0;

        QueryValuePending = false;
        QueryValue = default;

        _venationInfos.AsSpan().Clear();
        _heroVenationInfos.AsSpan().Clear();
        _tempSeriesSkillArr.AsSpan().Clear();
        _heroTempSeriesSkillArr.AsSpan().Clear();
        _seriesSkillArr.AsSpan().Clear();
        SeriesSkillReady = false;
        SeriesSkillStep = 0;
        NewMissionPending = false;

        MyselfRecogId = 0;
        MyselfRecogIdSet = false;

        AbilitySet = false;
        MyAbility = default;
        MyGold = 0;
        MyJob = 0;
        MyIPowerLevel = 0;
        MyGameGold = 0;
        MyGameDiamond = 0;
        MyGameGird = 0;
        MyLevel = 0;
        MyHp = 0;
        MyMaxHp = 0;
        MyMp = 0;
        MyMaxMp = 0;
        MyExp = 0;
        MyMaxExp = 0;
        MyIPowerExp = 0;
        MyNimbusExp = 0;
        MagicRange = 0;
        MyWeight = 0;
        MyMaxWeight = 0;
        MyWearWeight = 0;
        MyMaxWearWeight = 0;
        MyHandWeight = 0;
        MyMaxHandWeight = 0;
        MyHitPoint = 0;
        MySpeedPoint = 0;
        MyAntiPoison = 0;
        MyPoisonRecover = 0;
        MyHealthRecover = 0;
        MySpellRecover = 0;
        MyAntiMagic = 0;
        MyIPowerRecover = 0;
        MyAddDamage = 0;
        MyDecDamage = 0;

        MarketUserMode = 0;
        MarketItemType = 0;
        MarketCurrentPage = 0;
        MarketMaxPage = 0;
        _hudOverlayText = string.Empty;

        MapMoving = true;
        MapCenterX = 0;
        MapCenterY = 0;
        MapCenterSet = false;
        MapTitle = string.Empty;
        MapMusicId = -1;
        DayBright = 0;
        DarkLevel = 0;
        ViewFog = false;
        MiniMapVisible = false;
        MiniMapIndex = -1;

        GameGoldName = string.Empty;
        GamePointName = string.Empty;
        GameGold = 0;
        GamePoint = 0;

        ShopSellType = 0;

        MerchantMode = MirMerchantMode.None;
        CurrentMerchantId = 0;
        MerchantDialogX = 0;
        MerchantDialogY = 0;
        MerchantDialogFace = 0;
        MerchantNpcName = string.Empty;
        MerchantSaying = string.Empty;
        MerchantDialogOpen = false;
        MerchantMenuTopLine = 0;
        LastSellPriceQuote = 0;
        LastBookCountQuote = 0;
        LastRepairCostQuote = 0;
        LastSoldOutGoodsId = 0;

        AllowGroup = false;
        MyGuildName = string.Empty;
        MyGuildRankName = string.Empty;
        GuildDialogName = string.Empty;
        GuildDialogFlag = string.Empty;
        GuildCommanderMode = false;
        LevelRankType = 0;
        LevelRankPage = 0;
        LevelRankHasData = false;
        BoxOpen = false;
        BoxParam = 0;
        BoxServerItemIndex = 0;
        BoxNameMaxLen = Grobal2.ItemNameLen;
        Array.Clear(_boxItems, 0, _boxItems.Length);
        BookOpen = false;
        BookMerchantId = 0;
        BookPath = 0;
        BookPage = 0;
        BookLabel = string.Empty;
        RefineOpen = false;

        DealOpen = false;
        DealWho = string.Empty;
        DealMyGold = 0;
        DealRemoteGold = 0;

        HeroBagSize = 0;
        HeroEnergyType = 0;
        HeroEnergy = 0;
        HeroMaxEnergy = 0;
        HeroActorIdSet = false;
        HeroActorId = 0;
        HeroAbilitySet = false;
        HeroGold = 0;
        HeroJob = 0;
        HeroIPowerLevel = 0;
        HeroGloryPoint = 0;
        HeroAbility = default;
        HeroIPowerExp = 0;
        HeroNimbusExp = 0;
        HeroHitPoint = 0;
        HeroSpeedPoint = 0;
        HeroAntiPoison = 0;
        HeroPoisonRecover = 0;
        HeroHealthRecover = 0;
        HeroSpellRecover = 0;
        HeroAntiMagic = 0;
        HeroIPowerRecover = 0;
        HeroAddDamage = 0;
        HeroDecDamage = 0;
        HeroLoyalty = string.Empty;

        ServerTime = 0;
        AttackMode = 0;
        AttackModeLabel = string.Empty;
        OpenAutoPlay = false;
        ActionLock = false;
        MoveBusy = false;
        MoveErr = 0;
        ClientConfig = default;
        ClientConfigSet = false;
        SpeedRateEnabled = false;
        SpeedRateShow = false;
        HitSpeedRate = 0;
        MagSpeedRate = 0;
        MoveSpeedRate = 0;
        CanRunHuman = false;
        CanRunMon = false;
        CanRunNpc = false;
        CanRunAllInWarZone = false;
        CanRunSafeZone = false;
        AreaStateValue = 0;
        AutoSay = false;
        MultiHero = false;
        Skill114Mp = false;
        Skill68Mp = false;
        EatItemInvTime = 0;
        ForceNotViewFog = false;
        OpenStallSystem = false;
        AutoLongAttack = false;
        HeroEnabled = false;
        BonusPoint = 0;
        BonusTick = default;
        BonusAbility = default;
        BaseNakedAbility = default;
        ShowFashion = false;
        HeroShowFashion = false;
        MyLuck = 0;
        MyEnergy = 0;
        MyHungryState = 0;
        MyDigFragment = false;
        MyNextPowerHit = false;
        MyCanLongHit = false;
        MyCanWideHit = false;
        MyCanStnHit = false;
        MyCanCrsHit = false;
        MyNextTwinHit = false;
        MyLatestTwinHitTickMs = 0;
        MyCanSquHit = false;
        MySquHitPoint = 0;
        MyMaxSquHitPoint = 0;
        MyNextFireHit = false;
        MyLatestFireHitTickMs = 0;
        MyNextPursueHit = false;
        MyLatestPursueHitTickMs = 0;
        MyNextRushHit = false;
        MyLatestRushHitTickMs = 0;
        MyNextSmiteHit = false;
        MyLatestSmiteHitTickMs = 0;
        MyNextSmiteLongHit = false;
        MyLatestSmiteLongHitTickMs = 0;
        MyNextSmiteLongHit2 = false;
        MyLatestSmiteLongHitTickMs2 = 0;
        MyNextSmiteLongHit3 = false;
        MyLatestSmiteLongHitTickMs3 = 0;
        MyNextSmiteWideHit = false;
        MyLatestSmiteWideHitTickMs = 0;
        MyNextSmiteWideHit2 = false;
        MyLatestSmiteWideHitTickMs2 = 0;
        MyCanSLonHit = false;
        MyLatestSLonHitTickMs = 0;
    }

    public void Tick(long nowTimestamp, long nowMs)
    {
        TickInstanceHealthGauge(nowMs);
        TickChangeFace(nowTimestamp);
        TickWeaponEffect(nowMs);
        TickMagic(nowMs);
        TickMagicEffs(nowMs);
        TickMapMagicEffects(nowMs);
        TickNormalEffects(nowMs);
        TickLoopEffects(nowMs);
        TickStruckEffects(nowMs);
    }

    public void UpsertLoopNormalEffect(int type, int x, int y, long nowMs)
    {
        if (type == 0)
            return;

        LoopEffectKey key = new(type, x, y);
        if (_loopNormalEffects.TryGetValue(key, out LoopNormalEffect existing))
        {
            _loopNormalEffects[key] = existing with { LastSeenMs = nowMs };
            return;
        }

        _loopNormalEffects.Add(key, new LoopNormalEffect(type, x, y, StartMs: nowMs, LastSeenMs: nowMs));
    }

    public void UpsertLoopScreenEffect(int type, int x, int y, long nowMs)
    {
        if (type == 0)
            return;

        LoopEffectKey key = new(type, x, y);
        if (_loopScreenEffects.TryGetValue(key, out LoopScreenEffect existing))
        {
            _loopScreenEffects[key] = existing with { LastSeenMs = nowMs };
            return;
        }

        _loopScreenEffects.Add(key, new LoopScreenEffect(type, x, y, StartMs: nowMs, LastSeenMs: nowMs));
    }

    private void TickLoopEffects(long nowMs)
    {
        TrimLoopEffects(_loopNormalEffects, MaxLoopNormalEffects, nowMs);
        TrimLoopEffects(_loopScreenEffects, MaxLoopScreenEffects, nowMs);
    }

    private static void TrimLoopEffects<T>(Dictionary<LoopEffectKey, T> effects, int maxCount, long nowMs) where T : struct
    {
        if (effects.Count == 0)
            return;

        List<LoopEffectKey>? expired = null;
        foreach ((LoopEffectKey key, T effect) in effects)
        {
            long lastSeenMs = effect switch
            {
                LoopNormalEffect e => e.LastSeenMs,
                LoopScreenEffect e => e.LastSeenMs,
                _ => 0
            };

            if (lastSeenMs > 0 && nowMs - lastSeenMs > LoopEffectKeepAliveMs)
                (expired ??= new List<LoopEffectKey>(8)).Add(key);
        }

        if (expired != null)
        {
            foreach (LoopEffectKey key in expired)
                effects.Remove(key);
        }

        if (effects.Count <= maxCount)
            return;

        List<KeyValuePair<LoopEffectKey, T>> list = new(effects.Count);
        foreach ((LoopEffectKey key, T effect) in effects)
            list.Add(new KeyValuePair<LoopEffectKey, T>(key, effect));

        list.Sort(static (a, b) =>
        {
            long aLast = a.Value switch
            {
                LoopNormalEffect e => e.LastSeenMs,
                LoopScreenEffect e => e.LastSeenMs,
                _ => 0
            };
            long bLast = b.Value switch
            {
                LoopNormalEffect e => e.LastSeenMs,
                LoopScreenEffect e => e.LastSeenMs,
                _ => 0
            };
            return aLast.CompareTo(bLast);
        });

        int removeCount = list.Count - maxCount;
        for (int i = 0; i < removeCount; i++)
            effects.Remove(list[i].Key);
    }

    private void TickInstanceHealthGauge(long nowMs)
    {
        if (_actors.Count == 0)
            return;

        List<(int ActorId, ActorMarker Actor)>? updates = null;

        foreach ((int actorId, ActorMarker actor) in _actors)
        {
            if (!actor.InstanceOpenHealth)
                continue;

            int durationMs = actor.OpenHealthDurationMs;
            if (durationMs <= 0)
                continue;

            if (nowMs - actor.OpenHealthStartMs > durationMs)
                (updates ??= new List<(int, ActorMarker)>()).Add((actorId, actor));
        }

        if (updates == null)
            return;

        foreach ((int actorId, ActorMarker actor) in updates)
            _actors[actorId] = actor with { InstanceOpenHealth = false };
    }

    private void TickChangeFace(long nowTimestamp)
    {
        if (_pendingChangeFacesByActorId.Count == 0)
            return;

        List<int>? ready = null;

        foreach ((int actorId, PendingChangeFace pending) in _pendingChangeFacesByActorId)
        {
            if (!_actors.TryGetValue(actorId, out ActorMarker actor))
            {
                (ready ??= new List<int>(4)).Add(actorId);
                continue;
            }

            if (IsActorIdle(actor, nowTimestamp))
                (ready ??= new List<int>(4)).Add(actorId);
        }

        if (ready == null)
            return;

        foreach (int actorId in ready)
        {
            if (!_pendingChangeFacesByActorId.Remove(actorId, out PendingChangeFace pending))
                continue;

            _changingFaceActorIds.Remove(pending.NewRecogId);

            if (!_actors.TryGetValue(actorId, out ActorMarker actor))
                continue;

            ApplyChangeFaceNow(actorId, pending.NewRecogId, pending.Feature, pending.Status, nowTimestamp, actor);
        }
    }

    private void TickWeaponEffect(long nowMs)
    {
        if (_actors.Count == 0)
            return;

        const long FrameMs = 120;
        const int MaxFrames = 5;
        const long DurationMs = FrameMs * MaxFrames;

        List<(int ActorId, ActorMarker Actor)>? updates = null;

        foreach ((int actorId, ActorMarker actor) in _actors)
        {
            if (!actor.WeaponEffect)
                continue;

            if (actor.WeaponEffectStartMs <= 0)
                (updates ??= new List<(int, ActorMarker)>()).Add((actorId, actor));
            else if (nowMs - actor.WeaponEffectStartMs >= DurationMs)
                (updates ??= new List<(int, ActorMarker)>()).Add((actorId, actor));
        }

        if (updates == null)
            return;

        foreach ((int actorId, ActorMarker actor) in updates)
            _actors[actorId] = actor with { WeaponEffect = false, WeaponEffectStartMs = 0 };
    }

    private void TickMagic(long nowMs)
    {
        if (_actors.Count == 0)
            return;

        List<(int ActorId, ActorMarker Actor)>? updates = null;
        List<(byte EffectNumber, byte EffectType, int X, int Y)>? mapMagicSpawns = null;
        List<(byte EffectNumber, byte EffectType, int FromX, int FromY, int ToX, int ToY, int TargetActorId, int MagicLevel)>? magicEffSpawns = null;

        foreach ((int actorId, ActorMarker actor) in _actors)
        {
            ActorMarker current = actor;

            if (current.Action != Grobal2.SM_SPELL)
            {
                if (current.MagicServerCode != 0 || current.MagicWaitStartMs != 0 || current.MagicAnimStartMs != 0 || current.MagicHold)
                {
                    (updates ??= new List<(int, ActorMarker)>()).Add((
                        actorId,
                        current with { MagicServerCode = 0, MagicWaitStartMs = 0, MagicAnimStartMs = 0, MagicHold = false }));
                }
                continue;
            }

            long requestStartMs = current.MagicWaitStartMs;
            long animStartMs = current.MagicAnimStartMs;
            if (requestStartMs <= 0 || animStartMs <= 0)
            {
                if (requestStartMs <= 0)
                    requestStartMs = nowMs;
                if (animStartMs <= 0)
                    animStartMs = requestStartMs;

                current = current with { MagicWaitStartMs = requestStartMs, MagicAnimStartMs = animStartMs, MagicHold = false };
                (updates ??= new List<(int, ActorMarker)>()).Add((actorId, current));
            }

            int effectNumber = current.MagicEffectNumber;
            int frames = GetSpellEffectFrames(effectNumber);
            int frameTimeMs = GetSpellEffectFrameTimeMs(effectNumber);
            if (frames <= 0 || frameTimeMs <= 0)
            {
                if (current.MagicServerCode != 0 || current.MagicWaitStartMs != 0 || current.MagicAnimStartMs != 0 || current.MagicHold)
                {
                    (updates ??= new List<(int, ActorMarker)>()).Add((
                        actorId,
                        current with { MagicServerCode = 0, MagicWaitStartMs = 0, MagicAnimStartMs = 0, MagicHold = false }));
                }
                continue;
            }

            long requestElapsedMs = nowMs - current.MagicWaitStartMs;
            if (requestElapsedMs < 0)
                requestElapsedMs = 0;

            long animElapsedMs = nowMs - current.MagicAnimStartMs;
            if (animElapsedMs < 0)
                animElapsedMs = 0;

            long timeoutMs = current.IsMyself ? 1800 : 900;
            long fireAtMs = (long)(frames - 1) * frameTimeMs;
            long endAtMs = (long)frames * frameTimeMs;

            if (current.MagicServerCode < 0)
            {
                if (requestElapsedMs > timeoutMs)
                {
                    long newAnimStartMs = current.MagicAnimStartMs;
                    if (fireAtMs > 0)
                        newAnimStartMs = nowMs - fireAtMs;

                    (updates ??= new List<(int, ActorMarker)>()).Add((
                        actorId,
                        current with { MagicServerCode = 0, MagicAnimStartMs = newAnimStartMs, MagicHold = false }));
                    continue;
                }

                if (fireAtMs > 0 && animElapsedMs >= fireAtMs)
                {
                    long holdAtMs = fireAtMs - 1;
                    if (holdAtMs < 0)
                        holdAtMs = 0;

                    long newAnimStartMs = nowMs - holdAtMs;
                    if (newAnimStartMs != current.MagicAnimStartMs || !current.MagicHold)
                    {
                        (updates ??= new List<(int, ActorMarker)>()).Add((
                            actorId,
                            current with { MagicAnimStartMs = newAnimStartMs, MagicHold = true }));
                    }
                }

                continue;
            }

            bool changed = false;

            if (current.MagicHold)
            {
                if (fireAtMs > 0 && animElapsedMs < fireAtMs)
                {
                    current = current with { MagicAnimStartMs = nowMs - fireAtMs };
                    animElapsedMs = fireAtMs;
                    changed = true;
                }

                current = current with { MagicHold = false };
                changed = true;
            }

            if (current.MagicServerCode > 0 && fireAtMs > 0 && animElapsedMs >= fireAtMs)
            {
                if ((uint)effectNumber <= byte.MaxValue && (uint)current.MagicEffectType <= byte.MaxValue)
                {
                    byte magicType = (byte)current.MagicEffectType;
                    if (MagicEffTimeline.Get(magicType).HasFlight)
                    {
                        (magicEffSpawns ??= new List<(byte, byte, int, int, int, int, int, int)>(8)).Add(((byte)effectNumber, magicType, current.X, current.Y, current.MagicTargetX, current.MagicTargetY, current.MagicTarget, current.MagicFireLevel));
                    }
                    else
                    {
                        (mapMagicSpawns ??= new List<(byte, byte, int, int)>(8)).Add(((byte)effectNumber, magicType, current.MagicTargetX, current.MagicTargetY));
                    }
                }

                current = current with { MagicServerCode = 0 };
                changed = true;
            }

            if (current.MagicServerCode == 0 && endAtMs > 0 && animElapsedMs >= endAtMs)
            {
                current = current with { MagicWaitStartMs = 0, MagicAnimStartMs = 0, MagicHold = false };
                changed = true;
            }

            if (changed)
                (updates ??= new List<(int, ActorMarker)>()).Add((actorId, current));
        }

        if (mapMagicSpawns != null)
        {
            foreach ((byte effectNumber, byte effectType, int x, int y) in mapMagicSpawns)
                AddMapMagicEffect(effectNumber, effectType, x, y, nowMs);
        }

        if (magicEffSpawns != null)
        {
            foreach ((byte effectNumber, byte effectType, int fromX, int fromY, int toX, int toY, int targetActorId, int magicLevel) in magicEffSpawns)
                AddMagicEff(effectNumber, effectType, fromX, fromY, toX, toY, targetActorId, magicLevel, nowMs);
        }

        if (updates == null)
            return;

        foreach ((int actorId, ActorMarker actor) in updates)
            _actors[actorId] = actor;
    }

    private void TickMagicEffs(long nowMs)
    {
        if (_magicEffs.Count == 0)
            return;

        for (int i = _magicEffs.Count - 1; i >= 0; i--)
        {
            MagicEffInstance effect = _magicEffs[i];
            if (effect.EffectNumber == 0)
            {
                _magicEffs.RemoveAt(i);
                continue;
            }

            long startMs = effect.StartMs;
            if (startMs <= 0)
                startMs = nowMs;

            long elapsed = nowMs - startMs;
            if (elapsed < 0)
                elapsed = 0;

            int travelDurationMs = effect.TravelDurationMs;
            if (travelDurationMs < 0)
                travelDurationMs = 0;

            int explosionFrames = 0;
            int explosionFrameTimeMs = 0;
            if (!MagicEffExplosionAtlas.TryGetInfo(effect.EffectNumber, effect.EffectType, effect.MagicLevel, out _, out _, out explosionFrames, out explosionFrameTimeMs) ||
                explosionFrames <= 0 ||
                explosionFrameTimeMs <= 0)
            {
                MagicEffTimelineInfo fallback = MagicEffTimeline.Get(effect.EffectType);
                explosionFrames = fallback.ExplosionFrames;
                explosionFrameTimeMs = fallback.FrameTimeMs;
            }

            long explosionDurationMs = (long)explosionFrames * explosionFrameTimeMs;
            if (explosionDurationMs < 0)
                explosionDurationMs = 0;

            long totalDurationMs = travelDurationMs + explosionDurationMs;
            if (totalDurationMs > 0 && elapsed >= totalDurationMs)
                _magicEffs.RemoveAt(i);
        }
    }

    private static int GetSpellEffectFrames(int effectNumber)
    {
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

    private static int GetSpellEffectFrameTimeMs(int effectNumber)
    {
        (_, _, _, int frameTimeMs, _) = MirActionTiming.GetHumanActionInfo(Grobal2.SM_SPELL);
        if (frameTimeMs <= 0)
            frameTimeMs = 60;

        if (effectNumber == 26)
            frameTimeMs = Math.Max(1, frameTimeMs / 2);

        return frameTimeMs;
    }

    private void TickMapMagicEffects(long nowMs)
    {
        if (_mapMagicEffects.Count == 0)
            return;

        for (int i = _mapMagicEffects.Count - 1; i >= 0; i--)
        {
            MapMagicEffect effect = _mapMagicEffects[i];
            if (effect.EffectNumber <= 0)
            {
                _mapMagicEffects.RemoveAt(i);
                continue;
            }

            long startMs = effect.StartMs;
            if (startMs <= 0)
                startMs = nowMs;

            if (!MapMagicEffectAtlas.TryGetInfo(effect.EffectNumber, effect.EffectType, out _, out _, out int frames, out int frameTimeMs) ||
                frames <= 0 ||
                frameTimeMs <= 0)
            {
                _mapMagicEffects.RemoveAt(i);
                continue;
            }

            long durationMs = (long)frames * frameTimeMs;
            if (durationMs > 0 && nowMs - startMs >= durationMs)
                _mapMagicEffects.RemoveAt(i);
        }
    }

    private void TickNormalEffects(long nowMs)
    {
        if (_normalEffects.Count == 0)
            return;

        for (int i = _normalEffects.Count - 1; i >= 0; i--)
        {
            NormalEffect effect = _normalEffects[i];
            if (effect.Type <= 0)
            {
                _normalEffects.RemoveAt(i);
                continue;
            }

            if (!NormalEffectAtlas.TryGetInfo(effect.Type, out _, out _, out int frames, out int frameTimeMs) || frames <= 0 || frameTimeMs <= 0)
            {
                _normalEffects.RemoveAt(i);
                continue;
            }

            long startMs = effect.StartMs;
            if (startMs <= 0)
                startMs = nowMs;

            long durationMs = (long)frames * frameTimeMs;
            if (durationMs > 0 && nowMs - startMs >= durationMs)
                _normalEffects.RemoveAt(i);
        }
    }

    private void TickStruckEffects(long nowMs)
    {
        if (_struckEffects.Count == 0)
            return;

        for (int i = _struckEffects.Count - 1; i >= 0; i--)
        {
            StruckEffect effect = _struckEffects[i];
            if (effect.ActorId == 0 || effect.Type <= 0)
            {
                _struckEffects.RemoveAt(i);
                continue;
            }

            if (!_actors.ContainsKey(effect.ActorId))
            {
                _struckEffects.RemoveAt(i);
                continue;
            }

            if (!StruckEffectAtlas.TryGetInfo(effect.Type, out _, out _, out int frames, out int frameTimeMs) || frames <= 0 || frameTimeMs <= 0)
            {
                _struckEffects.RemoveAt(i);
                continue;
            }

            long startMs = effect.StartMs;
            if (startMs <= 0)
                startMs = nowMs;

            long durationMs = (long)frames * frameTimeMs;
            if (durationMs > 0 && nowMs - startMs >= durationMs)
                _struckEffects.RemoveAt(i);
        }
    }

    public void AddMapMagicEffect(byte effectNumber, byte effectType, int x, int y, long startMs)
    {
        if (effectNumber == 0)
            return;

        if (startMs <= 0)
            startMs = Environment.TickCount64;

        _mapMagicEffects.Add(new MapMagicEffect(effectNumber, effectType, x, y, startMs));
    }

    public void AddMagicEff(byte effectNumber, byte effectType, int fromX, int fromY, int toX, int toY, int targetActorId, int magicLevel, long startMs)
    {
        if (effectNumber == 0)
            return;

        
        if (effectNumber == 44)
            return;

        if (startMs <= 0)
            startMs = Environment.TickCount64;

        int travelDurationMs = ComputeMagicEffTravelDurationMs(fromX, fromY, toX, toY);
        byte dir16 = ComputeMagicEffDir16(fromX, fromY, toX, toY);

        _magicEffs.Add(new MagicEffInstance(effectNumber, effectType, fromX, fromY, toX, toY, targetActorId, magicLevel, startMs, dir16, travelDurationMs));
        if (_magicEffs.Count > 256)
            _magicEffs.RemoveRange(0, _magicEffs.Count - 256);
    }

    public void ClearMapMagicEffects() => _mapMagicEffects.Clear();

    public void ClearMagicEffs() => _magicEffs.Clear();

    private static int ComputeMagicEffTravelDurationMs(int fromX, int fromY, int toX, int toY)
    {
        long dxPx = (long)(toX - fromX) * Grobal2.UNITX;
        long dyPx = (long)(toY - fromY) * Grobal2.UNITY;
        long dominantPx = Math.Max(Math.Abs(dxPx), Math.Abs(dyPx));
        if (dominantPx <= 0)
            return 0;

        long durationMs = (dominantPx * 900 + 499) / 500;
        if (durationMs <= 0)
            return 0;
        if (durationMs > int.MaxValue)
            return int.MaxValue;

        return (int)durationMs;
    }

    private static byte ComputeMagicEffDir16(int fromX, int fromY, int toX, int toY)
    {
        int dxPx = (toX - fromX) * Grobal2.UNITX;
        int dyPx = (toY - fromY) * Grobal2.UNITY;

        int dir = ClFunc.GetFlyDirection16(0, 0, dxPx, dyPx);
        if ((uint)dir > 15)
            dir &= 15;

        return (byte)dir;
    }

    public void AddNormalEffect(int type, int x, int y, long startMs)
    {
        if (type == 0)
            return;

        if (startMs <= 0)
            startMs = Environment.TickCount64;

        _normalEffects.Add(new NormalEffect(type, x, y, startMs));
        if (_normalEffects.Count > 256)
            _normalEffects.RemoveRange(0, _normalEffects.Count - 256);
    }

    public bool TryAddStruckEffect(int actorId, int type, int tag, long startMs)
    {
        if (actorId == 0 || type == 0)
            return false;

        if (!_actors.ContainsKey(actorId))
            return false;

        if (startMs <= 0)
            startMs = Environment.TickCount64;

        _struckEffects.Add(new StruckEffect(actorId, type, tag, startMs));
        if (_struckEffects.Count > 256)
            _struckEffects.RemoveRange(0, _struckEffects.Count - 256);

        return true;
    }

    public void RemoveNormalEffectAt(int index)
    {
        if ((uint)index >= (uint)_normalEffects.Count)
            return;
        _normalEffects.RemoveAt(index);
    }

    public void RemoveStruckEffectAt(int index)
    {
        if ((uint)index >= (uint)_struckEffects.Count)
            return;
        _struckEffects.RemoveAt(index);
    }

    public void ClearNormalEffects() => _normalEffects.Clear();

    public void ClearStruckEffects() => _struckEffects.Clear();

    private static bool IsActorIdle(ActorMarker actor, long nowTimestamp)
    {
        if (nowTimestamp <= actor.ActionStartTimestamp)
            return true;

        long elapsedMs = (nowTimestamp - actor.ActionStartTimestamp) * 1000 / Stopwatch.Frequency;
        if (elapsedMs < 0)
            return true;

        int durationMs = MirActionTiming.GetActionDurationMs(actor.Action);
        if (durationMs <= 0)
            return true;

        return elapsedMs >= durationMs;
    }

    public bool TryApplyActionMessage(string datablock, long nowTickMs, out string? chatLine, out ActMessageSideEffect sideEffect)
    {
        chatLine = null;
        sideEffect = default;

        if (string.IsNullOrWhiteSpace(datablock))
            return false;

        ReadOnlySpan<char> span = datablock.AsSpan().Trim();
        if (span.Length == 0)
            return false;

        if (span[0] != '+')
            return false;

        if (span.Length >= 4 && span[1] == 'G' && span[2] == 'D' && span[3] == '/')
        {
            ReadOnlySpan<char> data = span[4..];
            int slash = data.IndexOf('/');
            if (slash >= 0)
                data = data[..slash];

            if (!uint.TryParse(data, out uint rtime) || rtime == 0 || ServerTime == rtime)
                return true;

            ServerTime = rtime;
            ActionLock = false;
            MoveBusy = false;
            MoveErr = 0;
            return true;
        }

        string tag = span[1..].ToString();

        switch (tag)
        {
            case "DIG":
                MyDigFragment = true;
                return true;
            case "PWR":
                MyNextPowerHit = true;
                return true;
            case "LNG":
                MyCanLongHit = true;
                return true;
            case "ULNG":
                MyCanLongHit = false;
                return true;
            case "WID":
                MyCanWideHit = true;
                return true;
            case "UWID":
                MyCanWideHit = false;
                return true;
            case "STN":
                MyCanStnHit = true;
                return true;
            case "USTN":
                MyCanStnHit = false;
                return true;
            case "CRS":
                MyCanCrsHit = true;
                chatLine = "双龙斩开启";
                return true;
            case "UCRS":
                MyCanCrsHit = false;
                chatLine = "双龙斩关闭";
                return true;
            case "TWN":
                MyNextTwinHit = true;
                MyLatestTwinHitTickMs = nowTickMs;
                chatLine = "召集雷电力量成功";
                sideEffect = new ActMessageSideEffect(StruckEffectType: 210, SfxId: 142, SoundFile: null);
                return true;
            case "UTWN":
                MyNextTwinHit = false;
                chatLine = "雷电力量消失";
                return true;
            case "SQU":
                MyCanSquHit = true;
                chatLine = "[龙影剑法] 开启";
                return true;
            case "FIR":
                MyNextFireHit = true;
                MyLatestFireHitTickMs = nowTickMs;
                return true;
            case "PUR":
                MyNextPursueHit = true;
                MyLatestPursueHitTickMs = nowTickMs;
                return true;
            case "RSH":
                MyNextRushHit = true;
                MyLatestRushHitTickMs = nowTickMs;
                return true;
            case "SMI":
                MyNextSmiteHit = true;
                MyLatestSmiteHitTickMs = nowTickMs;
                return true;
            case "SMIL3":
                MyNextSmiteLongHit3 = true;
                MyLatestSmiteLongHitTickMs3 = nowTickMs;
                chatLine = "[血魂一击] 已准备...";
                return true;
            case "SMIL":
                MyNextSmiteLongHit = true;
                MyLatestSmiteLongHitTickMs = nowTickMs;
                return true;
            case "SMIL2":
                MyNextSmiteLongHit2 = true;
                MyLatestSmiteLongHitTickMs2 = nowTickMs;
                chatLine = "[断空斩] 已准备...";
                return true;
            case "SMIW":
                MyNextSmiteWideHit = true;
                MyLatestSmiteWideHitTickMs = nowTickMs;
                return true;
            case "SMIW2":
                MyNextSmiteWideHit2 = true;
                MyLatestSmiteWideHitTickMs2 = nowTickMs;
                chatLine = "[倚天辟地] 已准备";
                sideEffect = new ActMessageSideEffect(StruckEffectType: 0, SfxId: 0, SoundFile: "S6-1.wav");
                return true;
            case "MDS":
                chatLine = "[美杜莎之瞳] 技能可施展";
                sideEffect = new ActMessageSideEffect(StruckEffectType: 1110, SfxId: 0, SoundFile: "M1-2.wav");
                return true;
            case "UFIR":
                MyNextFireHit = false;
                return true;
            case "UPUR":
                MyNextPursueHit = false;
                return true;
            case "USMI":
                MyNextSmiteHit = false;
                return true;
            case "URSH":
                MyNextRushHit = false;
                return true;
            case "USMIL":
                MyNextSmiteLongHit = false;
                return true;
            case "USML3":
                MyNextSmiteLongHit3 = false;
                return true;
            case "USML2":
                MyNextSmiteLongHit2 = false;
                return true;
            case "USMIW":
                MyNextSmiteWideHit = false;
                return true;
            case "USMIW2":
                MyNextSmiteWideHit2 = false;
                return true;
            case "USQU":
                MyCanSquHit = false;
                chatLine = "[龙影剑法] 关闭";
                return true;
            case "SLON":
                MyCanSLonHit = true;
                MyLatestSLonHitTickMs = nowTickMs;
                chatLine = "[开天斩] 力量凝聚...";
                return true;
            case "USLON":
                MyCanSLonHit = false;
                chatLine = "[开天斩] 力量消失";
                return true;
            default:
                return false;
        }
    }

    public void ApplyPlayerConfig(bool hero, bool showFashion)
    {
        if (hero)
            HeroShowFashion = showFashion;
        else
            ShowFashion = showFashion;
    }

    public void ApplySquarePowerUp(int hitPoint, int maxHitPoint)
    {
        MySquHitPoint = hitPoint;
        if (MyMaxSquHitPoint != maxHitPoint)
            MyMaxSquHitPoint = maxHitPoint;
        if (MySquHitPoint > MyMaxSquHitPoint)
            MySquHitPoint = MyMaxSquHitPoint;
    }

    public void ApplyRunHuman(bool canRunHuman) => CanRunHuman = canRunHuman;

    public void ApplyRunSafeZone(bool canRunSafeZone) => CanRunSafeZone = canRunSafeZone;

    public void ApplyAreaState(int value) => AreaStateValue = value;

    public bool TryApplyServerConfig(int recog, ushort series, string bodyEncoded)
    {
        OpenAutoPlay = (recog & 0xFF) == 1;
        SpeedRateEnabled = series != 0;
        SpeedRateShow = SpeedRateEnabled;

        if (!EdCode.TryDecodeBuffer(bodyEncoded, out ClientConf conf))
            return false;

        ClientConfig = conf;
        ClientConfigSet = true;
        CanRunHuman = conf.RunHuman != 0;
        CanRunMon = conf.RunMon != 0;
        CanRunNpc = conf.RunNpc != 0;
        CanRunAllInWarZone = conf.WarRunAll != 0;
        return true;
    }

    public bool TryApplyServerConfig2(int param, int tag, ushort series, string bodyEncoded)
    {
        if (!EdCode.TryDecodeBuffer(bodyEncoded, out ServerConfig conf))
            return false;

        AutoSay = conf.AutoSay != 0;

        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref conf, 1));
        MultiHero = bytes.Length > 1 && bytes[1] != 0;
        Skill114Mp = bytes.Length > 2 && bytes[2] != 0;
        Skill68Mp = bytes.Length > 3 && bytes[3] != 0;

        if (series > 200)
            EatItemInvTime = series;

        ForceNotViewFog = ((param >> 8) & 0xFF) != 0;
        if (ForceNotViewFog)
        {
            DarkLevel = 0;
            ViewFog = false;
        }
        OpenStallSystem = (param & 0xFF) != 0;
        AutoLongAttack = (tag & 0xFF) != 0;
        HeroEnabled = ((tag >> 8) & 0xFF) != 0;
        return true;
    }

    public void ApplyDayChanging(int dayBright, int darkLevel)
    {
        DayBright = dayBright;
        DarkLevel = ForceNotViewFog ? 0 : darkLevel;
        ViewFog = DarkLevel != 0;
    }

    public void ApplyServerConfig3(int param, int tag, ushort series)
    {
        byte seriesLo = (byte)(series & 0xFF);
        if (seriesLo == 0)
            return;

        SpeedRateEnabled = true;
        SpeedRateShow = false;
        HitSpeedRate = (byte)Math.Min(68, param & 0xFF);
        MagSpeedRate = (byte)Math.Min(68, (param >> 8) & 0xFF);
        MoveSpeedRate = (byte)Math.Min(68, tag & 0xFF);
    }

    public bool TryApplyAdjustBonus(int bonusPoint, string bodyEncoded)
    {
        BonusPoint = bonusPoint;
        BonusTick = default;
        BonusAbility = default;
        BaseNakedAbility = default;

        if (string.IsNullOrWhiteSpace(bodyEncoded))
            return false;

        using IEnumerator<string> iter = SplitSlashSegments(bodyEncoded).GetEnumerator();
        if (!iter.MoveNext())
            return false;
        string tickEncoded = iter.Current;
        if (!iter.MoveNext())
            return false;
        string bonusEncoded = iter.Current;
        if (!iter.MoveNext())
            return false;
        string nakedEncoded = iter.Current;

        if (!EdCode.TryDecodeBuffer(tickEncoded, out NakedAbility tick))
            return false;
        if (!EdCode.TryDecodeBuffer(bonusEncoded, out NakedAbility bonus))
            return false;
        if (!EdCode.TryDecodeBuffer(nakedEncoded, out NakedAbility naked))
            return false;

        BonusTick = tick;
        BonusAbility = bonus;
        BaseNakedAbility = naked;
        return true;
    }

    public bool TryApplyMyTitles(bool hero, string bodyEncoded, out int titleCount)
    {
        titleCount = 0;

        HumTitle[] titles = Array.Empty<HumTitle>();
        if (!string.IsNullOrWhiteSpace(bodyEncoded))
        {
            titles = new HumTitle[6];
            int count = 0;

            foreach (string segment in SplitSlashSegments(bodyEncoded))
            {
                if (count >= titles.Length)
                    break;

                if (!EdCode.TryDecodeBuffer(segment, out HumTitle title))
                    break;

                titles[count++] = title;
            }

            int compact = 0;
            for (int i = 0; i < count; i++)
            {
                if (titles[i].Index > 0)
                    titles[compact++] = titles[i];
            }

            Array.Resize(ref titles, compact);
            titleCount = compact;
        }

        if (hero)
            _heroTitles = titles;
        else
            _myTitles = titles;

        return true;
    }

    public bool TryApplyServerTitles(int compressedLen, string bodyEncoded, out int titleCount)
    {
        titleCount = 0;
        _serverTitles = Array.Empty<ClientStdItem>();

        if (compressedLen <= 0 || string.IsNullOrEmpty(bodyEncoded))
            return true;

        byte[] decoded = EdCode.DecodeBytes(bodyEncoded);
        if (decoded.Length < compressedLen)
            return false;

        if (decoded.Length != compressedLen)
            decoded = decoded.AsSpan(0, compressedLen).ToArray();

        byte[] decompressed;
        try
        {
            decompressed = ZLibDecompress(decoded);
        }
        catch
        {
            return false;
        }

        int itemSize = Marshal.SizeOf<ClientStdItem>();
        if (itemSize <= 0 || decompressed.Length % itemSize != 0)
            return false;

        int count = decompressed.Length / itemSize;
        var items = new ClientStdItem[count];
        ReadOnlySpan<byte> bytes = decompressed;

        for (int i = 0; i < count; i++)
        {
            items[i] = MemoryMarshal.Read<ClientStdItem>(bytes.Slice(i * itemSize, itemSize));
        }

        _serverTitles = items;
        titleCount = count;
        return true;
    }

    public static string? DescribeSecretPropertyHintCode(int hintCode) => hintCode switch
    {
        0 => "秘密属性：可对装备进行秘密属性操作。",
        1 => "秘密属性：操作失败（可能因等级/属性不足）。",
        2 => "秘密属性：找不到物品。",
        3 => "秘密属性：没有可操作的秘密属性。",
        4 => "秘密属性：装备条件不足。",
        5 => "秘密属性：材料不足。",
        6 => "秘密属性：幸运等级不足。",
        7 => "秘密属性：卷轴已成功操作一次。",
        10 => "秘密属性：操作成功。",
        11 => "秘密属性：操作失败（可能因装备等级过高或幸运不足）。",
        12 => "秘密属性：找不到皮肤。",
        13 => "秘密属性：缺少皮肤。",
        14 => "秘密属性：幸运值不足。",
        15 => "秘密属性：没有技能，操作失败。",
        _ => null
    };

    public void ApplySecretProperty(ushort luckValue, ushort energyValue)
    {
        MyLuck = luckValue > byte.MaxValue ? byte.MaxValue : (byte)luckValue;
        MyEnergy = energyValue > byte.MaxValue ? byte.MaxValue : (byte)energyValue;
    }

    public void ApplyMyHungryState(int hungryState)
    {
        MyHungryState = hungryState;
    }

    public bool TryApplySendHeros(string bodyEncoded, out int heroCount)
    {
        heroCount = 0;
        _heroes = Array.Empty<HeroInfo>();
        _selectedHeroName = string.Empty;

        if (string.IsNullOrWhiteSpace(bodyEncoded))
            return true;

        using IEnumerator<string> iter = SplitSlashSegments(bodyEncoded).GetEnumerator();
        if (!iter.MoveNext())
            return false;

        _selectedHeroName = EdCode.DecodeString(iter.Current).Trim();

        if (!iter.MoveNext())
            return true;

        byte[] bytes = EdCode.DecodeBytes(iter.Current);
        int heroSize = Marshal.SizeOf<HeroInfo>();
        if (heroSize <= 0 || bytes.Length < heroSize)
            return false;

        int count = bytes.Length / heroSize;
        var heroes = new List<HeroInfo>(capacity: Math.Min(7, count));
        for (int i = 0; i < count; i++)
        {
            HeroInfo hero = MemoryMarshal.Read<HeroInfo>(bytes.AsSpan(i * heroSize, heroSize));
            if (string.IsNullOrWhiteSpace(hero.ChrNameString))
                continue;

            heroes.Add(hero);
        }

        _heroes = heroes.ToArray();
        heroCount = _heroes.Length;
        return true;
    }

    public bool TryApplySuiteStrs(int compressedLen, string bodyEncoded, out int itemCount)
    {
        itemCount = 0;
        _suiteItems = Array.Empty<ClientSuiteItem>();

        if (compressedLen <= 0 || string.IsNullOrEmpty(bodyEncoded))
            return true;

        byte[] decoded = EdCode.DecodeBytes(bodyEncoded);
        if (decoded.Length < compressedLen)
            return false;

        if (decoded.Length != compressedLen)
            decoded = decoded.AsSpan(0, compressedLen).ToArray();

        byte[] decompressed;
        try
        {
            decompressed = ZLibDecompress(decoded);
        }
        catch
        {
            return false;
        }

        int itemSize = Marshal.SizeOf<ClientSuiteItem>();
        if (itemSize <= 0 || decompressed.Length % itemSize != 0)
            return false;

        int count = decompressed.Length / itemSize;
        var items = new ClientSuiteItem[count];
        ReadOnlySpan<byte> bytes = decompressed;

        for (int i = 0; i < count; i++)
        {
            items[i] = MemoryMarshal.Read<ClientSuiteItem>(bytes.Slice(i * itemSize, itemSize));
        }

        _suiteItems = items;
        itemCount = count;
        return true;
    }

    public bool TryApplyAllshine(int byteLen, string bodyEncoded)
    {
        _allshineBytes = Array.Empty<byte>();

        if (byteLen <= 0 || string.IsNullOrEmpty(bodyEncoded))
            return true;

        byte[] decoded = EdCode.DecodeBytes(bodyEncoded);
        if (decoded.Length < byteLen)
            return false;

        if (decoded.Length != byteLen)
            decoded = decoded.AsSpan(0, byteLen).ToArray();

        _allshineBytes = decoded;
        return true;
    }

    public bool TryApplyOpenStall(int actorRecogId, ushort dir, ushort x, ushort y, string bodyEncoded, out StallActorMarker stall)
    {
        stall = default;
        if (actorRecogId == 0)
            return false;

        if (!EdCode.TryDecodeBuffer(bodyEncoded, out StallInfo info))
            return false;

        stall = new StallActorMarker(Open: info.IsOpen, Looks: info.Looks, Name: info.NameString);

        if (stall.Open)
            _stalls[actorRecogId] = stall;
        else
            _stalls.Remove(actorRecogId);

        if (_actors.TryGetValue(actorRecogId, out ActorMarker actor))
        {
            byte bdir = dir > byte.MaxValue ? byte.MaxValue : (byte)dir;
            _actors[actorRecogId] = actor with { X = x, Y = y, Dir = bdir };
        }

        if (!stall.Open && UserStallOpen && UserStallActorId == actorRecogId)
        {
            UserStallOpen = false;
            UserStallActorId = 0;
            UserStallName = string.Empty;
            UserStallItemCount = 0;
            _userStallItems.AsSpan().Clear();
        }

        return true;
    }

    public bool TryApplyUserStall(int actorRecogId, string bodyEncoded, out int itemCount, out string stallName)
    {
        itemCount = 0;
        stallName = string.Empty;

        if (actorRecogId == 0)
            return false;

        if (!EdCode.TryDecodeBuffer(bodyEncoded, out ClientStallInfo info))
            return false;

        UserStallActorId = actorRecogId;
        UserStallName = info.StallNameString;
        UserStallItemCount = info.ItemCount;
        UserStallOpen = info.ItemCount > 0;

        _userStallItems.AsSpan().Clear();
        if (UserStallOpen)
        {
            _userStallItems[0] = info.Item0;
            _userStallItems[1] = info.Item1;
            _userStallItems[2] = info.Item2;
            _userStallItems[3] = info.Item3;
            _userStallItems[4] = info.Item4;
            _userStallItems[5] = info.Item5;
            _userStallItems[6] = info.Item6;
            _userStallItems[7] = info.Item7;
            _userStallItems[8] = info.Item8;
            _userStallItems[9] = info.Item9;
        }

        itemCount = UserStallItemCount;
        stallName = UserStallName;
        return true;
    }

    public void ApplyCollectExp(uint exp, uint ipExp)
    {
        if (CollectExp < CollectExpMax || CollectIpExp < CollectIpExpMax)
            CollectExpShineCount = 20;

        CollectExp = exp;
        CollectIpExp = ipExp;
    }

    public void ApplyCollectExpState(byte level, uint expMax, uint ipExpMax)
    {
        CollectExpLevel = level;
        CollectExpMax = expMax;
        CollectIpExpMax = ipExpMax;
    }

    public bool TryApplyQueryValue(ushort param, string bodyEncoded, out QueryValueRequest request)
    {
        request = default;

        if (string.IsNullOrWhiteSpace(bodyEncoded))
        {
            QueryValuePending = false;
            QueryValue = default;
            return true;
        }

        string prompt = EdCode.DecodeString(bodyEncoded);
        byte mode = (byte)(param & 0xFF);
        byte icon = (byte)(param >> 8);

        request = new QueryValueRequest(prompt, mode, icon);
        QueryValuePending = true;
        QueryValue = request;
        return true;
    }

    public void ApplySeriesSkillReady(bool hero, int recog, ushort param, ushort step)
    {
        if (hero)
            return;

        _seriesSkillArr[0] = unchecked((byte)(recog & 0xFF));
        _seriesSkillArr[1] = unchecked((byte)((recog >> 16) & 0xFF));
        _seriesSkillArr[2] = unchecked((byte)(param & 0xFF));
        _seriesSkillArr[3] = unchecked((byte)(param >> 8));

        SeriesSkillStep = step;
        SeriesSkillReady = true;

        MyNextSmiteHit = false;
        MyLatestSmiteHitTickMs = 0;
        MyNextRushHit = false;
        MyLatestRushHitTickMs = 0;
        MyNextSmiteLongHit = false;
        MyLatestSmiteLongHitTickMs = 0;
        MyNextSmiteLongHit2 = false;
        MyLatestSmiteLongHitTickMs2 = 0;
        MyNextSmiteLongHit3 = false;
        MyLatestSmiteLongHitTickMs3 = 0;
        MyNextSmiteWideHit = false;
        MyLatestSmiteWideHitTickMs = 0;
        MyNextSmiteWideHit2 = false;
        MyLatestSmiteWideHitTickMs2 = 0;
    }

    public void ApplyFireSeriesSkillResult(int resultCode)
    {
        SeriesSkillReady = false;

        if (resultCode is 1)
        {
            MyNextSmiteHit = false;
            MyLatestSmiteHitTickMs = 0;
            MyNextRushHit = false;
            MyLatestRushHitTickMs = 0;
            MyNextSmiteLongHit = false;
            MyLatestSmiteLongHitTickMs = 0;
            MyNextSmiteLongHit2 = false;
            MyLatestSmiteLongHitTickMs2 = 0;
            MyNextSmiteLongHit3 = false;
            MyLatestSmiteLongHitTickMs3 = 0;
            MyNextSmiteWideHit = false;
            MyLatestSmiteWideHitTickMs = 0;
            MyNextSmiteWideHit2 = false;
            MyLatestSmiteWideHitTickMs2 = 0;
        }
    }

    public bool TryApplySetSeriesSkillSlot(bool hero, ushort slotIndex, int slotValue, out byte appliedValue)
    {
        appliedValue = 0;
        if (slotIndex >= _tempSeriesSkillArr.Length)
            return false;

        byte value = slotValue < 0 ? (byte)0 : unchecked((byte)(slotValue & 0xFF));

        if (hero)
            _heroTempSeriesSkillArr[slotIndex] = value;
        else
            _tempSeriesSkillArr[slotIndex] = value;

        appliedValue = value;
        return true;
    }

    public bool TryApplySeriesSkillArr(bool hero, int recog, ushort param, ushort tag, string bodyEncoded)
    {
        if (hero)
        {
            _heroTempSeriesSkillArr[0] = unchecked((byte)(recog & 0xFF));
            _heroTempSeriesSkillArr[1] = unchecked((byte)((recog >> 16) & 0xFF));
            _heroTempSeriesSkillArr[2] = unchecked((byte)(param & 0xFF));
            _heroTempSeriesSkillArr[3] = unchecked((byte)(tag & 0xFF));
            return TryDecodeVenationInfos(bodyEncoded, _heroVenationInfos);
        }

        _tempSeriesSkillArr[0] = unchecked((byte)(recog & 0xFF));
        _tempSeriesSkillArr[1] = unchecked((byte)((recog >> 16) & 0xFF));
        _tempSeriesSkillArr[2] = unchecked((byte)(param & 0xFF));
        _tempSeriesSkillArr[3] = unchecked((byte)(tag & 0xFF));
        return TryDecodeVenationInfos(bodyEncoded, _venationInfos);
    }

    public bool TryApplyTrainVenation(bool hero, int resultCode, string bodyEncoded)
    {
        if (resultCode != 0)
            return true;

        return hero
            ? TryDecodeVenationInfos(bodyEncoded, _heroVenationInfos)
            : TryDecodeVenationInfos(bodyEncoded, _venationInfos);
    }

    public bool TryApplyBreakPoint(bool hero, int resultCode, string bodyEncoded)
    {
        if (resultCode != 0)
            return true;

        return hero
            ? TryDecodeVenationInfos(bodyEncoded, _heroVenationInfos)
            : TryDecodeVenationInfos(bodyEncoded, _venationInfos);
    }

    private static bool TryDecodeVenationInfos(string bodyEncoded, VenationInfo[] destination)
    {
        destination.AsSpan().Clear();

        if (string.IsNullOrWhiteSpace(bodyEncoded))
            return true;

        byte[] bytes = EdCode.DecodeBytes(bodyEncoded);
        int needed = destination.Length * 2;
        if (bytes.Length < needed)
            return false;

        for (int i = 0; i < destination.Length; i++)
        {
            int offset = i * 2;
            destination[i] = new VenationInfo { Level = bytes[offset], Point = bytes[offset + 1] };
        }

        return true;
    }

    public IReadOnlyDictionary<int, MirMission> GetMissions(byte missionClass)
    {
        if (_missionsByClass.TryGetValue(missionClass, out Dictionary<int, MirMission>? missions))
            return missions;

        return EmptyMissions;
    }

    public void ClearNewMissionPending()
    {
        NewMissionPending = false;
    }

    public bool TryApplySetMission(byte missionClass, ushort operation, int missionId, ushort showDialogFlag, string bodyEncoded, out string? debugSummary)
    {
        debugSummary = null;

        if (missionClass is < 1 or > 4)
            return true;

        if (operation == 2)
        {
            bool removed = _missionsByClass.TryGetValue(missionClass, out Dictionary<int, MirMission>? missions)
                && missions.Remove(missionId);

            debugSummary = removed ? "removed" : "remove-miss";
            return true;
        }

        if (operation != 1)
        {
            debugSummary = $"op={operation}";
            return true;
        }

        string decoded = string.IsNullOrWhiteSpace(bodyEncoded) ? string.Empty : EdCode.DecodeString(bodyEncoded);
        if (!TryParseMission(decoded, out string title, out string description))
        {
            debugSummary = "invalid-body";
            return false;
        }

        if (!_missionsByClass.TryGetValue(missionClass, out Dictionary<int, MirMission>? dict))
        {
            dict = new Dictionary<int, MirMission>();
            _missionsByClass[missionClass] = dict;
        }

        bool added = !dict.ContainsKey(missionId);
        dict[missionId] = new MirMission(missionId, title, description);

        bool showDialog = showDialogFlag != 0;
        NewMissionPending = added && !showDialog;

        debugSummary = $"{(added ? "add" : "update")} title='{title}' show={showDialog}";
        return true;
    }

    private static bool TryParseMission(string payload, out string title, out string description)
    {
        title = string.Empty;
        description = string.Empty;

        if (string.IsNullOrWhiteSpace(payload))
            return false;

        int titleKey = payload.IndexOf("title=", StringComparison.OrdinalIgnoreCase);
        int descKey = payload.IndexOf("desc=", StringComparison.OrdinalIgnoreCase);

        if (titleKey < 0 || descKey < 0 || descKey <= titleKey)
            return false;

        int titleStart = titleKey + 6;
        title = payload[titleStart..descKey].Trim();

        int descStart = descKey + 5;
        description = descStart <= payload.Length ? payload[descStart..].Trim() : string.Empty;
        return true;
    }

    private static readonly IReadOnlyDictionary<int, MirMission> EmptyMissions = new Dictionary<int, MirMission>();

    public void SetWaitingDetectItemMove(ClientItem item, int index)
    {
        _waitingDetectItem = item;
        _waitingDetectItemIndex = index;
        _waitingDetectItemSet = item.MakeIndex != 0;
    }

    public bool TryApplyMoveDetectItemResult(int resultCode, out string? chatLine)
    {
        chatLine = resultCode switch
        {
            -1 => "[失败] 包裹没有此物品",
            -2 => "[失败] 放入的不是灵媒物品",
            -3 => "[失败] 要取下的灵媒物品不存在",
            -4 => "[失败] 要取下的灵媒物品不正确",
            _ => null
        };

        if (!_waitingDetectItemSet || _waitingDetectItem.MakeIndex == 0)
            return true;

        ClientItem pending = _waitingDetectItem;
        int pendingIndex = _waitingDetectItemIndex;
        _waitingDetectItem = default;
        _waitingDetectItemIndex = 0;
        _waitingDetectItemSet = false;

        if (resultCode is 0 or -3 or -4)
        {
            _detectItem = pending;
            return true;
        }

        if (resultCode is 1 or -1 or -2)
        {
            AddItemBag(pending, IsBagIndex(pendingIndex) ? pendingIndex : -1);
            RebuildBagIndex();
            return true;
        }

        return true;
    }

    public bool TryApplyUpdateDetectItem(ushort spiritQ, ushort spirit)
    {
        if (_detectItem.MakeIndex == 0)
            return false;

        ClientItem item = _detectItem;
        item.S.Eva.SpiritQ = (byte)Math.Min(spiritQ, (ushort)byte.MaxValue);
        item.S.Eva.Spirit = (byte)Math.Min(spirit, (ushort)byte.MaxValue);
        _detectItem = item;
        return true;
    }

    public void ApplyDetectItemMineId(int mineId) => _detectItemMineId = mineId;

    private static byte[] ZLibDecompress(ReadOnlySpan<byte> compressed)
    {
        if (compressed.IsEmpty)
            return Array.Empty<byte>();

        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private bool IsBagIndex(int index) => (uint)index < (uint)_bagSlots.Length;

    public void SetMapMoving(bool moving) => MapMoving = moving;

    public void SetMapCenter(int x, int y)
    {
        MapCenterX = x;
        MapCenterY = y;
        MapCenterSet = true;
    }

    public void ApplyMapLoaded(int x, int y, long nowTimestamp)
    {
        MapCenterX = x;
        MapCenterY = y;
        MapCenterSet = true;

        _dropItems.Clear();
        _doorOpenOverrides.Clear();
        _stalls.Clear();

        ActorMarker? myselfMarker = null;
        if (MyselfRecogIdSet && MyselfRecogId != 0 && _actors.TryGetValue(MyselfRecogId, out ActorMarker existing))
            myselfMarker = existing;

        _actors.Clear();
        _pendingActorInfos.Clear();
        if (MyselfRecogIdSet && MyselfRecogId != 0)
        {
            _actors[MyselfRecogId] = myselfMarker is { } marker
                ? marker with
                {
                    X = MapCenterX,
                    Y = MapCenterY,
                    FromX = MapCenterX,
                    FromY = MapCenterY,
                    Action = Grobal2.SM_TURN,
                    ActionStartTimestamp = nowTimestamp,
                    IsMyself = true
                }
                : new ActorMarker(
                    X: MapCenterX,
                    Y: MapCenterY,
                    FromX: MapCenterX,
                    FromY: MapCenterY,
                    Dir: 0,
                    Feature: 0,
                    Status: 0,
                    Action: Grobal2.SM_TURN,
                    ActionStartTimestamp: nowTimestamp,
                    IsMyself: true);
        }

        MapMoving = false;
    }

    public void ApplyServerLogon(int recogId, int x, int y, ushort dir, MessageBodyWL body, long nowTimestamp, long nowMs)
    {
        MyselfRecogId = recogId;
        MyselfRecogIdSet = true;

        _actors.Clear();
        _pendingActorInfos.Clear();
        _stalls.Clear();
        _pendingChangeFacesByActorId.Clear();
        _changingFaceActorIds.Clear();
        if (recogId != 0)
        {
            _actors[recogId] = new ActorMarker(
                X: x,
                Y: y,
                FromX: x,
                FromY: y,
                Dir: (byte)(dir & 0xFF),
                Feature: body.Param1,
                Status: body.Param2,
                Action: Grobal2.SM_TURN,
                ActionStartTimestamp: nowTimestamp,
                IsMyself: true,
                LastQueryUserNameMs: nowMs);
        }

        MapCenterX = x;
        MapCenterY = y;
        MapCenterSet = true;
    }

    public bool TryGetMyself(out ActorMarker actor)
    {
        actor = default;
        if (!MyselfRecogIdSet || MyselfRecogId == 0)
            return false;
        return _actors.TryGetValue(MyselfRecogId, out actor);
    }

    public void ApplyClearObjects()
    {
        MapMoving = true;
        _pendingActorInfos.Clear();

        if (MyselfRecogIdSet && MyselfRecogId != 0 && _actors.TryGetValue(MyselfRecogId, out ActorMarker myself))
        {
            _actors.Clear();
            _actors[MyselfRecogId] = myself with { IsMyself = true };
        }
        else
        {
            _actors.Clear();
        }

        _dropItems.Clear();
        _mapEvents.Clear();
        _doorOpenOverrides.Clear();
        _stalls.Clear();
        _pendingChangeFacesByActorId.Clear();
        _changingFaceActorIds.Clear();
        _normalEffects.Clear();
        _struckEffects.Clear();
        _mapMagicEffects.Clear();
        _magicEffs.Clear();
    }

    public void ApplyActorHide(int recogId)
    {
        if (recogId == 0)
            return;

        if (MyselfRecogIdSet && recogId == MyselfRecogId)
            return;

        if (_pendingChangeFacesByActorId.Remove(recogId, out PendingChangeFace pending))
            _changingFaceActorIds.Remove(pending.NewRecogId);

        _changingFaceActorIds.Remove(recogId);
        _actors.Remove(recogId);
        _pendingActorInfos.Remove(recogId);
        _stalls.Remove(recogId);
    }

    public void ApplyMyselfPositionUpdate(int x, int y)
    {
        if (MapCenterSet && MapCenterX == x && MapCenterY == y)
            return;

        MapCenterX = x;
        MapCenterY = y;
        MapCenterSet = true;
    }

    public bool TryGetActor(int recogId, out ActorMarker actor) => _actors.TryGetValue(recogId, out actor);

    public void SetActor(int recogId, ActorMarker actor) => _actors[recogId] = actor;

    public bool TryApplyActorAction(
        ushort ident,
        int recogId,
        int x,
        int y,
        ushort dir,
        int feature,
        int status,
        string? userName,
        string? descUserName,
        byte? nameColor,
        float? nameOffset,
        long nowTimestamp,
        long nowMs,
        out ActorMarker actor,
        out ushort previousAction)
    {
        actor = default;
        previousAction = 0;

        if (recogId == 0)
            return false;

        bool isMyself = MyselfRecogIdSet && recogId == MyselfRecogId;
        bool moveAction = MirDirection.IsMoveAction(ident);
        bool isDeathAction = ident is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH;
        bool isAliveAction = ident == Grobal2.SM_ALIVE;

        if (_actors.TryGetValue(recogId, out ActorMarker existing))
        {
            previousAction = existing.Action;
            int fromX = moveAction ? existing.X : x;
            int fromY = moveAction ? existing.Y : y;
            bool itemExplore = existing.ItemExplore;
            if (isDeathAction)
                itemExplore = (dir & 0xFF00) != 0;
            else if (isAliveAction)
                itemExplore = false;

            actor = existing with
            {
                X = x,
                Y = y,
                FromX = fromX,
                FromY = fromY,
                Dir = (byte)(dir & 0xFF),
                Feature = feature,
                Status = status,
                Action = ident,
                ActionStartTimestamp = nowTimestamp,
                IsMyself = isMyself,
                ItemExplore = itemExplore
            };
        }
        else
        {
            if (IsChangingFace(recogId))
                return false;

            bool itemExplore = isDeathAction && (dir & 0xFF00) != 0;
            actor = new ActorMarker(
                X: x,
                Y: y,
                FromX: x,
                FromY: y,
                Dir: (byte)(dir & 0xFF),
                Feature: feature,
                Status: status,
                Action: ident,
                ActionStartTimestamp: nowTimestamp,
                IsMyself: isMyself,
                ItemExplore: itemExplore,
                LastQueryUserNameMs: nowMs);
        }

        if (!string.IsNullOrEmpty(userName))
        {
            actor = actor with
            {
                UserName = userName,
                DescUserName = descUserName ?? actor.DescUserName,
                NameOffset = nameOffset ?? actor.NameOffset
            };
        }
        else if (!string.IsNullOrEmpty(descUserName))
        {
            actor = actor with { DescUserName = descUserName };
        }

        if (nameColor is { } color)
            actor = actor with { NameColor = color };

        actor = ApplyPendingActorInfoIfAny(recogId, actor);
        _actors[recogId] = actor;

        if (isMyself)
            ApplyMyselfPositionUpdate(x, y);

        return true;
    }

    public bool TryApplyActorSimpleAction(
        ushort ident,
        int recogId,
        int x,
        int y,
        ushort dir,
        long nowTimestamp,
        long nowMs,
        out ActorMarker actor,
        out ushort previousAction)
    {
        actor = default;
        previousAction = 0;

        if (recogId == 0)
            return false;

        bool isMyself = MyselfRecogIdSet && recogId == MyselfRecogId;
        bool moveAction = MirDirection.IsMoveAction(ident);

        if (_actors.TryGetValue(recogId, out ActorMarker existing))
        {
            previousAction = existing.Action;
            int fromX = moveAction ? existing.X : x;
            int fromY = moveAction ? existing.Y : y;

            actor = existing with
            {
                X = x,
                Y = y,
                FromX = fromX,
                FromY = fromY,
                Dir = (byte)(dir & 0xFF),
                Action = ident,
                ActionStartTimestamp = nowTimestamp,
                IsMyself = isMyself
            };
        }
        else
        {
            if (IsChangingFace(recogId))
                return false;

            actor = new ActorMarker(
                X: x,
                Y: y,
                FromX: x,
                FromY: y,
                Dir: (byte)(dir & 0xFF),
                Feature: 0,
                Status: 0,
                Action: ident,
                ActionStartTimestamp: nowTimestamp,
                IsMyself: isMyself,
                LastQueryUserNameMs: nowMs);
        }

        actor = ApplyPendingActorInfoIfAny(recogId, actor);
        _actors[recogId] = actor;

        if (isMyself)
            ApplyMyselfPositionUpdate(x, y);

        return true;
    }

    public bool TryApplyActorPositionMove(int recogId, int x, int y, ushort dir, PositionMoveMessage msg, long nowTimestamp, long nowMs)
    {
        int fromX = x;
        int fromY = y;
        if (_actors.TryGetValue(recogId, out ActorMarker previous))
        {
            fromX = previous.X;
            fromY = previous.Y;
        }

        if (!TryApplyActorAction(
                Grobal2.SM_TURN,
                recogId,
                x,
                y,
                dir,
                feature: unchecked((int)msg.Feature),
                status: unchecked((int)msg.Status),
                userName: null,
                descUserName: null,
                nameColor: null,
                nameOffset: null,
                nowTimestamp,
                nowMs,
                out ActorMarker actor,
                out _))
        {
            return false;
        }

        _actors[recogId] = actor with { Hp = unchecked((int)msg.Hp), MaxHp = unchecked((int)msg.MaxHp) };

        const byte TeleportEffectNumber = 75;
        byte teleportEffectType = (byte)MagicType.ExploBujauk;

        if (fromX != x || fromY != y)
            AddMagicEff(TeleportEffectNumber, teleportEffectType, fromX, fromY, fromX, fromY, targetActorId: 0, magicLevel: 0, startMs: nowMs);

        AddMagicEff(TeleportEffectNumber, teleportEffectType, x, y, x, y, targetActorId: 0, magicLevel: 0, startMs: nowMs);
        return true;
    }

    public bool TryApplyActorUserName(int recogId, string userName, string descUserName, byte nameColor, byte attribute, float? nameOffset)
    {
        if (recogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
        {
            StagePendingActorInfo(recogId, new PendingActorInfo(
                UserName: userName,
                DescUserName: descUserName,
                NameColor: nameColor,
                Attribute: attribute,
                NameOffset: nameOffset,
                Light: null));
            return true;
        }

        byte newAttribute = actor.Attribute;
        if (attribute is >= 1 and <= 5)
            newAttribute = attribute;

        _actors[recogId] = actor with
        {
            UserName = userName,
            DescUserName = descUserName,
            NameColor = nameColor,
            Attribute = newAttribute,
            NameOffset = nameOffset ?? actor.NameOffset
        };

        return true;
    }

    public bool TryApplyActorNameColor(int recogId, byte nameColor)
    {
        if (recogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
        {
            StagePendingActorInfo(recogId, new PendingActorInfo(
                UserName: null,
                DescUserName: null,
                NameColor: nameColor,
                Attribute: null,
                NameOffset: null,
                Light: null));
            return true;
        }

        _actors[recogId] = actor with { NameColor = nameColor };
        return true;
    }

    public bool TryApplyActorLight(int recogId, int light)
    {
        if (recogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
        {
            StagePendingActorInfo(recogId, new PendingActorInfo(
                UserName: null,
                DescUserName: null,
                NameColor: null,
                Attribute: null,
                NameOffset: null,
                Light: light));
            return true;
        }

        _actors[recogId] = actor with { ChrLight = light };
        return true;
    }

    private void StagePendingActorInfo(int recogId, PendingActorInfo update)
    {
        if (recogId == 0)
            return;

        if (_pendingActorInfos.TryGetValue(recogId, out PendingActorInfo existing))
        {
            _pendingActorInfos[recogId] = new PendingActorInfo(
                UserName: update.UserName ?? existing.UserName,
                DescUserName: update.DescUserName ?? existing.DescUserName,
                NameColor: update.NameColor ?? existing.NameColor,
                Attribute: update.Attribute ?? existing.Attribute,
                NameOffset: update.NameOffset ?? existing.NameOffset,
                Light: update.Light ?? existing.Light);
            return;
        }

        if (_pendingActorInfos.Count > 2048)
            _pendingActorInfos.Clear();

        _pendingActorInfos.Add(recogId, update);
    }

    private ActorMarker ApplyPendingActorInfoIfAny(int recogId, ActorMarker actor)
    {
        if (!_pendingActorInfos.Remove(recogId, out PendingActorInfo pending))
            return actor;

        ActorMarker updated = actor;

        if (!string.IsNullOrEmpty(pending.UserName))
        {
            updated = updated with
            {
                UserName = pending.UserName,
                DescUserName = pending.DescUserName ?? updated.DescUserName,
                NameOffset = pending.NameOffset ?? updated.NameOffset
            };
        }
        else if (!string.IsNullOrEmpty(pending.DescUserName))
        {
            updated = updated with { DescUserName = pending.DescUserName };
        }

        if (pending.NameColor is { } color)
            updated = updated with { NameColor = color };

        if (pending.Attribute is { } attribute)
        {
            byte newAttribute = updated.Attribute;
            if (attribute is >= 1 and <= 5)
                newAttribute = attribute;
            updated = updated with { Attribute = newAttribute };
        }

        if (pending.Light is { } light)
            updated = updated with { ChrLight = light };

        return updated;
    }

    public bool TryApplyMyselfFeatureChanged(int feature, ushort featureEx)
    {
        if (!MyselfRecogIdSet || MyselfRecogId == 0)
            return false;

        if (!_actors.TryGetValue(MyselfRecogId, out ActorMarker actor))
            return false;

        _actors[MyselfRecogId] = actor with { Feature = feature, FeatureEx = featureEx };
        return true;
    }

    public bool TryApplyActorUserNameQuerySent(int recogId, long nowMs)
    {
        if (recogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
            return false;

        _actors[recogId] = actor with { LastQueryUserNameMs = nowMs };
        return true;
    }

    public bool TryApplyActorLastDamage(int recogId, int damage, long nowMs)
    {
        if (recogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
            return false;

        _actors[recogId] = actor with
        {
            LastDamage = damage,
            LastDamageTimestampMs = damage != 0 ? nowMs : actor.LastDamageTimestampMs
        };
        return true;
    }

    public bool TryApplyActorFeatureChanged(int recogId, int feature, int featureEx, string bodyEncoded)
    {
        if (recogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
            return false;

        int titleIndex = actor.TitleIndex;
        if (!string.IsNullOrEmpty(bodyEncoded) && EdCode.TryDecodeBuffer(bodyEncoded, out MessageBodyWL body))
            titleIndex = body.Param1 & 0xFFFF;

        _actors[recogId] = actor with { Feature = feature, FeatureEx = featureEx, TitleIndex = titleIndex };
        return true;
    }

    public bool TryApplyActorCharStatusChanged(int recogId, int state, ushort hitSpeed)
    {
        if (recogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
            return false;

        _actors[recogId] = actor with { Status = state, HitSpeed = hitSpeed };
        return true;
    }

    public bool TryApplyActorSpell(int recogId, int targetX, int targetY, int effectNum, int magicId, long nowTimestamp, long nowMs)
    {
        if (recogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
            return false;

        byte dir = actor.Dir;
        if (targetX != actor.X || targetY != actor.Y)
            dir = MirDirection.GetFlyDirection(actor.X, actor.Y, targetX, targetY);

        int effectNumber = effectNum % 255;
        int spellLv = effectNum / 255;
        int magicSerial = magicId % 300;
        int poison = magicId / 300;

        _actors[recogId] = actor with
        {
            Dir = dir,
            Action = Grobal2.SM_SPELL,
            ActionStartTimestamp = nowTimestamp,
            MagicServerCode = -1,
            MagicSerial = magicSerial,
            MagicEffectNumber = effectNumber,
            MagicEffectType = 0,
            MagicTarget = 0,
            MagicTargetX = targetX,
            MagicTargetY = targetY,
            MagicSpellLevel = spellLv,
            MagicPoison = poison,
            MagicFireLevel = 0,
            MagicWaitStartMs = nowMs,
            MagicAnimStartMs = nowMs,
            MagicHold = false
        };

        return true;
    }

    public bool TryApplyActorMagicFire(
        int recogId,
        int targetX,
        int targetY,
        byte effectType,
        byte effectNumber,
        int targetRecogId,
        int magFireLevel)
    {
        if (recogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
            return false;

        if (actor.MagicServerCode == 0)
            return false;

        _actors[recogId] = actor with
        {
            MagicServerCode = 255,
            MagicEffectNumber = effectNumber,
            MagicEffectType = effectType,
            MagicTarget = targetRecogId,
            MagicTargetX = targetX,
            MagicTargetY = targetY,
            MagicFireLevel = magFireLevel
        };

        return true;
    }

    public bool TryApplyActorMagicFireFail(int recogId)
    {
        if (recogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
            return false;

        if (actor.MagicServerCode == 0)
            return false;

        _actors[recogId] = actor with { MagicServerCode = 0 };
        return true;
    }

    public bool TryApplyActorStruck(
        int recogId,
        int hp,
        int maxHp,
        int damage,
        bool hasBody,
        MessageBodyWL body,
        long nowTimestamp,
        long nowMs,
        out ActorMarker sfxActor)
    {
        sfxActor = default;

        if (recogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
            return false;

        int feature = actor.Feature;
        int status = actor.Status;
        if (hasBody)
        {
            feature = body.Param1;
            status = body.Param2;
        }

        
        
        bool alreadyDeadAction = actor.Action is Grobal2.SM_DEATH or Grobal2.SM_NOWDEATH or Grobal2.SM_SKELETON;
        bool deadHp = hp <= 0;

        ushort action = alreadyDeadAction
            ? actor.Action
            : deadHp
                ? Grobal2.SM_NOWDEATH
                : Grobal2.SM_STRUCK;

        long actionStartTimestamp = alreadyDeadAction ? actor.ActionStartTimestamp : nowTimestamp;

        _actors[recogId] = actor with
        {
            Feature = feature,
            Status = status,
            Action = action,
            ActionStartTimestamp = actionStartTimestamp,
            Hp = hp,
            MaxHp = maxHp,
            LastDamage = damage,
            LastDamageTimestampMs = damage > 0 ? nowMs : actor.LastDamageTimestampMs
        };

        sfxActor = actor with { Feature = feature, Status = status };
        return true;
    }

    public bool TryApplyActorLifeStatus(int recogId, int hp, int maxHp, int damage, int feature, int status, long nowMs)
    {
        if (recogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
            return false;

        _actors[recogId] = actor with
        {
            Feature = feature != 0 ? feature : actor.Feature,
            Status = status != 0 ? status : actor.Status,
            Hp = hp,
            MaxHp = maxHp,
            LastDamage = damage,
            LastDamageTimestampMs = damage > 0 ? nowMs : actor.LastDamageTimestampMs
        };

        return true;
    }

    public bool RemoveActor(int recogId)
    {
        _stalls.Remove(recogId);
        return _actors.Remove(recogId);
    }

    public MirBagItemsUpdate ApplyBagItems(string bodyEncoded)
    {
        _bagItems.Clear();
        _bagSlots.AsSpan().Clear();

        int count = 0;
        var sample = new List<string>(8);

        foreach (string segment in SplitSlashSegments(bodyEncoded))
        {
            if (!EdCode.TryDecodeBuffer(segment, out ClientItem item))
                continue;

            if (item.MakeIndex != 0)
                AddItemBag(item);

            count++;
            if (sample.Count < 8 && !string.IsNullOrWhiteSpace(item.NameString))
                sample.Add(item.NameString);
        }

        RebuildBagIndex();
        return new MirBagItemsUpdate(count, sample.Count > 0 ? string.Join(", ", sample) : string.Empty);
    }

    public bool TryApplyBagLayout(ReadOnlySpan<ClientItem> savedSlots)
    {
        if (savedSlots.Length != _bagSlots.Length)
            return false;

        if (!AreSameBagItems(_bagSlots, savedSlots))
            return false;

        savedSlots.CopyTo(_bagSlots);
        ArrangeBagItems();
        RebuildBagIndex();
        return true;
    }

    public bool TryApplyAddBagItem(string bodyEncoded, out ClientItem item)
    {
        item = default;
        if (!EdCode.TryDecodeBuffer(bodyEncoded, out item))
            return false;

        if (item.MakeIndex != 0)
            AddItemBag(item);

        RebuildBagIndex();
        return true;
    }

    public bool TryApplyUpdateBagItem(string bodyEncoded, out ClientItem item)
    {
        item = default;
        if (!EdCode.TryDecodeBuffer(bodyEncoded, out item))
            return false;

        if (item.MakeIndex == 0)
            return true;

        if (!TryUpdateBagItem(item))
            AddItemBag(item);

        TryUpdateUseItemByMakeIndex(_useItems, item);
        RebuildBagIndex();
        return true;
    }

    public MirBagItemsUpdate ApplyHeroBagItems(string bodyEncoded, int bagSize)
    {
        if (bagSize >= 10)
            HeroBagSize = bagSize;

        _heroBagItems.Clear();
        _heroBagSlots.AsSpan().Clear();

        int count = 0;
        var sample = new List<string>(8);

        foreach (string segment in SplitSlashSegments(bodyEncoded))
        {
            if (!EdCode.TryDecodeBuffer(segment, out ClientItem item))
                continue;

            if (item.MakeIndex != 0)
                AddHeroItemBag(item);

            count++;
            if (sample.Count < 8 && !string.IsNullOrWhiteSpace(item.NameString))
                sample.Add(item.NameString);
        }

        RebuildHeroBagIndex();
        return new MirBagItemsUpdate(count, sample.Count > 0 ? string.Join(", ", sample) : string.Empty);
    }

    public bool TryApplyHeroAddBagItem(string bodyEncoded, out ClientItem item)
    {
        item = default;
        if (!EdCode.TryDecodeBuffer(bodyEncoded, out item))
            return false;

        if (item.MakeIndex != 0)
            AddHeroItemBag(item);

        RebuildHeroBagIndex();
        return true;
    }

    private static bool AreSameBagItems(ReadOnlySpan<ClientItem> currentSlots, ReadOnlySpan<ClientItem> savedSlots)
    {
        for (int i = 0; i < savedSlots.Length; i++)
        {
            ClientItem saved = savedSlots[i];
            if (saved.MakeIndex == 0 || string.IsNullOrWhiteSpace(saved.NameString))
                continue;

            bool ok = false;
            for (int j = 0; j < currentSlots.Length; j++)
            {
                ClientItem cur = currentSlots[j];
                if (cur.MakeIndex != saved.MakeIndex)
                    continue;
                if (!string.Equals(cur.NameString, saved.NameString, StringComparison.Ordinal))
                    continue;

                if (cur.Dura == saved.Dura && cur.DuraMax == saved.DuraMax)
                    ok = true;
                break;
            }

            if (!ok)
                return false;
        }

        for (int i = 0; i < currentSlots.Length; i++)
        {
            ClientItem cur = currentSlots[i];
            if (cur.MakeIndex == 0 || string.IsNullOrWhiteSpace(cur.NameString))
                continue;

            bool ok = false;
            for (int j = 0; j < savedSlots.Length; j++)
            {
                ClientItem saved = savedSlots[j];
                if (saved.MakeIndex != cur.MakeIndex)
                    continue;
                if (!string.Equals(saved.NameString, cur.NameString, StringComparison.Ordinal))
                    continue;

                if (saved.Dura == cur.Dura && saved.DuraMax == cur.DuraMax)
                    ok = true;
                break;
            }

            if (!ok)
                return false;
        }

        return true;
    }

    public bool TryApplyHeroUpdateBagItem(string bodyEncoded, out ClientItem item)
    {
        item = default;
        if (!EdCode.TryDecodeBuffer(bodyEncoded, out item))
            return false;

        if (item.MakeIndex == 0)
            return true;

        if (!TryUpdateHeroBagItem(item))
            AddHeroItemBag(item);

        TryUpdateUseItemByMakeIndex(_heroUseItems, item);
        RebuildHeroBagIndex();
        return true;
    }

    public bool TryApplyCounterItemChange(int makeIndex, int count, int msgNum, string name, out ClientItem item)
    {
        item = default;
        if (makeIndex == 0)
            return false;

        bool requireName = !string.IsNullOrWhiteSpace(name);

        for (int i = 0; i < _bagSlots.Length; i++)
        {
            if (_bagSlots[i].MakeIndex != makeIndex)
                continue;
            if (_bagSlots[i].S.Overlap <= 0)
                continue;
            if (requireName && !string.Equals(_bagSlots[i].NameString, name, StringComparison.Ordinal))
                continue;

            item = _bagSlots[i];
            ushort next = count < 1 ? (ushort)0 : unchecked((ushort)Math.Clamp(count, 0, ushort.MaxValue));

            if (next == 0)
            {
                item.Dura = 0;
                _bagSlots[i] = default;
            }
            else
            {
                item.Dura = next;
                _bagSlots[i] = item;
            }

            ArrangeBagItems();
            RebuildBagIndex();
            return true;
        }

        return false;
    }

    public bool TryApplyHeroCounterItemChange(int makeIndex, int count, int msgNum, string name, out ClientItem item)
    {
        item = default;
        if (makeIndex == 0)
            return false;

        bool requireName = !string.IsNullOrWhiteSpace(name);

        for (int i = 0; i < _heroBagSlots.Length; i++)
        {
            if (_heroBagSlots[i].MakeIndex != makeIndex)
                continue;
            if (_heroBagSlots[i].S.Overlap <= 0)
                continue;
            if (requireName && !string.Equals(_heroBagSlots[i].NameString, name, StringComparison.Ordinal))
                continue;

            item = _heroBagSlots[i];
            ushort next = count < 1 ? (ushort)0 : unchecked((ushort)Math.Clamp(count, 0, ushort.MaxValue));

            if (next == 0)
            {
                item.Dura = 0;
                _heroBagSlots[i] = default;
            }
            else
            {
                item.Dura = next;
                _heroBagSlots[i] = item;
            }

            ArrangeHeroBagItems();
            RebuildHeroBagIndex();
            return true;
        }

        return false;
    }

    public MirBagItemsUpdate ApplyUseItems(string bodyEncoded, bool hero)
    {
        Dictionary<int, ClientItem> target = hero ? _heroUseItems : _useItems;
        target.Clear();

        int count = 0;
        var sample = new List<string>(8);

        using IEnumerator<string> iter = SplitSlashSegments(bodyEncoded).GetEnumerator();
        while (true)
        {
            if (!iter.MoveNext())
                break;
            string indexText = iter.Current;

            if (!iter.MoveNext())
                break;
            string encodedItem = iter.Current;

            if (!int.TryParse(indexText.Trim(), out int index) || index < 0)
                continue;

            if (!EdCode.TryDecodeBuffer(encodedItem, out ClientItem item))
                continue;

            target[index] = item;
            count++;

            if (sample.Count < 8 && !string.IsNullOrWhiteSpace(item.NameString))
                sample.Add($"{index}:{item.NameString}");
        }

        return new MirBagItemsUpdate(count, sample.Count > 0 ? string.Join(", ", sample) : string.Empty);
    }

    public bool TryApplyUseItemDuraChange(int index, int newDura, int newDuraMax, bool hero, out ClientItem item)
    {
        item = default;
        if (index < 0)
            return false;

        Dictionary<int, ClientItem> target = hero ? _heroUseItems : _useItems;
        if (!target.TryGetValue(index, out item))
            return false;

        if (string.IsNullOrWhiteSpace(item.NameString))
            return false;

        item.Dura = unchecked((ushort)newDura);
        item.DuraMax = unchecked((ushort)newDuraMax);
        target[index] = item;
        return true;
    }

    public bool TryApplyUseItemDuraOnlyChange(int index, int newDura, bool hero, out ClientItem item)
    {
        item = default;
        if (index < 0)
            return false;

        Dictionary<int, ClientItem> target = hero ? _heroUseItems : _useItems;
        if (!target.TryGetValue(index, out item))
            return false;

        if (string.IsNullOrWhiteSpace(item.NameString))
            return false;

        item.Dura = unchecked((ushort)newDura);
        target[index] = item;
        return true;
    }

    public MirBagItemsUpdate ApplyShopItems(string bodyEncoded, int sellType)
    {
        ShopSellType = sellType;

        int count = 0;
        var sample = new List<string>(8);

        foreach (string segment in SplitSlashSegments(bodyEncoded))
        {
            if (!EdCode.TryDecodeBuffer(segment, out ShopItem item))
                continue;

            if (!_shopItemsByClass.TryGetValue(item.Class, out List<ShopItem>? bucket))
            {
                bucket = new List<ShopItem>();
                _shopItemsByClass[item.Class] = bucket;
            }

            bucket.Add(item);
            count++;

            if (sample.Count < 8 && !string.IsNullOrWhiteSpace(item.ItemNameString))
                sample.Add($"{item.Class}:{item.ItemNameString}");
        }

        return new MirBagItemsUpdate(count, sample.Count > 0 ? string.Join(", ", sample) : string.Empty);
    }

    public void ApplyHeroPowerUp(int energyType, int energy, int maxEnergy)
    {
        HeroEnergyType = energyType;
        HeroEnergy = energy;
        HeroMaxEnergy = maxEnergy;
    }

    public void ApplyHeroState(int heroActorId)
    {
        if (heroActorId <= 0)
            return;

        HeroActorIdSet = true;
        HeroActorId = heroActorId;
    }

    public void ApplyHeroStateDisappear(int recogId)
    {
        if (recogId != 0)
        {
            ApplyActorHide(recogId);
            return;
        }

        if (HeroActorIdSet && HeroActorId != 0)
            ApplyActorHide(HeroActorId);

        HeroActorIdSet = false;
        HeroActorId = 0;

        HeroBagSize = 0;
        _heroBagItems.Clear();
        _heroBagSlots.AsSpan().Clear();
        _heroUseItems.Clear();

        HeroEnergyType = 0;
        HeroEnergy = 0;
        HeroMaxEnergy = 0;

        HeroAbilitySet = false;
        HeroGold = 0;
        HeroJob = 0;
        HeroIPowerLevel = 0;
        HeroGloryPoint = 0;
        HeroAbility = default;
        HeroIPowerExp = 0;
        HeroNimbusExp = 0;

        HeroHitPoint = 0;
        HeroSpeedPoint = 0;
        HeroAntiPoison = 0;
        HeroPoisonRecover = 0;
        HeroHealthRecover = 0;
        HeroSpellRecover = 0;
        HeroAntiMagic = 0;
        HeroIPowerRecover = 0;
        HeroAddDamage = 0;
        HeroDecDamage = 0;
        HeroLoyalty = string.Empty;
    }

    public void ApplyHeroLoyalty(string loyalty)
    {
        HeroLoyalty = string.IsNullOrWhiteSpace(loyalty) ? "50.00%" : loyalty.Trim();
    }

    public (string NpcName, string Saying) ApplyMerchantSay(int merchantId, int face, string saying)
    {
        int x = MapCenterX;
        int y = MapCenterY;
        if (TryGetMyself(out ActorMarker myself))
        {
            x = myself.X;
            y = myself.Y;
        }

        MerchantDialogX = x;
        MerchantDialogY = y;

        if (CurrentMerchantId != merchantId)
        {
            CurrentMerchantId = merchantId;
            MerchantMode = MirMerchantMode.None;
            _merchantGoods.Clear();
            _merchantDetailItems.Clear();
            MerchantMenuTopLine = 0;
            MerchantDialogOpen = false;
        }

        string npcName = string.Empty;
        string message = saying ?? string.Empty;

        if (!string.IsNullOrEmpty(message))
        {
            int split = message.IndexOf('/');
            if (split >= 0)
            {
                npcName = message[..split].Trim();
                message = split + 1 < message.Length ? message[(split + 1)..] : string.Empty;
            }
        }

        MerchantDialogFace = face;
        MerchantNpcName = npcName;
        MerchantSaying = message;
        MerchantDialogOpen = true;

        return (npcName, message);
    }

    public void CloseMerchantDialog()
    {
        MerchantDialogOpen = false;
        MerchantMode = MirMerchantMode.None;
        CurrentMerchantId = 0;
        MerchantMenuTopLine = 0;
    }

    public void ApplyMerchantMode(int merchantId, MirMerchantMode mode)
    {
        CurrentMerchantId = merchantId;
        MerchantMode = mode;
    }

    public void ApplySellPriceQuote(int price)
    {
        LastSellPriceQuote = price;
    }

    public void ApplyBookCountQuote(int bookCount)
    {
        LastBookCountQuote = bookCount;
    }

    public void ApplyRepairCostQuote(int cost)
    {
        LastRepairCostQuote = cost;
    }

    public void ApplyBuyItemSuccess(int goldAfter, int soldOutGoodsId)
    {
        ApplyGoldChanged(goldAfter, MyGameGold);
        LastSoldOutGoodsId = soldOutGoodsId;

        for (int i = 0; i < _merchantGoods.Count; i++)
        {
            MirMerchantGoods g = _merchantGoods[i];
            if (g.Grade < 0 || g.Stock != soldOutGoodsId)
                continue;

            _merchantGoods.RemoveAt(i);
            if (i >= 0 && i < _merchantDetailItems.Count)
                _merchantDetailItems.RemoveAt(i);
            break;
        }
    }

    public MirBagItemsUpdate ApplySaveItemList(int merchantId, string bodyEncoded)
    {
        CurrentMerchantId = merchantId;
        MerchantMode = MirMerchantMode.GetSave;
        MerchantMenuTopLine = 0;

        _storageItems.Clear();
        _merchantGoods.Clear();
        _merchantDetailItems.Clear();

        int count = 0;
        var sample = new List<string>(8);

        foreach (string segment in SplitSlashSegments(bodyEncoded))
        {
            if (!EdCode.TryDecodeBuffer(segment, out ClientItem item))
                continue;

            _storageItems.Add(item);
            _merchantGoods.Add(ConvertStorageItemToGoods(item));
            count++;

            if (sample.Count < 8 && !string.IsNullOrWhiteSpace(item.NameString))
                sample.Add(item.NameString);
        }

        return new MirBagItemsUpdate(count, sample.Count > 0 ? string.Join(", ", sample) : string.Empty);
    }

    public bool TryApplyTakeBackStorageItem(int makeIndex, int index, out ClientItem item)
    {
        item = default;
        if (_storageItems.Count == 0)
            return false;

        int found = -1;
        if (index >= 0 && index < _storageItems.Count)
        {
            ClientItem candidate = _storageItems[index];
            if (makeIndex == 0 || candidate.MakeIndex == makeIndex)
                found = index;
        }

        if (found < 0 && makeIndex != 0)
            found = _storageItems.FindIndex(ci => ci.MakeIndex == makeIndex);

        if (found < 0)
            return false;

        item = _storageItems[found];
        _storageItems.RemoveAt(found);
        RebuildStorageGoods();
        return true;
    }

    public void ApplyGroupModeChanged(bool allowGroup)
    {
        AllowGroup = allowGroup;
    }

    public int ApplyGroupMembers(string bodyDecoded)
    {
        _groupMembers.Clear();
        if (string.IsNullOrWhiteSpace(bodyDecoded))
            return 0;

        foreach (string segment in SplitSlashSegments(bodyDecoded))
        {
            string name = segment.Trim();
            if (string.IsNullOrEmpty(name))
                break;
            _groupMembers.Add(name);
        }

        return _groupMembers.Count;
    }

    public void ClearGroupMembers()
    {
        _groupMembers.Clear();
    }

    public void ApplySubAbility(int recog, ushort param, ushort tag, ushort series)
    {
        MyHitPoint = (byte)(param & 0xFF);
        MySpeedPoint = (byte)((param >> 8) & 0xFF);

        MyAntiPoison = (byte)(tag & 0xFF);
        MyPoisonRecover = (byte)((tag >> 8) & 0xFF);

        MyHealthRecover = (byte)(series & 0xFF);
        MySpellRecover = (byte)((series >> 8) & 0xFF);

        ushort recogLow = (ushort)(recog & 0xFFFF);
        ushort recogHigh = (ushort)((recog >> 16) & 0xFFFF);
        MyAntiMagic = (byte)(recogLow & 0xFF);
        MyIPowerRecover = (byte)((recogLow >> 8) & 0xFF);
        MyAddDamage = (byte)(recogHigh & 0xFF);
        MyDecDamage = (byte)((recogHigh >> 8) & 0xFF);
    }

    public void ApplyHeroSubAbility(int recog, ushort param, ushort tag, ushort series)
    {
        HeroHitPoint = (byte)(param & 0xFF);
        HeroSpeedPoint = (byte)((param >> 8) & 0xFF);

        HeroAntiPoison = (byte)(tag & 0xFF);
        HeroPoisonRecover = (byte)((tag >> 8) & 0xFF);

        HeroHealthRecover = (byte)(series & 0xFF);
        HeroSpellRecover = (byte)((series >> 8) & 0xFF);

        ushort recogLow = (ushort)(recog & 0xFFFF);
        ushort recogHigh = (ushort)((recog >> 16) & 0xFFFF);
        HeroAntiMagic = (byte)(recogLow & 0xFF);
        HeroIPowerRecover = (byte)((recogLow >> 8) & 0xFF);
        HeroAddDamage = (byte)(recogHigh & 0xFF);
        HeroDecDamage = (byte)((recogHigh >> 8) & 0xFF);
    }

    public MirBagItemsUpdate ApplyMarketList(int userMode, int itemType, bool first, string bodyEncoded)
    {
        MarketUserMode = userMode;
        MarketItemType = itemType;

        if (first)
            _marketItems.Clear();

        if (string.IsNullOrEmpty(bodyEncoded))
            return default;

        string decoded = EdCode.DecodeString(bodyEncoded);
        using IEnumerator<string> iter = SplitSlashSegments(decoded).GetEnumerator();

        if (!iter.MoveNext() || !int.TryParse(iter.Current, out int countExpected) || countExpected <= 0)
        {
            MarketCurrentPage = 0;
            MarketMaxPage = 0;
            return default;
        }

        if (iter.MoveNext())
        {
            if (int.TryParse(iter.Current, out int cur))
                MarketCurrentPage = cur;
        }
        if (iter.MoveNext())
        {
            if (int.TryParse(iter.Current, out int max))
                MarketMaxPage = max;
        }

        int count = 0;
        var sample = new List<string>(8);

        for (int i = 0; i < countExpected && iter.MoveNext(); i++)
        {
            if (!EdCode.TryDecodeBuffer(iter.Current, out MarketItem item))
                continue;

            _marketItems.Add(item);
            count++;

            string name = item.Item.NameString;
            if (sample.Count < 8 && !string.IsNullOrWhiteSpace(name))
                sample.Add(name);
        }

        return new MirBagItemsUpdate(count, sample.Count > 0 ? string.Join(", ", sample) : string.Empty);
    }

    public void ClearMarket()
    {
        MarketUserMode = 0;
        MarketItemType = 0;
        MarketCurrentPage = 0;
        MarketMaxPage = 0;
        _marketItems.Clear();
    }

    public void ApplyReadMiniMapOk(int mapIndex)
    {
        if (mapIndex >= 1)
        {
            MiniMapVisible = true;
            MiniMapIndex = mapIndex - 1;
        }
        else
        {
            MiniMapVisible = false;
            MiniMapIndex = -1;
        }
    }

    public void ApplyReadMiniMapFail()
    {
        MiniMapVisible = false;
        MiniMapIndex = -1;
    }

    public void ApplyChangeGuildName(string bodyDecoded)
    {
        if (string.IsNullOrWhiteSpace(bodyDecoded))
        {
            MyGuildName = string.Empty;
            MyGuildRankName = string.Empty;
            return;
        }

        int idx = bodyDecoded.IndexOf('/', StringComparison.Ordinal);
        if (idx < 0)
        {
            MyGuildName = bodyDecoded.Trim();
            MyGuildRankName = string.Empty;
            return;
        }

        MyGuildName = bodyDecoded[..idx].Trim();
        MyGuildRankName = bodyDecoded[(idx + 1)..].Trim();
    }

    public void ApplyOpenGuildDialog(string bodyDecoded)
    {
        _guildDialogLines.Clear();
        _guildNoticeLines.Clear();

        GuildDialogName = string.Empty;
        GuildDialogFlag = string.Empty;
        GuildCommanderMode = false;

        if (string.IsNullOrEmpty(bodyDecoded))
            return;

        string[] parts = bodyDecoded.Split('\r');
        if (parts.Length > 0)
            GuildDialogName = parts[0].Trim();
        if (parts.Length > 1)
            GuildDialogFlag = parts[1].Trim();
        if (parts.Length > 2)
            GuildCommanderMode = string.Equals(parts[2].Trim(), "1", StringComparison.Ordinal);

        bool inNotice = false;
        for (int i = 3; i < parts.Length; i++)
        {
            string line = parts[i];
            if (string.Equals(line, "<Notice>", StringComparison.OrdinalIgnoreCase))
            {
                inNotice = true;
                continue;
            }

            if (line.Length > 0 && line[0] == '<' && line[^1] == '>' && !string.Equals(line, "<Notice>", StringComparison.OrdinalIgnoreCase))
                inNotice = false;

            _guildDialogLines.Add(line);
            if (inNotice)
                _guildNoticeLines.Add(line);
        }
    }

    public int ApplyGuildMemberList(string bodyDecoded)
    {
        _guildMembers.Clear();
        if (string.IsNullOrWhiteSpace(bodyDecoded))
            return 0;

        int rank = 0;
        string rankName = string.Empty;

        foreach (string segment in SplitSlashSegments(bodyDecoded))
        {
            string token = segment.Trim();
            if (token.Length == 0)
                continue;

            char prefix = token[0];
            if (prefix == '#')
            {
                if (!int.TryParse(token.AsSpan(1), out rank))
                    rank = 0;
                continue;
            }

            if (prefix == '*')
            {
                rankName = token[1..].Trim();
                continue;
            }

            _guildMembers.Add(new MirGuildMember(token, rank, rankName));
        }

        return _guildMembers.Count;
    }

    public bool TryApplyOpenBox(int param, string bodyEncoded, out int itemCount)
    {
        itemCount = 0;
        BoxOpen = false;
        BoxParam = param;
        BoxServerItemIndex = 0;
        BoxNameMaxLen = Grobal2.ItemNameLen;
        Array.Clear(_boxItems, 0, _boxItems.Length);

        if (string.IsNullOrEmpty(bodyEncoded))
            return false;

        byte[] decodedBytes = EdCode.DecodeBytes(bodyEncoded);
        ReadOnlySpan<byte> bytes = decodedBytes;
        if (bytes.IsEmpty)
            return false;

        const int boxCount = 9;
        const int trailerBytes = 10; 

        if (bytes.Length % boxCount != 0)
        {
            int bytes20 = boxCount * (20 + trailerBytes);
            int bytes14 = boxCount * (14 + trailerBytes);
            if (bytes.Length >= bytes20)
                bytes = bytes[..bytes20];
            else if (bytes.Length >= bytes14)
                bytes = bytes[..bytes14];
            else
                return false;
        }

        int itemSize = bytes.Length / boxCount;
        int maxNameLen = itemSize - trailerBytes;
        if (maxNameLen <= 0)
            return false;

        for (int i = 0; i < boxCount; i++)
        {
            int offset = i * itemSize;
            if (offset + itemSize > bytes.Length)
                return false;

            byte nameLen = bytes[offset];
            int clampedNameLen = nameLen > maxNameLen ? maxNameLen : nameLen;

            string name = clampedNameLen > 0
                ? GbkEncoding.Instance.GetString(bytes.Slice(offset + 1, clampedNameLen))
                : string.Empty;

            int rateOffset = offset + 1 + maxNameLen;
            if (rateOffset + 1 + 8 > bytes.Length)
                return false;

            byte rate = bytes[rateOffset];
            int looks = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(rateOffset + 1, 4));
            int number = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(rateOffset + 5, 4));

            _boxItems[i] = new MirBoxItem(name, rate, looks, number);
        }

        BoxOpen = true;
        BoxNameMaxLen = maxNameLen;
        itemCount = boxCount;
        return true;
    }

    public void ApplySelectBoxFlash(int itemIndex)
    {
        if (!BoxOpen)
            return;

        BoxServerItemIndex = itemIndex;
    }

    public void CloseBox()
    {
        BoxOpen = false;
        BoxParam = 0;
        BoxServerItemIndex = 0;
        BoxNameMaxLen = Grobal2.ItemNameLen;
        Array.Clear(_boxItems, 0, _boxItems.Length);
    }

    public void ApplyOpenBook(int merchantId, int bookPath, int bookPage, string bookLabel)
    {
        BookMerchantId = merchantId;
        BookPath = bookPath;
        BookPage = bookPage;
        BookLabel = bookLabel ?? string.Empty;
        BookOpen = BookPath > 0;

        if (!BookOpen)
        {
            BookMerchantId = 0;
            BookPath = 0;
            BookPage = 0;
            BookLabel = string.Empty;
        }
    }

    public void CloseBook()
    {
        BookOpen = false;
        BookMerchantId = 0;
        BookPath = 0;
        BookPage = 0;
        BookLabel = string.Empty;
    }

    public void OpenRefine() => RefineOpen = true;

    public void CloseRefine() => RefineOpen = false;

    public bool TrySetBookPage(int page)
    {
        if (!BookOpen)
            return false;

        if (BookPath == 1)
            page = Math.Clamp(page, 0, 4);
        else if (page < 0)
            page = 0;

        if (page == BookPage)
            return false;

        BookPage = page;
        return true;
    }

    public void PrepareLevelRankRequest(int page, int type)
    {
        LevelRankPage = page;
        LevelRankType = type;
        LevelRankHasData = false;
        _humanLevelRanks.Clear();
        _heroLevelRanks.Clear();
    }

    public bool TryApplyLevelRank(int page, int type, string bodyEncoded, out int count)
    {
        count = 0;
        LevelRankPage = page;
        LevelRankType = type;
        LevelRankHasData = false;
        _humanLevelRanks.Clear();
        _heroLevelRanks.Clear();

        if (page < 0 || string.IsNullOrEmpty(bodyEncoded))
            return false;

        byte[] bytes = EdCode.DecodeBytes(bodyEncoded);

        if (type is >= 0 and <= 3)
        {
            int rankSize = Marshal.SizeOf<HumanLevelRank>();
            const int rankCount = 10;
            if (bytes.Length < rankSize * rankCount)
                return false;

            for (int i = 0; i < rankCount; i++)
            {
                HumanLevelRank rank = MemoryMarshal.Read<HumanLevelRank>(bytes.AsSpan(i * rankSize, rankSize));
                string name = rank.CharNameString.Trim();
                if (name.Length == 0)
                    continue;

                _humanLevelRanks.Add(new MirHumanLevelRank(name, rank.Level, rank.Index));
            }

            count = _humanLevelRanks.Count;
            LevelRankHasData = count > 0;
            return true;
        }

        if (type is >= 4 and <= 7)
        {
            int rankSize = Marshal.SizeOf<HeroLevelRank>();
            const int rankCount = 10;
            if (bytes.Length < rankSize * rankCount)
                return false;

            for (int i = 0; i < rankCount; i++)
            {
                HeroLevelRank rank = MemoryMarshal.Read<HeroLevelRank>(bytes.AsSpan(i * rankSize, rankSize));
                string masterName = rank.MasterNameString.Trim();
                string heroName = rank.HeroNameString.Trim();
                if (masterName.Length == 0 && heroName.Length == 0)
                    continue;

                _heroLevelRanks.Add(new MirHeroLevelRank(masterName, heroName, rank.Level, rank.Index));
            }

            count = _heroLevelRanks.Count;
            LevelRankHasData = count > 0;
            return true;
        }

        return false;
    }

    public bool TryApplyUserState(string bodyEncoded)
    {
        if (string.IsNullOrEmpty(bodyEncoded))
            return false;

        byte[] bytes = EdCode.DecodeBytes(bodyEncoded);
        int headerSize = Marshal.SizeOf<UserStateInfoHeader>();
        if (bytes.Length < headerSize)
            return false;

        UserStateInfoHeader header = MemoryMarshal.Read<UserStateInfoHeader>(bytes);

        int itemSize = Marshal.SizeOf<ClientItem>();
        const int titlesCount = 6;
        int titleSize = Marshal.SizeOf<HumTitle>();
        int titlesBytes = titlesCount * titleSize;
        int trailerBytes = 1 + titlesBytes; 

        int remaining = bytes.Length - headerSize;
        int itemCount;
        int trailerOffset = -1;

        if (remaining >= trailerBytes && (remaining - trailerBytes) % itemSize == 0)
        {
            itemCount = (remaining - trailerBytes) / itemSize;
            trailerOffset = headerSize + (itemCount * itemSize);
        }
        else
        {
            itemCount = remaining / itemSize;
            int leftover = remaining - (itemCount * itemSize);
            if (leftover == trailerBytes)
                trailerOffset = headerSize + (itemCount * itemSize);
        }

        var useItems = new ClientItem[itemCount];
        for (int i = 0; i < itemCount; i++)
        {
            useItems[i] = MemoryMarshal.Read<ClientItem>(bytes.AsSpan(headerSize + i * itemSize, itemSize));
        }

        byte activeTitle = 0;
        HumTitle[] titles = Array.Empty<HumTitle>();

        if (trailerOffset >= 0 && bytes.Length >= trailerOffset + trailerBytes)
        {
            activeTitle = bytes[trailerOffset];

            titles = new HumTitle[titlesCount];
            int titlesOffset = trailerOffset + 1;
            for (int i = 0; i < titlesCount; i++)
            {
                titles[i] = MemoryMarshal.Read<HumTitle>(bytes.AsSpan(titlesOffset + (i * titleSize), titleSize));
            }

            int compact = 0;
            for (int i = 0; i < titles.Length; i++)
            {
                if (titles[i].Index > 0)
                    titles[compact++] = titles[i];
            }

            if (compact != titles.Length)
                Array.Resize(ref titles, compact);
        }

        _lastUserState = new MirUserStateSnapshot
        {
            Feature = header.Feature,
            UserName = header.UserNameString.Trim(),
            NameColor = unchecked((byte)header.NameColor),
            GuildName = header.GuildNameString.Trim(),
            GuildRankName = header.GuildRankNameString.Trim(),
            Gender = header.Gender,
            HumAttr = header.HumAttr,
            ActiveTitle = activeTitle,
            Titles = titles,
            UseItems = useItems,
        };

        return true;
    }

    public void OpenDeal(string dealWho)
    {
        DealOpen = true;
        DealWho = dealWho.Trim();
        DealMyGold = 0;
        DealRemoteGold = 0;
        _dealMyItems.Clear();
        _dealRemoteItems.Clear();
    }

    public void CancelDealAndRestoreGold()
    {
        if (!DealOpen)
            return;

        if (_dealMyItems.Count > 0)
        {
            foreach (ClientItem item in _dealMyItems)
                AddItemBag(item);

            _dealMyItems.Clear();
            RebuildBagIndex();
        }

        if (DealMyGold > 0)
        {
            ApplyGoldChanged(MyGold + DealMyGold, MyGameGold);
            DealMyGold = 0;
        }

        DealRemoteGold = 0;
        DealOpen = false;
        DealWho = string.Empty;
        _dealRemoteItems.Clear();
    }

    public void CloseDeal()
    {
        DealOpen = false;
        DealWho = string.Empty;
        DealMyGold = 0;
        DealRemoteGold = 0;
        _dealMyItems.Clear();
        _dealRemoteItems.Clear();
    }

    public bool TryApplyDealMyAddItem(ClientItem item)
    {
        if (item.MakeIndex == 0)
            return false;

        for (int i = 0; i < _dealMyItems.Count; i++)
        {
            if (_dealMyItems[i].MakeIndex == item.MakeIndex)
                return true;
        }

        if (_dealMyItems.Count >= 12)
            return false;

        ClientItem fromBag = item;
        if (TryRemoveBagItemByMakeIndex(item.MakeIndex, out ClientItem removed))
            fromBag = removed;

        _dealMyItems.Add(fromBag);
        return true;
    }

    public bool TryApplyDealMyDelItem(int makeIndex, out ClientItem item)
    {
        item = default;
        if (makeIndex == 0)
            return false;

        for (int i = 0; i < _dealMyItems.Count; i++)
        {
            if (_dealMyItems[i].MakeIndex != makeIndex)
                continue;

            item = _dealMyItems[i];
            _dealMyItems.RemoveAt(i);

            AddItemBag(item);
            RebuildBagIndex();
            return true;
        }

        return false;
    }

    public bool TryApplyDealRemoteAddItem(string bodyEncoded, out ClientItem item)
    {
        item = default;
        if (!EdCode.TryDecodeBuffer(bodyEncoded, out item))
            return false;

        string name = item.NameString;
        if (!string.IsNullOrWhiteSpace(name) && item.S.Overlap > 0)
        {
            for (int i = 0; i < _dealRemoteItems.Count; i++)
            {
                if (string.Equals(_dealRemoteItems[i].NameString, name, StringComparison.Ordinal))
                {
                    ClientItem existing = _dealRemoteItems[i];
                    existing.MakeIndex = item.MakeIndex;
                    _dealRemoteItems[i] = existing;
                    return true;
                }
            }
        }

        if (_dealRemoteItems.Count < 20)
            _dealRemoteItems.Add(item);

        return true;
    }

    public bool TryApplyDealRemoteDelItem(string bodyEncoded, out ClientItem item)
    {
        item = default;
        if (!EdCode.TryDecodeBuffer(bodyEncoded, out item))
            return false;

        string name = item.NameString;
        for (int i = 0; i < _dealRemoteItems.Count; i++)
        {
            if (_dealRemoteItems[i].MakeIndex == item.MakeIndex &&
                string.Equals(_dealRemoteItems[i].NameString, name, StringComparison.Ordinal))
            {
                _dealRemoteItems.RemoveAt(i);
                return true;
            }
        }

        return true;
    }

    public void ApplyDealRemoteGold(int gold)
    {
        DealRemoteGold = gold;
    }

    public void ApplyDealMyGoldChanged(int dealGold, int goldAfter)
    {
        DealMyGold = dealGold;
        ApplyGoldChanged(goldAfter, MyGameGold);
    }

    public MirBagItemsUpdate ApplyMerchantGoodsList(int merchantId, int expectedCount, string bodyEncoded, MirMerchantMode mode)
    {
        CurrentMerchantId = merchantId;
        MerchantMode = mode;
        MerchantMenuTopLine = 0;

        _merchantGoods.Clear();
        _merchantDetailItems.Clear();

        string decoded = EdCode.DecodeString(bodyEncoded);
        if (string.IsNullOrWhiteSpace(decoded))
            return default;

        int count = 0;
        var sample = new List<string>(8);

        using IEnumerator<string> iter = SplitSlashSegments(decoded).GetEnumerator();
        while (true)
        {
            if (!iter.MoveNext())
                break;
            string gname = iter.Current.Trim();

            if (!iter.MoveNext())
                break;
            string gsub = iter.Current.Trim();

            if (!iter.MoveNext())
                break;
            string gprice = iter.Current.Trim();

            if (!iter.MoveNext())
                break;
            string gstock = iter.Current.Trim();

            if (string.IsNullOrEmpty(gname) || string.IsNullOrEmpty(gprice) || string.IsNullOrEmpty(gstock))
                break;

            int submenu = int.TryParse(gsub, out int submenuValue) ? submenuValue : 0;
            int price = int.TryParse(gprice, out int priceValue) ? priceValue : 0;
            int stock = int.TryParse(gstock, out int stockValue) ? stockValue : 0;

            _merchantGoods.Add(new MirMerchantGoods(gname, submenu, price, stock, Grade: -1));
            count++;

            if (sample.Count < 8)
                sample.Add(gname);
        }

        return new MirBagItemsUpdate(count, sample.Count > 0 ? string.Join(", ", sample) : string.Empty);
    }

    public MirBagItemsUpdate ApplyMerchantDetailGoodsList(int merchantId, int countExpected, int topLine, string bodyEncoded)
    {
        CurrentMerchantId = merchantId;
        MerchantMode = MirMerchantMode.DetailMenu;
        MerchantMenuTopLine = topLine;

        _merchantGoods.Clear();
        _merchantDetailItems.Clear();

        string decoded = EdCode.DecodeString(bodyEncoded);
        if (string.IsNullOrWhiteSpace(decoded))
            return default;

        int count = 0;
        var sample = new List<string>(8);

        foreach (string segment in SplitSlashSegments(decoded))
        {
            if (!EdCode.TryDecodeBuffer(segment, out ClientItem item))
                continue;

            _merchantDetailItems.Add(item);

            string name = item.NameString;
            int price = item.DuraMax;
            int stock = item.MakeIndex;
            int grade = (int)Math.Round(item.Dura / 1000.0);
            _merchantGoods.Add(new MirMerchantGoods(name, SubMenu: 0, price, stock, grade));

            count++;
            if (sample.Count < 8 && !string.IsNullOrWhiteSpace(name))
                sample.Add(name);
        }

        return new MirBagItemsUpdate(count, sample.Count > 0 ? string.Join(", ", sample) : string.Empty);
    }

    public MirBagItemsUpdate ApplyMagics(string bodyEncoded, bool hero)
    {
        Dictionary<(byte Class, ushort MagicId), ClientMagic> targetById = hero ? _heroMagicsById : _myMagicsById;
        List<ClientMagic> targetMagicList = hero ? _heroMagics : _myMagics;
        List<ClientMagic> targetIpList = hero ? _heroIpMagics : _myIpMagics;

        targetById.Clear();
        targetMagicList.Clear();
        targetIpList.Clear();

        int count = 0;
        var sample = new List<string>(8);

        foreach (string segment in SplitSlashSegments(bodyEncoded))
        {
            if (!EdCode.TryDecodeBuffer(segment, out ClientMagic magic))
                continue;

            var key = (magic.Def.Class, magic.Def.MagicId);
            targetById[key] = magic;

            if (magic.Def.Class == 0)
                targetMagicList.Add(magic);
            else
                targetIpList.Add(magic);

            count++;
            if (sample.Count < 8 && !string.IsNullOrWhiteSpace(magic.Def.MagicNameString))
                sample.Add($"{magic.Def.Class}:{magic.Def.MagicNameString}(lv{magic.Level})");
        }

        if (!hero)
            MoveMagicToFront(targetMagicList, 67);

        return new MirBagItemsUpdate(count, sample.Count > 0 ? string.Join(", ", sample) : string.Empty);
    }

    public bool TryApplyAddMagic(string bodyEncoded, bool hero, out ClientMagic magic)
    {
        magic = default;
        if (!EdCode.TryDecodeBuffer(bodyEncoded, out magic))
            return false;

        Dictionary<(byte Class, ushort MagicId), ClientMagic> targetById = hero ? _heroMagicsById : _myMagicsById;
        List<ClientMagic> targetMagicList = hero ? _heroMagics : _myMagics;
        List<ClientMagic> targetIpList = hero ? _heroIpMagics : _myIpMagics;

        var key = (magic.Def.Class, magic.Def.MagicId);
        targetById[key] = magic;

        RemoveMagicFromLists(targetMagicList, targetIpList, key);

        if (magic.Def.Class == 0)
            targetMagicList.Add(magic);
        else
            targetIpList.Add(magic);

        if (!hero)
            MoveMagicToFront(targetMagicList, 67);

        return true;
    }

    public bool TryApplyDelMagic(int magicId, int magicClass, bool hero)
    {
        
        var key = ((byte)0, (ushort)(magicId & 0xFFFF));

        Dictionary<(byte Class, ushort MagicId), ClientMagic> targetById = hero ? _heroMagicsById : _myMagicsById;
        List<ClientMagic> targetMagicList = hero ? _heroMagics : _myMagics;
        List<ClientMagic> targetIpList = hero ? _heroIpMagics : _myIpMagics;

        bool removed = targetById.Remove(key);
        removed |= RemoveMagicFromLists(targetMagicList, targetIpList, key);
        return removed;
    }

    public bool TryApplyConvertMagic(int fromClass, int toClass, int fromId, int toId, string bodyEncoded, bool hero, out ClientMagic magic)
    {
        magic = default;
        if (!EdCode.TryDecodeBuffer(bodyEncoded, out magic))
            return false;

        Dictionary<(byte Class, ushort MagicId), ClientMagic> targetById = hero ? _heroMagicsById : _myMagicsById;
        List<ClientMagic> targetMagicList = hero ? _heroMagics : _myMagics;
        List<ClientMagic> targetIpList = hero ? _heroIpMagics : _myIpMagics;

        
        var fromKey = ((byte)0, (ushort)(fromId & 0xFFFF));
        var toKey = ((byte)0, (ushort)(toId & 0xFFFF));

        targetById.Remove(fromKey);
        RemoveMagicFromLists(targetMagicList, targetIpList, fromKey);

        magic.Def.MagicId = toKey.Item2;

        targetById[toKey] = magic;
        RemoveMagicFromLists(targetMagicList, targetIpList, toKey);

        targetMagicList.Add(magic);

        if (!hero)
            MoveMagicToFront(targetMagicList, 67);

        return true;
    }

    public bool TryApplyMagicLvExp(int magicIdEncoded, int level, int train, bool hero)
    {
        byte kind = (byte)((magicIdEncoded >> 16) & 0xFFFF);
        ushort id = (ushort)(magicIdEncoded & 0xFFFF);
        var key = (kind, id);

        Dictionary<(byte Class, ushort MagicId), ClientMagic> targetById = hero ? _heroMagicsById : _myMagicsById;
        List<ClientMagic> targetMagicList = hero ? _heroMagics : _myMagics;
        List<ClientMagic> targetIpList = hero ? _heroIpMagics : _myIpMagics;

        if (!targetById.TryGetValue(key, out ClientMagic magic) && kind != 0)
        {
            key = (0, id);
            if (!targetById.TryGetValue(key, out magic))
                return false;
        }

        magic.Level = (byte)Math.Clamp(level, 0, byte.MaxValue);
        magic.CurTrain = train;
        targetById[key] = magic;

        ReplaceMagicInLists(targetMagicList, targetIpList, key, magic);
        return true;
    }

    public bool TryApplyMagicMaxLv(int magicIdEncoded, int maxLevel, bool hero)
    {
        ushort id = (ushort)(magicIdEncoded & 0xFFFF);
        var key = ((byte)0, id);

        Dictionary<(byte Class, ushort MagicId), ClientMagic> targetById = hero ? _heroMagicsById : _myMagicsById;
        List<ClientMagic> targetMagicList = hero ? _heroMagics : _myMagics;
        List<ClientMagic> targetIpList = hero ? _heroIpMagics : _myIpMagics;

        if (!targetById.TryGetValue(key, out ClientMagic magic))
            return false;

        magic.Def.TrainLv = (byte)Math.Clamp(maxLevel, 0, byte.MaxValue);
        targetById[key] = magic;

        ReplaceMagicInLists(targetMagicList, targetIpList, key, magic);
        return true;
    }

    public bool TrySetMagicKey(bool hero, ushort magicId, byte newKey, out ushort clearedMagicId)
    {
        clearedMagicId = 0;

        var key = ((byte)0, magicId);

        Dictionary<(byte Class, ushort MagicId), ClientMagic> targetById = hero ? _heroMagicsById : _myMagicsById;
        List<ClientMagic> targetMagicList = hero ? _heroMagics : _myMagics;
        List<ClientMagic> targetIpList = hero ? _heroIpMagics : _myIpMagics;

        if (!targetById.TryGetValue(key, out ClientMagic magic))
            return false;

        if (newKey != 0)
        {
            for (int i = 0; i < targetMagicList.Count; i++)
            {
                ClientMagic other = targetMagicList[i];
                if (other.Def.MagicId == magicId || other.Key != (char)newKey)
                    continue;

                other.Key = '\0';
                var otherKey = ((byte)0, other.Def.MagicId);
                targetById[otherKey] = other;
                ReplaceMagicInLists(targetMagicList, targetIpList, otherKey, other);
                clearedMagicId = other.Def.MagicId;
                break;
            }
        }

        magic.Key = (char)newKey;
        targetById[key] = magic;
        ReplaceMagicInLists(targetMagicList, targetIpList, key, magic);
        return true;
    }

    public bool TryApplyBagItemRemove(string bodyEncoded, out ClientItem item)
    {
        item = default;
        if (!EdCode.TryDecodeBuffer(bodyEncoded, out item))
            return false;

        if (item.MakeIndex != 0)
        {
            if (RemoveBagItem(item.MakeIndex))
                ArrangeBagItems();

            RemoveUseItemByMakeIndex(_useItems, item.MakeIndex);
        }

        RebuildBagIndex();
        return true;
    }

    public bool TryApplyHeroBagItemRemove(string bodyEncoded, out ClientItem item)
    {
        item = default;
        if (!EdCode.TryDecodeBuffer(bodyEncoded, out item))
            return false;

        if (item.MakeIndex != 0)
        {
            if (RemoveHeroBagItem(item.MakeIndex))
                ArrangeHeroBagItems();

            RemoveUseItemByMakeIndex(_heroUseItems, item.MakeIndex);
        }

        RebuildHeroBagIndex();
        return true;
    }

    public bool TryRemoveBagItemByMakeIndex(int makeIndex, out ClientItem item)
    {
        item = default;
        if (makeIndex == 0)
            return false;

        for (int i = _bagSlots.Length - 1; i >= 0; i--)
        {
            if (_bagSlots[i].MakeIndex != makeIndex)
                continue;

            item = _bagSlots[i];
            _bagSlots[i] = default;

            ArrangeBagItems();
            RebuildBagIndex();
            return true;
        }

        return false;
    }

    public void RestoreBagItem(ClientItem item)
    {
        if (item.MakeIndex == 0)
            return;

        if (_bagItems.ContainsKey(item.MakeIndex))
            return;

        AddItemBag(item);
        RebuildBagIndex();
    }

    public void RestoreHeroBagItem(ClientItem item)
    {
        if (item.MakeIndex == 0)
            return;

        if (_heroBagItems.ContainsKey(item.MakeIndex))
            return;

        AddHeroItemBag(item);
        RebuildHeroBagIndex();
    }

    public bool TryRemoveUseItemBySlot(int where, bool hero, out ClientItem item)
    {
        item = default;

        if (where < Grobal2.U_DRESS || where > Grobal2.U_CHARM)
            return false;

        Dictionary<int, ClientItem> useItems = hero ? _heroUseItems : _useItems;
        if (!useItems.TryGetValue(where, out item) || item.MakeIndex == 0)
        {
            item = default;
            return false;
        }

        useItems.Remove(where);
        return true;
    }

    public void SetUseItemSlot(int where, bool hero, ClientItem item)
    {
        if (where < Grobal2.U_DRESS || where > Grobal2.U_CHARM)
            return;

        Dictionary<int, ClientItem> useItems = hero ? _heroUseItems : _useItems;
        if (item.MakeIndex == 0)
            useItems.Remove(where);
        else
            useItems[where] = item;
    }

    public bool TryRemoveHeroBagItemByMakeIndex(int makeIndex, out ClientItem item)
    {
        item = default;
        if (makeIndex == 0)
            return false;

        for (int i = _heroBagSlots.Length - 1; i >= 0; i--)
        {
            if (_heroBagSlots[i].MakeIndex != makeIndex)
                continue;

            item = _heroBagSlots[i];
            _heroBagSlots[i] = default;

            ArrangeHeroBagItems();
            RebuildHeroBagIndex();
            return true;
        }

        return false;
    }

    public bool TrySwapBagSlots(int indexA, int indexB, bool hero)
    {
        if (indexA == indexB)
            return false;

        if (hero)
        {
            int limit = Math.Max(0, _heroBagSlots.Length - 6);
            if (HeroBagSize >= 10)
                limit = Math.Min(HeroBagSize, limit);

            if ((uint)indexA >= (uint)limit || (uint)indexB >= (uint)limit)
                return false;

            (_heroBagSlots[indexA], _heroBagSlots[indexB]) = (_heroBagSlots[indexB], _heroBagSlots[indexA]);
            RebuildHeroBagIndex();
            return true;
        }

        if ((uint)indexA >= (uint)_bagSlots.Length || (uint)indexB >= (uint)_bagSlots.Length)
            return false;

        (_bagSlots[indexA], _bagSlots[indexB]) = (_bagSlots[indexB], _bagSlots[indexA]);
        RebuildBagIndex();
        return true;
    }

    public bool TryGetBagSlot(int index, bool hero, out ClientItem item)
    {
        item = default;

        if (hero)
        {
            int limit = Math.Max(0, _heroBagSlots.Length - 6);
            if (HeroBagSize >= 10)
                limit = Math.Min(HeroBagSize, limit);

            if ((uint)index >= (uint)limit)
                return false;

            item = _heroBagSlots[index];
            return true;
        }

        if ((uint)index >= (uint)_bagSlots.Length)
            return false;

        item = _bagSlots[index];
        return true;
    }

    public MirBagItemsUpdate ApplyDelItemList(string bodyEncoded) => ApplyDelItemList(bodyEncoded, onlyBag: true);

    public MirBagItemsUpdate ApplyDelItemList(string bodyEncoded, bool onlyBag)
    {
        string decoded = EdCode.DecodeString(bodyEncoded);
        if (string.IsNullOrWhiteSpace(decoded))
            return default;

        int removed = 0;
        var sample = new List<string>(8);
        string? pendingName = null;

        foreach (string segment in SplitSlashSegments(decoded))
        {
            if (pendingName == null)
            {
                pendingName = segment.Trim();
                continue;
            }

            if (int.TryParse(segment.Trim(), out int makeIndex) && makeIndex != 0)
            {
                if (RemoveBagItem(makeIndex))
                {
                    removed++;
                    if (sample.Count < 8 && !string.IsNullOrWhiteSpace(pendingName))
                        sample.Add(pendingName);
                }

                if (!onlyBag)
                    RemoveUseItemByMakeIndex(_useItems, makeIndex);
            }

            pendingName = null;
        }

        ArrangeBagItems();
        RebuildBagIndex();
        return new MirBagItemsUpdate(removed, sample.Count > 0 ? string.Join(", ", sample) : string.Empty);
    }

    public MirBagItemsUpdate ApplyHeroDelItemList(string bodyEncoded, bool onlyBag)
    {
        string decoded = EdCode.DecodeString(bodyEncoded);
        if (string.IsNullOrWhiteSpace(decoded))
            return default;

        int removed = 0;
        var sample = new List<string>(8);
        string? pendingName = null;

        foreach (string segment in SplitSlashSegments(decoded))
        {
            if (pendingName == null)
            {
                pendingName = segment.Trim();
                continue;
            }

            if (int.TryParse(segment.Trim(), out int makeIndex) && makeIndex != 0)
            {
                if (RemoveHeroBagItem(makeIndex))
                {
                    removed++;
                    if (sample.Count < 8 && !string.IsNullOrWhiteSpace(pendingName))
                        sample.Add(pendingName);
                }

                if (!onlyBag)
                    RemoveUseItemByMakeIndex(_heroUseItems, makeIndex);
            }

            pendingName = null;
        }

        ArrangeHeroBagItems();
        RebuildHeroBagIndex();
        return new MirBagItemsUpdate(removed, sample.Count > 0 ? string.Join(", ", sample) : string.Empty);
    }

    public void ApplyItemShow(int id, int x, int y, int looks, string name, long nowMs)
    {
        if (id <= 0)
            return;

        if (_dropItems.ContainsKey(id))
            return;

        _dropItems[id] = new DropItemMarker(id, x, y, looks, name.Trim(), nowMs);
    }

    public void ApplyItemHide(int id)
    {
        if (id <= 0)
            return;
        _dropItems.Remove(id);
    }

    public void ApplyShowEvent(int id, int x, int y, int eventType, int eventParam, int eventLevel, int dir, long nowMs)
    {
        if (id <= 0)
            return;

        _mapEvents[id] = new MapEventMarker(id, x, y, eventType, eventParam, eventLevel, dir, nowMs);
    }

    public void ApplyHideEvent(int id)
    {
        if (id <= 0)
            return;

        _mapEvents.Remove(id);
    }

    public void SetDoorOverride(int mapKey, bool open) => _doorOpenOverrides[mapKey] = open;

    public void ApplyMapDescription(int musicId, string title)
    {
        MapMusicId = musicId;
        MapTitle = title.Trim();
    }

    public void ApplyGameGoldName(int gold, int point, string body)
    {
        string goldName = GameGoldName;
        string pointName = GamePointName;

        if (!string.IsNullOrWhiteSpace(body))
        {
            body = body.Replace('\n', '\r');
            int split = body.IndexOf('\r');
            if (split >= 0)
            {
                goldName = body[..split].Trim();
                pointName = split + 1 < body.Length ? body[(split + 1)..].Trim() : string.Empty;
            }
            else
            {
                goldName = body.Trim();
                pointName = string.Empty;
            }
        }

        GameGold = gold;
        GamePoint = point;
        GameGoldName = goldName;
        GamePointName = pointName;
    }

    public void ApplyAbility(int gold, byte job, byte iPowerLevel, uint gameGold, Ability ability)
    {
        AbilitySet = true;
        MyAbility = ability;
        MyGold = gold;
        MyJob = job;
        MyIPowerLevel = iPowerLevel;
        MyGameGold = gameGold;

        MyLevel = ability.Level;
        MyHp = ability.HP;
        MyMaxHp = ability.MaxHP;
        MyMp = ability.MP;
        MyMaxMp = ability.MaxMP;
        MyExp = ability.Exp;
        MyMaxExp = ability.MaxExp;
        MyWeight = ability.Weight;
        MyMaxWeight = ability.MaxWeight;
        MyWearWeight = ability.WearWeight;
        MyMaxWearWeight = ability.MaxWearWeight;
        MyHandWeight = ability.HandWeight;
        MyMaxHandWeight = ability.MaxHandWeight;

        UpdateHudOverlayText();

        if (MyselfRecogIdSet && MyselfRecogId != 0 && _actors.TryGetValue(MyselfRecogId, out ActorMarker myself))
            _actors[MyselfRecogId] = myself with { Hp = MyHp, MaxHp = MyMaxHp };
    }

    public void ApplyWinExp(int exp)
    {
        MyExp = exp;
        if (AbilitySet)
        {
            Ability ability = MyAbility;
            ability.Exp = exp;
            MyAbility = ability;
            UpdateHudOverlayText();
        }
    }

    public void ApplyLevelUp(int level)
    {
        MyLevel = level;
        if (AbilitySet)
        {
            Ability ability = MyAbility;
            ability.Level = level < 0 ? (ushort)0 : unchecked((ushort)Math.Min(level, ushort.MaxValue));
            MyAbility = ability;
            UpdateHudOverlayText();
        }
    }

    public void ApplyWinIpExp(int exp, ushort magicRange)
    {
        MyIPowerExp = exp;
        if (magicRange is >= 3 and <= 28)
            MagicRange = magicRange;
    }

    public void ApplyHeroWinIpExp(int exp)
    {
        HeroIPowerExp = exp;
    }

    public void ApplyWinNimbusExp(int exp)
    {
        MyNimbusExp = exp;
    }

    public void ApplyHeroWinNimbusExp(int exp)
    {
        HeroNimbusExp = exp;
    }

    public void ApplyOldAbility(int gold, byte job, byte iPowerLevel, uint gameGold, OldAbility ability)
    {
        AbilitySet = true;
        MyAbility = ConvertOldAbility(ability);
        MyGold = gold;
        MyJob = job;
        MyIPowerLevel = iPowerLevel;
        MyGameGold = gameGold;

        MyLevel = ability.Level;
        MyHp = ability.HP;
        MyMaxHp = ability.MaxHP;
        MyMp = ability.MP;
        MyMaxMp = ability.MaxMP;
        MyExp = (int)Math.Min(int.MaxValue, ability.Exp);
        MyMaxExp = (int)Math.Min(int.MaxValue, ability.MaxExp);
        MyWeight = ability.Weight;
        MyMaxWeight = ability.MaxWeight;
        MyWearWeight = ability.WearWeight;
        MyMaxWearWeight = ability.MaxWearWeight;
        MyHandWeight = ability.HandWeight;
        MyMaxHandWeight = ability.MaxHandWeight;

        UpdateHudOverlayText();

        if (MyselfRecogIdSet && MyselfRecogId != 0 && _actors.TryGetValue(MyselfRecogId, out ActorMarker myself))
            _actors[MyselfRecogId] = myself with { Hp = MyHp, MaxHp = MyMaxHp };
    }

    public void ApplyHeroAbility(int gold, byte job, byte iPowerLevel, ushort gloryPoint, Ability ability)
    {
        HeroAbilitySet = true;
        HeroGold = gold;
        HeroJob = job;
        HeroIPowerLevel = iPowerLevel;
        HeroGloryPoint = gloryPoint;
        HeroAbility = ability;

        if (HeroActorIdSet && HeroActorId != 0 && _actors.TryGetValue(HeroActorId, out ActorMarker hero))
            _actors[HeroActorId] = hero with { Hp = ability.HP, MaxHp = ability.MaxHP };
    }

    public void ApplyHeroWinExp(int exp)
    {
        Ability hero = HeroAbility;
        hero.Exp = exp;
        HeroAbility = hero;
    }

    public void ApplyHeroLevelUp(int level)
    {
        Ability hero = HeroAbility;
        hero.Level = level < 0 ? (ushort)0 : unchecked((ushort)Math.Min(level, ushort.MaxValue));
        HeroAbility = hero;
    }

    public void ApplyHealthSpellChanged(int recogId, int hp, int mp, int maxHp)
    {
        if (recogId == 0)
            return;

        if (_actors.TryGetValue(recogId, out ActorMarker actor))
            _actors[recogId] = actor with { Hp = hp, MaxHp = maxHp };

        if (MyselfRecogIdSet && recogId == MyselfRecogId)
        {
            AbilitySet = true;
            MyHp = hp;
            MyMaxHp = maxHp;
            MyMp = mp;
            if (MyMaxMp < MyMp)
                MyMaxMp = MyMp;

            Ability ability = MyAbility;
            ability.HP = unchecked((ushort)Math.Clamp(hp, 0, ushort.MaxValue));
            ability.MaxHP = unchecked((ushort)Math.Clamp(maxHp, 0, ushort.MaxValue));
            ability.MP = unchecked((ushort)Math.Clamp(mp, 0, ushort.MaxValue));
            ability.MaxMP = unchecked((ushort)Math.Clamp(MyMaxMp, 0, ushort.MaxValue));
            MyAbility = ability;

            UpdateHudOverlayText();
        }
    }

    public void ApplyInternalPower(int recogId, int iPower)
    {
        if (recogId == 0)
            return;

        if (_actors.TryGetValue(recogId, out ActorMarker actor))
            _actors[recogId] = actor with { IPower = iPower };
    }

    public void ApplyOpenHealth(int recogId, int hp, int maxHp)
    {
        if (recogId == 0)
            return;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
            return;

        bool isMyself = MyselfRecogIdSet && recogId == MyselfRecogId;

        ActorMarker updated = actor with { OpenHealth = true };
        if (!isMyself)
            updated = updated with { Hp = hp, MaxHp = maxHp };

        _actors[recogId] = updated;
    }

    public void ApplyCloseHealth(int recogId)
    {
        if (recogId == 0)
            return;

        if (_actors.TryGetValue(recogId, out ActorMarker actor))
            _actors[recogId] = actor with { OpenHealth = false };
    }

    public void ApplyInstanceHealthGauge(int recogId, int hp, int maxHp, long nowMs)
    {
        if (recogId == 0)
            return;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
            return;

        bool isMyself = MyselfRecogIdSet && recogId == MyselfRecogId;

        ActorMarker updated = actor with
        {
            InstanceOpenHealth = true,
            OpenHealthStartMs = nowMs,
            OpenHealthDurationMs = 2_000
        };

        if (!isMyself)
            updated = updated with { Hp = hp, MaxHp = maxHp };

        _actors[recogId] = updated;
    }

    public bool TryQueueChangeFace(int recogId, int newRecogId, int feature, int status, long nowTimestamp)
    {
        if (recogId == 0 || newRecogId == 0)
            return false;

        if (!_actors.TryGetValue(recogId, out ActorMarker actor))
            return false;

        if (newRecogId == recogId)
        {
            _actors[recogId] = actor with { Feature = feature, Status = status };
            return true;
        }

        _pendingChangeFacesByActorId[recogId] = new PendingChangeFace(newRecogId, feature, status, nowTimestamp);
        _changingFaceActorIds.Add(newRecogId);
        return true;
    }

    private void ApplyChangeFaceNow(int recogId, int newRecogId, int feature, int status, long nowTimestamp, ActorMarker actor)
    {
        ActorMarker updated = actor with
        {
            Feature = feature,
            Status = status,
            FromX = actor.X,
            FromY = actor.Y,
            Action = Grobal2.SM_TURN,
            ActionStartTimestamp = nowTimestamp
        };

        _actors.Remove(recogId);
        _actors[newRecogId] = updated;

        if (MyselfRecogIdSet && recogId == MyselfRecogId)
            MyselfRecogId = newRecogId;

        if (HeroActorIdSet && recogId == HeroActorId)
            HeroActorId = newRecogId;

        if (UserStallActorId == recogId)
            UserStallActorId = newRecogId;

        if (_stalls.TryGetValue(recogId, out StallActorMarker stall))
        {
            _stalls.Remove(recogId);
            _stalls[newRecogId] = stall;
        }
    }

    public void ApplyWeightChanged(int weight, int wearWeight, int handWeight)
    {
        MyWeight = weight;
        MyWearWeight = wearWeight;
        MyHandWeight = handWeight;

        if (AbilitySet)
        {
            Ability ability = MyAbility;
            ability.Weight = unchecked((ushort)Math.Clamp(weight, 0, ushort.MaxValue));
            ability.WearWeight = unchecked((ushort)Math.Clamp(wearWeight, 0, ushort.MaxValue));
            ability.HandWeight = unchecked((ushort)Math.Clamp(handWeight, 0, ushort.MaxValue));
            MyAbility = ability;
            UpdateHudOverlayText();
        }
    }

    private static Ability ConvertOldAbility(OldAbility ability)
    {
        static int ConvertPackedByteRange(ushort value)
        {
            int lo = value & 0xFF;
            int hi = (value >> 8) & 0xFF;
            return (hi << 16) | lo;
        }

        return new Ability
        {
            Level = ability.Level,
            AC = ConvertPackedByteRange(ability.AC),
            MAC = ConvertPackedByteRange(ability.MAC),
            DC = ConvertPackedByteRange(ability.DC),
            MC = ConvertPackedByteRange(ability.MC),
            SC = ConvertPackedByteRange(ability.SC),
            HP = ability.HP,
            MP = ability.MP,
            MaxHP = ability.MaxHP,
            MaxMP = ability.MaxMP,
            Exp = (int)Math.Min(int.MaxValue, ability.Exp),
            MaxExp = (int)Math.Min(int.MaxValue, ability.MaxExp),
            Weight = ability.Weight,
            MaxWeight = ability.MaxWeight,
            WearWeight = ability.WearWeight,
            MaxWearWeight = ability.MaxWearWeight,
            HandWeight = ability.HandWeight,
            MaxHandWeight = ability.MaxHandWeight
        };
    }

    public void ApplyGoldChanged(int gold, uint gameGold)
    {
        MyGold = gold;
        MyGameGold = gameGold;

        if (AbilitySet)
            UpdateHudOverlayText();
    }

    public void ApplyRefDiamond(int diamond, int gird)
    {
        MyGameDiamond = diamond;
        MyGameGird = gird;
    }

    public void ApplyAttackMode(int mode)
    {
        AttackMode = unchecked((byte)(mode & 0xFF));
        AttackModeLabel = mode switch
        {
            Grobal2.HAM_ALL => "All",
            Grobal2.HAM_PEACE => "Peace",
            Grobal2.HAM_DEAR => "Dear",
            Grobal2.HAM_MASTER => "Master",
            Grobal2.HAM_GROUP => "Group",
            Grobal2.HAM_GUILD => "Guild",
            Grobal2.HAM_PKATTACK => "Red/White",
            _ => $"Unknown({mode})"
        };
    }

    public void ApplyWeaponBreakEffect(int recogId, long nowMs)
    {
        if (recogId == 0)
            return;

        if (_actors.TryGetValue(recogId, out ActorMarker actor))
            _actors[recogId] = actor with { WeaponEffect = true, WeaponEffectStartMs = nowMs };
    }

    private void UpdateHudOverlayText()
    {
        if (!AbilitySet)
        {
            _hudOverlayText = string.Empty;
            return;
        }

        string hpPart = MyMaxHp > 0 ? $"{MyHp}/{MyMaxHp}" : MyHp.ToString();
        string mpPart = MyMaxMp > 0 ? $"{MyMp}/{MyMaxMp}" : MyMp.ToString();
        string expPart = MyMaxExp > 0 ? $"{MyExp}/{MyMaxExp}" : MyExp.ToString();
        string weightPart = (MyWeight | MyWearWeight | MyHandWeight) != 0
            ? $"  W {MyWeight}/{MyWearWeight}/{MyHandWeight}"
            : string.Empty;

        _hudOverlayText = MyGold > 0
            ? $"Lv {MyLevel}  HP {hpPart}  MP {mpPart}  EXP {expPart}  Gold {MyGold}{weightPart}"
            : $"Lv {MyLevel}  HP {hpPart}  MP {mpPart}  EXP {expPart}{weightPart}";
    }

    private void AddItemBag(ClientItem item, int index = -1)
    {
        if (item.MakeIndex == 0)
            return;

        if ((uint)index < (uint)_bagSlots.Length && _bagSlots[index].MakeIndex == 0)
        {
            _bagSlots[index] = item;
            return;
        }

        if (item.S.Overlap < 1)
        {
            for (int i = 0; i < _bagSlots.Length; i++)
            {
                if (_bagSlots[i].MakeIndex == item.MakeIndex)
                    return;
            }
        }

        if (item.S.StdMode <= 3)
        {
            int max = Math.Min(6, _bagSlots.Length);
            for (int i = 0; i < max; i++)
            {
                if (_bagSlots[i].MakeIndex == 0)
                {
                    _bagSlots[i] = item;
                    return;
                }
            }
        }

        bool stacked = false;
        for (int i = 6; i < _bagSlots.Length; i++)
        {
            if (_bagSlots[i].MakeIndex != item.MakeIndex)
                continue;

            if (_bagSlots[i].S.Overlap <= 0)
                continue;

            int sum = _bagSlots[i].Dura + item.Dura;
            _bagSlots[i].Dura = unchecked((ushort)Math.Clamp(sum, 0, ushort.MaxValue));
            stacked = true;
            break;
        }

        if (!stacked)
        {
            for (int i = 6; i < _bagSlots.Length; i++)
            {
                if (_bagSlots[i].MakeIndex == 0)
                {
                    _bagSlots[i] = item;
                    break;
                }
            }
        }

        ArrangeBagItems();
    }

    private bool TryUpdateBagItem(ClientItem item)
    {
        for (int i = _bagSlots.Length - 1; i >= 0; i--)
        {
            if (_bagSlots[i].MakeIndex != item.MakeIndex)
                continue;

            _bagSlots[i] = item;
            return true;
        }

        return false;
    }

    private bool RemoveBagItem(int makeIndex)
    {
        for (int i = _bagSlots.Length - 1; i >= 0; i--)
        {
            if (_bagSlots[i].MakeIndex != makeIndex)
                continue;

            _bagSlots[i] = default;
            return true;
        }

        return false;
    }

    private void ArrangeBagItems()
    {
        for (int i = 0; i < _bagSlots.Length; i++)
        {
            if (_bagSlots[i].MakeIndex == 0)
                continue;

            for (int k = i + 1; k < _bagSlots.Length; k++)
            {
                if (_bagSlots[k].MakeIndex != _bagSlots[i].MakeIndex)
                    continue;

                if (_bagSlots[i].S.Overlap > 0)
                {
                    int sum = _bagSlots[i].Dura + _bagSlots[k].Dura;
                    _bagSlots[i].Dura = unchecked((ushort)Math.Clamp(sum, 0, ushort.MaxValue));
                }

                _bagSlots[k] = default;
            }
        }

        if (_bagSlots.Length <= 46)
            return;

        for (int i = 46; i < _bagSlots.Length; i++)
        {
            if (_bagSlots[i].MakeIndex == 0)
                continue;

            for (int k = 6; k <= 45 && k < _bagSlots.Length; k++)
            {
                if (_bagSlots[k].MakeIndex != 0)
                    continue;

                _bagSlots[k] = _bagSlots[i];
                _bagSlots[i] = default;
                break;
            }
        }
    }

    private void RebuildBagIndex()
    {
        _bagItems.Clear();

        for (int i = 0; i < _bagSlots.Length; i++)
        {
            ClientItem item = _bagSlots[i];
            if (item.MakeIndex == 0)
                continue;

            _bagItems[item.MakeIndex] = item;
        }
    }

    private void AddHeroItemBag(ClientItem item, int index = -1)
    {
        if (item.MakeIndex == 0)
            return;

        int limit = Math.Max(0, _heroBagSlots.Length - 6);

        if ((uint)index < (uint)limit && _heroBagSlots[index].MakeIndex == 0)
        {
            _heroBagSlots[index] = item;
            return;
        }

        if (item.S.Overlap < 1)
        {
            for (int i = 0; i < limit; i++)
            {
                if (_heroBagSlots[i].MakeIndex == item.MakeIndex)
                    return;
            }
        }

        bool stacked = false;
        for (int i = 0; i < limit; i++)
        {
            if (_heroBagSlots[i].MakeIndex != item.MakeIndex)
                continue;

            if (_heroBagSlots[i].S.Overlap <= 0)
                continue;

            int sum = _heroBagSlots[i].Dura + item.Dura;
            _heroBagSlots[i].Dura = unchecked((ushort)Math.Clamp(sum, 0, ushort.MaxValue));
            stacked = true;
            break;
        }

        if (!stacked)
        {
            for (int i = 0; i < limit; i++)
            {
                if (_heroBagSlots[i].MakeIndex == 0)
                {
                    _heroBagSlots[i] = item;
                    break;
                }
            }
        }

        ArrangeHeroBagItems();
    }

    private bool TryUpdateHeroBagItem(ClientItem item)
    {
        for (int i = _heroBagSlots.Length - 1; i >= 0; i--)
        {
            if (_heroBagSlots[i].MakeIndex != item.MakeIndex)
                continue;

            _heroBagSlots[i] = item;
            return true;
        }

        return false;
    }

    private bool RemoveHeroBagItem(int makeIndex)
    {
        for (int i = _heroBagSlots.Length - 1; i >= 0; i--)
        {
            if (_heroBagSlots[i].MakeIndex != makeIndex)
                continue;

            _heroBagSlots[i] = default;
            return true;
        }

        return false;
    }

    private void ArrangeHeroBagItems()
    {
        for (int i = 0; i < _heroBagSlots.Length; i++)
        {
            if (_heroBagSlots[i].MakeIndex == 0)
                continue;

            string name = _heroBagSlots[i].NameString;
            for (int k = i + 1; k < _heroBagSlots.Length; k++)
            {
                if (_heroBagSlots[k].MakeIndex != _heroBagSlots[i].MakeIndex)
                    continue;

                if (!string.Equals(_heroBagSlots[k].NameString, name, StringComparison.Ordinal))
                    continue;

                if (_heroBagSlots[i].S.Overlap > 0)
                {
                    int sum = _heroBagSlots[i].Dura + _heroBagSlots[k].Dura;
                    _heroBagSlots[i].Dura = unchecked((ushort)Math.Clamp(sum, 0, ushort.MaxValue));
                }

                _heroBagSlots[k] = default;
            }
        }
    }

    private void RebuildHeroBagIndex()
    {
        _heroBagItems.Clear();

        for (int i = 0; i < _heroBagSlots.Length; i++)
        {
            ClientItem item = _heroBagSlots[i];
            if (item.MakeIndex == 0)
                continue;

            _heroBagItems[item.MakeIndex] = item;
        }
    }

    private static void RemoveUseItemByMakeIndex(Dictionary<int, ClientItem> useItems, int makeIndex)
    {
        if (useItems.Count == 0 || makeIndex == 0)
            return;

        int keyToRemove = -1;
        foreach ((int key, ClientItem item) in useItems)
        {
            if (item.MakeIndex != makeIndex)
                continue;

            keyToRemove = key;
            break;
        }

        if (keyToRemove >= 0)
            useItems.Remove(keyToRemove);
    }

    private static bool TryUpdateUseItemByMakeIndex(Dictionary<int, ClientItem> useItems, ClientItem item)
    {
        if (useItems.Count == 0 || item.MakeIndex == 0)
            return false;

        foreach ((int key, ClientItem existing) in useItems)
        {
            if (existing.MakeIndex != item.MakeIndex)
                continue;

            useItems[key] = item;
            return true;
        }

        return false;
    }

    private static IEnumerable<string> SplitSlashSegments(string body)
    {
        if (string.IsNullOrEmpty(body))
            yield break;

        int start = 0;
        while (start <= body.Length)
        {
            int idx = body.IndexOf('/', start);
            if (idx < 0)
            {
                if (start < body.Length)
                    yield return body[start..];
                yield break;
            }

            if (idx > start)
                yield return body[start..idx];

            start = idx + 1;
        }
    }

    private static void MoveMagicToFront(List<ClientMagic> list, ushort magicId)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Def.MagicId != magicId)
                continue;

            if (i == 0)
                return;

            ClientMagic magic = list[i];
            list.RemoveAt(i);
            list.Insert(0, magic);
            return;
        }
    }

    private static bool RemoveMagicFromLists(List<ClientMagic> magicList, List<ClientMagic> ipList, (byte Class, ushort MagicId) key)
    {
        int removed = magicList.RemoveAll(m => m.Def.Class == key.Class && m.Def.MagicId == key.MagicId);
        removed += ipList.RemoveAll(m => m.Def.Class == key.Class && m.Def.MagicId == key.MagicId);
        return removed > 0;
    }

    private static void ReplaceMagicInLists(List<ClientMagic> magicList, List<ClientMagic> ipList, (byte Class, ushort MagicId) key, ClientMagic magic)
    {
        List<ClientMagic> target = key.Class == 0 ? magicList : ipList;
        List<ClientMagic> other = key.Class == 0 ? ipList : magicList;

        other.RemoveAll(m => m.Def.Class == key.Class && m.Def.MagicId == key.MagicId);

        for (int i = 0; i < target.Count; i++)
        {
            if (target[i].Def.Class != key.Class || target[i].Def.MagicId != key.MagicId)
                continue;

            target[i] = magic;
            return;
        }

        target.Add(magic);
    }

    private void RebuildStorageGoods()
    {
        _merchantGoods.Clear();
        foreach (ClientItem item in _storageItems)
            _merchantGoods.Add(ConvertStorageItemToGoods(item));
    }

    private static MirMerchantGoods ConvertStorageItemToGoods(ClientItem item)
    {
        int stock = (int)Math.Round(item.Dura / 1000.0);
        int grade = (int)Math.Round(item.DuraMax / 1000.0);
        return new MirMerchantGoods(item.NameString, SubMenu: 0, Price: item.MakeIndex, Stock: stock, Grade: grade);
    }
}
