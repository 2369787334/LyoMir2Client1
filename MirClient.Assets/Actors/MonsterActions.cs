namespace MirClient.Assets.Actors;

public static class MonsterActions
{
    public readonly record struct ActionInfo(int Start, int Frames, int Skip, int FrameTimeMs, int UseTick);

    public readonly record struct MonsterAction
    {
        public ActionInfo ActStand { get; init; }
        public ActionInfo ActWalk { get; init; }
        public ActionInfo ActAttack { get; init; }
        public ActionInfo ActCritical { get; init; }
        public ActionInfo ActStruck { get; init; }
        public ActionInfo ActDie { get; init; }
        public ActionInfo ActDeath { get; init; }
    }

    private static readonly MonsterAction MA9 = new()
    {
        ActStand = new ActionInfo(0, 1, 7, 200, 0),
        ActWalk = new ActionInfo(64, 6, 2, 120, 3),
        ActAttack = new ActionInfo(64, 6, 2, 150, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(64, 6, 2, 100, 0),
        ActDie = new ActionInfo(0, 1, 7, 140, 0),
        ActDeath = new ActionInfo(0, 1, 7, 0, 0)
    };

    private static readonly MonsterAction MA10 = new()
    {
        ActStand = new ActionInfo(0, 4, 4, 200, 0),
        ActWalk = new ActionInfo(64, 6, 2, 120, 3),
        ActAttack = new ActionInfo(128, 4, 4, 150, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(192, 2, 0, 100, 0),
        ActDie = new ActionInfo(208, 4, 4, 140, 0),
        ActDeath = new ActionInfo(272, 1, 0, 0, 0)
    };

    private static readonly MonsterAction MA11 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 120, 3),
        ActAttack = new ActionInfo(160, 6, 4, 100, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 140, 0),
        ActDeath = new ActionInfo(340, 1, 0, 0, 0)
    };

