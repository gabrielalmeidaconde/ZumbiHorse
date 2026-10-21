using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mapa de navegação dos bots. Cada "nó" é uma célula onde um zumbi consegue ficar em pé.
/// As ligações entre nós (andar, cair, pular) são descobertas simulando o movimento com a
/// mesma física do Zombie, contra os colisores reais da fase. Perigos que se movem
/// (serras móveis, trens, fogo, ossos) não entram aqui: o bot confere na hora de agir.
/// </summary>
public class NavGrid
{
    public const float Dt = 1f / 60f;
    const float HalfW = Zombie.Width / 2f, BodyH = Zombie.Height;
    const int MaxSteps = 110;

    public enum EdgeKind { Walk, Jump, Drop, Ride }

    public struct Edge
    {
        public Vector2Int From, To;
        public EdgeKind Kind;
        public float TargetX;
        public float Cost;
        public bool ToGoal;
    }

    public enum Outcome { Landed, Reached, Goal, Death, Timeout, LandedOnTrain }

    /// <summary>Perigo dinâmico: (caixa do zumbi, segundos à frente) → morre?</summary>
    public delegate bool DynamicHazard(Rect body, float ahead);

    /// <summary>Plataforma que se move (teto do trem): a caixa encosta nela? devolve a altura do teto.</summary>
    public delegate bool MovingSolid(Rect box, float ahead, out float top);

    readonly List<Rect> solids = new List<Rect>();
    readonly List<bool> solidIce = new List<bool>();
    readonly List<Rect> hazards = new List<Rect>();
    readonly Dictionary<Vector2Int, List<int>> solidBuckets = new Dictionary<Vector2Int, List<int>>();
    readonly Dictionary<Vector2Int, List<int>> hazardBuckets = new Dictionary<Vector2Int, List<int>>();
    readonly List<Rect> goals = new List<Rect>();

    public readonly HashSet<Vector2Int> Nodes = new HashSet<Vector2Int>();
    public readonly HashSet<Vector2Int> GoalNodes = new HashSet<Vector2Int>();
    public readonly Dictionary<Vector2Int, List<Edge>> Edges = new Dictionary<Vector2Int, List<Edge>>();

    public float GoalX { get; private set; }

    // ================================================================== construção

    /// <summary>
    /// Monta a grade a partir dos colisores atuais (mais caixas extras, para testar onde colocar um item;
    /// 'removed' finge que esses objetos já explodiram, para testar uma bomba).
    /// </summary>
    public static NavGrid Build(List<Rect> extraSolids = null, List<Rect> extraHazards = null, ICollection<GridObject> removed = null)
    {
        var g = new NavGrid();
        g.Collect(extraSolids, extraHazards, removed);
        g.BuildNodes();
        g.BuildEdges();
        g.AddTrainEdges();
        return g;
    }

