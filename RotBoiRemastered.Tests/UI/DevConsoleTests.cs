using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using RotBoiRemastered.Core;
using RotBoiRemastered.Entities;
using RotBoiRemastered.Systems;
using RotBoiRemastered.UI;
using RotBoiRemastered.World;

namespace RotBoiRemastered.Tests.UI;

[Collection("GameProfileState")]
public class DevConsoleTests
{
    private static GameSession MakeSession() => new(Battleground.GenerateSound(), 1280, 720, new Random(1));

    private static void Submit(DevConsole console, GameSession session, string command)
    {
        console.Open();
        foreach (char c in command)
            console.HandleTextInput(c);
        console.Update(session, new HashSet<Keys> { Keys.Enter }, 0);
    }

    [Fact]
    public void KillAll_ZeroesEveryEnemysHp_IncludingABossMidScriptedPhase()
    {
        // Beaudis.TakeDamage refuses damage outright while SurvivalActive
        // (see its Dying/SurvivalActive/_phaseProtectionTimer gate) -- a real
        // kill only ever completes through a dedicated Dying flag set
        // elsewhere, never through repeated TakeDamage calls. /killall has to
        // bypass TakeDamage entirely (set Hp directly) or it would silently
        // fail to kill a boss caught in that state.
        var session = MakeSession();
        var boss = new Beaudis(0, 0, 100, new Random(1));
        boss.DebugSetPhase(3); // enters Endure, where TakeDamage is a no-op
        var minion = new Enemy(0, 0, speed: 0, size: 10, Color.Red, damage: 1, hp: 10,
            expValue: 1, difficulty: 1, awarenessRange: 100f);
        session.State.EnemyHolster.Add(boss);
        session.State.ActiveBoss = boss;
        session.State.EnemyHolster.Add(minion);
        var console = new DevConsole();

        Submit(console, session, "/killall");

        Assert.Equal(0, boss.Hp);
        Assert.Equal(0, minion.Hp);
    }

    [Fact]
    public void KillAll_WithNoEnemies_LogsWithoutThrowing()
    {
        var session = MakeSession();
        var console = new DevConsole();

        var exception = Record.Exception(() => Submit(console, session, "/killall"));

        Assert.Null(exception);
    }

    private static Aphantasia MakeAphantasiaBoss(GameSession session)
    {
        var arena = BossArenaFactory.Create("aphantasia", Progression.FinalBossLevel);
        var boss = new Aphantasia(1000, 1000, arena, new Random(9),
            noHealing: true, noExtract: true);
        session.State.ActiveBoss = boss;
        return boss;
    }

    [Fact]
    public void TestPhase_TypedKeyInFull_JumpsTheActiveBossToThatPattern()
    {
        var session = MakeSession();
        Aphantasia boss = MakeAphantasiaBoss(session);
        var console = new DevConsole();

        Submit(console, session, "/testphase blender");

        Assert.Equal(3, boss.Phase);
        Assert.Equal("blender", boss.CurrentPattern.Key);
    }

    [Fact]
    public void TestPhase_UnknownKey_LogsWithoutThrowingAndLeavesTheBossAlone()
    {
        var session = MakeSession();
        Aphantasia boss = MakeAphantasiaBoss(session);
        int phaseBefore = boss.Phase;
        var console = new DevConsole();

        var exception = Record.Exception(() => Submit(console, session, "/testphase nonexistent_key"));

        Assert.Null(exception);
        Assert.Equal(phaseBefore, boss.Phase);
    }

    [Fact]
    public void TestPhase_WithNoActiveBoss_LogsWithoutThrowing()
    {
        var session = MakeSession();
        var console = new DevConsole();

        var exception = Record.Exception(() => Submit(console, session, "/testphase blender"));

        Assert.Null(exception);
    }

    [Fact]
    public void TestPhase_SpaceThenEnter_SelectsTheFirstDropdownCandidate()
    {
        var session = MakeSession();
        Aphantasia boss = MakeAphantasiaBoss(session);
        var console = new DevConsole();
        console.Open();
        foreach (char c in "/testphase ")
            console.HandleTextInput(c);
        // One Update populates the live dropdown from the buffer above;
        // Enter with no further typing should run its top (first) candidate,
        // matching the console's own doc comment on pressing space for a list.
        console.Update(session, new HashSet<Keys>(), 0);
        console.Update(session, new HashSet<Keys> { Keys.Enter }, 0);

        Assert.Equal(Aphantasia.DebugTestPhaseKeys[0].Key, boss.CurrentPattern.Key);
    }

