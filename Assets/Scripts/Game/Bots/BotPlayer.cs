using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum BotDifficulty { Facil, Medio, Dificil }

/// <summary>
/// Cérebro de um jogador controlado pelo computador. Ele "aperta botões" no InputSource do bot,
/// exatamente como um jogador humano, então usa as mesmas regras do jogo.
/// </summary>
public class BotPlayer
{
    public BotDifficulty Difficulty;
    public bool NeverJump;                 // só para testes
    public static bool PlaceOutOfTheWay;   // só para testes: coloca itens longe do caminho

    public BotPlayer(BotDifficulty d) { Difficulty = d; }

    public string Label => Difficulty == BotDifficulty.Facil ? "Fácil" : Difficulty == BotDifficulty.Medio ? "Médio" : "Difícil";

    public Color LabelColor => Difficulty == BotDifficulty.Facil ? new Color(0.5f, 1f, 0.45f)
                             : Difficulty == BotDifficulty.Medio ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.4f, 0.35f);

    // ------------------------------------------------------------------ ajustes por dificuldade

    float ReactionTime => Difficulty == BotDifficulty.Facil ? 0.45f : Difficulty == BotDifficulty.Medio ? 0.18f : 0.04f;
    float JumpErrorChance => Difficulty == BotDifficulty.Facil ? 0.25f : Difficulty == BotDifficulty.Medio ? 0.07f : 0f;
    float DangerCheckChance => Difficulty == BotDifficulty.Facil ? 0.25f : 1f;
    float EvadeChance => Difficulty == BotDifficulty.Facil ? 0.15f : Difficulty == BotDifficulty.Medio ? 0.6f : 1f;
    bool WatchesBones => Difficulty == BotDifficulty.Dificil;
    float WalkSpeed => Difficulty == BotDifficulty.Facil ? 0.8f : 1f;
    float ThinkTime => Difficulty == BotDifficulty.Facil ? Random.Range(1.2f, 2.2f) : Difficulty == BotDifficulty.Medio ? Random.Range(0.6f, 1.2f) : Random.Range(0.3f, 0.7f);
    float Patience => Difficulty == BotDifficulty.Facil ? 3f : Difficulty == BotDifficulty.Medio ? 5f : 8f;

    // sem chegar mais perto do cérebro por este tempo: primeiro arrisca (para de esperar os perigos), depois desiste
    float BoldTime => Patience + 3f;
    const float GiveUpTime = 18f, GiveUpNoPathTime = 7f;

    // a plataforma móvel não conta: os bots não sabem andar nela (o mapa deles ignora ela, como o trem)
    static bool IsBlock(ItemType t) => t == ItemType.Caixa || t == ItemType.Prancha || t == ItemType.Pedra || t == ItemType.Gelo || t == ItemType.CaixaPodre || t == ItemType.Esteira ||
                                       t == ItemType.Gangorra;
    static bool IsTrap(ItemType t) => t == ItemType.Espinhos || t == ItemType.Serra || t == ItemType.SerraMovel || t == ItemType.Canhao || t == ItemType.LancaChamas;

    static Vector2Int SpawnNode(PlayerSlot p) => NavGrid.NodeAt(Level.SpawnPoint(p.Index));

    // ------------------------------------------------------------------ estado

    float delay;
    ItemInstance pickTarget;

    ItemInstance planFor;
    List<Plan> candidates;
    int evalIndex;
    Plan chosen;
    float rotateTimer;

    Vector2Int lastNode = new Vector2Int(-99, -99);
    List<NavGrid.Edge> path;
    float replanTimer, nodeTimer, waitTimer, reactionTimer, evadeRollTimer, fallbackJumpTimer;
    bool evadeRoll, checkDanger;
    float takeoffOffset;
    float holdLock;
    float rideTargetX;
    bool hasRideTarget;
    float airTargetX;
    bool hasAirTarget;
    readonly Dictionary<(Vector2Int, Vector2Int), float> penalties = new Dictionary<(Vector2Int, Vector2Int), float>();
    float stuckTimer, bestRemaining, hailTimer;
    bool noPath;
    NavGrid.Edge? doing;   // ligação que o bot está fazendo agora (para saber se ela deu errado)
    bool leftGround;

    /// <summary>Empacado faz tempo: para de esperar os perigos passarem e arrisca.</summary>
    bool Bold => stuckTimer > BoldTime;

    class Plan { public Vector2Int Pivot; public int Rotation; }

    public void ResetPhase()
    {
        delay = ThinkTime;
        pickTarget = null;
        planFor = null;
        chosen = null;
        candidates = null;
        path = null;
        lastNode = new Vector2Int(-99, -99);
        penalties.Clear();
        hasAirTarget = false;
        waitTimer = nodeTimer = 0f;
        takeoffOffset = 0f;
        stuckTimer = hailTimer = 0f;
        bestRemaining = float.MaxValue;
        noPath = false;
        doing = null;
        leftGround = false;
    }

    static void Press(InputSource i, string what)
    {
        int f = Time.frameCount;
        if (what == "confirm") i.BotConfirmFrame = f;
        else if (what == "cw") i.BotRotateFrame = f;
        else if (what == "ccw") i.BotRotateCCWFrame = f;
        else if (what == "jump") { i.BotJumpFrame = f; i.BotJumpHeld = true; }
    }

    static float Steer(float dx) => Mathf.Abs(dx) > 0.1f ? Mathf.Sign(dx) : 0f;

    public void Tick(GameManager gm, PlayerSlot p, BotSystem sys, float dt)
    {
        p.Input.BotMove = Vector2.zero;
        switch (gm.Phase)
        {
            case Phase.Pick: TickPick(gm, p, sys, dt); break;
            case Phase.Place: TickPlace(p, sys, dt); break;
            case Phase.Run: TickRun(p, sys, dt); break;
            default: p.Input.BotJumpHeld = false; break;
        }
    }

    // ================================================================== escolha

    void TickPick(GameManager gm, PlayerSlot p, BotSystem sys, float dt)
    {
        if (p.Picked || p.Cursor == null) return;
        if (delay > 0f) { delay -= dt; return; }
        if (pickTarget == null || pickTarget.Owner >= 0) pickTarget = ChooseItem(gm.BoxItems, p, sys);
        if (pickTarget == null) return;

        Vector2 to = (Vector2)pickTarget.transform.position + pickTarget.RotatedCenter - p.Cursor.Position;
        if (to.magnitude < 0.3f) Press(p.Input, "confirm");
        else p.Input.BotMove = Vector2.ClampMagnitude(to * 3f, 1f);
    }

    ItemInstance ChooseItem(IReadOnlyList<ItemInstance> items, PlayerSlot p, BotSystem sys)
    {
        var free = items.Where(i => i != null && i.Owner < 0).ToList();
        if (free.Count == 0) return null;
        if (Difficulty == BotDifficulty.Facil) return free[Random.Range(0, free.Count)];

        bool pathExists = sys.Nav != null && sys.Nav.HasPath(SpawnNode(p));
        float noise = Difficulty == BotDifficulty.Medio ? 2f : 0.8f;
        return free.OrderByDescending(i =>
        {
            var t = i.Def.Type;
            float s = pathExists ? (IsTrap(t) ? 3f : IsBlock(t) ? 1f : 1.5f)
                                 : (IsBlock(t) || t == ItemType.Bomba ? 3f : t == ItemType.Mola || t == ItemType.Ventilador ? 1.5f : 0.5f);
            return s + Random.Range(0f, noise);
        }).First();
    }

    // ================================================================== posicionamento

    void TickPlace(PlayerSlot p, BotSystem sys, float dt)
    {
        if (p.Placed || p.Item == null || p.Cursor == null) return;
        if (planFor != p.Item)
        {
            planFor = p.Item;
            chosen = null;
            candidates = GenerateCandidates(p, sys.Nav);
            evalIndex = 0;
            delay = ThinkTime * 0.6f;
        }
        if (delay > 0f) { delay -= dt; return; }

        if (chosen == null)
        {
            chosen = EvaluateNext(p, sys.Nav);
            if (chosen == null) return;
        }

        // girar até a rotação escolhida
        if (p.Item.Def.CanRotate && p.Item.Rotation != chosen.Rotation)
        {
            rotateTimer -= dt;
            if (rotateTimer <= 0f)
            {
                int diff = (chosen.Rotation - p.Item.Rotation + 4) % 4;
                Press(p.Input, diff == 3 ? "cw" : "ccw");
                rotateTimer = 0.12f;
            }
            return;
        }

        Vector2 target = Level.CellCenter(chosen.Pivot) + p.Item.RotatedCenter;
        Vector2 to = target - p.Cursor.Position;
        if (to.magnitude > 0.07f)
        {
            p.Input.BotMove = Vector2.ClampMagnitude(to * 4f, 1f);
            return;
        }
        if (p.Item.CanPlaceAt(p.Item.PivotFor(p.Cursor.Position))) Press(p.Input, "confirm");
        else chosen = null; // alguém ocupou o lugar: escolhe outro
    }

    /// <summary>Testa candidatos (no máximo um por quadro, porque refazer a grade custa caro).</summary>
    Plan EvaluateNext(PlayerSlot p, NavGrid nav)
    {
        var def = p.Item.Def;
        while (evalIndex < candidates.Count)
        {
            var c = candidates[evalIndex++];
            if (!ItemInstance.CanPlace(def, c.Pivot, c.Rotation)) continue;
            bool verify = nav != null && !PlaceOutOfTheWay && (Difficulty == BotDifficulty.Dificil || (Difficulty == BotDifficulty.Medio && evalIndex <= 8));
            if (!verify || evalIndex >= candidates.Count) return c;

            bool hadPath = nav.HasPath(SpawnNode(p));
            bool wantsPath = hadPath || IsBlock(def.Type) || def.Type == ItemType.Bomba || def.Type == ItemType.Mola;
            if (!wantsPath) return c;
            if (!BotSystem.TakeBuildBudget()) { evalIndex--; return null; } // uma grade por vez, para não travar a tela
            Extras(def, c, out var solids, out var hazards, out var blasted);
            var test = NavGrid.Build(solids, hazards, blasted);
            if (test.HasPath(SpawnNode(p))) return c;   // continua (ou passa a ser) possível
            if (!hadPath && evalIndex >= 16) return c;   // não achou nada melhor
            return null;                                 // tenta o próximo no quadro seguinte
        }
        // nenhum serviu: qualquer lugar válido
        for (int i = 0; i < 200; i++)
        {
            var c = new Plan { Pivot = new Vector2Int(Random.Range(1, Level.W - 1), Random.Range(0, Level.H - 2)), Rotation = 0 };
            if (ItemInstance.CanPlace(def, c.Pivot, c.Rotation)) return c;
        }
        return new Plan { Pivot = Level.WorldToCell(p.Cursor.Position), Rotation = p.Item.Rotation };
    }

    /// <summary>Como o item ficaria para a grade de navegação (sólido, perigo ou, para a bomba, o que ela destrói).</summary>
    static void Extras(ItemDef def, Plan c, out List<Rect> solids, out List<Rect> hazards, out HashSet<GridObject> blasted)
    {
        solids = new List<Rect>();
        hazards = new List<Rect>();
        blasted = def.Type == ItemType.Bomba ? ItemInstance.BlastVictims(c.Pivot) : null;
        // a plataforma móvel não entra no mapa dos bots (ela não fica onde foi colocada)
        var cells = def.Type == ItemType.PlataformaMovel ? new Vector2Int[0] : ItemDefs.Footprint(def, c.Rotation);
        foreach (var cell in cells)
        {
            var w = Level.CellCenter(c.Pivot + cell);
            var r = new Rect(w.x - 0.5f, w.y - 0.5f, 1f, 1f);
            if (IsTrap(def.Type) && def.Type != ItemType.Canhao && def.Type != ItemType.LancaChamas)
                hazards.Add(new Rect(r.x + 0.1f, r.y + 0.1f, 0.8f, 0.8f));
            else if (def.Type != ItemType.Bomba && def.Type != ItemType.Portal) solids.Add(r);
        }
        if (def.Type == ItemType.LancaChamas)
        {
            Vector2 dir = ItemDefs.Rotate(Vector2.right, c.Rotation);
            Vector2 a = Level.CellCenter(c.Pivot) + dir * 0.5f, b = Level.CellCenter(c.Pivot) + dir * 3f;
            Vector2 perp = new Vector2(-dir.y, dir.x) * 0.3f;
            hazards.Add(Rect.MinMaxRect(Mathf.Min(a.x, b.x) + Mathf.Min(perp.x, -perp.x), Mathf.Min(a.y, b.y) + Mathf.Min(perp.y, -perp.y),
                                        Mathf.Max(a.x, b.x) + Mathf.Max(perp.x, -perp.x), Mathf.Max(a.y, b.y) + Mathf.Max(perp.y, -perp.y)));
        }
    }

    List<Plan> GenerateCandidates(PlayerSlot p, NavGrid nav)
    {
        var def = p.Item.Def;
        var type = def.Type;
        var list = new List<Plan>();

        if (PlaceOutOfTheWay)
        {
            for (int x = 1 + p.Index * 4; x < Level.W - 1; x += 2) list.Add(new Plan { Pivot = new Vector2Int(x, Level.H - 3), Rotation = 0 });
            return list;
        }

        List<Vector2Int> pathNodes = new List<Vector2Int>();
        var spawn = SpawnNode(p);
        if (nav != null)
        {
            var path = nav.FindPath(spawn);
            foreach (var e in path) pathNodes.Add(e.From);
        }
        bool pathExists = pathNodes.Count > 0;

        if (Difficulty == BotDifficulty.Facil || nav == null)
        {
            AddRandom(list, def, 40);
            return list;
        }

        int goalDir = Level.GoalPosition.x >= Level.SpawnPoint(p.Index).x ? 1 : -1;

        if (!pathExists && (IsBlock(type) || type == ItemType.Bomba || type == ItemType.Mola || type == ItemType.Ventilador))
        {
            // fase impossível: ajuda a passar perto do ponto mais longe que dá para chegar
            var reach = nav.Reachable(spawn);
            var frontier = reach.Count > 0 ? reach.OrderBy(n => Mathf.Abs(NavGrid.NodeFeet(n).x - Level.GoalPosition.x)).ThenByDescending(n => n.y).First() : spawn;
            if (type == ItemType.Bomba)
            {
                // bomba: mira primeiro no que dá para destruir logo à frente desse ponto
                var targets = Level.Occupied.Where(kv => kv.Value != null && !kv.Value.Indestructible).Select(kv => kv.Key)
                    .Where(k => Mathf.Abs(k.x - frontier.x) <= 6 && Mathf.Abs(k.y - frontier.y) <= 5)
                    .OrderBy(k => Mathf.Abs(k.x - frontier.x) + Mathf.Abs(k.y - frontier.y) + ((k.x - frontier.x) * goalDir < 0 ? 4 : 0))
                    .Take(14);
                foreach (var k in targets) list.Add(new Plan { Pivot = k, Rotation = 0 });
            }
            for (int dx = 1; dx <= 4; dx++)
                for (int dy = -3; dy <= 1; dy++)
                {
                    var pivot = frontier + new Vector2Int(dx * goalDir, dy);
                    if (type == ItemType.Bomba) pivot = frontier + new Vector2Int(dx * goalDir, dy + 1);
                    list.Add(new Plan { Pivot = pivot, Rotation = 0 });
                    if (type == ItemType.Prancha && dy >= 0) list.Add(new Plan { Pivot = pivot, Rotation = 1 });
                }
            if (Difficulty == BotDifficulty.Medio) Shuffle(list, 0.3f);
            AddRandom(list, def, 10);
            return list;
        }

        if (pathExists && (IsTrap(type) || (IsBlock(type) && Difficulty == BotDifficulty.Dificil)))
        {
            // armadilha (ou obstáculo) no caminho, de preferência no meio dele
            int n = pathNodes.Count;
            var order = Enumerable.Range(Mathf.Min(2, n - 1), Mathf.Max(1, n - 3)).OrderBy(i => Mathf.Abs(i - n * 0.55f) + Random.Range(0f, 2f)).ToList();
            foreach (int i in order)
            {
                var node = pathNodes[i];
                switch (type)
                {
                    case ItemType.Canhao:
                        for (int d = 3; d <= 5; d++)
                        {
                            list.Add(new Plan { Pivot = node + new Vector2Int(-d, 0), Rotation = 0 });
                            list.Add(new Plan { Pivot = node + new Vector2Int(d, 0), Rotation = 2 });
                        }
                        break;
                    case ItemType.LancaChamas:
                        list.Add(new Plan { Pivot = node + new Vector2Int(-2, 0), Rotation = 0 });
                        list.Add(new Plan { Pivot = node + new Vector2Int(2, 0), Rotation = 2 });
                        list.Add(new Plan { Pivot = node + new Vector2Int(0, 2), Rotation = 3 });
                        break;
                    case ItemType.SerraMovel:
                        list.Add(new Plan { Pivot = node, Rotation = 0 });
                        list.Add(new Plan { Pivot = node + Vector2Int.up, Rotation = 1 });
                        break;
                    default:
                        list.Add(new Plan { Pivot = node, Rotation = 0 });
                        list.Add(new Plan { Pivot = node + Vector2Int.up, Rotation = 0 });
                        break;
                }
            }
            if (Difficulty == BotDifficulty.Medio)
            {
                // o médio erra um pouco a mira
                foreach (var c in list) if (Random.value < 0.4f) c.Pivot += new Vector2Int(Random.Range(-1, 2), Random.Range(0, 2));
                Shuffle(list, 0.5f);
            }
            AddRandom(list, def, 20);
            return list;
        }

        // o resto: perto do caminho
        foreach (var node in pathNodes.OrderBy(_ => Random.value).Take(8))
            list.Add(new Plan { Pivot = node + new Vector2Int(Random.Range(-1, 2), Random.Range(1, 4)), Rotation = 0 });
        AddRandom(list, def, 30);
        return list;
    }

    static void AddRandom(List<Plan> list, ItemDef def, int count)
    {
        for (int i = 0; i < count; i++)
            list.Add(new Plan
            {
                Pivot = new Vector2Int(Random.Range(5, Level.W - 5), Random.Range(0, Level.H - 4)),
                Rotation = def.CanRotate ? Random.Range(0, 4) : 0,
            });
    }

    static void Shuffle(List<Plan> list, float amount)
    {
        for (int i = 0; i < list.Count; i++)
            if (Random.value < amount)
            {
                int j = Random.Range(0, list.Count);
                (list[i], list[j]) = (list[j], list[i]);
            }
    }

    // ================================================================== corrida

    void TickRun(PlayerSlot p, BotSystem sys, float dt)
    {
        var inp = p.Input;
        var z = p.Zombie;
        if (z == null || z.State != ZombieState.Alive || sys.Nav == null) { inp.BotJumpHeld = false; return; }

        // muito tempo sem chegar mais perto do cérebro: desiste, para a rodada não ficar travada esperando
        stuckTimer += dt;
        if (DemoDriver.Active && Bold && stuckTimer - dt <= BoldTime)
            Debug.Log($"[Demo] P{p.Index + 1} bot empacado: vai arriscar em {z.Position} (t={GameManager.Instance.PhaseTime:0.0}s)");
        if (stuckTimer > (noPath ? GiveUpNoPathTime : GiveUpTime))
        {
            if (DemoDriver.Active)
                Debug.Log($"[Demo] P{p.Index + 1} bot desistiu em {z.Position} ({(noPath ? "sem caminho" : "empacado")}, t={GameManager.Instance.PhaseTime:0.0}s)");
            inp.BotJumpHeld = false;
            GameManager.Instance.GiveUp(p);
            return;
        }

        Vector2 pos = z.Position, vel = z.Velocity;
        bool grounded = z.IsGrounded;
        evadeRollTimer -= dt;
        fallbackJumpTimer -= dt;
        holdLock -= dt;
        // segura o pulo até o topo (pulo completo, igual à simulação)
        if (inp.BotJumpHeld && holdLock <= 0f && vel.y <= 0f) inp.BotJumpHeld = false;

        // no ar: vai mirando no ponto de pouso
        if (!grounded)
        {
            leftGround = true;
            if (hasAirTarget) inp.BotMove.x = Steer(airTargetX - pos.x);
            return;
        }
        if (holdLock > 0f) return; // acabou de apertar pulo: espera a física executar
        hasAirTarget = false;

        // perigo chegando? pula para desviar (trem passa embaixo, serra passa por baixo)
        if (evadeRollTimer <= 0f) { evadeRoll = Random.value < EvadeChance; evadeRollTimer = 0.6f; }
        if (evadeRoll && sys.Threatened(z, 0.6f, WatchesBones))
        {
            Evade(p, inp, z, sys);
            return;
        }

        if (z.GroundTrain != null) { RideTrain(p, inp, z, sys); return; }

        var nav = sys.Nav;
        var node = NavGrid.NodeAt(pos);
        if (!nav.Nodes.Contains(node))
        {
            // fora da grade: volta para o nó mais perto (se ele estiver bem aqui em cima/embaixo, não adianta esperar)
            if (nav.Nearest(pos, 1.3f, out var near) && Mathf.Abs(NavGrid.NodeFeet(near).x - pos.x) > 0.1f)
                inp.BotMove.x = Steer(NavGrid.NodeFeet(near).x - pos.x);
            else Fallback(inp, z);
            return;
        }

        if (node != lastNode)
        {
            // pulou e caiu em outro lugar (vento, esteira, caixa podre...): essa ligação não é confiável
            if (doing.HasValue && leftGround && !doing.Value.ToGoal && doing.Value.To != node) Penalize(doing.Value, 6f);
            doing = null;
            leftGround = false;
            lastNode = node;
            nodeTimer = 0f;
            waitTimer = 0f;
            path = null;
            checkDanger = Random.value < DangerCheckChance;
            hailTimer = 0f;
            var route = nav.FindPath(node);
            noPath = route.Count == 0 && !nav.GoalNodes.Contains(node);
            Progress(noPath ? 1000f + Vector2.Distance(NavGrid.NodeFeet(node), Level.GoalPosition) : route.Sum(r => r.Cost));
        }
        nodeTimer += dt;
        replanTimer -= dt;
        if (path == null || replanTimer <= 0f)
        {
            path = nav.FindPath(node, penalties);
            // sem caminho até o cérebro: pelo menos chega no ponto mais perto dele
            if (path.Count == 0 && noPath) path = nav.FindPath(node, penalties, nav.ClosestToGoal(node));
            replanTimer = 1f;
        }
        if (path.Count == 0) { HailMary(p, inp, z, node, dt); return; }

        // empacou neste nó: evita a ligação que não está dando certo
        if (nodeTimer > 3.5f + waitTimer)
        {
            Penalize(path[0], 6f);
            path = null;
            nodeTimer = 0f;
            return;
        }

        var e = path[0];
        if (e.From != node) { path = null; return; }
        if (e.Kind == NavGrid.EdgeKind.Ride) { Board(p, inp, z, sys, e, node, dt); return; }

        if (e.Kind != NavGrid.EdgeKind.Jump)
        {
            if (checkDanger && !Bold && !sys.SafeMove(nav, z, e.TargetX, false, WatchesBones))
            {
                Wait(e, dt);
                return;
            }
            inp.BotMove.x = NavGrid.GroundSteer(e.TargetX - pos.x, vel.x, z.OnIce) * WalkSpeed;
            doing = e;
            airTargetX = e.TargetX;
            hasAirTarget = true;
            return;
        }

        // pulo: para no meio do nó, espera o tempo de reação e pula mirando no destino
        float takeoff = NavGrid.NodeFeet(node).x + takeoffOffset;
        if (Mathf.Abs(pos.x - takeoff) > 0.15f)
        {
            inp.BotMove.x = NavGrid.GroundSteer(takeoff - pos.x, vel.x, z.OnIce);
            reactionTimer = ReactionTime * Random.Range(0.7f, 1.3f);
            return;
        }
        if (Mathf.Abs(vel.x) > 1.2f) { inp.BotMove.x = -Mathf.Sign(vel.x); return; }
        if (reactionTimer > 0f) { reactionTimer -= dt; return; }
        if (NeverJump) return;
        if (checkDanger && !Bold && !sys.SafeMove(nav, z, e.TargetX, true, WatchesBones))
        {
            if (DemoDriver.Active && waitTimer == 0f) Debug.Log($"[Demo] P{p.Index + 1} bot espera (perigo no pulo) em {pos}");
            Wait(e, dt);
            return;
        }

        float target = e.TargetX;
        bool sloppy = Random.value < JumpErrorChance;
        if (sloppy) target += Random.Range(-1.3f, 1.3f); // pulo mal calculado
        takeoffOffset = Random.value < JumpErrorChance ? Random.Range(-0.4f, 0.4f) : 0f;
        Jump(inp, target);
        doing = sloppy ? (NavGrid.Edge?)null : e; // erro do próprio bot não conta contra a ligação
    }

    void Penalize(NavGrid.Edge e, float amount)
    {
        var key = (e.From, e.To);
        penalties[key] = (penalties.TryGetValue(key, out float old) ? old : 0f) + amount;
    }

    /// <summary>Anota quanto falta até o cérebro; chegar mais perto zera o tempo empacado.</summary>
    void Progress(float remaining)
    {
        if (remaining >= bestRemaining - 0.25f) return;
        bestRemaining = remaining;
        stuckTimer = 0f;
    }

    /// <summary>Espera o perigo passar; se demorar demais, procura outro caminho.</summary>
    void Wait(NavGrid.Edge e, float dt)
    {
        waitTimer += dt;
        if (waitTimer < Patience) return;
        Penalize(e, 8f);
        path = null;
        waitTimer = 0f;
        nodeTimer = 0f;
    }

    /// <summary>Foge de um perigo: testa pular parado, correr ou pular para os lados, e faz o que sobrevive.</summary>
    void Evade(PlayerSlot p, InputSource inp, Zombie z, BotSystem sys)
    {
        var nav = sys.Nav;
        Vector2 pos = z.Position;
        var options = new (bool jump, float tx)[]
        {
            (true, pos.x), (false, pos.x - 3f), (false, pos.x + 3f), (true, pos.x - 3f), (true, pos.x + 3f),
        };
        foreach (var (jump, tx) in options)
        {
            var o = nav.Simulate(pos, z.Velocity, jump, tx, out _, out _,
                                 (body, ahead) => sys.DynamicHazardAt(body, ahead, WatchesBones, z.GroundTrain), 1.2f);
            if (o == NavGrid.Outcome.Death) continue;
            if (DemoDriver.Active) Debug.Log($"[Demo] P{p.Index + 1} bot desvia ({(jump ? "pulo" : "corre")} para x={tx:0.0}) em {pos}");
            if (jump) Jump(inp, tx);
            else { inp.BotMove.x = Steer(tx - pos.x); airTargetX = tx; hasAirTarget = true; }
            return;
        }
        // nada é seguro: pula e torce
        Jump(inp, pos.x);
    }

    void Jump(InputSource inp, float targetX)
    {
        doing = null;
        Press(inp, "jump");
        holdLock = 0.15f;
        airTargetX = targetX;
        hasAirTarget = true;
    }

    static bool InTrainLane(Vector2 feet)
    {
        var n = NavGrid.NodeAt(feet);
        foreach (var zone in Level.TrainZones) if (zone.Contains(n)) return true;
        return false;
    }

    /// <summary>Do lado da pista: espera o trem chegar e pula no teto dele.</summary>
    void Board(PlayerSlot p, InputSource inp, Zombie z, BotSystem sys, NavGrid.Edge e, Vector2Int node, float dt)
    {
        Vector2 pos = z.Position, vel = z.Velocity;
        float cx = NavGrid.NodeFeet(node).x;
        if (Mathf.Abs(pos.x - cx) > 0.15f)
        {
            inp.BotMove.x = NavGrid.GroundSteer(cx - pos.x, vel.x, z.OnIce);
            reactionTimer = ReactionTime;
            return;
        }
        if (Mathf.Abs(vel.x) > 1.2f) { inp.BotMove.x = -Mathf.Sign(vel.x); return; }
        if (reactionTimer > 0f) { reactionTimer -= dt; return; }
        if (NeverJump) return;

        int dir = e.TargetX >= pos.x ? 1 : -1;
        for (int k = 1; k <= 6; k++)
        {
            float tx = pos.x + dir * k * 0.75f;
            var o = sys.Nav.Simulate(pos, vel, true, tx, out _, out _,
                                     (body, ahead) => sys.DynamicHazardAt(body, ahead, WatchesBones, null), 0f, false,
                                     (Rect box, float ahead, out float top) => sys.TrainBodyAt(box, ahead, out top));
            if (o != NavGrid.Outcome.LandedOnTrain) continue;
            if (DemoDriver.Active) Debug.Log($"[Demo] P{p.Index + 1} bot embarca no trem (pulo para x={tx:0.0}) em {pos}");
            rideTargetX = e.TargetX;
            hasRideTarget = true;
            Jump(inp, tx);
            return;
        }
        Wait(e, dt);
    }

    /// <summary>Em cima do trem: anda no teto até o lado certo e desce quando o pulo de saída é seguro.</summary>
    void RideTrain(PlayerSlot p, InputSource inp, Zombie z, BotSystem sys)
    {
        var train = z.GroundTrain;
        Vector2 pos = z.Position;
        if (!hasRideTarget) { rideTargetX = ExitTarget(sys.Nav, pos); hasRideTarget = true; } // subiu sem querer
        float tx = rideTargetX;
        int dir = tx >= pos.x ? 1 : -1;

        if (Mathf.Abs(tx - pos.x) < 5.5f && !NeverJump)
        {
            var o = sys.Nav.Simulate(pos, z.Velocity, true, tx, out var end, out _,
                                     (body, ahead) => sys.DynamicHazardAt(body, ahead, WatchesBones, train), 0f, true);
            if (o == NavGrid.Outcome.Goal || (o == NavGrid.Outcome.Landed && !InTrainLane(end)))
            {
                if (DemoDriver.Active) Debug.Log($"[Demo] P{p.Index + 1} bot desce do trem (pulo para x={tx:0.0}) em {pos}");
                hasRideTarget = false;
                Jump(inp, tx);
                return;
            }
        }
        // vai para a ponta da frente do teto (longe do para-choque): quando o trem chegar no fim, o pulo de saída é curto
        float endX = dir > 0 ? train.RightX - 0.7f : train.LeftX + 0.7f;
        inp.BotMove.x = NavGrid.GroundSteer(endX - pos.x, z.Velocity.x - train.Velocity.x, false);
    }

    /// <summary>Onde descer do trem: um lugar ao lado da pista em que ele está que tenha caminho até o cérebro (o mais curto).</summary>
    static float ExitTarget(NavGrid nav, Vector2 pos)
    {
        var at = NavGrid.NodeAt(pos);
        float best = float.MaxValue, x = Level.GoalPosition.x;
        foreach (var zone in Level.TrainZones)
        {
            if (at.x < zone.xMin - 1 || at.x > zone.xMax || Mathf.Abs(at.y - (zone.yMin + 1)) > 3) continue;
            foreach (var n in nav.Nodes)
            {
                if (zone.Contains(n) || n.y < zone.yMin - 1 || n.y > zone.yMin + 3) continue;
                if (!(n.x >= zone.xMin - 2 && n.x < zone.xMin) && !(n.x > zone.xMax - 1 && n.x <= zone.xMax + 1)) continue;
                var path = nav.FindPath(n);
                if (path.Count == 0 && !nav.GoalNodes.Contains(n)) continue;
                float cost = path.Sum(e => e.Cost);
                if (cost < best) { best = cost; x = NavGrid.NodeFeet(n).x; }
            }
        }
        return x;
    }

    /// <summary>
    /// Sem caminho até o cérebro e já no ponto mais perto dele: olha um pouco e pula no escuro na direção dele
    /// (às vezes a física surpreende; senão ele cai, ou acaba desistindo).
    /// </summary>
    void HailMary(PlayerSlot p, InputSource inp, Zombie z, Vector2Int node, float dt)
    {
        Vector2 pos = z.Position;
        inp.BotMove.x = NavGrid.GroundSteer(NavGrid.NodeFeet(node).x - pos.x, z.Velocity.x, z.OnIce);
        hailTimer += dt;
        if (hailTimer < 1.5f || NeverJump) return;
        hailTimer = 0f;
        int dir = Level.GoalPosition.x >= pos.x ? 1 : -1;
        if (DemoDriver.Active) Debug.Log($"[Demo] P{p.Index + 1} bot sem caminho: pulo no escuro em {pos}");
        Jump(inp, pos.x + dir * 12f);
    }

    /// <summary>Fora da grade e sem saber onde está: vai na direção do cérebro pulando paredes e buracos.</summary>
    void Fallback(InputSource inp, Zombie z)
    {
        Vector2 pos = z.Position;
        int dir = Level.GoalPosition.x >= pos.x ? 1 : -1;
        if (noPath) Progress(1000f + Vector2.Distance(pos, Level.GoalPosition)); // sem caminho: vale chegar mais perto
        inp.BotMove.x = dir * WalkSpeed;
        var filter = new ContactFilter2D { useTriggers = false };
        var hits = new List<Collider2D>();
        bool wall = Physics2D.OverlapBox(pos + new Vector2(0.7f * dir, 0.5f), new Vector2(0.3f, 0.5f), 0f, filter, hits) > 0 &&
                    hits.Any(h => h.attachedRigidbody == null || h.attachedRigidbody.GetComponent<Zombie>() == null);
        hits.Clear();
        bool gap = Physics2D.OverlapBox(pos + new Vector2(1.0f * dir, -0.6f), new Vector2(0.4f, 1f), 0f, filter, hits) == 0;
        if ((wall || gap) && fallbackJumpTimer <= 0f && !NeverJump)
        {
            Jump(inp, pos.x + dir * 8f); // pulo inteiro: um pulo curto cai no buraco
            fallbackJumpTimer = 0.5f;
        }
    }
}
