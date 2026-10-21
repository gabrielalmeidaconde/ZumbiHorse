using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Roda os bots antes do resto do jogo em cada quadro (para os "botões" deles valerem no mesmo quadro),
/// monta a grade de navegação no começo de cada fase e prevê os perigos que se movem.
/// </summary>
[DefaultExecutionOrder(-90)]
public class BotSystem : MonoBehaviour
{
    public NavGrid Nav { get; private set; }

    static int lastBuildFrame = -100;

    /// <summary>Montar uma grade de teste custa alguns milissegundos: no máximo uma a cada 3 quadros.</summary>
    public static bool TakeBuildBudget()
    {
        if (Time.frameCount - lastBuildFrame < 3) return false;
        lastBuildFrame = Time.frameCount;
        return true;
    }

    Phase lastPhase = (Phase)(-1);
    int lastRound = -1;

    readonly List<MovingSaw> saws = new List<MovingSaw>();
    readonly List<Train> trains = new List<Train>();
    readonly List<Flamethrower> flames = new List<Flamethrower>();

    void Update()
    {
        var gm = GameManager.Instance;
        if (gm == null || Time.timeScale == 0f) return;

        bool anyBot = false;
        foreach (var p in gm.Players) if (p.IsBot) { anyBot = true; break; }

        if (gm.Phase != lastPhase || gm.Round != lastRound)
        {
            lastPhase = gm.Phase;
            lastRound = gm.Round;
            Nav = null;
            if (anyBot && (gm.Phase == Phase.Pick || gm.Phase == Phase.Place || gm.Phase == Phase.Run))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Nav = NavGrid.Build();
                GatherDynamic();
                if (DemoDriver.Active)
                {
                    int edges = 0;
                    foreach (var l in Nav.Edges.Values) edges += l.Count;
                    Debug.Log($"[Demo] grade dos bots ({gm.Phase}): {Nav.Nodes.Count} nós, {edges} ligações, {sw.ElapsedMilliseconds} ms");
                }
            }
            foreach (var p in gm.Players) if (p.IsBot) p.Bot.ResetPhase();
        }

        foreach (var p in gm.Players)
            if (p.IsBot) p.Bot.Tick(gm, p, this, Time.deltaTime);

    }

    void GatherDynamic()
    {
        saws.Clear(); trains.Clear(); flames.Clear();
        saws.AddRange(FindObjectsByType<MovingSaw>(FindObjectsSortMode.None));
        trains.AddRange(FindObjectsByType<Train>(FindObjectsSortMode.None));
        foreach (var r in RunBehaviour.All) if (r is Flamethrower f) flames.Add(f);
    }

    // ------------------------------------------------------------------ perigos dinâmicos

    static bool CircleHitsRect(Vector2 c, float radius, Rect r)
    {
        float x = Mathf.Clamp(c.x, r.xMin, r.xMax), y = Mathf.Clamp(c.y, r.yMin, r.yMax);
        return (c - new Vector2(x, y)).sqrMagnitude < radius * radius;
    }

    /// <summary>Algum perigo que se move vai encostar nesta caixa daqui a 'ahead' segundos?</summary>
    public bool DynamicHazardAt(Rect body, float ahead, bool bones, Train ignoreTrain)
    {
        body = Rect.MinMaxRect(body.xMin - 0.08f, body.yMin - 0.04f, body.xMax + 0.08f, body.yMax + 0.04f); // margem
        foreach (var s in saws)
            if (s != null && CircleHitsRect(s.PredictWorld(ahead), MovingSaw.BladeRadius + 0.1f, body)) return true;
        foreach (var t in trains)
        {
            if (t == null || t == ignoreTrain) continue;
            t.PredictBumpers(ahead, out var l, out var r);
            if (l.Overlaps(body) || r.Overlaps(body)) return true;
        }
        foreach (var f in flames)
            if (f != null && f.PredictOn(ahead) && f.FlameRect.Overlaps(body)) return true;
        if (bones)
            foreach (var p in Projectile.All)
            {
                if (p == null) continue;
                Vector2 at = (Vector2)p.transform.position + p.Direction * Projectile.Speed * ahead;
                if (CircleHitsRect(at, 0.25f, body)) return true;
            }
        return false;
    }

    /// <summary>A caixa encosta no corpo de algum trem daqui a 'ahead' segundos?</summary>
    public bool TrainBodyAt(Rect box, float ahead, out float top)
    {
        top = 0f;
        foreach (var t in trains)
        {
            if (t == null) continue;
            var r = t.PredictBody(ahead);
            if (r.Overlaps(box)) { top = r.yMax; return true; }
        }
        return false;
    }

    /// <summary>Ficar parado aqui vai matar o zumbi nos próximos 'horizon' segundos?</summary>
    public bool Threatened(Zombie z, float horizon, bool bones)
    {
        var body = NavGrid.Body(z.Position);
        body = Rect.MinMaxRect(body.xMin - 0.05f, body.yMin, body.xMax + 0.05f, body.yMax);
        for (float t = 0f; t <= horizon; t += 0.05f)
            if (DynamicHazardAt(body, t, bones, z.GroundTrain)) return true;
        return false;
    }

    /// <summary>Andar (ou pular) agora na direção de targetX é seguro?</summary>
    public bool SafeMove(NavGrid nav, Zombie z, float targetX, bool jump, bool bones)
    {
        var train = z.GroundTrain;
        var o = nav.Simulate(z.Position, z.Velocity, jump, targetX, out _, out _,
                             (body, ahead) => DynamicHazardAt(body, ahead, bones, train), jump ? 0f : 0.7f);
        return o != NavGrid.Outcome.Death;
    }
}
