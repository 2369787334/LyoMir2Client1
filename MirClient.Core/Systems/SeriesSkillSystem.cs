using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class SeriesSkillSystem
{
    private const long FireCooldownMs = 1000;
    private const long ActionCooldownMs = 120;

    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    public bool Visible { get; private set; }
    public bool ControlHero { get; private set; }
    public int SelfVenationSelectedIndex { get; private set; }
    public int HeroVenationSelectedIndex { get; private set; }
    public long LastActionMs { get; private set; }

    public SeriesSkillSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public void EnsureUiState()
    {
        if (!Visible)
            return;

        SelfVenationSelectedIndex = Math.Clamp(SelfVenationSelectedIndex, 0, 3);
        HeroVenationSelectedIndex = Math.Clamp(HeroVenationSelectedIndex, 0, 3);

        if (!IsHeroAvailable())
            ControlHero = false;
    }

    public void Toggle(bool logUi)
    {
        if (Visible)
        {
            Close(logUi);
            return;
        }

        Open(logUi);
    }

    public void Open(bool logUi)
    {
        Visible = true;
        ControlHero = false;
        EnsureUiState();

        if (logUi)
            _log?.Invoke("[ui] venation/series opened");
    }

    public void Close(bool logUi)
    {
        if (!Visible && !ControlHero)
            return;

        Visible = false;
        ControlHero = false;

        if (logUi)
            _log?.Invoke("[ui] venation/series closed");
    }

    public bool TryToggleControlHero(bool logUi)
    {
        EnsureUiState();

        if (!IsHeroAvailable())
            return false;

        ControlHero = !ControlHero;

        if (logUi)
            _log?.Invoke(ControlHero ? "[ui] venation control=hero" : "[ui] venation control=self");

        return true;
    }

    public void SelectVenationIndex(int index)
    {
        EnsureUiState();

        index = Math.Clamp(index, 0, 3);

        if (ControlHero && IsHeroAvailable())
            HeroVenationSelectedIndex = index;
        else
            SelfVenationSelectedIndex = index;
    }

    public bool IsActionCooldownActive(long nowMs) => nowMs - LastActionMs < ActionCooldownMs;

    public void TryFireSeriesSkill(long nowMs, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        EnsureUiState();

        if (nowMs - LastActionMs < FireCooldownMs)
            return;

        LastActionMs = nowMs;

        (int recog, ushort series) = GetTargetRecogSeries();

        _ = _session.SendClientMessageAsync(Grobal2.CM_FIRESERIESSKILL, recog, 0, 0, series, token)
            .ContinueWith(
                t => _log?.Invoke($"[series] CM_FIRESERIESSKILL send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"[series] CM_FIRESERIESSKILL recog={recog} series={series}");
    }

    public void TryTrainVenation(long nowMs, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        EnsureUiState();

        if (IsActionCooldownActive(nowMs))
            return;

        LastActionMs = nowMs;

        (int recog, ushort series) = GetTargetRecogSeries();
        int venation = GetSelectedVenationIndex();

        _ = _session.SendClientMessageAsync(Grobal2.CM_TRAINVENATION, recog, unchecked((ushort)venation), 0, series, token)
            .ContinueWith(
                t => _log?.Invoke($"[venation] CM_TRAINVENATION send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"[venation] CM_TRAINVENATION recog={recog} venation={venation} series={series}");
    }

    public void TryBreakPoint(long nowMs, int point, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        EnsureUiState();

        if (IsActionCooldownActive(nowMs))
            return;

        ushort tag = (ushort)Math.Clamp(point, 0, ushort.MaxValue);
        if (tag == 0)
            return;

        LastActionMs = nowMs;

        (int recog, ushort series) = GetTargetRecogSeries();
        int venation = GetSelectedVenationIndex();

        _ = _session.SendClientMessageAsync(Grobal2.CM_BREAKPOINT, recog, unchecked((ushort)venation), tag, series, token)
            .ContinueWith(
                t => _log?.Invoke($"[venation] CM_BREAKPOINT send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"[venation] CM_BREAKPOINT recog={recog} venation={venation} point={tag} series={series}");
    }

    public void TrySetSeriesSkillSlot(long nowMs, int slotIndex, int magicId, bool hero, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        EnsureUiState();

        if (IsActionCooldownActive(nowMs))
            return;

        slotIndex = Math.Clamp(slotIndex, 0, 3);
        magicId = Math.Clamp(magicId, 0, ushort.MaxValue);

        LastActionMs = nowMs;

        ushort series = hero ? (ushort)1 : (ushort)0;

        _ = _session.SendClientMessageAsync(Grobal2.CM_SETSERIESSKILL, slotIndex, unchecked((ushort)magicId), 0, series, token)
            .ContinueWith(
                t => _log?.Invoke($"[series] CM_SETSERIESSKILL send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"[series] CM_SETSERIESSKILL slot={slotIndex} magicId={magicId} hero={(hero ? 1 : 0)}");
    }

    private int GetSelectedVenationIndex()
    {
        bool hero = ControlHero && IsHeroAvailable();
        return hero ? HeroVenationSelectedIndex : SelfVenationSelectedIndex;
    }

    private (int Recog, ushort Series) GetTargetRecogSeries()
    {
        bool hero = ControlHero && IsHeroAvailable();
        int recog = hero ? _world.HeroActorId : _world.MyselfRecogId;
        ushort series = hero ? (ushort)1 : (ushort)0;
        return (recog, series);
    }

    private bool IsHeroAvailable() => _world.HeroActorIdSet && _world.HeroActorId != 0;
}
