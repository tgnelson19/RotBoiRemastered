using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
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
}