    private static readonly MonsterAction MA12 = new()
    {
        ActStand = new ActionInfo(0, 4, 4, 200, 0),
        ActWalk = new ActionInfo(64, 6, 2, 120, 3),
        ActAttack = new ActionInfo(128, 6, 2, 150, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(192, 2, 0, 150, 0),
        ActDie = new ActionInfo(208, 4, 4, 160, 0),
        ActDeath = new ActionInfo(272, 1, 0, 0, 0)
    };

    private static readonly MonsterAction MA13 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(10, 8, 2, 160, 0),
        ActAttack = new ActionInfo(30, 6, 4, 120, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(110, 2, 0, 100, 0),
        ActDie = new ActionInfo(130, 10, 0, 120, 0),
        ActDeath = new ActionInfo(20, 9, 0, 150, 0)
    };

    private static readonly MonsterAction MA14 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 3),
        ActAttack = new ActionInfo(160, 6, 4, 100, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 120, 0),
        ActDeath = new ActionInfo(340, 10, 0, 100, 0)
    };

    private static readonly MonsterAction MA15 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 3),
        ActAttack = new ActionInfo(160, 6, 4, 100, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 120, 0),
        ActDeath = new ActionInfo(1, 1, 0, 100, 0)
    };

    private static readonly MonsterAction MA16 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 3),
        ActAttack = new ActionInfo(160, 6, 4, 160, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 4, 6, 160, 0),
        ActDeath = new ActionInfo(0, 1, 0, 160, 0)
    };

    private static readonly MonsterAction MA17 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 60, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 3),
        ActAttack = new ActionInfo(160, 6, 4, 100, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 100, 0),
        ActDeath = new ActionInfo(340, 1, 0, 140, 0)
    };

    private static readonly MonsterAction MA19 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 3),
        ActAttack = new ActionInfo(160, 6, 4, 100, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 140, 0),
        ActDeath = new ActionInfo(340, 1, 0, 140, 0)
    };

    private static readonly MonsterAction MA20 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 3),
        ActAttack = new ActionInfo(160, 6, 4, 120, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 100, 0),
        ActDeath = new ActionInfo(340, 10, 0, 170, 0)
    };

    private static readonly MonsterAction MA21 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(0, 0, 0, 0, 0),
        ActAttack = new ActionInfo(10, 6, 4, 120, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(20, 2, 0, 100, 0),
        ActDie = new ActionInfo(30, 10, 0, 160, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA22 = new()
    {
        ActStand = new ActionInfo(80, 4, 6, 200, 0),
        ActWalk = new ActionInfo(160, 6, 4, 160, 3),
        ActAttack = new ActionInfo(240, 6, 4, 100, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(320, 2, 0, 100, 0),
        ActDie = new ActionInfo(340, 10, 0, 160, 0),
        ActDeath = new ActionInfo(0, 6, 4, 170, 0)
    };

    private static readonly MonsterAction MA23 = new()
    {
        ActStand = new ActionInfo(20, 4, 6, 200, 0),
        ActWalk = new ActionInfo(100, 6, 4, 160, 3),
        ActAttack = new ActionInfo(180, 6, 4, 100, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(260, 2, 0, 100, 0),
        ActDie = new ActionInfo(280, 10, 0, 160, 0),
        ActDeath = new ActionInfo(0, 20, 0, 100, 0)
    };

    private static readonly MonsterAction MA24 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 3),
        ActAttack = new ActionInfo(160, 6, 4, 100, 0),
        ActCritical = new ActionInfo(240, 6, 4, 100, 0),
        ActStruck = new ActionInfo(320, 2, 0, 100, 0),
        ActDie = new ActionInfo(340, 10, 0, 140, 0),
        ActDeath = new ActionInfo(420, 1, 0, 140, 0)
    };

    private static readonly MonsterAction MA25 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(70, 10, 0, 200, 3),
        ActAttack = new ActionInfo(20, 6, 4, 120, 0),
        ActCritical = new ActionInfo(10, 6, 4, 120, 0),
        ActStruck = new ActionInfo(50, 2, 0, 100, 0),
        ActDie = new ActionInfo(60, 10, 0, 200, 0),
        ActDeath = new ActionInfo(80, 10, 0, 200, 3)
    };

    private static readonly MonsterAction MA26 = new()
    {
        ActStand = new ActionInfo(0, 1, 7, 200, 0),
        ActWalk = new ActionInfo(0, 0, 0, 160, 0),
        ActAttack = new ActionInfo(56, 6, 2, 350, 0),
        ActCritical = new ActionInfo(64, 6, 2, 350, 0),
        ActStruck = new ActionInfo(0, 4, 4, 100, 0),
        ActDie = new ActionInfo(24, 10, 0, 120, 0),
        ActDeath = new ActionInfo(0, 0, 0, 150, 0)
    };

    private static readonly MonsterAction MA27 = new()
    {
        ActStand = new ActionInfo(0, 1, 7, 200, 0),
        ActWalk = new ActionInfo(0, 0, 0, 160, 0),
        ActAttack = new ActionInfo(0, 0, 0, 250, 0),
        ActCritical = new ActionInfo(0, 0, 0, 250, 0),
        ActStruck = new ActionInfo(0, 0, 0, 100, 0),
        ActDie = new ActionInfo(0, 10, 0, 120, 0),
        ActDeath = new ActionInfo(0, 0, 0, 150, 0)
    };

    private static readonly MonsterAction MA28 = new()
    {
        ActStand = new ActionInfo(80, 4, 6, 200, 0),
        ActWalk = new ActionInfo(160, 6, 4, 160, 3),
        ActAttack = new ActionInfo(0, 6, 4, 100, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 120, 0),
        ActDeath = new ActionInfo(0, 10, 0, 100, 0)
    };

    private static readonly MonsterAction MA29 = new()
    {
        ActStand = new ActionInfo(80, 4, 6, 200, 0),
        ActWalk = new ActionInfo(160, 6, 4, 160, 3),
        ActAttack = new ActionInfo(240, 6, 4, 100, 0),
        ActCritical = new ActionInfo(0, 10, 0, 100, 0),
        ActStruck = new ActionInfo(320, 2, 0, 100, 0),
        ActDie = new ActionInfo(340, 10, 0, 120, 0),
        ActDeath = new ActionInfo(0, 10, 0, 100, 0)
    };

    private static readonly MonsterAction MA30 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(0, 10, 0, 160, 3),
        ActAttack = new ActionInfo(10, 6, 4, 120, 0),
        ActCritical = new ActionInfo(10, 6, 4, 100, 0),
        ActStruck = new ActionInfo(20, 2, 0, 100, 0),
        ActDie = new ActionInfo(30, 20, 0, 120, 0),
        ActDeath = new ActionInfo(0, 10, 0, 140, 3)
    };

    private static readonly MonsterAction MA31 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(0, 10, 0, 200, 3),
        ActAttack = new ActionInfo(10, 6, 4, 120, 0),
        ActCritical = new ActionInfo(0, 6, 4, 120, 0),
        ActStruck = new ActionInfo(0, 2, 8, 100, 0),
        ActDie = new ActionInfo(20, 10, 0, 200, 0),
        ActDeath = new ActionInfo(0, 10, 0, 200, 3)
    };

    private static readonly MonsterAction MA32 = new()
    {
        ActStand = new ActionInfo(0, 1, 9, 200, 0),
        ActWalk = new ActionInfo(0, 6, 4, 200, 3),
        ActAttack = new ActionInfo(0, 6, 4, 120, 0),
        ActCritical = new ActionInfo(0, 6, 4, 120, 0),
        ActStruck = new ActionInfo(0, 2, 8, 100, 0),
        ActDie = new ActionInfo(80, 10, 0, 80, 0),
        ActDeath = new ActionInfo(80, 10, 0, 200, 3)
    };

    private static readonly MonsterAction MA33 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 200, 3),
        ActAttack = new ActionInfo(160, 6, 4, 120, 0),
        ActCritical = new ActionInfo(340, 6, 4, 120, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 200, 0),
        ActDeath = new ActionInfo(260, 10, 0, 200, 0)
    };

    private static readonly MonsterAction MA34 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 200, 3),
        ActAttack = new ActionInfo(160, 6, 4, 120, 0),
        ActCritical = new ActionInfo(320, 6, 4, 120, 0),
        ActStruck = new ActionInfo(400, 2, 0, 100, 0),
        ActDie = new ActionInfo(420, 20, 0, 200, 0),
        ActDeath = new ActionInfo(420, 20, 0, 200, 0)
    };

    private static readonly MonsterAction MA35 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(0, 0, 0, 0, 0),
        ActAttack = new ActionInfo(30, 10, 0, 150, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(0, 1, 9, 0, 0),
        ActDie = new ActionInfo(0, 0, 0, 0, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA111 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(0, 0, 0, 0, 0),
        ActAttack = new ActionInfo(30, 23, 0, 180, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(0, 1, 9, 0, 0),
        ActDie = new ActionInfo(0, 0, 0, 0, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA36 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(0, 0, 0, 0, 0),
        ActAttack = new ActionInfo(30, 20, 0, 150, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(0, 1, 9, 0, 0),
        ActDie = new ActionInfo(0, 0, 0, 0, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA37 = new()
    {
        ActStand = new ActionInfo(30, 4, 6, 200, 0),
        ActWalk = new ActionInfo(0, 0, 0, 0, 0),
        ActAttack = new ActionInfo(30, 4, 6, 150, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(0, 1, 9, 0, 0),
        ActDie = new ActionInfo(0, 0, 0, 0, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA38 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(0, 0, 0, 0, 0),
        ActAttack = new ActionInfo(80, 6, 4, 150, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(0, 0, 0, 0, 0),
        ActDie = new ActionInfo(0, 0, 0, 0, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA39 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 300, 0),
        ActWalk = new ActionInfo(0, 0, 0, 0, 0),
        ActAttack = new ActionInfo(10, 6, 4, 150, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(20, 2, 0, 150, 0),
        ActDie = new ActionInfo(30, 10, 0, 80, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA40 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 250, 0),
        ActWalk = new ActionInfo(80, 6, 4, 210, 3),
        ActAttack = new ActionInfo(160, 6, 4, 110, 0),
        ActCritical = new ActionInfo(580, 20, 0, 135, 0),
        ActStruck = new ActionInfo(240, 2, 0, 120, 0),
        ActDie = new ActionInfo(260, 20, 0, 130, 0),
        ActDeath = new ActionInfo(260, 20, 0, 130, 0)
    };

    private static readonly MonsterAction MA41 = new()
    {
        ActStand = new ActionInfo(0, 2, 8, 200, 0),
        ActWalk = new ActionInfo(0, 2, 8, 200, 0),
        ActAttack = new ActionInfo(0, 2, 8, 200, 0),
        ActCritical = new ActionInfo(0, 2, 8, 200, 0),
        ActStruck = new ActionInfo(0, 2, 8, 200, 0),
        ActDie = new ActionInfo(0, 2, 8, 200, 0),
        ActDeath = new ActionInfo(0, 2, 8, 200, 0)
    };

    private static readonly MonsterAction MA42 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(10, 8, 2, 160, 0),
        ActAttack = new ActionInfo(0, 0, 0, 0, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(0, 0, 0, 0, 0),
        ActDie = new ActionInfo(30, 10, 0, 120, 0),
        ActDeath = new ActionInfo(30, 10, 0, 150, 0)
    };

    private static readonly MonsterAction MA43 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 0),
        ActAttack = new ActionInfo(160, 6, 4, 160, 0),
        ActCritical = new ActionInfo(160, 6, 4, 160, 0),
        ActStruck = new ActionInfo(240, 2, 0, 150, 0),
        ActDie = new ActionInfo(260, 10, 0, 120, 0),
        ActDeath = new ActionInfo(340, 10, 0, 100, 0)
    };

    private static readonly MonsterAction MA44 = new()
    {
        ActStand = new ActionInfo(0, 10, 0, 300, 0),
        ActWalk = new ActionInfo(10, 6, 4, 150, 0),
        ActAttack = new ActionInfo(20, 6, 4, 150, 0),
        ActCritical = new ActionInfo(40, 10, 0, 150, 0),
        ActStruck = new ActionInfo(40, 2, 8, 150, 0),
        ActDie = new ActionInfo(30, 6, 4, 150, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA45 = new()
    {
        ActStand = new ActionInfo(0, 10, 0, 300, 0),
        ActWalk = new ActionInfo(0, 10, 0, 300, 0),
        ActAttack = new ActionInfo(10, 10, 0, 300, 0),
        ActCritical = new ActionInfo(10, 10, 0, 100, 0),
        ActStruck = new ActionInfo(0, 1, 9, 300, 0),
        ActDie = new ActionInfo(0, 1, 9, 300, 0),
        ActDeath = new ActionInfo(0, 1, 9, 300, 0)
    };

    private static readonly MonsterAction MA46 = new()
    {
        ActStand = new ActionInfo(0, 20, 0, 100, 0),
        ActWalk = new ActionInfo(0, 0, 0, 0, 0),
        ActAttack = new ActionInfo(0, 0, 0, 0, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(0, 0, 0, 0, 0),
        ActDie = new ActionInfo(0, 0, 0, 0, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA47 = new()
    {
        ActStand = new ActionInfo(0, 0, 0, 200, 0),
        ActWalk = new ActionInfo(50, 10, 0, 200, 3),
        ActAttack = new ActionInfo(10, 6, 4, 120, 0),
        ActCritical = new ActionInfo(10, 6, 4, 120, 0),
        ActStruck = new ActionInfo(40, 10, 0, 100, 0),
        ActDie = new ActionInfo(0, 1, 0, 200, 0),
        ActDeath = new ActionInfo(0, 1, 0, 200, 0)
    };

    private static readonly MonsterAction MA48 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 3),
        ActAttack = new ActionInfo(160, 6, 4, 160, 0),
        ActCritical = new ActionInfo(340, 6, 4, 160, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 160, 0),
        ActDeath = new ActionInfo(0, 1, 0, 160, 0)
    };

    private static readonly MonsterAction MA49 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 3),
        ActAttack = new ActionInfo(160, 6, 4, 160, 0),
        ActCritical = new ActionInfo(340, 6, 4, 160, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 160, 0),
        ActDeath = new ActionInfo(420, 4, 6, 200, 0)
    };

    private static readonly MonsterAction MA50 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 0),
        ActAttack = new ActionInfo(160, 6, 4, 160, 0),
        ActCritical = new ActionInfo(340, 6, 4, 160, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 160, 0),
        ActDeath = new ActionInfo(420, 4, 6, 200, 0)
    };

    private static readonly MonsterAction MA51 = new()
    {
        ActStand = new ActionInfo(0, 20, 0, 150, 0),
        ActWalk = new ActionInfo(0, 20, 0, 150, 3),
        ActAttack = new ActionInfo(20, 10, 0, 150, 0),
        ActCritical = new ActionInfo(20, 10, 0, 150, 0),
        ActStruck = new ActionInfo(20, 2, 8, 100, 0),
        ActDie = new ActionInfo(400, 18, 0, 150, 0),
        ActDeath = new ActionInfo(400, 18, 0, 150, 0)
    };

    private static readonly MonsterAction MA131 = new()
    {
        ActStand = new ActionInfo(0, 20, 0, 150, 0),
        ActWalk = new ActionInfo(0, 20, 0, 150, 3),
        ActAttack = new ActionInfo(20, 20, 0, 150, 0),
        ActCritical = new ActionInfo(20, 10, 0, 150, 0),
        ActStruck = new ActionInfo(20, 2, 8, 100, 0),
        ActDie = new ActionInfo(400, 18, 0, 150, 0),
        ActDeath = new ActionInfo(400, 18, 0, 150, 0)
    };

    private static readonly MonsterAction MA52 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 150, 0),
        ActWalk = new ActionInfo(0, 4, 6, 150, 3),
        ActAttack = new ActionInfo(10, 4, 6, 300, 0),
        ActCritical = new ActionInfo(10, 4, 6, 300, 0),
        ActStruck = new ActionInfo(0, 4, 6, 150, 0),
        ActDie = new ActionInfo(0, 4, 6, 300, 0),
        ActDeath = new ActionInfo(0, 4, 6, 300, 0)
    };

    private static readonly MonsterAction MA53 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 150, 0),
        ActWalk = new ActionInfo(0, 4, 6, 150, 3),
        ActAttack = new ActionInfo(0, 4, 6, 150, 0),
        ActCritical = new ActionInfo(0, 4, 6, 150, 0),
        ActStruck = new ActionInfo(0, 4, 6, 150, 0),
        ActDie = new ActionInfo(0, 4, 6, 150, 0),
        ActDeath = new ActionInfo(0, 4, 6, 150, 0)
    };

    private static readonly MonsterAction MA54 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 0),
        ActAttack = new ActionInfo(160, 6, 4, 160, 0),
        ActCritical = new ActionInfo(340, 10, 0, 160, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 160, 0),
        ActDeath = new ActionInfo(420, 4, 6, 200, 0)
    };

    private static readonly MonsterAction MA55 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(0, 0, 0, 0, 0),
        ActAttack = new ActionInfo(0, 0, 0, 150, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(0, 1, 9, 0, 0),
        ActDie = new ActionInfo(0, 0, 0, 0, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA56 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(0, 0, 0, 0, 0),
        ActAttack = new ActionInfo(10, 10, 0, 150, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(0, 1, 9, 0, 0),
        ActDie = new ActionInfo(0, 0, 0, 0, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA57 = new()
    {
        ActStand = new ActionInfo(0, 3, 0, 160, 0),
        ActWalk = new ActionInfo(0, 0, 0, 0, 0),
        ActAttack = new ActionInfo(3, 8, 0, 160, 0),
        ActCritical = new ActionInfo(22, 8, 0, 160, 0),
        ActStruck = new ActionInfo(0, 1, 9, 0, 0),
        ActDie = new ActionInfo(0, 0, 0, 0, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA58 = new()
    {
        ActStand = new ActionInfo(0, 1, 0, 160, 0),
        ActWalk = new ActionInfo(0, 0, 0, 0, 0),
        ActAttack = new ActionInfo(1, 34, 0, 160, 0),
        ActCritical = new ActionInfo(47, 33, 0, 160, 0),
        ActStruck = new ActionInfo(0, 1, 9, 0, 0),
        ActDie = new ActionInfo(0, 0, 0, 0, 0),
        ActDeath = new ActionInfo(0, 0, 0, 0, 0)
    };

    private static readonly MonsterAction MA59 = new()
    {
        ActStand = new ActionInfo(0, 10, 0, 300, 0),
        ActWalk = new ActionInfo(0, 0, 0, 150, 0),
        ActAttack = new ActionInfo(0, 10, 0, 150, 0),
        ActCritical = new ActionInfo(0, 0, 0, 150, 0),
        ActStruck = new ActionInfo(0, 0, 0, 150, 0),
        ActDie = new ActionInfo(0, 0, 0, 150, 0),
        ActDeath = new ActionInfo(0, 0, 0, 150, 0)
    };

    private static readonly MonsterAction MA60 = new()
    {
        ActStand = new ActionInfo(0, 1, 0, 300, 0),
        ActWalk = new ActionInfo(0, 0, 0, 150, 0),
        ActAttack = new ActionInfo(0, 1, 0, 150, 0),
        ActCritical = new ActionInfo(0, 0, 0, 150, 0),
        ActStruck = new ActionInfo(0, 0, 0, 150, 0),
        ActDie = new ActionInfo(0, 0, 0, 150, 0),
        ActDeath = new ActionInfo(0, 0, 0, 150, 0)
    };

    private static readonly MonsterAction MA65 = new()
    {
        ActStand = new ActionInfo(0, 10, 0, 150, 0),
        ActWalk = new ActionInfo(10, 10, 0, 150, 0),
        ActAttack = new ActionInfo(20, 10, 0, 150, 0),
        ActCritical = new ActionInfo(20, 10, 0, 150, 0),
        ActStruck = new ActionInfo(30, 4, 6, 100, 0),
        ActDie = new ActionInfo(40, 10, 0, 150, 0),
        ActDeath = new ActionInfo(40, 10, 0, 150, 0)
    };

    private static readonly MonsterAction MA66 = new()
    {
        ActStand = new ActionInfo(0, 20, 0, 150, 0),
        ActWalk = new ActionInfo(0, 20, 0, 150, 3),
        ActAttack = new ActionInfo(20, 10, 0, 150, 0),
        ActCritical = new ActionInfo(20, 10, 0, 150, 0),
        ActStruck = new ActionInfo(30, 2, 8, 100, 0),
        ActDie = new ActionInfo(400, 18, 0, 150, 0),
        ActDeath = new ActionInfo(400, 18, 0, 150, 0)
    };

    private static readonly MonsterAction MA67 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 150, 0),
        ActWalk = new ActionInfo(0, 4, 6, 150, 3),
        ActAttack = new ActionInfo(10, 4, 6, 300, 0),
        ActCritical = new ActionInfo(10, 4, 6, 300, 0),
        ActStruck = new ActionInfo(0, 4, 6, 150, 0),
        ActDie = new ActionInfo(0, 4, 6, 300, 0),
        ActDeath = new ActionInfo(0, 4, 6, 300, 0)
    };

    private static readonly MonsterAction MA91 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 3),
        ActAttack = new ActionInfo(160, 6, 4, 100, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 120, 0),
        ActDeath = new ActionInfo(1040, 15, 0, 100, 0)
    };

    private static readonly MonsterAction MA92 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 3),
        ActAttack = new ActionInfo(160, 6, 4, 100, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 120, 0),
        ActDeath = new ActionInfo(1060, 15, 0, 100, 0)
    };

    private static readonly MonsterAction MA93 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 3),
        ActAttack = new ActionInfo(160, 6, 4, 100, 0),
        ActCritical = new ActionInfo(0, 0, 0, 0, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 120, 0),
        ActDeath = new ActionInfo(1080, 15, 0, 100, 0)
    };

    private static readonly MonsterAction MAG25 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 0),
        ActAttack = new ActionInfo(160, 6, 4, 160, 0),
        ActCritical = new ActionInfo(340, 10, 0, 160, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 160, 0),
        ActDeath = new ActionInfo(426, 4, 6, 120, 0)
    };

    private static readonly MonsterAction MAG26 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 0),
        ActAttack = new ActionInfo(160, 6, 4, 120, 0),
        ActCritical = new ActionInfo(340, 7, 3, 120, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 160, 0),
        ActDeath = new ActionInfo(422, 4, 6, 120, 0)
    };

    private static readonly MonsterAction MAG27 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 0),
        ActAttack = new ActionInfo(160, 6, 4, 120, 0),
        ActCritical = new ActionInfo(340, 10, 0, 120, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 160, 0),
        ActDeath = new ActionInfo(420, 10, 0, 120, 0)
    };

    private static readonly MonsterAction MAG28 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 160, 0),
        ActAttack = new ActionInfo(160, 10, 0, 120, 0),
        ActCritical = new ActionInfo(340, 6, 4, 120, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 9, 1, 160, 0),
        ActDeath = new ActionInfo(420, 4, 6, 120, 0)
    };

    private static readonly MonsterAction MAG29 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 120, 0),
        ActAttack = new ActionInfo(160, 6, 4, 110, 0),
        ActCritical = new ActionInfo(340, 8, 2, 110, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 8, 2, 120, 0),
        ActDeath = new ActionInfo(420, 7, 3, 120, 0)
    };

    private static readonly MonsterAction MAG30 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 120, 0),
        ActAttack = new ActionInfo(160, 6, 4, 110, 0),
        ActCritical = new ActionInfo(340, 8, 2, 110, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 120, 0),
        ActDeath = new ActionInfo(420, 9, 1, 120, 0)
    };

    private static readonly MonsterAction MAG31 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 140, 0),
        ActAttack = new ActionInfo(160, 6, 4, 110, 0),
        ActCritical = new ActionInfo(340, 8, 2, 110, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 7, 3, 120, 0),
        ActDeath = new ActionInfo(420, 7, 3, 120, 0)
    };

    private static readonly MonsterAction MA120 = new()
    {
        ActStand = new ActionInfo(0, 1, 9, 200, 0),
        ActWalk = new ActionInfo(0, 0, 0, 200, 3),
        ActAttack = new ActionInfo(80, 10, 0, 120, 0),
        ActCritical = new ActionInfo(0, 0, 0, 120, 0),
        ActStruck = new ActionInfo(160, 2, 8, 100, 0),
        ActDie = new ActionInfo(240, 10, 0, 200, 0),
        ActDeath = new ActionInfo(240, 10, 0, 200, 0)
    };

    private static readonly MonsterAction MA121 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 200, 3),
        ActAttack = new ActionInfo(160, 6, 4, 120, 0),
        ActCritical = new ActionInfo(340, 6, 4, 120, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 8, 2, 200, 0),
        ActDeath = new ActionInfo(260, 8, 2, 200, 0)
    };

    private static readonly MonsterAction MA122 = new()
    {
        ActStand = new ActionInfo(0, 10, 0, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 200, 3),
        ActAttack = new ActionInfo(160, 6, 4, 120, 0),
        ActCritical = new ActionInfo(340, 6, 4, 120, 0),
        ActStruck = new ActionInfo(240, 2, 0, 100, 0),
        ActDie = new ActionInfo(260, 10, 0, 200, 0),
        ActDeath = new ActionInfo(260, 10, 0, 200, 0)
    };

    private static readonly MonsterAction MA123 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 200, 3),
        ActAttack = new ActionInfo(160, 6, 4, 120, 0),
        ActCritical = new ActionInfo(400, 6, 4, 120, 0),
        ActStruck = new ActionInfo(240, 2, 8, 100, 0),
        ActDie = new ActionInfo(320, 10, 0, 200, 0),
        ActDeath = new ActionInfo(400, 10, 0, 200, 0)
    };

    private static readonly MonsterAction MA123_815 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 200, 3),
        ActAttack = new ActionInfo(160, 6, 4, 120, 0),
        ActCritical = new ActionInfo(400, 10, 0, 120, 0),
        ActStruck = new ActionInfo(240, 2, 8, 100, 0),
        ActDie = new ActionInfo(320, 10, 0, 200, 0),
        ActDeath = new ActionInfo(400, 10, 0, 200, 0)
    };

    private static readonly MonsterAction MA123_825 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 200, 3),
        ActAttack = new ActionInfo(160, 9, 1, 120, 0),
        ActCritical = new ActionInfo(400, 9, 1, 120, 0),
        ActStruck = new ActionInfo(240, 2, 8, 100, 0),
        ActDie = new ActionInfo(320, 10, 0, 200, 0),
        ActDeath = new ActionInfo(400, 10, 0, 200, 0)
    };

    private static readonly MonsterAction MA123_827 = new()
    {
        ActStand = new ActionInfo(0, 4, 6, 200, 0),
        ActWalk = new ActionInfo(80, 6, 4, 200, 3),
        ActAttack = new ActionInfo(160, 6, 4, 120, 0),
        ActCritical = new ActionInfo(400, 6, 4, 120, 0),
        ActStruck = new ActionInfo(240, 2, 8, 100, 0),
        ActDie = new ActionInfo(320, 10, 0, 200, 0),
        ActDeath = new ActionInfo(480, 10, 0, 200, 0)
    };

    public static MonsterAction GetRaceByPm(int race, int appearance)
    {
        switch (race)
        {
            case 9: return MA9;
            case 10: return MA10;
            case 11: return MA11;
            case 12: return MA12;
            case 13: return MA13;
            case 14: return MA14;
            case 15: return MA15;
            case 16: return MA16;
            case 17: return MA14;
            case 18: return MA14;
            case 19: return MA19;
            case 20: return MA19;
            case 21: return MA19;
            case 22: return MA15;
            case 23: return MA14;
            case 24: return MA12;
            case 25: return MAG25;
            case 26: return MAG26;
            case 27: return MAG27;
            case 28: return MAG28;
            case 29: return MAG29;
            case 30: return MA17;
            case 31: return MA17;
            case 32: return MA24;
            case 33: return MA25;
            case 34: return MA30;
            case 35: return MA31;
            case 36: return MA32;
            case 37: return MA19;
            case 38: return MAG29;
            case 39: return MAG30;
            case 40: return MA19;
            case 41: return MA20;
            case 42: return MA20;
            case 43: return MA21;
            case 44: return MAG31;
            case 45: return MA19;
            case 46: return MA50;
            case 47: return MA22;
            case 48: return MA23;
            case 49: return MA23;
            case 50:
                return appearance switch
                {
                    23 => MA36,
                    24 or 25 or 27 or 32 => MA37,
                    33 => MA35,
                    >= 35 and <= 41 => MA41,
                    >= 42 and <= 47 => MA46,
                    48 or 49 or 50 or 52 or 53 => MA41,
                    (>= 54 and <= 58) or (>= 94 and <= 98) => MA59,
                    59 => MA60,
                    (>= 60 and <= 68) or (>= 70 and <= 75) or (>= 90 and <= 92) => MA55,
                    >= 76 and <= 80 => MA35,
                    >= 81 and <= 83 => MA56,
                    84 => MA57,
                    85 => MA58,
                    111 => MA111,
                    132 => MA131,
                    _ => MA35
                };
            case 51: return MA50;
            case 52: return MA19;
            case 53: return MA19;
            case 54: return MA28;
            case 55: return MA29;
            case 56: return MA43;
            case 57: return MA15;
            case 58: return MA15;
            case 60: return MA33;
            case 61: return MA33;
            case 62: return MA33;
            case 63: return MA34;
            case 64: return MA19;
            case 65: return MA19;
            case 66: return MA19;
            case 67: return MA19;
            case 68: return MA19;
            case 69: return MA19;
            case 70: return MA33;
            case 71: return MA33;
            case 72: return MA33;
            case 73: return MA19;
            case 74: return MA19;
            case 75: return MA39;
            case 76: return MA38;
            case 77: return MA39;
            case 78: return MA40;
            case 79: return MA19;
            case 80: return MA42;
            case 81: return MA43;
            case 83: return MA44;
            case 84: return MA47;
            case 85: return MA47;
            case 86: return MA47;
            case 87: return MA47;
            case 88: return MA47;
            case 89: return MA47;
            case 90: return MA47;
            case 91: return MA91;
            case 92: return MA92;
            case 93: return MA93;
            case 94: return MA28;
            case 95: return MA29;
            case 98: return MA27;
            case 99: return MA26;
            case 101: return MA33;
            case 102: return MA48;
            case 103: return MA49;
            case 104: return MA49;
            case 105: return MA49;
            case 106: return MA50;
            case 109: return MA51;
            case 110: return MA50;
            case 111: return MA54;
            case 113:
            case 114:
            case 115:
                return MA33;
            case 117: return MA67;
            case 118:
            case 119:
                return MA65;
            case 120: return MA66;
            case 121: return MA121;
            case 122: return MA122;
            case 123:
                return appearance switch
                {
                    812 => MA120,
                    815 => MA123_815,
                    825 => MA123_825,
                    827 => MA123_827,
                    _ => MA123
                };
            default:
                return MA19;
        }
    }
}