    void Collect(List<Rect> extraSolids, List<Rect> extraHazards, ICollection<GridObject> removed)
    {
        GoalX = Level.GoalPosition.x;
        foreach (var c in Object.FindObjectsByType<Collider2D>(FindObjectsSortMode.None))
        {
            if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
            var rb = c.attachedRigidbody;
            if (rb != null && (rb.GetComponent<Zombie>() != null || rb.GetComponent<Projectile>() != null)) continue;
            if (c.GetComponentInParent<Train>() != null) continue;          // trem é perigo dinâmico
            var item = c.GetComponentInParent<ItemInstance>();
            if (item != null && !item.Placed) continue;                      // fantasmas e itens da caixa
            if (removed != null && removed.Count > 0)
            {
                var g = c.GetComponentInParent<GridObject>();
                if (g != null && removed.Contains(g)) continue;
            }

            var b = c.bounds;
            var r = Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);
            if (!c.isTrigger)
            {
                var surf = c.GetComponent<Surface>();
                AddRect(solids, solidBuckets, r);
                solidIce.Add(surf != null && surf.Slippery);
                continue;
            }

            if (c.GetComponent<Goal>() != null) { goals.Add(r); continue; }
            if (c.GetComponent<Hazard>() != null)
            {
                if (c.GetComponentInParent<MovingSaw>() != null) continue;   // dinâmico
                AddRect(hazards, hazardBuckets, Grow(r, 0.05f));          // margem de segurança
                continue;
            }
            // molas e portais: os bots evitam (o movimento fica imprevisível)
            if (c.GetComponent<Spring>() != null || c.GetComponent<Portal>() != null)
                AddRect(hazards, hazardBuckets, r);
        }
        if (extraSolids != null) foreach (var r in extraSolids) { AddRect(solids, solidBuckets, r); solidIce.Add(false); }
        if (extraHazards != null) foreach (var r in extraHazards) AddRect(hazards, hazardBuckets, r);
    }

    static Rect Grow(Rect r, float m) => Rect.MinMaxRect(r.xMin - m, r.yMin - m, r.xMax + m, r.yMax + m);

    static Vector2Int Bucket(float x, float y) =>
        new Vector2Int(Mathf.FloorToInt(x - Level.Origin.x), Mathf.FloorToInt(y - Level.Origin.y));

    static void AddRect(List<Rect> list, Dictionary<Vector2Int, List<int>> buckets, Rect r)
    {
        int idx = list.Count;
        list.Add(r);
        var a = Bucket(r.xMin, r.yMin);
        var b = Bucket(r.xMax, r.yMax);
        a.x = Mathf.Max(a.x, -2); a.y = Mathf.Max(a.y, -4);
        b.x = Mathf.Min(b.x, Level.W + 1); b.y = Mathf.Min(b.y, Level.H + 2);
        for (int x = a.x; x <= b.x; x++)
            for (int y = a.y; y <= b.y; y++)
            {
                var k = new Vector2Int(x, y);
                if (!buckets.TryGetValue(k, out var l)) buckets[k] = l = new List<int>();
                l.Add(idx);
            }
    }

    bool Overlaps(List<Rect> list, Dictionary<Vector2Int, List<int>> buckets, Rect r)
    {
        var a = Bucket(r.xMin, r.yMin);
        var b = Bucket(r.xMax, r.yMax);
        for (int x = a.x; x <= b.x; x++)
            for (int y = a.y; y <= b.y; y++)
            {
                if (!buckets.TryGetValue(new Vector2Int(x, y), out var l)) continue;
                foreach (int i in l)
                {
                    var o = list[i];
                    if (r.xMin < o.xMax && r.xMax > o.xMin && r.yMin < o.yMax && r.yMax > o.yMin) return true;
                }
            }
        return false;
    }

    public static Rect Body(Vector2 feet) => Rect.MinMaxRect(feet.x - HalfW, feet.y + 0.02f, feet.x + HalfW, feet.y + BodyH);

    public bool SolidAt(Rect r) => Overlaps(solids, solidBuckets, r);
    public bool HazardAt(Rect r) => Overlaps(hazards, hazardBuckets, r);

    bool GoalAt(Rect r)
    {
        foreach (var g in goals)
            if (r.xMin < g.xMax && r.xMax > g.xMin && r.yMin < g.yMax && r.yMax > g.yMin) return true;
        return false;
    }

    /// <summary>Tem chão logo abaixo dos pés? Devolve a altura do topo.</summary>
    bool Ground(Vector2 feet, out float top) => Ground(feet, out top, out _);

    bool Ground(Vector2 feet, out float top, out bool ice)
    {
        top = float.MinValue;
        ice = false;
        var probe = Rect.MinMaxRect(feet.x - HalfW * 0.8f, feet.y - 0.06f, feet.x + HalfW * 0.8f, feet.y + 0.005f);
        var a = Bucket(probe.xMin, probe.yMin);
        var b = Bucket(probe.xMax, probe.yMax);
        for (int x = a.x; x <= b.x; x++)
            for (int y = a.y; y <= b.y; y++)
            {
                if (!solidBuckets.TryGetValue(new Vector2Int(x, y), out var l)) continue;
                foreach (int i in l)
                {
                    var o = solids[i];
                    if (probe.xMin < o.xMax && probe.xMax > o.xMin && probe.yMin < o.yMax && probe.yMax > o.yMin)
                    {
                        if (o.yMax >= top) ice = solidIce[i];
                        top = Mathf.Max(top, o.yMax);
                    }
                }
            }
        return top > float.MinValue;
    }

    public static Vector2 NodeFeet(Vector2Int n) => new Vector2(Level.Origin.x + n.x + 0.5f, Level.Origin.y + n.y);

    public static Vector2Int NodeAt(Vector2 feet) =>
        new Vector2Int(Mathf.FloorToInt(feet.x - Level.Origin.x), Mathf.RoundToInt(feet.y - Level.Origin.y));

    void BuildNodes()
    {
        for (int x = 0; x < Level.W; x++)
            for (int y = 0; y < Level.H; y++)
            {
                var n = new Vector2Int(x, y);
                var feet = NodeFeet(n);
                var body = Body(feet);
                if (!Ground(feet, out float top) || Mathf.Abs(top - feet.y) > 0.05f) continue;
                if (SolidAt(body) || HazardAt(body)) continue;
                Nodes.Add(n);
                if (GoalAt(body)) GoalNodes.Add(n);
            }
    }

    void BuildEdges()
    {
        foreach (var n in Nodes)
        {
            var list = new List<Edge>();
            Edges[n] = list;
            if (GoalNodes.Contains(n)) continue;
            var feet = NodeFeet(n);

            // tentar ir direto ao cérebro (só vale se chegar nele)
            TryEdge(list, n, feet, GoalX, true, EdgeKind.Jump, true);
            TryEdge(list, n, feet, GoalX, false, EdgeKind.Walk, true);

            foreach (var m in Nodes)
            {
                int dx = m.x - n.x, dy = m.y - n.y;
                if (m == n || Mathf.Abs(dx) > 6 || dy > 3 || dy < -9) continue;
                float tx = NodeFeet(m).x;
                if (dy == 0 && Mathf.Abs(dx) == 1) { TryEdge(list, n, feet, tx, false, EdgeKind.Walk); continue; }
                TryEdge(list, n, feet, tx, true, EdgeKind.Jump);
                if (dy < 0) TryEdge(list, n, feet, tx, false, EdgeKind.Drop);
            }
        }
    }

    /// <summary>
    /// Pistas de trem: andar pelo chão da pista fica caro (o trem atropela), e ganha uma ligação
    /// "carona" de um lado da pista para o outro (embarca no teto e desce do outro lado).
    /// </summary>
    void AddTrainEdges()
    {
        foreach (var z in Level.TrainZones)
        {
            foreach (var list in Edges.Values)
                for (int i = 0; i < list.Count; i++)
                {
                    var e = list[i];
                    if (z.Contains(e.To) && e.Kind != EdgeKind.Ride) { e.Cost += 3f; list[i] = e; }
                }

            var left = new List<Vector2Int>();
            var right = new List<Vector2Int>();
            foreach (var n in Nodes)
            {
                if (n.y < z.yMin - 1 || n.y > z.yMin + 3 || z.Contains(n)) continue;
                if (n.x >= z.xMin - 2 && n.x < z.xMin) left.Add(n);
                else if (n.x > z.xMax - 1 && n.x <= z.xMax + 1) right.Add(n);
            }
            float rideCost = z.width / 4f + 3f;
            foreach (var a in left)
                foreach (var b in right)
                {
                    Edges[a].Add(new Edge { From = a, To = b, Kind = EdgeKind.Ride, TargetX = NodeFeet(b).x, Cost = rideCost, ToGoal = GoalNodes.Contains(b) });
                    Edges[b].Add(new Edge { From = b, To = a, Kind = EdgeKind.Ride, TargetX = NodeFeet(a).x, Cost = rideCost, ToGoal = GoalNodes.Contains(a) });
                }
        }
    }

    void TryEdge(List<Edge> list, Vector2Int from, Vector2 feet, float targetX, bool jump, EdgeKind kind, bool goalOnly = false)
    {
        var o = Simulate(feet, Vector2.zero, jump, targetX, out Vector2 end, out float time);
        if (goalOnly && o != Outcome.Goal) return;
        Vector2Int to;
        bool toGoal = false;
        if (o == Outcome.Goal) { to = from; toGoal = true; }
        else if (o == Outcome.Landed || o == Outcome.Reached)
        {
            to = NodeAt(end);
            if (to == from || !Nodes.Contains(to)) return;
            toGoal = GoalNodes.Contains(to);
            if (o == Outcome.Landed && kind == EdgeKind.Walk) kind = EdgeKind.Drop;
        }
        else return;

        // só aceita pulos/quedas "robustos": com um pouco de erro na saída, tem que dar no mesmo lugar
        if (o == Outcome.Landed || o == Outcome.Goal)
        {
            for (int k = -1; k <= 1; k += 2)
            {
                var o2 = Simulate(feet + new Vector2(0.12f * k, 0f), new Vector2(0.9f * k, 0f), jump, targetX, out Vector2 end2, out _);
                if (o2 != o) return;
                if (o == Outcome.Landed && NodeAt(end2) != to) return;
            }
        }

        // andar de célula em célula é contínuo (o bot não para), então custa só a travessia
        float cost = kind == EdgeKind.Walk ? 1f / Zombie.MaxSpeed + 0.02f : time + (kind == EdgeKind.Jump ? 0.35f : 0.05f);
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (e.ToGoal == toGoal && (toGoal || e.To == to))
            {
                if (cost < e.Cost) list[i] = new Edge { From = from, To = to, Kind = kind, TargetX = targetX, Cost = cost, ToGoal = toGoal };
                return;
            }
        }
        list.Add(new Edge { From = from, To = to, Kind = kind, TargetX = targetX, Cost = cost, ToGoal = toGoal });
    }

    // ================================================================== simulação

    /// <summary>
    /// Controle no chão (igual ao do bot): anda até o alvo e freia apertando para o lado contrário
    /// quando a distância de parada alcança o alvo. No gelo freia bem mais devagar.
    /// </summary>
    public static float GroundSteer(float dx, float vx, bool ice)
    {
        float brake = ice ? 14f : Zombie.GroundAccel;
        if (Mathf.Abs(dx) < 0.1f) return Mathf.Abs(vx) > 0.3f ? -Mathf.Sign(vx) : 0f;
        bool toward = vx * dx > 0f;
        float stop = toward ? vx * vx / (2f * brake) : 0f;
        if (stop >= Mathf.Abs(dx) - 0.05f) return -Mathf.Sign(vx);
        return Mathf.Sign(dx);
    }

    /// <summary>
    /// Simula o zumbi saindo de 'feet' com velocidade inicial 'vel', pulando ou não, sempre indo na
    /// direção de targetX (com o botão de pulo segurado até o topo). Mesma política que o bot usa.
    /// </summary>
    public Outcome Simulate(Vector2 feet, Vector2 vel, bool jump, float targetX, out Vector2 end, out float time,
                            DynamicHazard dynamic = null, float maxTime = 0f, bool forceJump = false, MovingSolid moving = null)
    {
        Vector2 p = feet;
        bool airborne = false, settling = false;
        int steps = maxTime > 0f ? Mathf.CeilToInt(maxTime / Dt) : MaxSteps;
        for (int i = 0; i < steps; i++)
        {
            float t = i * Dt;
            float groundTop = 0f;
            bool ice = false;
            bool grounded = vel.y <= 0.01f && Ground(p, out groundTop, out ice);
            if (grounded && airborne)
            {
                // encostou no chão (a checagem de chão alcança um pouquinho abaixo dos pés, igual ao Zombie)
                p.y = groundTop;
                airborne = false;
                if (GoalAt(Body(p))) { end = p; time = t; return Outcome.Goal; }
                // no gelo ainda escorrega: continua simulando a freada
                if (!(ice && Mathf.Abs(vel.x) > 0.3f)) { end = p; time = t; return Outcome.Landed; }
                settling = true;
            }
            if (settling && grounded && Mathf.Abs(vel.x) <= 0.3f) { end = p; time = t; return Outcome.Landed; }
            if (i == 0 && jump && (grounded || forceJump)) { vel.y = Zombie.JumpVelocity; grounded = false; }

            float dx = targetX - p.x;
            float input;
            if (settling) input = Mathf.Abs(vel.x) > 0.3f ? -Mathf.Sign(vel.x) : 0f;
            else if (grounded) input = GroundSteer(dx, vel.x, ice);
            else input = Mathf.Abs(dx) > 0.1f ? Mathf.Sign(dx) : 0f;
            float accel = grounded ? (ice ? (input != 0 ? 14f : 2.5f) : (input != 0 ? Zombie.GroundAccel : Zombie.GroundDecel))
                                   : (input != 0 ? Zombie.AirAccel : Zombie.AirDecel);
            vel.x = Mathf.MoveTowards(vel.x, input * Zombie.MaxSpeed, accel * Dt);
            if (grounded) vel.y = 0f;
            else
            {
                vel.y -= Zombie.Gravity * (vel.y < 0f ? Zombie.FallMultiplier : 1f) * Dt;
                vel.y = Mathf.Max(vel.y, -Zombie.MaxFall);
            }

            // eixo x
            float nx = p.x + vel.x * Dt;
            if (SolidAt(Body(new Vector2(nx, p.y)))) vel.x = 0f; else p.x = nx;

            // eixo y
            bool landed = false;
            float ny = p.y + vel.y * Dt;
            if (SolidAt(Body(new Vector2(p.x, ny))))
            {
                if (vel.y < 0f && Ground(new Vector2(p.x, ny), out float top) && top <= p.y + 0.05f) { p.y = top; landed = true; }
                vel.y = 0f;
            }
            else p.y = ny;
            if (!grounded && !landed) airborne = true;

            var body = Body(p);
            time = t + Dt;
            end = p;
            if (HazardAt(body) || p.y < Level.KillY) return Outcome.Death;
            if (dynamic != null && dynamic(body, t)) return Outcome.Death;
            if (moving != null && airborne && moving(Rect.MinMaxRect(body.xMin, p.y - 0.06f, body.xMax, body.yMax), t, out float roof))
            {
                // encostou no trem: só vale se for por cima (caindo no teto)
                if (vel.y < 0f && roof <= p.y + 0.15f) { end = new Vector2(p.x, roof); return Outcome.LandedOnTrain; }
                return Outcome.Death;
            }
            if (GoalAt(body)) return Outcome.Goal;
            if (airborne && landed)
            {
                Ground(p, out _, out bool landIce);
                if (!(landIce && Mathf.Abs(vel.x) > 0.3f)) return Outcome.Landed;
                airborne = false;
                settling = true;
            }
            if (!jump && !airborne && Mathf.Abs(targetX - p.x) < 0.12f && Mathf.Abs(vel.x) < 3f && i > 2) return Outcome.Reached;
        }
        end = p;
        time = steps * Dt;
        return Outcome.Timeout;
    }

    // ================================================================== caminhos

    /// <summary>
    /// Menor caminho (Dijkstra) do nó 'start' até o cérebro (ou até 'target', se for dado). Vazio se não tiver caminho.
    /// </summary>
    public List<Edge> FindPath(Vector2Int start, Dictionary<(Vector2Int, Vector2Int), float> penalties = null, Vector2Int? target = null)
    {
        var result = new List<Edge>();
        if (!Nodes.Contains(start)) return result;
        var dist = new Dictionary<Vector2Int, float> { [start] = 0f };
        var prev = new Dictionary<Vector2Int, Edge>();
        var open = new List<Vector2Int> { start };
        var closed = new HashSet<Vector2Int>();
        Edge? bestGoal = null;
        float bestGoalCost = float.MaxValue;

        while (open.Count > 0)
        {
            int bi = 0;
            for (int i = 1; i < open.Count; i++) if (dist[open[i]] < dist[open[bi]]) bi = i;
            var n = open[bi];
            open.RemoveAt(bi);
            if (!closed.Add(n)) continue;
            float dn = dist[n];
            if (dn >= bestGoalCost) break;
            if (target.HasValue ? n == target.Value : GoalNodes.Contains(n)) { BuildResult(result, prev, start, n); return result; }

            foreach (var e in Edges[n])
            {
                float c = dn + e.Cost;
                if (penalties != null && penalties.TryGetValue((e.From, e.To), out float pen)) c += pen;
                if (e.ToGoal && !GoalNodes.Contains(e.To))
                {
                    if (!target.HasValue && c < bestGoalCost) { bestGoalCost = c; bestGoal = e; }
                    continue;
                }
                if (dist.TryGetValue(e.To, out float old) && old <= c) continue;
                dist[e.To] = c;
                prev[e.To] = e;
                open.Add(e.To);
            }
        }
        if (bestGoal.HasValue)
        {
            BuildResult(result, prev, start, bestGoal.Value.From);
            result.Add(bestGoal.Value);
        }
        return result;
    }

    static void BuildResult(List<Edge> result, Dictionary<Vector2Int, Edge> prev, Vector2Int start, Vector2Int end)
    {
        var n = end;
        var rev = new List<Edge>();
        while (n != start && prev.TryGetValue(n, out var e)) { rev.Add(e); n = e.From; }
        rev.Reverse();
        result.AddRange(rev);
    }

    public bool HasPath(Vector2Int start) => FindPath(start).Count > 0 || GoalNodes.Contains(start);

    /// <summary>Todos os nós alcançáveis a partir de start.</summary>
    public HashSet<Vector2Int> Reachable(Vector2Int start)
    {
        var seen = new HashSet<Vector2Int>();
        if (!Nodes.Contains(start)) return seen;
        var stack = new Stack<Vector2Int>();
        stack.Push(start);
        seen.Add(start);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            foreach (var e in Edges[n])
                if (!e.ToGoal || GoalNodes.Contains(e.To))
                    if (seen.Add(e.To)) stack.Push(e.To);
        }
        return seen;
    }

    /// <summary>O nó alcançável a partir de 'start' que fica mais perto do cérebro.</summary>
    public Vector2Int ClosestToGoal(Vector2Int start)
    {
        var best = start;
        float bestD = float.MaxValue;
        foreach (var n in Reachable(start))
        {
            float d = (NodeFeet(n) - Level.GoalPosition).sqrMagnitude;
            if (d < bestD) { bestD = d; best = n; }
        }
        return best;
    }

    /// <summary>Nó mais próximo de uma posição (para quando o bot não está exatamente num nó).</summary>
    public bool Nearest(Vector2 feet, float maxDist, out Vector2Int node)
    {
        node = default;
        float best = maxDist;
        bool found = false;
        foreach (var n in Nodes)
        {
            float d = Vector2.Distance(NodeFeet(n), feet);
            if (d < best) { best = d; node = n; found = true; }
        }
        return found;
    }
}