    [Fact]
    public void TestPhase_SpaceThenDownThenEnter_SelectsTheSecondDropdownCandidate()
    {
        var session = MakeSession();
        Aphantasia boss = MakeAphantasiaBoss(session);
        var console = new DevConsole();
        console.Open();
        foreach (char c in "/testphase ")
            console.HandleTextInput(c);
        console.Update(session, new HashSet<Keys> { Keys.Down }, 0);
        console.Update(session, new HashSet<Keys> { Keys.Enter }, 0);

        Assert.Equal(Aphantasia.DebugTestPhaseKeys[1].Key, boss.CurrentPattern.Key);
    }

    [Fact]
    public void TestPhase_Tab_FillsSelectedCandidateWithoutRunningIt()
    {
        var session = MakeSession();
        Aphantasia boss = MakeAphantasiaBoss(session);
        int phaseBefore = boss.Phase;
        string patternBefore = boss.CurrentPattern.Key;
        var console = new DevConsole();
        console.Open();
        foreach (char c in "/testphase ")
            console.HandleTextInput(c);
        // Move to the third candidate, then Tab -- this must not jump the
        // boss at all, only fill the buffer.
        console.Update(session, new HashSet<Keys> { Keys.Down }, 0);
        console.Update(session, new HashSet<Keys> { Keys.Down }, 0);
        console.Update(session, new HashSet<Keys> { Keys.Tab }, 0);

        Assert.Equal(phaseBefore, boss.Phase);
        Assert.Equal(patternBefore, boss.CurrentPattern.Key);

        // A follow-up bare Enter (no further typing, no dropdown left to
        // select from) runs exactly what Tab filled in -- the third
        // candidate, not the dropdown's default first entry -- proving Tab
        // actually populated the buffer with the selection rather than
        // leaving it untouched or resetting it.
        console.Update(session, new HashSet<Keys> { Keys.Enter }, 0);

        Assert.Equal(Aphantasia.DebugTestPhaseKeys[2].Key, boss.CurrentPattern.Key);
    }

    [Fact]
    public void CommandMenu_Tab_OnANoArgumentCommand_FillsItInWithoutRunning()
    {
        // "god" takes no arguments, so Enter on it would run immediately --
        // Tab must fill "/god" into the buffer instead and leave state alone.
        var session = MakeSession();
        var console = new DevConsole();
        console.Open();
        foreach (char c in "/g")
            console.HandleTextInput(c);
        bool before = session.State.BossDebugInvincible;

        console.Update(session, new HashSet<Keys> { Keys.Down }, 0);
        console.Update(session, new HashSet<Keys> { Keys.Tab }, 0);

        Assert.Equal(before, session.State.BossDebugInvincible);

        // The tabbed-in buffer has no dropdown left (a bare command), so the
        // next Enter falls through to running the literal buffer text.
        console.Update(session, new HashSet<Keys> { Keys.Enter }, 0);

        Assert.NotEqual(before, session.State.BossDebugInvincible);
    }

    [Fact]
    public void CommandMenu_DownPastLastVisibleRow_ScrollsTheWindowInsteadOfStayingPinned()
    {
        // Aphantasia's test-phase dropdown has far more than MaxCommandMenuRows
        // (8) entries, so Down all the way to the 9th candidate must scroll
        // the visible window down by one rather than the highlighted row
        // getting stuck clamped to the last visible slot.
        var session = MakeSession();
        MakeAphantasiaBoss(session);
        var console = new DevConsole();
        console.Open();
        foreach (char c in "/testphase ")
            console.HandleTextInput(c);
        console.Update(session, new HashSet<Keys>(), 0);
        var initial = console.DebugMenuState;
        Assert.True(initial.CandidateCount > 8);
        Assert.Equal(0, initial.ScrollOffset);

        for (int i = 0; i < 8; i++)
            console.Update(session, new HashSet<Keys> { Keys.Down }, 0);

        var afterEightDowns = console.DebugMenuState;
        Assert.Equal(8, afterEightDowns.Selection);
        Assert.Equal(1, afterEightDowns.ScrollOffset);

        // Walking back Up past the top of the now-scrolled window scrolls it
        // back up in step with the selection.
        for (int i = 0; i < 8; i++)
            console.Update(session, new HashSet<Keys> { Keys.Up }, 0);

        var afterEightUps = console.DebugMenuState;
        Assert.Equal(0, afterEightUps.Selection);
        Assert.Equal(0, afterEightUps.ScrollOffset);
    }

