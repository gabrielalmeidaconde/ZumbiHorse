using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A fase atual (32x18 células), montada a partir de um MapDef desenhado em texto.
/// Guarda também a grade de ocupação usada para posicionar itens e para as bombas.
/// </summary>
public static class Level
{
    public const int W = 32, H = 18;
    public static readonly Vector2 Origin = new Vector2(-W / 2f, -H / 2f);

    public static MapDef Map { get; private set; } = Maps.All[0];

    /// <summary>Áreas onde não dá para colocar itens.</summary>
    public static RectInt StartZone, GoalZone;
    public static readonly List<RectInt> TrainZones = new List<RectInt>();
    public static Vector2Int GoalCell;

    public static readonly Dictionary<Vector2Int, GridObject> Occupied = new Dictionary<Vector2Int, GridObject>();
    public static Transform Root;

    static readonly List<Vector2Int> spawns = new List<Vector2Int>();
    static Transform background;
    static int backgroundTheme = -1;

    public static float KillY => Origin.y - 1.5f;

    public static Vector2 CellCenter(Vector2Int c) => Origin + new Vector2(c.x + 0.5f, c.y + 0.5f);
    public static Vector2Int WorldToCell(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x - Origin.x), Mathf.FloorToInt(p.y - Origin.y));
    public static bool InBounds(Vector2Int c) => c.x >= 0 && c.x < W && c.y >= 0 && c.y < H - 1;

    public static bool IsProtected(Vector2Int c)
    {
        if (StartZone.Contains(c) || GoalZone.Contains(c)) return true;
        foreach (var z in TrainZones) if (z.Contains(c)) return true;
        return false;
    }

    /// <summary>Ponto de nascimento do jogador i (pés no chão da célula marcada com o número).</summary>
    public static Vector2 SpawnPoint(int i)
    {
        var c = spawns.Count > 0 ? spawns[i % spawns.Count] : new Vector2Int(1 + i, 5);
        return new Vector2(Origin.x + c.x + 0.5f, Origin.y + c.y + 0.02f);
    }

    public static Vector2 GoalPosition => CellCenter(GoalCell);

    // ------------------------------------------------------------------ construção

    public static void Build(MapDef map)
    {
        Clear();
        Map = map;
        BuildBackground(map.Theme);
        Root = new GameObject("Fase: " + map.Name).transform;

        FindMarkers(map);

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var c = new Vector2Int(x, y);
                switch (map.At(x, y))
                {
                    case '#': CreateTerrain(c, '#'); break;
                    case 'S': CreateTerrain(c, 'S'); break;
                    case 'i': CreateTerrain(c, 'i'); break;
                    case '=': CreateRail(c); break;
                    case 'a': CreateAcid(c, map.At(x, y + 1) != 'a'); break;
                    case '^': CreateSpikes(c); break;
                    case 'X': CreateSaw(c); break;
                    case 'p': CreatePedestal(c); break;
                    case 'G': CreateGoal(c); break;
                }
            }
        RefreshTerrainSprites();

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                char ch = map.At(x, y);
                if (ch == 't') AddDecor(Art.Tombstone(x), new Vector2Int(x, y), -2);
                else if (ch == 'T') AddDecor(Art.DeadTree, new Vector2Int(x, y), -4);
            }

        // Limites invisíveis (paredes laterais e teto).
        AddWall(new Vector2(Origin.x - 0.5f, 6f), new Vector2(1f, 40f));
        AddWall(new Vector2(-Origin.x + 0.5f, 6f), new Vector2(1f, 40f));
        AddWall(new Vector2(0f, -Origin.y + 0.5f), new Vector2(W + 2f, 1f));

        foreach (var t in map.Trains)
        {
            Train.Create(t, Root);
            TrainZones.Add(TrainZone(t));
        }
        foreach (var s in map.Saws) CreateMovingSaw(s);
    }

    /// <summary>Só os dados da fase (nome, largada, chegada, trilhos), sem criar nada: usado pelo cliente online.</summary>
    public static void LoadInfo(MapDef map)
    {
        Map = map;
        FindMarkers(map);
        TrainZones.Clear();
        foreach (var t in map.Trains) TrainZones.Add(TrainZone(t));
    }

    static RectInt TrainZone(TrainDef t) => new RectInt(t.FromX, t.TrackRow + 1, t.ToX - t.FromX + 1, 2);

    /// <summary>Apaga a fase e o fundo (o cliente online só desenha o que o host manda).</summary>
    public static void ClearAll()
    {
        Clear();
        if (background != null) Object.Destroy(background.gameObject);
        background = null;
        backgroundTheme = -1;
    }

    public static void Clear()
    {
        if (Root != null) Object.Destroy(Root.gameObject);
        Root = null;
        Occupied.Clear();
        TrainZones.Clear();
    }

    static void FindMarkers(MapDef map)
    {
        spawns.Clear();
        GoalCell = new Vector2Int(W - 3, 7);
        var found = new Vector2Int?[4];
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                char ch = map.At(x, y);
                if (ch >= '1' && ch <= '4') found[ch - '1'] = new Vector2Int(x, y);
                else if (ch == 'G') GoalCell = new Vector2Int(x, y);
            }
        foreach (var f in found) if (f.HasValue) spawns.Add(f.Value);
        if (spawns.Count == 0) spawns.Add(new Vector2Int(1, 5));

        int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue;
        foreach (var s in spawns) { minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x); minY = Mathf.Min(minY, s.y); }
        minX = Mathf.Max(0, minX - 1);
        maxX = Mathf.Min(W - 1, maxX + 1);
        StartZone = new RectInt(minX, minY, maxX - minX + 1, 4);

        int gx0 = Mathf.Max(0, GoalCell.x - 2), gx1 = Mathf.Min(W - 1, GoalCell.x + 2);
        GoalZone = new RectInt(gx0, GoalCell.y - 1, gx1 - gx0 + 1, 4);
    }

    static GameObject NewCell(string name, Vector2Int c)
    {
        var go = new GameObject($"{name} {c.x},{c.y}");
        go.transform.SetParent(Root);
        go.transform.position = CellCenter(c);
        return go;
    }

    static GridObject Register(GameObject go, Vector2Int c, bool indestructible, bool terrain = false, char kind = '.')
    {
        var g = go.AddComponent<GridObject>();
        g.IsTerrain = terrain;
        g.TerrainKind = kind;
        g.Indestructible = indestructible;
        g.Cells.Add(c);
        g.Register();
        return g;
    }

    static void AddWall(Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Limite");
        go.transform.SetParent(Root);
        go.transform.position = pos;
        go.AddComponent<BoxCollider2D>().size = size;
    }

    static void CreateTerrain(Vector2Int c, char kind)
    {
        var go = NewCell(kind == 'S' ? "Pedra" : kind == 'i' ? "Gelo" : "Terra", c);
        go.AddComponent<SpriteRenderer>().sortingOrder = 0;
        go.AddComponent<BoxCollider2D>().size = Vector2.one;
        if (kind == 'i') go.AddComponent<Surface>().Slippery = true;
        Register(go, c, kind == 'S', true, kind);
    }

    /// <summary>Atualiza qual bloco de chão tem grama (o de cima de cada coluna).</summary>
    public static void RefreshTerrainSprites()
    {
        foreach (var kv in Occupied)
        {
            var g = kv.Value;
            if (g == null || !g.IsTerrain) continue;
            var c = kv.Key;
            bool top = !(Occupied.TryGetValue(c + Vector2Int.up, out var above) && above != null && above.IsTerrain);
            var sr = g.GetComponent<SpriteRenderer>();
            if (sr == null) continue;
            sr.sprite = g.TerrainKind == 'S' ? Art.Stone(top) : g.TerrainKind == 'i' ? Art.IceTile : Art.Ground(top, c.x * 7 + c.y * 3);
        }
    }

    static void CreateRail(Vector2Int c)
    {
        var go = NewCell("Trilho", c);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Art.Rail;
        sr.sortingOrder = 1;
        var box = go.AddComponent<BoxCollider2D>();
        box.size = new Vector2(1f, 0.3f);
        box.offset = new Vector2(0f, 0.35f);
        Register(go, c, true);
    }

    static void CreateAcid(Vector2Int c, bool top)
    {
        var go = NewCell("Acido", c);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sortingOrder = 14;
        SpriteAnim.Add(go, 3f, Art.Acid(top, 0), Art.Acid(top, 1));
        var box = go.AddComponent<BoxCollider2D>();
        box.isTrigger = true;
        box.size = new Vector2(1f, top ? 0.75f : 1f);
        box.offset = new Vector2(0f, top ? -0.12f : 0f);
        go.AddComponent<Hazard>();
        Register(go, c, true);
    }

    static void CreateSpikes(Vector2Int c)
    {
        var go = NewCell("Espinhos", c);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Art.Spikes;
        sr.sortingOrder = 6;
        var b = go.AddComponent<BoxCollider2D>();
        b.size = new Vector2(1f, 0.32f);
        b.offset = new Vector2(0f, -0.34f);
        var hz = new GameObject("Pontas");
        hz.transform.SetParent(go.transform, false);
        hz.transform.localPosition = new Vector3(0f, 0.1f, 0f);
        var t = hz.AddComponent<BoxCollider2D>();
        t.isTrigger = true;
        t.size = new Vector2(0.8f, 0.55f);
        hz.AddComponent<Hazard>();
        Register(go, c, true);
    }

    static void CreateSaw(Vector2Int c)
    {
        var go = NewCell("Serra", c);
        var cc = go.AddComponent<CircleCollider2D>();
        cc.isTrigger = true;
        cc.radius = 0.5f;
        go.AddComponent<Hazard>();
        var blade = new GameObject("Lamina");
        blade.transform.SetParent(go.transform, false);
        var sr = blade.AddComponent<SpriteRenderer>();
        sr.sprite = Art.Saw;
        sr.sortingOrder = 6;
        blade.AddComponent<Spinner>().DegreesPerSecond = -420f;
        Register(go, c, true);
    }

    static void CreateMovingSaw(SawPathDef def)
    {
        var go = new GameObject("Serra movel");
        go.transform.SetParent(Root);
        go.transform.position = CellCenter(def.From);
        MovingSaw.Build(go, Vector2.zero, def.To - def.From, def.Speed);
        var g = go.AddComponent<GridObject>();
        g.Indestructible = true;
        Vector2Int step = new Vector2Int(System.Math.Sign(def.To.x - def.From.x), System.Math.Sign(def.To.y - def.From.y));
        var cell = def.From;
        g.Cells.Add(cell);
        while (cell != def.To && step != Vector2Int.zero) { cell += step; g.Cells.Add(cell); }
        g.Register();
    }

    static void CreatePedestal(Vector2Int c)
    {
        var go = NewCell("Pedestal", c);
        go.AddComponent<SpriteRenderer>().sprite = Art.Pedestal;
        go.AddComponent<BoxCollider2D>().size = Vector2.one;
        Register(go, c, true);
    }

    static void CreateGoal(Vector2Int c)
    {
        var goal = NewCell("Cerebro (chegada)", c);
        var col = goal.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(1.1f, 1.1f);
        goal.AddComponent<Goal>();

        var glow = new GameObject("Brilho");
        glow.transform.SetParent(goal.transform, false);
        var gsr = glow.AddComponent<SpriteRenderer>();
        gsr.sprite = Art.Glow;
        gsr.color = new Color(1f, 0.5f, 0.7f, 0.45f);
        gsr.sortingOrder = 3;
        glow.transform.localScale = Vector3.one * 0.8f;

        var brain = new GameObject("Cerebro");
        brain.transform.SetParent(goal.transform, false);
        var bsr = brain.AddComponent<SpriteRenderer>();
        bsr.sprite = Art.Brain;
        bsr.sortingOrder = 4;
        var sp = brain.AddComponent<Spinner>();
        sp.BobAmplitude = 0.12f;
        sp.BobSpeed = 3f;
    }

    /// <summary>Enfeite apoiado no chão da célula c (some junto se o chão embaixo explodir).</summary>
    static void AddDecor(Sprite sprite, Vector2Int c, int order)
    {
        Transform parent = Occupied.TryGetValue(c + Vector2Int.down, out var below) && below != null ? below.transform : Root;
        var go = new GameObject("Decoracao");
        go.transform.SetParent(parent);
        go.transform.position = CellCenter(c) + new Vector2(0f, -0.5f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
    }

    static void BuildBackground(int theme)
    {
        if (background != null && backgroundTheme == theme) return;
        if (background != null) Object.Destroy(background.gameObject);
        backgroundTheme = theme;
        background = new GameObject("Fundo").transform;
        var t = Art.Theme(theme);

        Make("Ceu", Art.Sky(theme), new Vector2(0, 0), new Vector3(70f, 0.5f, 1f), Color.white, -100);
        Make("Brilho da lua", Art.Glow, new Vector2(10.5f, 5.5f), Vector3.one * 2.4f, new Color(t.Moon.r, t.Moon.g, t.Moon.b, 0.22f), -99);
        Make("Lua", Art.Moon, new Vector2(10.5f, 5.5f), Vector3.one, t.Moon, -98);
        Make("Morros distantes", Art.Hills(1 + theme * 2, t.HillFar), new Vector2(0, -8.5f), new Vector3(1.4f, 2.0f, 1f), Color.white, -97);
        Make("Morros", Art.Hills(2 + theme * 2, t.HillNear), new Vector2(2, -9.5f), new Vector3(1.3f, 1.5f, 1f), Color.white, -96);

        var fog1 = Make("Neblina", Art.Fog, new Vector2(-6, -3.5f), new Vector3(2.4f, 1.6f, 1f), Color.white, -95);
        fog1.AddComponent<Spinner>().DriftX = 0.5f;
        var fog2 = Make("Neblina", Art.Fog, new Vector2(14, -6f), new Vector3(3f, 1.4f, 1f), Color.white, 20);
        fog2.AddComponent<Spinner>().DriftX = -0.35f;
    }

    static GameObject Make(string name, Sprite sprite, Vector2 pos, Vector3 scale, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(background);
        go.transform.position = pos;
        go.transform.localScale = scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return go;
    }
}