    [Fact]
    public void CommandMenu_ScrollWheel_MovesSelectionLikeArrowKeysAndScrollsPastOverflow()
    {
        // A negative scroll delta (wheel away from the user, MonoGame's
        // ScrollWheelValue decreasing) should step the selection down one
        // notch at a time, same direction as Down, including scrolling the
        // window past MaxCommandMenuRows (8) overflow the same way Down does.
        var session = MakeSession();
        MakeAphantasiaBoss(session);
        var console = new DevConsole();
        console.Open();
        foreach (char c in "/testphase ")
            console.HandleTextInput(c);
        console.Update(session, new HashSet<Keys>(), 0);
        Assert.Equal((0, 0), (console.DebugMenuState.Selection, console.DebugMenuState.ScrollOffset));

        // One notch = 120 units; eight notches down should land exactly on
        // the same (selection, scroll) state the arrow-key overflow test
        // reaches after eight individual Down presses.
        console.Update(session, new HashSet<Keys>(), 0, scrollWheelDelta: -8 * 120);

        var afterScrollDown = console.DebugMenuState;
        Assert.Equal(8, afterScrollDown.Selection);
        Assert.Equal(1, afterScrollDown.ScrollOffset);

        // Scrolling toward the user steps back up the same way, notch for
        // notch, landing back at the top.
        console.Update(session, new HashSet<Keys>(), 0, scrollWheelDelta: 8 * 120);

        var afterScrollUp = console.DebugMenuState;
        Assert.Equal(0, afterScrollUp.Selection);
        Assert.Equal(0, afterScrollUp.ScrollOffset);
    }

    [Fact]
    public void CommandMenu_SlashGThenDownThenEnter_SelectsGodAndRunsItImmediately()
    {
        // "g" matches "give", "god", and "vfxgallery" (in that Commands-table
        // order) -- "god" takes no arguments, so selecting it should run
        // immediately rather than filling "/god " and waiting for more.
        var session = MakeSession();
        var console = new DevConsole();
        console.Open();
        foreach (char c in "/g")
            console.HandleTextInput(c);
        bool before = session.State.BossDebugInvincible;

        console.Update(session, new HashSet<Keys> { Keys.Down }, 0);
        console.Update(session, new HashSet<Keys> { Keys.Enter }, 0);

        Assert.NotEqual(before, session.State.BossDebugInvincible);
    }

    [Fact]
    public void CommandMenu_SpawnChainsThroughItemAndRarityDropdownsBeforeExecutingOnce()
    {
        var session = MakeSession();
        var console = new DevConsole();
        console.Open();

        // "spawn" is the sole match for "/spawn" and takes arguments --
        // selecting it must fill "/spawn " into the buffer rather than
        // running it (there's nothing valid to spawn yet).
        foreach (char c in "/spawn")
            console.HandleTextInput(c);
        console.Update(session, new HashSet<Keys> { Keys.Enter }, 0);
        Assert.Empty(session.State.LootCrateList);

        // The item-name dropdown is now live and unfiltered; a bare Enter
        // picks its first candidate and fills it in rather than running,
        // since rarity is still an unfilled slot after it.
        foreach (char c in "3 ")
            console.HandleTextInput(c);
        console.Update(session, new HashSet<Keys> { Keys.Enter }, 0);
        Assert.Empty(session.State.LootCrateList);

        // Rarity is the last configured slot -- a bare Enter here picks its
        // first candidate and runs the finished command.
        console.Update(session, new HashSet<Keys> { Keys.Enter }, 0);

        Assert.Single(session.State.LootCrateList);
    }
}
