using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum Phase { Lobby, Pick, Place, Run, RoundEnd, Score, Victory }

public class PlayerSlot
{
    public int Index;
    public InputSource Input;
    public bool Ready;
    public int Score, PrevScore;
    public int JoinFrame;
    public ItemInstance Item;
    public PlayerCursor Cursor;
    public Zombie Zombie;
    public bool Picked, Placed;
    public ItemInstance PortalFirst;   // primeiro portal do par, já colocado
    public BotPlayer Bot;              // null = jogador humano
    public bool IsBot => Bot != null;
    public int Kills;
    public readonly List<KeyValuePair<string, int>> RoundPoints = new List<KeyValuePair<string, int>>();

    // O que a interface mostra. No host vem dos objetos (RefreshViews); no cliente online vem da rede.
    public ZombieState? ZombieView;
    public Vector2 ZombiePos;
    public Vector2? CursorView;
    public ItemType? HeldView;
    public bool HeldSecondPortal;

    public Color Color => Art.PlayerColor(Index);
    public string Name => "P" + (Index + 1);
    public string FullName => IsBot ? Name + " (CPU)" : Name;
}

/// <summary>
/// Controla a partida no estilo Ultimate Chicken Horse:
/// Lobby → Escolha de item → Posicionamento → Corrida até o cérebro → Placar → ... → Vitória.
/// Toda a interface é desenhada com IMGUI (OnGUI) para o projeto não depender de prefabs.
/// A parte do multiplayer online fica em GameManager.Net.cs.
/// </summary>
public partial class GameManager : MonoBehaviour
{
    /// <summary>Pontos para vencer a partida.</summary>
    public static int WinScore = 10;
    public const int MaxPlayers = 8;
    const float PickTime = 20f, PlaceTime = 30f, RunTime = 75f;
    /// <summary>Quando todos os humanos já morreram ou chegaram, os bots têm no máximo este tempo.</summary>
    const float BotsOnlyTime = 12f;
    const int PointsGoal = 2, PointsFirst = 1, PointsTrap = 1;

    public static GameManager Instance { get; private set; }
    public static bool RunActive;

    public Phase Phase { get; private set; } = Phase.Lobby;
    public float PhaseTime { get; private set; }
    public int Round { get; private set; }
    public readonly List<PlayerSlot> Players = new List<PlayerSlot>();
    public IReadOnlyList<ItemInstance> BoxItems => boxItems;

    /// <summary>Mapa escolhido no lobby; Maps.All.Length = aleatório.</summary>
    public int MapIndex { get; private set; }
    /// <summary>Só para testes: força os itens da caixa.</summary>
    public static List<ItemType> ForcedBox;
    /// <summary>Só para testes: deixa a partida começar só com bots.</summary>
    public static bool AllowBotsOnly;

    Camera cam;
    static float shake;
    bool paused;
    float lobbyCountdown = -1f;

    readonly List<ItemInstance> boxItems = new List<ItemInstance>();
    GameObject boxRoot;
    static readonly Vector2 BoxCenter = new Vector2(0f, 0.5f);
    const float BoxSpacing = 6f; // a gangorra tem 5 de largura

    float pickTimer, placeTimer, runTimer, doneTimer = -1f;
    bool timeUp, botsOnly;
    bool nobodyLastRound;   // ninguém chegou na rodada anterior: a próxima caixa traz uma bomba
    readonly List<int> finishOrder = new List<int>();
    string roundMessage = "";
    Color roundMessageColor = Color.white;
    int winner = -1;
    float confettiTimer;

    struct FloatText { public Vector2 Pos; public string Text; public Color Color; public float Start, Duration; public int Size; }
    readonly List<FloatText> floats = new List<FloatText>();

    // ================================================================== ciclo de vida

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        Application.targetFrameRate = 60;
        Application.runInBackground = true;
        Time.fixedDeltaTime = 1f / 60f;
        Time.timeScale = 1f;

        Zombie.All.Clear();
        Projectile.All.Clear();
        RunBehaviour.All.Clear();
        ItemInstance.AllPlaced.Clear();
        RunActive = false;

        SetupCamera();
        Sfx.Init(gameObject);
        gameObject.AddComponent<BotSystem>();
        gameObject.AddComponent<NetGame>();
        Level.Build(Maps.All[0]);
        DemoDriver.TryAttach(this);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Time.timeScale = 1f;
    }

    void SetupCamera()
    {
        cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            cam = go.AddComponent<Camera>();
        }
        if (FindAnyObjectByType<AudioListener>() == null) cam.gameObject.AddComponent<AudioListener>();
        cam.orthographic = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color32(16, 12, 34, 255);
        cam.transform.position = new Vector3(0f, 0f, -10f);
        cam.transform.rotation = Quaternion.identity;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;
    }

    void LateUpdate()
    {
        if (cam == null) return;
        cam.orthographicSize = Mathf.Max(Level.H / 2f + 0.4f, (Level.W / 2f + 0.3f) / Mathf.Max(0.1f, cam.aspect));
        Vector2 off = shake > 0f ? Random.insideUnitCircle * shake : Vector2.zero;
        cam.transform.position = new Vector3(off.x, off.y, -10f);
        shake = Mathf.MoveTowards(shake, 0f, Time.unscaledDeltaTime * 1.5f);
    }

    public static void Shake(float amount) => shake = Mathf.Max(shake, amount);

    void Update()
    {
        HandleOnlineKeys();
        if (Replica) { ReplicaUpdate(); return; }
        if (HandlePause()) return;
        if (paused) return;

        PhaseTime += Time.deltaTime;
        switch (Phase)
        {
            case Phase.Lobby: UpdateLobby(); break;
            case Phase.Pick: UpdatePick(); break;
            case Phase.Place: UpdatePlace(); break;
            case Phase.Run: UpdateRun(); break;
            case Phase.RoundEnd: UpdateRoundEnd(); break;
            case Phase.Score: UpdateScore(); break;
            case Phase.Victory: UpdateVictory(); break;
        }
        floats.RemoveAll(f => Time.time - f.Start > f.Duration);
    }

    void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0f;
        doneTimer = -1f;
    }

    /// <summary>Retorna true se o pause mudou neste quadro (para não repassar o mesmo botão).</summary>
    bool HandlePause()
    {
        if (Phase == Phase.Lobby) return false;
        var devices = ActiveDevices();
        bool toggle = devices.Any(d => d.PauseDown);
        if (paused)
        {
            if (devices.Any(d => d.BackDown))
            {
                BackToLobby();
                return true;
            }
            if (devices.Any(d => d.ConfirmDown)) toggle = true;
        }
        if (!toggle) return false;
        SetPaused(!paused);
        Sfx.Play("tick");
        return true;
    }

    public void SetPaused(bool on)
    {
        paused = on;
        Time.timeScale = paused ? 0f : 1f;
    }

    // ================================================================== lobby

    public PlayerSlot AddPlayer(InputSource input)
    {
        if (Players.Count >= MaxPlayers) return null;
        int index = 0;
        while (Players.Any(p => p.Index == index)) index++;
        var slot = new PlayerSlot { Index = index, Input = input, JoinFrame = Time.frameCount };
        Players.Add(slot);
        Players.Sort((a, b) => a.Index.CompareTo(b.Index));
        Sfx.Play("join");
        Sfx.PlayVar("groan", 0.8f);
        return slot;
    }

    /// <summary>Coloca um bot no próximo lugar livre.</summary>
    public PlayerSlot AddBot(BotDifficulty difficulty)
    {
        var slot = AddPlayer(new InputSource { Kind = DeviceKind.Bot });
        if (slot == null) return null;
        slot.Bot = new BotPlayer(difficulty);
        slot.Ready = true;
        return slot;
    }

    PlayerSlot LastBot => Players.Where(p => p.IsBot).OrderBy(p => p.JoinFrame).LastOrDefault();

    void UpdateLobby()
    {
        var devices = ActiveDevices();
        foreach (var d in devices)
        {
            if (!d.ConfirmDown || Players.Any(p => p.Input.SameDevice(d))) continue;
            // lotado de bots: o humano toma o lugar do último bot
            if (Players.Count >= MaxPlayers && LastBot != null) Players.Remove(LastBot);
            if (Players.Count < MaxPlayers) AddPlayer(d);
        }

        foreach (var d in devices)
        {
            int dx = d.MoveXPressed();
            if (dx != 0) SetMap(MapIndex + dx);

            // bots: cima adiciona, baixo remove, girar (horário) muda a dificuldade do último
            int dy = d.MoveYPressed();
            if (dy > 0)
            {
                if (AddBot(LastBot != null ? LastBot.Bot.Difficulty : BotDifficulty.Medio) == null) Sfx.Play("error");
            }
            else if (dy < 0)
            {
                var last = LastBot;
                if (last != null) { Players.Remove(last); Sfx.Play("tick"); }
            }
            if (d.RotateCWDown && LastBot != null)
            {
                var b = LastBot.Bot;
                b.Difficulty = (BotDifficulty)(((int)b.Difficulty + 1) % 3);
                Sfx.Play("pick");
            }
        }

        foreach (var p in Players.ToList())
        {
            if (!p.Input.Connected) { Players.Remove(p); continue; }
            if (p.JoinFrame == Time.frameCount || !Listening(p.Input)) continue;
            if (p.Input.ConfirmDown && !p.Ready)
            {
                p.Ready = true;
                Sfx.Play("pick");
            }
            else if (p.Input.BackDown)
            {
                if (p.Ready) p.Ready = false;
                else Players.Remove(p);
                Sfx.Play("tick");
            }
        }

        bool allReady = Players.Count >= 2 && Players.All(p => p.Ready) && (AllowBotsOnly || Players.Any(p => !p.IsBot));
        if (!allReady) lobbyCountdown = -1f;
        else if (lobbyCountdown < 0f) { lobbyCountdown = 3f; Sfx.Play("tick"); }
        else
        {
            int before = Mathf.CeilToInt(lobbyCountdown);
            lobbyCountdown -= Time.deltaTime;
            if (Mathf.CeilToInt(lobbyCountdown) != before && lobbyCountdown > 0f) Sfx.Play("tick");
            if (lobbyCountdown <= 0f) StartMatch();
        }

#if !UNITY_EDITOR && !UNITY_WEBGL
        if (devices.Count > 0 && devices[0].Kind == DeviceKind.Keyboard1 && !devices[0].Remote && devices[0].PauseDown &&
            NetGame.Instance.Mode == NetMode.Offline) Application.Quit();
#endif
    }

    /// <summary>Escolhe o mapa (com volta) e mostra ele atrás do lobby.</summary>
    public void SetMap(int index)
    {
        int n = Maps.All.Length + 1;
        MapIndex = ((index % n) + n) % n;
        if (MapIndex < Maps.All.Length) Level.Build(Maps.All[MapIndex]);
        Sfx.Play("tick");
    }

    MapDef SelectedMap => MapIndex < Maps.All.Length ? Maps.All[MapIndex] : Level.Map;

    void StartMatch()
    {
        lobbyCountdown = -1f;
        Round = 0;
        nobodyLastRound = false;
        foreach (var p in Players) { p.Score = 0; p.PrevScore = 0; p.Ready = false; }
        ClearRoundObjects();
        Level.Build(MapIndex < Maps.All.Length ? Maps.All[MapIndex] : Maps.All[Random.Range(0, Maps.All.Length)]);
        Sfx.Play("go");
        BeginPick();
    }

    public void BackToLobby()
    {
        paused = false;
        Time.timeScale = 1f;
        ClearRoundObjects();
        Level.Build(SelectedMap);
        foreach (var p in Players) { p.Ready = false; p.Score = 0; p.PrevScore = 0; }
        winner = -1;
        SetPhase(Phase.Lobby);
    }

    void ClearRoundObjects()
    {
        RunActive = false;
        if (boxRoot != null) Destroy(boxRoot);
        boxRoot = null;
        boxItems.Clear();
        foreach (var p in Players)
        {
            if (p.Cursor != null) Destroy(p.Cursor.gameObject);
            p.Cursor = null;
            if (p.Item != null && !p.Item.Placed) Destroy(p.Item.gameObject);
            p.Item = null;
            p.PortalFirst = null;
            p.Zombie = null;
        }
        Zombie.ClearAll();
        Projectile.ClearAll();
    }

    // ================================================================== escolha de itens

    void BeginPick()
    {
        Round++;
        SetPhase(Phase.Pick);
        foreach (var p in Players)
        {
            p.Picked = p.Placed = false;
            p.Item = null;
            p.PortalFirst = null;
            p.Kills = 0;
            p.RoundPoints.Clear();
        }

        pickTimer = PickTime;
        int count = Players.Count + 2;
        var types = new List<ItemType>();
        while (types.Count < count)
        {
            var t = ItemDefs.Random();
            if (types.Count(x => x == t) >= 2) continue;
            types.Add(t);
        }
        // sempre pelo menos um bloco para construir
        if (!types.Any(IsBuildingBlock))
            types[Random.Range(0, types.Count)] = Random.value < 0.5f ? ItemType.Prancha : ItemType.Caixa;
        // ninguém chegou na rodada passada: sempre tem uma bomba para destravar a fase
        if (nobodyLastRound && !types.Contains(ItemType.Bomba))
        {
            var spots = Enumerable.Range(0, count).Where(i => !IsBuildingBlock(types[i])).ToList();
            types[spots.Count > 0 ? spots[Random.Range(0, spots.Count)] : 0] = ItemType.Bomba;
        }
        if (ForcedBox != null && ForcedBox.Count > 0)
            for (int i = 0; i < count; i++) types[i] = ForcedBox[(i + (Round - 1) * count) % ForcedBox.Count];

        boxRoot = new GameObject("Caixa de itens");
        float width = BoxPerRow(count) * BoxSpacing + 1f, height = BoxHeight(count);
        Quad(boxRoot, Vector2.zero, new Vector2(80f, 50f), new Color(0f, 0f, 0f, 0.55f), 100);
        Quad(boxRoot, BoxCenter, new Vector2(width + 0.35f, height + 0.35f), new Color(0.72f, 0.5f, 0.95f), 101);
        Quad(boxRoot, BoxCenter, new Vector2(width, height), new Color(0.12f, 0.08f, 0.17f, 0.98f), 102);

        for (int i = 0; i < count; i++)
        {
            var item = ItemFactory.Create(types[i]);
            item.SetCollidersEnabled(false);
            item.SetSortingBoost(110);
            item.transform.SetParent(boxRoot.transform, true);
            item.ShowAt(BoxSlot(i, count));
            boxItems.Add(item);
        }

        for (int i = 0; i < Players.Count; i++)
        {
            var p = Players[i];
            p.Cursor = PlayerCursor.Create(p.Index, BoxCenter + new Vector2((i - (Players.Count - 1) / 2f) * 2f, -height / 2f - 1.1f));
        }
    }

    static bool IsBuildingBlock(ItemType t) =>
        t == ItemType.Caixa || t == ItemType.Prancha || t == ItemType.Pedra || t == ItemType.Gelo || t == ItemType.CaixaPodre || t == ItemType.Esteira ||
        t == ItemType.PlataformaMovel || t == ItemType.Gangorra;

    // Mais de 6 itens (5 jogadores ou mais): duas fileiras
    const float BoxRowGap = 4.6f;
    static int BoxRows(int count) => count > 6 ? 2 : 1;
    static int BoxPerRow(int count) => BoxRows(count) == 2 ? (count + 1) / 2 : count;
    static float BoxHeight(int count) => 5f + (BoxRows(count) - 1) * BoxRowGap;

    static Vector2 BoxSlot(int i, int count)
    {
        int per = BoxPerRow(count), row = i / per, inRow = row == 0 ? per : count - per;
        float y = BoxRows(count) == 2 ? (row == 0 ? BoxRowGap : -BoxRowGap) / 2f : 0f;
        return BoxCenter + new Vector2((i % per - (inRow - 1) / 2f) * BoxSpacing, 0.3f + y);
    }

    static void Quad(GameObject parent, Vector2 pos, Vector2 size, Color color, int order)
    {
        var go = new GameObject("Painel");
        go.transform.SetParent(parent.transform, false);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = Art.Pixel;
        sr.color = color;
        sr.sortingOrder = order;
    }

    Rect ViewRect(float margin)
    {
        float h = cam.orthographicSize, w = h * cam.aspect;
        return Rect.MinMaxRect(-w + margin, -h + margin, w - margin, h - margin);
    }

    public ItemInstance HoveredItem(Vector2 p)
    {
        ItemInstance best = null;
        float bestD = 1.4f;
        foreach (var it in boxItems)
        {
            if (it == null || it.Owner >= 0) continue;
            float d = Vector2.Distance(p, (Vector2)it.transform.position + it.RotatedCenter);
            if (d < bestD) { bestD = d; best = it; }
        }
        return best;
    }

    void UpdatePick()
    {
        var bounds = ViewRect(0.5f);
        var hovered = new HashSet<ItemInstance>();
        foreach (var p in Players)
        {
            if (p.Picked || p.Cursor == null) continue;
            p.Cursor.Move(p.Input.Move, bounds);
            var h = HoveredItem(p.Cursor.Position);
            if (h != null) hovered.Add(h);
            if (!p.Input.ConfirmDown) continue;
            if (h == null) { Sfx.Play("error", 0.6f); continue; }
            h.Owner = p.Index;
            p.Item = h;
            p.Picked = true;
            p.Cursor.SetVisible(false);
            h.SetTint(new Color(0.65f, 0.65f, 0.65f, 1f));
            Sfx.Play("pick");
            Fx.Burst((Vector2)h.transform.position + h.RotatedCenter, p.Color, 12, 5f, 0.14f, 4f);
        }

        // acabou o tempo (alguém distraído, principalmente no online): ganha um item qualquer que sobrou
        pickTimer -= Time.deltaTime;
        if (pickTimer <= 0f)
            foreach (var p in Players.Where(p => !p.Picked))
            {
                var free = boxItems.Where(it => it != null && it.Owner < 0).ToList();
                if (free.Count == 0) break;
                var it = free[Random.Range(0, free.Count)];
                it.Owner = p.Index;
                p.Item = it;
                p.Picked = true;
                if (p.Cursor != null) p.Cursor.SetVisible(false);
                it.SetTint(new Color(0.65f, 0.65f, 0.65f, 1f));
                Sfx.Play("pick");
            }

        float pulse = 1.1f + Mathf.Sin(Time.time * 10f) * 0.04f;
        foreach (var it in boxItems)
            if (it != null) it.transform.localScale = Vector3.one * (hovered.Contains(it) ? pulse : it.Owner >= 0 ? 0.85f : 1f);

        if (Players.All(p => p.Picked))
        {
            if (doneTimer < 0f) doneTimer = 0.8f;
            doneTimer -= Time.deltaTime;
            if (doneTimer <= 0f) BeginPlace();
        }
    }

    // ================================================================== posicionamento

    void BeginPlace()
    {
        SetPhase(Phase.Place);
        placeTimer = PlaceTime;

        foreach (var p in Players)
        {
            if (p.Item == null) continue;
            p.Item.transform.SetParent(null, true);
            p.Item.transform.localScale = Vector3.one;
            p.Item.Rotation = 0;
            p.Item.ApplyRotation();
            p.Item.SetSortingBoost(30);
            boxItems.Remove(p.Item);
        }
        foreach (var it in boxItems) if (it != null) Destroy(it.gameObject);
        boxItems.Clear();
        if (boxRoot != null) Destroy(boxRoot);
        boxRoot = null;

        for (int i = 0; i < Players.Count; i++)
        {
            var p = Players[i];
            p.Placed = p.Item == null;
            if (p.Cursor == null) continue;
            p.Cursor.SetVisible(!p.Placed);
            p.Cursor.Position = new Vector2((i - (Players.Count - 1) / 2f) * 4f, 2.5f);
            if (p.Item != null)
            {
                var radius = p.Item.transform.Find("Raio");
                if (radius != null) radius.gameObject.SetActive(true);
            }
        }
    }

    void UpdatePlace()
    {
        var bounds = Rect.MinMaxRect(Level.Origin.x + 0.3f, Level.Origin.y + 0.3f, -Level.Origin.x - 0.3f, -Level.Origin.y - 1.3f);
        foreach (var p in Players)
        {
            if (p.Placed || p.Item == null || p.Cursor == null) continue;
            p.Cursor.Move(p.Input.Move, bounds);

            if (p.Item.Def.CanRotate)
            {
                // Rotation conta no sentido anti-horário
                int turn = (p.Input.RotateCCWDown ? 1 : 0) - (p.Input.RotateCWDown ? 1 : 0);
                if (turn != 0)
                {
                    p.Item.Rotation = (p.Item.Rotation + turn + 4) % 4;
                    p.Item.ApplyRotation();
                    Sfx.Play("rotate");
                }
            }

            var pivot = p.Item.PivotFor(p.Cursor.Position);
            p.Item.SnapTo(pivot);
            bool valid = p.Item.CanPlaceAt(pivot);
            p.Item.SetTint(valid ? new Color(1f, 1f, 1f, 0.8f) : new Color(1f, 0.3f, 0.3f, 0.55f));

            if (!p.Input.ConfirmDown) continue;
            if (!valid) { Sfx.Play("error"); continue; }
            var radius = p.Item.transform.Find("Raio");
            if (radius != null) radius.gameObject.SetActive(false);
            bool stays = p.Item.Place();
            if (!stays) p.Item = null;
            else if (p.Item.Def.Type == ItemType.Portal)
            {
                if (p.PortalFirst == null)
                {
                    // primeiro portal colocado: agora vem o segundo (roxo)
                    p.PortalFirst = p.Item;
                    var second = ItemFactory.Create(ItemType.Portal, 1);
                    second.Owner = p.Index;
                    second.SetCollidersEnabled(false);
                    second.SetSortingBoost(30);
                    second.Rotation = p.PortalFirst.Rotation;
                    second.ApplyRotation();
                    p.Item = second;
                    continue;
                }
                var a = p.PortalFirst.GetComponentInChildren<Portal>();
                var b = p.Item.GetComponentInChildren<Portal>();
                a.Partner = b;
                b.Partner = a;
                p.PortalFirst = null;
            }
            p.Placed = true;
            p.Cursor.SetVisible(false);
        }

        placeTimer -= Time.deltaTime;
        if (placeTimer <= 0f)
        {
            foreach (var p in Players.Where(p => !p.Placed))
            {
                if (p.Item != null) Destroy(p.Item.gameObject);
                if (p.PortalFirst != null) Destroy(p.PortalFirst.gameObject); // portal sem par não fica
                p.Item = null;
                p.PortalFirst = null;
                p.Placed = true;
                if (p.Cursor != null) p.Cursor.SetVisible(false);
            }
        }

        if (Players.All(p => p.Placed))
        {
            if (doneTimer < 0f) doneTimer = 0.7f;
            doneTimer -= Time.deltaTime;
            if (doneTimer <= 0f) BeginRun();
        }
    }

    // ================================================================== corrida

    void BeginRun()
    {
        SetPhase(Phase.Run);
        foreach (var p in Players)
        {
            if (p.Cursor != null) Destroy(p.Cursor.gameObject);
            p.Cursor = null;
        }
        Zombie.ClearAll();
        Projectile.ClearAll();
        finishOrder.Clear();
        timeUp = botsOnly = false;
        runTimer = RunTime;
        foreach (var p in Players) p.Zombie = Zombie.Spawn(p.Index, p.Input, Level.SpawnPoint(p.Index));
        RunActive = true;
        RunBehaviour.RunStarted();
        Sfx.Play("go");
        Sfx.PlayVar("groan");
    }

    void UpdateRun()
    {
        // só sobraram bots correndo: a rodada não fica esperando por eles
        bool humansOut = Players.Any(p => !p.IsBot) && Players.All(p => p.IsBot || p.Zombie == null || p.Zombie.State != ZombieState.Alive);
        botsOnly = humansOut && Players.Any(p => p.IsBot && p.Zombie != null && p.Zombie.State == ZombieState.Alive);
        if (botsOnly && runTimer > BotsOnlyTime) runTimer = BotsOnlyTime;

        runTimer -= Time.deltaTime;
        if (runTimer <= 0f && !timeUp)
        {
            timeUp = true;
            foreach (var p in Players)
                if (p.Zombie != null && p.Zombie.State == ZombieState.Alive) p.Zombie.Die(-1);
        }

        if (Players.All(p => p.Zombie == null || p.Zombie.State != ZombieState.Alive))
        {
            RunActive = false;
            RunBehaviour.RunEnded();
            int n = Players.Count;
            if (timeUp) { roundMessage = "TEMPO ESGOTADO!"; roundMessageColor = new Color(1f, 0.6f, 0.3f); }
            else if (finishOrder.Count == n) { roundMessage = "FÁCIL DEMAIS!"; roundMessageColor = new Color(1f, 0.85f, 0.3f); }
            else if (finishOrder.Count == 0) { roundMessage = "NINGUÉM CHEGOU!"; roundMessageColor = new Color(1f, 0.4f, 0.4f); }
            else { roundMessage = "FIM DA RODADA"; roundMessageColor = new Color(0.6f, 1f, 0.5f); }
            SetPhase(Phase.RoundEnd);
        }
    }

    public void OnZombieDied(Zombie z, int killer)
    {
        Vector2 pos = z.Position + Vector2.up * 1.2f;
        AddFloat(pos, "ARGH!", Art.PlayerColor(z.Index), 30, 1.2f);
        var slot = Players.FirstOrDefault(p => p.Index == killer);
        if (slot != null && killer != z.Index)
        {
            slot.Kills++;
            AddFloat(pos + Vector2.up * 0.7f, "armadilha de " + slot.Name, slot.Color, 20, 1.4f);
        }
    }

    /// <summary>Bot empacado (fase impossível para ele): desiste e o zumbi explode, para a rodada poder acabar.</summary>
    public void GiveUp(PlayerSlot p)
    {
        if (Phase != Phase.Run || p.Zombie == null || p.Zombie.State != ZombieState.Alive) return;
        AddFloat(p.Zombie.Position + Vector2.up * 2.2f, "DESISTIU!", p.Color, 26, 1.6f);
        p.Zombie.Die(-1);
    }

    public void OnZombieFinished(Zombie z)
    {
        finishOrder.Add(z.Index);
        AddFloat(Level.GoalPosition + Vector2.up * 1.2f, "NHAC!", Art.PlayerColor(z.Index), 36, 1.3f);
    }

    void UpdateRoundEnd()
    {
        if (PhaseTime < 1.4f) return;
        Projectile.ClearAll();
        ComputeScores();
        SetPhase(Phase.Score);
    }

    void ComputeScores()
    {
        int n = Players.Count;
        bool tooEasy = finishOrder.Count == n;
        bool nobody = finishOrder.Count == 0;
        nobodyLastRound = nobody;
        foreach (var p in Players)
        {
            p.RoundPoints.Clear();
            p.PrevScore = p.Score;
            if (tooEasy) continue;
            if (finishOrder.Contains(p.Index)) p.RoundPoints.Add(new KeyValuePair<string, int>("Chegou", PointsGoal));
            if (finishOrder.Count > 0 && finishOrder[0] == p.Index) p.RoundPoints.Add(new KeyValuePair<string, int>("Primeiro", PointsFirst));
            if (!nobody && p.Kills > 0)
                p.RoundPoints.Add(new KeyValuePair<string, int>(p.Kills > 1 ? $"Armadilha x{p.Kills}" : "Armadilha", PointsTrap * p.Kills));
            p.Score += p.RoundPoints.Sum(kv => kv.Value);
        }
        if (tooEasy) roundMessage = "FÁCIL DEMAIS! Todo mundo chegou, ninguém pontua.";
        else if (nobody) roundMessage = "NINGUÉM CHEGOU! Ninguém pontua.";
        else roundMessage = "";
    }

    // ================================================================== placar e vitória

    void UpdateScore()
    {
        if (PhaseTime < 1.6f) return;
        bool next = PhaseTime > 6f || Players.Any(p => p.Input.ConfirmDown);
        if (!next) return;

        int best = Players.Max(p => p.Score);
        var leaders = Players.Where(p => p.Score == best).ToList();
        if (best >= WinScore && leaders.Count == 1)
        {
            winner = leaders[0].Index;
            SetPhase(Phase.Victory);
            Sfx.Play("win");
            Sfx.PlayVar("groan");
        }
        else BeginPick();
    }

    void UpdateVictory()
    {
        confettiTimer -= Time.deltaTime;
        if (confettiTimer <= 0f)
        {
            confettiTimer = 0.12f;
            var r = ViewRect(1f);
            var col = Random.value < 0.5f ? Art.PlayerColor(winner) : Art.PlayerColor(Random.Range(0, Art.PlayerColors.Length));
            Fx.Burst(new Vector2(Random.Range(r.xMin, r.xMax), r.yMax), col, 6, 3f, 0.18f, 3f, 2.5f);
        }
        if (PhaseTime > 2f && Players.Any(p => p.Input.ConfirmDown)) BackToLobby();
    }

    // ================================================================== textos flutuantes

    void AddFloat(Vector2 pos, string text, Color color, int size, float duration)
    {
        floats.Add(new FloatText { Pos = pos, Text = text, Color = color, Size = size, Start = Time.time, Duration = duration });
        FloatAdded?.Invoke(pos, text, color, size, duration);
    }

    // ================================================================== interface (IMGUI)

    GUIStyle style;
    float S => Screen.height / 1080f;
    float VW => Screen.width / S;

    Rect R(float x, float y, float w, float h) => new Rect(x * S, y * S, w * S, h * S);

    void Text(Rect r, string s, int size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter, bool shadow = true)
    {
        style.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * S));
        style.alignment = anchor;
        if (shadow)
        {
            float o = Mathf.Max(1f, 3f * S);
            style.normal.textColor = new Color(0f, 0f, 0f, c.a * 0.85f);
            GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), s, style);
        }
        style.normal.textColor = c;
        GUI.Label(r, s, style);
    }

    static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void DrawSprite(Rect r, Sprite s, Color tint, bool flip = false)
    {
        if (s == null) return;
        var tex = s.texture;
        var uv = new Rect(s.rect.x / tex.width, s.rect.y / tex.height, s.rect.width / tex.width, s.rect.height / tex.height);
        if (flip) { uv.x += uv.width; uv.width = -uv.width; }
        var old = GUI.color;
        GUI.color = tint;
        GUI.DrawTextureWithTexCoords(r, tex, uv);
        GUI.color = old;
    }

    Vector2 ToGui(Vector2 world)
    {
        var sp = cam.WorldToScreenPoint(world);
        return new Vector2(sp.x, Screen.height - sp.y);
    }

    /// <summary>Texto centrado num ponto do mundo.</summary>
    void WorldText(Vector2 world, string s, int size, Color c)
    {
        var g = ToGui(world);
        float w = 600f * S, h = (size + 20) * S;
        Text(new Rect(g.x - w / 2f, g.y - h / 2f, w, h), s, size, c);
    }

    void OnGUI()
    {
        if (cam == null) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = false, clipping = TextClipping.Overflow };
            style.richText = false;
        }

        if (!Replica) RefreshViews();
        switch (Phase)
        {
            case Phase.Lobby: DrawLobby(); break;
            case Phase.Pick: DrawTopBar("ESCOLHA UM ITEM", pickTimer); DrawPick(); break;
            case Phase.Place: DrawTopBar("POSICIONE SEU ITEM", placeTimer); DrawPlace(); break;
            case Phase.Run: DrawTopBar("CORRA ATÉ O CÉREBRO!", runTimer); DrawRun(); break;
            case Phase.RoundEnd: DrawTopBar("", -1f); DrawRun(); DrawRoundEnd(); break;
            case Phase.Score: DrawScore(); break;
            case Phase.Victory: DrawVictory(); break;
        }

        foreach (var f in floats)
        {
            float t = (Time.time - f.Start) / f.Duration;
            var c = f.Color;
            c.a = Mathf.Clamp01((1f - t) * 2f);
            WorldText(f.Pos + Vector2.up * t * 1.2f, f.Text, f.Size, c);
        }

        if (paused) DrawPause();
        DrawOnline();
    }

    // ------------------------------------------------------------------ teclas e botões desenhados

    // Nos textos da interface: [ESPAÇO] é uma tecla, [<] [>] [^] [v] são as setas do teclado,
    // {A} {B} {X} {Y} são os botões coloridos do controle e {LB} {START} ... os outros botões dele.

    static string ConfirmKey(InputSource i) => i.Kind == DeviceKind.Keyboard1 ? "[ESPAÇO]" : i.Kind == DeviceKind.Keyboard2 ? "[ENTER]" : "{A}";
    static string BackKey(InputSource i) => i.Kind == DeviceKind.Keyboard1 ? "[Q]" : i.Kind == DeviceKind.Keyboard2 ? "[BACKSPACE]" : "{B}";
    static string RotateKey(InputSource i) => i.Kind == DeviceKind.Keyboard1 ? "[Q] [E]" : i.Kind == DeviceKind.Keyboard2 ? "[CTRL] [SHIFT]" : "{LB} {RB}";
    const string AnyConfirm = "[ESPAÇO] [ENTER] {A}";

    static readonly string[] DeviceTitles = { "TECLADO  ·  WASD", "TECLADO  ·  SETAS", "CONTROLE" };
    static readonly string[] ControlActions = { "Mover", "Pular", "Confirmar", "Girar item", "Voltar", "Pausar" };
    static readonly string[,] ControlKeys =
    {
        { "[W] [A] [S] [D]", "[<] [^] [v] [>]", "{ANALÓGICO} ou {D-PAD}" },
        { "[W] ou [ESPAÇO]", "[^]", "{A}" },
        { "[ESPAÇO]", "[ENTER]", "{A}" },
        { "[Q] [E]", "[CTRL] [SHIFT] direitos", "{LB} {RB} ou {X} {Y}" },
        { "[Q]", "[BACKSPACE]", "{B}" },
        { "[ESC]", "[ESC]", "{START}" },
    };

    static readonly Color CapEdge = new Color(0.04f, 0.03f, 0.07f), CapFace = new Color(0.93f, 0.91f, 0.97f),
                          CapSide = new Color(0.58f, 0.55f, 0.66f), CapInk = new Color(0.13f, 0.1f, 0.2f);

    struct KeyPiece { public string Text; public int Kind; public float W, PadL; } // 0 texto, 1 tecla, 2 seta, 3 botão redondo, 4 outro botão do controle
    readonly List<KeyPiece> keyPieces = new List<KeyPiece>();

    static int ArrowDir(string label) => label == "^" ? 0 : label == "<" ? 1 : label == "v" ? 2 : label == ">" ? 3 : -1;

    float TextWidth(string s, int size)
    {
        style.fontSize = Mathf.Max(8, Mathf.RoundToInt(size * S));
        return style.CalcSize(new GUIContent(s)).x;
    }

    /// <summary>Quebra a linha em texto e botões e mede cada pedaço. Devolve a largura total em pixels.</summary>
    float LayoutKeys(string s, int size)
    {
        keyPieces.Clear();
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char open = s[i];
            if (open != '[' && open != '{') continue;
            int end = s.IndexOf(open == '[' ? ']' : '}', i + 1);
            if (end < 0) break;
            AddKeyPiece(s.Substring(start, i - start), 0, size);
            string label = s.Substring(i + 1, end - i - 1);
            AddKeyPiece(label, open == '[' ? (ArrowDir(label) >= 0 ? 2 : 1) : (label.Length == 1 ? 3 : 4), size);
            start = end + 1;
            i = end;
        }
        AddKeyPiece(s.Substring(start), 0, size);

        float gap = size * S * 0.3f, total = 0f;
        for (int k = 0; k < keyPieces.Count; k++) total += keyPieces[k].W + (k > 0 ? gap : 0f);
        return total;
    }

    void AddKeyPiece(string text, int kind, int size)
    {
        float fs = size * S, capH = fs * 1.5f, padL = 0f, padR = 0f;
        if (kind == 0)
        {
            // espaços nas pontas viram margem (o rótulo mede só o texto)
            string t = text.Trim();
            if (t.Length == 0) return;
            padL = (text.Length - text.TrimStart().Length) * fs * 0.22f;
            padR = (text.Length - text.TrimEnd().Length) * fs * 0.22f;
            text = t;
        }
        float w = kind == 0 ? TextWidth(text, size)
                : kind == 2 || kind == 3 ? capH
                : Mathf.Max(capH, TextWidth(text, CapTextSize(size)) + fs * 0.8f);
        keyPieces.Add(new KeyPiece { Text = text, Kind = kind, W = padL + w + padR, PadL = padL });
    }

    static int CapTextSize(int size) => Mathf.RoundToInt(size * 0.78f);

    /// <summary>Linha de texto com as teclas e botões desenhados (veja os marcadores acima).</summary>
    void Keys(Rect r, string s, int size, Color c, TextAnchor anchor = TextAnchor.MiddleCenter)
    {
        float total = LayoutKeys(s, size);
        float x = anchor == TextAnchor.MiddleLeft ? r.x : anchor == TextAnchor.MiddleRight ? r.xMax - total : r.x + (r.width - total) / 2f;
        float fs = size * S, capH = fs * 1.5f, cy = r.y + r.height / 2f, gap = fs * 0.3f;
        for (int k = 0; k < keyPieces.Count; k++)
        {
            var p = keyPieces[k];
            var box = new Rect(x, cy - capH / 2f, p.W, capH);
            switch (p.Kind)
            {
                case 0: Text(new Rect(x + p.PadL, r.y, p.W - p.PadL, r.height), p.Text, size, c, TextAnchor.MiddleLeft); break;
                case 1: KeyCap(box, p.Text, size); break;
                case 2:
                    KeyCap(box, "", size);
                    float a = capH * 0.48f, depth = Mathf.Max(2f, 4f * S);
                    DrawSprite(new Rect(box.center.x - a / 2f, box.center.y - a / 2f - depth / 2f, a, a), Art.Arrow(ArrowDir(p.Text)), CapInk);
                    break;
                case 3: PadButton(box, p.Text, size); break;
                default: PadPill(box, p.Text, size); break;
            }
            x += p.W + gap;
        }
    }

    void KeyCap(Rect b, string label, int size)
    {
        float e = Mathf.Max(1f, 2f * S), depth = Mathf.Max(2f, 4f * S);
        Fill(b, CapEdge);
        Fill(new Rect(b.x + e, b.y + e, b.width - 2 * e, b.height - 2 * e), CapSide);
        Fill(new Rect(b.x + e, b.y + e, b.width - 2 * e, b.height - 2 * e - depth), CapFace);
        if (label.Length > 0) Text(new Rect(b.x, b.y + e - depth / 2f, b.width, b.height - 2 * e), label, CapTextSize(size), CapInk, TextAnchor.MiddleCenter, false);
    }

    void PadButton(Rect b, string label, int size)
    {
        Color col = label == "A" ? new Color(0.33f, 0.74f, 0.3f) : label == "B" ? new Color(0.88f, 0.3f, 0.28f)
                  : label == "X" ? new Color(0.28f, 0.52f, 0.95f) : new Color(0.93f, 0.74f, 0.2f);
        float e = Mathf.Max(1.5f, 2.5f * S);
        DrawSprite(b, Art.Dot, CapEdge);
        DrawSprite(new Rect(b.x + e, b.y + e, b.width - 2 * e, b.height - 2 * e), Art.Dot, col);
        Text(b, label, CapTextSize(size), Color.white, TextAnchor.MiddleCenter, false);
    }

    void PadPill(Rect b, string label, int size)
    {
        float e = Mathf.Max(1f, 2f * S);
        Fill(b, CapEdge);
        Fill(new Rect(b.x + e, b.y + e, b.width - 2 * e, b.height - 2 * e), new Color(0.3f, 0.29f, 0.37f));
        Text(b, label, CapTextSize(size), Color.white, TextAnchor.MiddleCenter, false);
    }

    /// <summary>Tabela com os controles de cada tipo de dispositivo. Devolve a altura usada.</summary>
    float DrawControls(float y)
    {
        float tw = Mathf.Min(VW - 60f, 1560f), x0 = (VW - tw) / 2f;
        float labelW = 200f, colW = (tw - labelW) / 3f, headH = 42f, rowH = 37f;
        int rows = ControlActions.Length;
        float h = headH + rows * rowH + 8f;
        Fill(R(x0, y, tw, h), new Color(0.08f, 0.06f, 0.13f, 0.9f));
        Fill(R(x0, y, tw, 4), new Color(0.72f, 0.5f, 0.95f));
        Text(R(x0 + 18, y + 2, labelW, headH), "CONTROLES", 22, new Color(0.85f, 0.7f, 1f), TextAnchor.MiddleLeft);

        for (int r = 0; r < rows; r++)
        {
            float ry = y + headH + r * rowH;
            if (r % 2 == 0) Fill(R(x0, ry, tw, rowH), new Color(1f, 1f, 1f, 0.035f));
            Text(R(x0 + 18, ry, labelW - 18, rowH), ControlActions[r], 20, new Color(0.82f, 0.8f, 0.9f), TextAnchor.MiddleLeft);
        }

        for (int d = 0; d < 3; d++)
        {
            float cx = x0 + labelW + d * colW;
            Fill(R(cx, y + 10, 2, h - 18), new Color(1f, 1f, 1f, 0.08f));

            // quem está jogando com este tipo de dispositivo: destaca a coluna para cada um achar os seus botões
            var users = Players.Where(p => !p.IsBot && (int)p.Input.Kind == d).ToList();
            if (users.Count > 0) Fill(R(cx + 2, y + 4, colW - 2, h - 4), new Color(users[0].Color.r, users[0].Color.g, users[0].Color.b, 0.09f));

            float chipW = 46f, chipGap = 6f;
            float titleW = TextWidth(DeviceTitles[d], 20) / S;
            float groupW = titleW + users.Count * (chipW + chipGap) + (users.Count > 0 ? 8f : 0f);
            float tx = cx + (colW - groupW) / 2f;
            Text(R(tx, y + 2, titleW, headH), DeviceTitles[d], 20, users.Count > 0 ? Color.white : new Color(0.75f, 0.73f, 0.82f), TextAnchor.MiddleLeft);
            float chipX = tx + titleW + 8f;
            foreach (var u in users)
            {
                Fill(R(chipX, y + 10, chipW, 26), u.Color);
                Text(R(chipX, y + 10, chipW, 26), u.Name, 17, Color.white, TextAnchor.MiddleCenter, false);
                chipX += chipW + chipGap;
            }

            for (int r = 0; r < rows; r++)
                Keys(R(cx + 8, y + headH + r * rowH, colW - 16, rowH), ControlKeys[r, d], 18, new Color(0.88f, 0.86f, 0.95f));
        }
        return h;
    }

    // ------------------------------------------------------------------ lobby

    void DrawLobby()
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.02f, 0.06f, 0.55f));

        float wob = Mathf.Sin(Time.time * 2f) * 4f;
        Text(R(0, 12 + wob, VW, 100), "ULTIMATE ZUMBI HORSE", 82, new Color(0.55f, 0.92f, 0.4f));
        if (!DrawOnlineBanner(R(0, 102, VW, 40)))
            Text(R(0, 104, VW, 36), "Monte a fase, arme as armadilhas e seja o único zumbi a comer o cérebro!", 24, new Color(0.9f, 0.85f, 1f));

        DrawMapSelector(148f);

        // 8 lugares: 2 fileiras de 4 cartões (zumbi à esquerda, textos à direita)
        float gap = 24f, cw = Mathf.Min(360f, (VW - 80f - gap * 3) / 4f), ch = 166f;
        float x0 = (VW - (cw * 4 + gap * 3)) / 2f;
        var hint = new Color(0.7f, 0.85f, 1f);
        var dim = new Color(0.65f, 0.65f, 0.7f);
        for (int i = 0; i < MaxPlayers; i++)
        {
            float x = x0 + (i % 4) * (cw + gap), y0 = 286f + (i / 4) * (ch + 12f);
            float tx = x + 118f, tw = cw - 124f;
            var slot = Players.FirstOrDefault(p => p.Index == i);
            Fill(R(x, y0, cw, ch), new Color(0.08f, 0.06f, 0.13f, 0.88f));
            Color pc = Art.PlayerColor(i);
            var sprite = R(x + 10, y0 + 22, 98, 109);
            if (slot == null)
            {
                Fill(R(x, y0, cw, 6), new Color(0.3f, 0.3f, 0.35f));
                Text(R(tx, y0 + 10, tw, 34), $"JOGADOR {i + 1}", 24, new Color(0.5f, 0.5f, 0.55f), TextAnchor.MiddleLeft);
                DrawSprite(sprite, Art.ZombieFrames(i)[0], new Color(0f, 0f, 0f, 0.5f));
                Text(R(tx, y0 + 46, tw, 26), "Para entrar:", 16, new Color(0.8f, 0.8f, 0.85f), TextAnchor.MiddleLeft);
                Keys(R(tx, y0 + 74, tw, 28), InputSource.GamepadsSupported ? "[ESPAÇO] [ENTER] {A}" : "[ESPAÇO] [ENTER]", 16, new Color(1f, 0.9f, 0.5f), TextAnchor.MiddleLeft);
                Text(R(tx, y0 + 104, tw, 22), InputSource.GamepadsSupported ? "WASD, setas ou controle" : "WASD ou setas", 13, dim, TextAnchor.MiddleLeft);
                Text(R(tx, y0 + 130, tw, 24), "ou CIMA: colocar bot", 14, hint, TextAnchor.MiddleLeft);
                continue;
            }

            Fill(R(x, y0, cw, 6), pc);
            Text(R(tx, y0 + 10, tw, 34), $"JOGADOR {i + 1}", 24, pc, TextAnchor.MiddleLeft);
            var frames = Art.ZombieFrames(i);

            if (slot.IsBot)
            {
                DrawSprite(sprite, frames[(int)(Time.time * 2f + i) % 2], Color.white);
                Text(R(tx, y0 + 46, tw, 26), "CPU", 18, new Color(0.85f, 0.85f, 0.9f), TextAnchor.MiddleLeft);
                Text(R(tx, y0 + 72, tw, 38), slot.Bot.Label.ToUpper(), 30, slot.Bot.LabelColor, TextAnchor.MiddleLeft);
                if (slot == LastBot)
                {
                    Keys(R(tx, y0 + 112, tw, 24), "[E] [SHIFT] {RB} dificuldade", 13, hint, TextAnchor.MiddleLeft);
                    Keys(R(tx, y0 + 138, tw, 24), "[S] [v] ou BAIXO: tirar", 13, dim, TextAnchor.MiddleLeft);
                }
                continue;
            }

            int f = slot.Ready ? 2 + (int)(Time.time * 8f) % 4 : (int)(Time.time * 2f) % 2;
            float hop = slot.Ready ? Mathf.Abs(Mathf.Sin(Time.time * 8f)) * 8f : 0f;
            DrawSprite(new Rect(sprite.x, sprite.y - hop * S, sprite.width, sprite.height), frames[f], Color.white, slot.Ready && (int)(Time.time * 2f) % 2 == 0);
            Text(R(tx, y0 + 46, tw, 26), DeviceLabel(slot), 17, new Color(0.85f, 0.85f, 0.9f), TextAnchor.MiddleLeft);
            if (slot.Ready)
            {
                Text(R(tx, y0 + 74, tw, 40), "PRONTO!", 32, new Color(0.5f, 1f, 0.45f), TextAnchor.MiddleLeft);
                Keys(R(tx, y0 + 126, tw, 26), $"{BackKey(slot.Input)} cancelar", 14, dim, TextAnchor.MiddleLeft);
            }
            else
            {
                float blink = 0.6f + 0.4f * Mathf.Sin(Time.time * 6f);
                Keys(R(tx, y0 + 78, tw, 34), $"{ConfirmKey(slot.Input)} ficar pronto", 18, new Color(1f, 0.9f, 0.5f, blink), TextAnchor.MiddleLeft);
                Keys(R(tx, y0 + 126, tw, 26), $"{BackKey(slot.Input)} sair", 14, dim, TextAnchor.MiddleLeft);
            }
        }

        string status;
        Color sc = new Color(1f, 1f, 1f);
        if (Players.Count < 2) status = "Precisa de pelo menos 2 jogadores (pode ser bot: aperte CIMA)";
        else if (!Players.Any(p => !p.IsBot) && !BotsOnlyAllowed) status = "Pelo menos um humano precisa entrar";
        else if (lobbyCountdown > 0f) { status = $"Começando em {Mathf.CeilToInt(lobbyCountdown)}..."; sc = new Color(0.5f, 1f, 0.45f); }
        else status = "Esperando todo mundo ficar pronto...";
        Text(R(0, 636, VW, 48), status, 34, sc);

        float th = DrawControls(688f);
        float y = 688f + th + 4f;
        float bw = Mathf.Min(VW - 60f, 1560f);
        Fill(R((VW - bw) / 2f, y - 2f, bw, 90f), new Color(0.05f, 0.03f, 0.09f, 0.8f));
        Keys(R(0, y, VW, 32), "BOTS:  [W] [^] ou CIMA adiciona  •  [S] [v] ou BAIXO tira  •  [E] [SHIFT] {RB} muda a dificuldade do último", 17, hint);
        Text(R(0, y + 32, VW, 28), $"Primeiro a fazer {WinScore} pontos vence.  Chegar no cérebro: +{PointsGoal}  •  Primeiro: +{PointsFirst}  •  Matar com sua armadilha: +{PointsTrap}  •  Se todos chegarem, ninguém pontua!", 18, new Color(0.65f, 0.95f, 0.6f));
        string pads = InputSource.GamepadsSupported ? $"Controles conectados: {InputSource.GamepadCount}" : "Input System não ativo: só teclado";
        Keys(R(0, y + 60, VW, 26), pads + "   •   [ESC] {START} pausar (na partida)   •   [O] jogar online", 15, new Color(0.6f, 0.6f, 0.66f));
    }

    void DrawMapSelector(float y)
    {
        float w = 980f, h = 125f, x = (VW - w) / 2f;
        Fill(R(x, y, w, h), new Color(0.08f, 0.06f, 0.13f, 0.88f));
        Fill(R(x, y, w, 4), new Color(0.72f, 0.5f, 0.95f));

        bool random = MapIndex >= Maps.All.Length;
        float tx = x + 70f, ty = y + 12f, cell = 198f / Level.W; // miniatura sempre do mesmo tamanho
        if (random)
        {
            Fill(R(tx, ty, Level.W * cell, Level.H * cell), new Color(0.12f, 0.09f, 0.2f));
            Text(R(tx, ty, Level.W * cell, Level.H * cell), "?", 70, new Color(0.85f, 0.7f, 1f));
        }
        else DrawMapThumbnail(Maps.All[MapIndex], tx, ty, cell);

        string name = random ? "Aleatório" : Maps.All[MapIndex].Name;
        string desc = random ? "Sorteia um dos mapas quando a partida começar." : Maps.All[MapIndex].Description;
        float textX = tx + Level.W * cell + 30f;
        Text(R(textX, y + 10, w - (textX - x) - 60, 50), $"MAPA {MapIndex + 1}/{Maps.All.Length + 1}:  {name}", 34, Color.white, TextAnchor.MiddleLeft);
        Text(R(textX, y + 58, w - (textX - x) - 60, 32), desc, 21, new Color(0.85f, 0.82f, 0.92f), TextAnchor.MiddleLeft);
        Keys(R(textX, y + 88, w - (textX - x) - 60, 30), "[A] [D]  [<] [>]  ou analógico: trocar mapa", 16, new Color(1f, 0.9f, 0.5f), TextAnchor.MiddleLeft);

        float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 4f);
        Text(R(x + 8, y, 50, h), "<", 60, new Color(1f, 0.9f, 0.5f, pulse));
        Text(R(x + w - 58, y, 50, h), ">", 60, new Color(1f, 0.9f, 0.5f, pulse));
    }

    /// <summary>Miniatura do mapa desenhada célula por célula.</summary>
    void DrawMapThumbnail(MapDef map, float x, float y, float cell)
    {
        Fill(R(x, y, Level.W * cell, Level.H * cell), new Color(0.1f, 0.08f, 0.17f));
        for (int cy = 0; cy < Level.H; cy++)
            for (int cx = 0; cx < Level.W; cx++)
            {
                char ch = map.At(cx, cy);
                Color c;
                switch (ch)
                {
                    case '#': c = new Color(0.45f, 0.34f, 0.27f); break;
                    case 'S': c = new Color(0.52f, 0.5f, 0.6f); break;
                    case 'i': c = new Color(0.65f, 0.86f, 0.97f); break;
                    case '=': c = new Color(0.62f, 0.46f, 0.3f); break;
                    case 'a': c = new Color(0.4f, 0.85f, 0.3f); break;
                    case '^': c = new Color(0.85f, 0.85f, 0.92f); break;
                    case 'X': c = new Color(0.95f, 0.3f, 0.3f); break;
                    case 'p': c = new Color(0.7f, 0.7f, 0.8f); break;
                    case 'G': c = new Color(1f, 0.55f, 0.72f); break;
                    case '1': case '2': case '3': case '4': case '5': case '6': case '7': case '8': c = Art.PlayerColor(ch - '1'); break;
                    default: continue;
                }
                Fill(R(x + cx * cell, y + (Level.H - 1 - cy) * cell, cell, cell), c);
            }
        foreach (var sp in map.Saws)
        {
            Fill(R(x + sp.From.x * cell, y + (Level.H - 1 - sp.From.y) * cell, cell, cell), new Color(0.95f, 0.3f, 0.3f));
            Fill(R(x + sp.To.x * cell, y + (Level.H - 1 - sp.To.y) * cell, cell, cell), new Color(0.95f, 0.3f, 0.3f, 0.5f));
        }
        foreach (var t in map.Trains)
        {
            float len = 5f + 2f * t.Wagons;
            float tx = Mathf.Lerp(t.FromX, t.ToX + 1 - len, t.StartT);
            Fill(R(x + tx * cell, y + (Level.H - 1 - (t.TrackRow + 1)) * cell, len * cell, cell), new Color(0.55f, 0.35f, 0.85f));
        }
    }

    // ------------------------------------------------------------------ partida

    void DrawTopBar(string title, float timer)
    {
        Fill(R(0, 0, VW, 64), new Color(0.05f, 0.03f, 0.09f, 0.75f));
        Text(R(24, 0, 500, 64), $"RODADA {Round}  ·  {Level.Map.Name}", 24, new Color(0.8f, 0.75f, 0.95f), TextAnchor.MiddleLeft);
        Text(R(0, 0, VW, 64), title, 36, Color.white);
        if (timer >= 0f)
        {
            Color tc = timer < 5f ? new Color(1f, 0.4f, 0.35f) : Color.white;
            Text(R(VW - 324, 0, 300, 64), Mathf.CeilToInt(timer).ToString(), 36, tc, TextAnchor.MiddleRight);
        }

        // placar resumido
        // com muitos jogadores as etiquetas encolhem para caber numa linha
        float chipW = Mathf.Min(290f, (VW - 48f - (Players.Count - 1) * 10f) / Mathf.Max(1, Players.Count)), x0 = 24f, y = 72f;
        int nameSize = chipW >= 260f ? 22 : chipW >= 200f ? 19 : 16, stSize = chipW >= 260f ? 18 : 14;
        for (int i = 0; i < Players.Count; i++)
        {
            var p = Players[i];
            float x = x0 + i * (chipW + 10f);
            Fill(R(x, y, chipW, 40), new Color(0.05f, 0.03f, 0.09f, 0.7f));
            Fill(R(x, y, 8, 40), p.Color);
            string st = "";
            if (Phase == Phase.Pick) st = p.Picked ? "pronto" : "";
            else if (Phase == Phase.Place) st = p.Placed ? "pronto" : "";
            else if (p.ZombieView.HasValue) st = p.ZombieView == ZombieState.Finished ? "chegou" : p.ZombieView == ZombieState.Dead ? "morreu" : "";
            string cpu = p.IsBot && chipW >= 200f ? " CPU" : "";
            Text(R(x + 16, y, chipW - 20, 40), $"{p.Name}{cpu}  {p.Score} pts", nameSize, p.Color, TextAnchor.MiddleLeft);
            Text(R(x, y, chipW - 12, 40), st, stSize, st == "morreu" ? new Color(1f, 0.5f, 0.45f) : new Color(0.6f, 1f, 0.5f), TextAnchor.MiddleRight);
        }

        if (Phase == Phase.Run && botsOnly)
            Text(R(VW - 524, y, 500, 40), "SÓ FALTAM OS BOTS!", 24, new Color(1f, 0.65f, 0.3f), TextAnchor.MiddleRight);
    }

    void DrawPick()
    {
        int count = boxView.Count;
        WorldText(BoxCenter + new Vector2(0f, BoxHeight(count) / 2f + 0.75f), "A CAIXA DE ITENS", 34, new Color(0.85f, 0.7f, 1f));
        for (int i = 0; i < count; i++)
        {
            var it = boxView[i];
            Vector2 slot = BoxSlot(i, count);
            if (it.Owner >= 0)
            {
                var owner = Players.FirstOrDefault(p => p.Index == it.Owner);
                WorldText(slot + new Vector2(0f, -1.55f), owner != null ? owner.Name : "", 30, Art.PlayerColor(it.Owner));
            }
            else
            {
                var def = ItemDefs.Get(it.Type);
                WorldText(slot + new Vector2(0f, -1.5f), def.Name, 22, Color.white);
                WorldText(slot + new Vector2(0f, -1.95f), def.Hint, 16, new Color(0.7f, 0.7f, 0.78f));
            }
        }
        if (nobodyLastRound && boxView.Any(it => it.Type == ItemType.Bomba))
            Text(R(0, 122, VW, 40), "Ninguém chegou na rodada passada: tem uma BOMBA na caixa para abrir caminho!", 24, new Color(1f, 0.7f, 0.35f));
        DrawCursorTags(false);
        DrawBottomHelp($"Mova o cursor até um item e aperte  {AnyConfirm}  para pegá-lo");
    }

    void DrawPlace()
    {
        DrawZone(Level.StartZone, "LARGADA");
        DrawZone(Level.GoalZone, "CHEGADA");
        foreach (var z in Level.TrainZones) DrawZone(z, "TRILHO DO TREM");
        DrawCursorTags(true);
        DrawBottomHelp($"{AnyConfirm} colocar   •   [Q] [E]  [CTRL] [SHIFT]  {{LB}} {{RB}} girar   •   vermelho = não dá para colocar aí");
    }

    void DrawZone(RectInt zone, string label)
    {
        Vector2 a = ToGui(Level.Origin + new Vector2(zone.xMin, zone.yMax));
        Vector2 b = ToGui(Level.Origin + new Vector2(zone.xMax, zone.yMin));
        var r = Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        float t = Mathf.Max(2f, 3f * S);
        var c = new Color(1f, 1f, 1f, 0.35f);
        Fill(new Rect(r.x, r.y, r.width, r.height), new Color(1f, 1f, 1f, 0.06f));
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        Text(new Rect(r.x, r.y + 4 * S, r.width, 30 * S), label, 18, new Color(1f, 1f, 1f, 0.7f));
    }

    void DrawCursorTags(bool withItem)
    {
        foreach (var p in Players)
        {
            if (!p.CursorView.HasValue || (Phase == Phase.Pick && p.Picked) || (Phase == Phase.Place && p.Placed)) continue;
            var g = ToGui(p.CursorView.Value);
            string label = p.Name;
            string keys = $"{ConfirmKey(p.Input)} pegar";
            if (withItem && p.HeldView.HasValue)
            {
                var def = ItemDefs.Get(p.HeldView.Value);
                string itemName = def.Type == ItemType.Portal ? (p.HeldSecondPortal ? "Portal 2 de 2" : "Portal 1 de 2") : def.Name;
                label = $"{p.Name} · {itemName}";
                keys = $"{ConfirmKey(p.Input)} colocar" + (def.CanRotate ? $"   {RotateKey(p.Input)} girar" : "");
            }
            Text(new Rect(g.x + 22 * S, g.y + 18 * S, 400 * S, 30 * S), label, 22, p.Color, TextAnchor.UpperLeft);
            if (!p.IsBot) Keys(new Rect(g.x + 22 * S, g.y + 48 * S, 600 * S, 28 * S), keys, 15, new Color(1f, 0.9f, 0.55f), TextAnchor.MiddleLeft);
        }
    }

    /// <summary>Barra de ajuda no rodapé; 'slide' de 0 a 1 esconde ela para baixo.</summary>
    void DrawBottomHelp(string s, float slide = 0f)
    {
        float y = 1028f + Mathf.Clamp01(slide) * 52f;
        Fill(R(0, y, VW, 52), new Color(0.05f, 0.03f, 0.09f, 0.78f));
        Keys(R(0, y, VW, 52), s, 20, new Color(0.88f, 0.86f, 0.95f));
    }

    void DrawRun()
    {
        foreach (var p in Players)
        {
            if (p.ZombieView != ZombieState.Alive) continue;
            WorldText(p.ZombiePos + new Vector2(0f, 1.55f), p.Name, 20, p.Color);
        }
        if (Phase == Phase.Run && PhaseTime < 1.1f)
        {
            float k = Mathf.Clamp01(PhaseTime / 0.15f);
            Color c = new Color(0.55f, 1f, 0.45f, Mathf.Clamp01((1.1f - PhaseTime) * 3f));
            Text(R(0, 380, VW, 200), "VAI!", Mathf.RoundToInt(80 + 60 * k), c);
        }
        // lembrete dos controles nas primeiras rodadas (some sozinho depois de alguns segundos)
        if (Phase == Phase.Run && Round <= 2 && PhaseTime < 7f)
            DrawBottomHelp("PULAR  [W] [ESPAÇO]  [^]  {A}   •   segure para pular mais alto   •   pule na parede para escalar   •   [ESC] {START} pausar",
                           (PhaseTime - 6.5f) / 0.5f);
    }

    void DrawRoundEnd()
    {
        float k = Mathf.Clamp01(PhaseTime / 0.2f);
        Text(R(0, 400, VW, 160), roundMessage, Mathf.RoundToInt(50 + 30 * k), roundMessageColor);
    }

    void DrawScore()
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.01f, 0.05f, 0.72f));
        Text(R(0, 60, VW, 90), $"PLACAR  —  RODADA {Round}", 60, new Color(0.85f, 0.75f, 1f));
        if (!string.IsNullOrEmpty(roundMessage)) Text(R(0, 150, VW, 50), roundMessage, 32, new Color(1f, 0.85f, 0.4f));

        // linhas de 150 px; com mais de 4 jogadores elas encolhem (k) para caber na tela
        float segW = 90f, barX = (VW - segW * WinScore) / 2f + 60f, rowH = Mathf.Min(150f, 680f / Mathf.Max(1, Players.Count));
        float k = rowH / 150f;
        float y0 = 250f + (680f - rowH * Players.Count) * 0.5f;
        float grow = Mathf.Clamp01((PhaseTime - 0.4f) / 1.0f);
        int Sz(float size, int min) => Mathf.Max(min, Mathf.RoundToInt(size * k));

        for (int i = 0; i < Players.Count; i++)
        {
            var p = Players[i];
            float y = y0 + i * rowH;
            var frames = Art.ZombieFrames(p.Index);
            bool scored = p.Score > p.PrevScore;
            int fr = scored ? 2 + (int)(Time.time * 8f) % 4 : (int)(Time.time * 2f) % 2;
            DrawSprite(R(barX - 130 - 90 * k, y - 10 * k, 90 * k, 100 * k), frames[fr], Color.white);
            Text(R(barX - 120, y, 110, 80 * k), p.Name, Sz(40, 22), p.Color, TextAnchor.MiddleLeft);
            if (p.IsBot) Text(R(barX - 120, y + 52 * k, 110, 30 * k), "CPU " + p.Bot.Label, Sz(16, 12), p.Bot.LabelColor, TextAnchor.MiddleLeft);

            float shown = Mathf.Lerp(p.PrevScore, p.Score, grow);
            for (int s = 0; s < WinScore; s++)
            {
                var r = R(barX + s * segW + 3, y + 10 * k, segW - 6, 60 * k);
                Fill(r, new Color(1f, 1f, 1f, 0.08f));
                float fill = Mathf.Clamp01(shown - s);
                if (fill <= 0f) continue;
                Color c = s < p.PrevScore ? p.Color : Color.Lerp(Color.white, p.Color, 0.35f + 0.25f * Mathf.Sin(Time.time * 8f));
                Fill(new Rect(r.x, r.y, r.width * fill, r.height), c);
            }
            if (p.Score > WinScore)
                Text(R(barX + WinScore * segW + 10, y, 120, 80 * k), $"+{p.Score - WinScore}", Sz(30, 18), p.Color, TextAnchor.MiddleLeft);

            string pts = string.Join("    ", p.RoundPoints.Select(kv => $"+{kv.Value} {kv.Key}"));
            Text(R(barX, y + 74 * k, segW * WinScore, 40 * k), pts, Sz(24, 14), new Color(0.9f, 0.9f, 0.95f), TextAnchor.MiddleLeft);
            Text(R(barX + segW * WinScore - 200, y + 74 * k, 200, 40 * k), $"{p.Score} pts", Sz(26, 15), p.Color, TextAnchor.MiddleRight);
        }

        // linha de chegada do placar
        float lineX = barX + segW * WinScore;
        Fill(R(lineX - 2, y0 - 20, 4, Players.Count * rowH), new Color(1f, 0.6f, 0.75f, 0.9f));
        DrawSprite(R(lineX - 30, y0 - 70, 60, 48), Art.Brain, Color.white);

        if (PhaseTime > 1.6f)
        {
            float blink = 0.6f + 0.4f * Mathf.Sin(Time.time * 5f);
            Keys(R(0, 960, VW, 50), $"{AnyConfirm}  continuar", 28, new Color(1f, 0.9f, 0.5f, blink));
        }
    }

    void DrawVictory()
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.01f, 0.05f, 0.6f));
        var w = Players.FirstOrDefault(p => p.Index == winner);
        if (w == null) return;
        float wob = Mathf.Sin(Time.time * 3f) * 8f;
        Text(R(0, 90 + wob, VW, 140), $"{w.FullName} VENCEU!", w.IsBot ? 96 : 120, w.Color);
        Text(R(0, 230, VW, 50), "O zumbi mais esperto do cemitério!", 32, new Color(0.9f, 0.85f, 1f));

        var frames = Art.ZombieFrames(w.Index);
        int f = 2 + (int)(Time.time * 9f) % 4;
        float hop = Mathf.Abs(Mathf.Sin(Time.time * 6f)) * 40f;
        DrawSprite(R(VW / 2 - 135, 330 - hop, 270, 300), frames[f], Color.white, (int)(Time.time * 1.5f) % 2 == 0);

        var ordered = Players.OrderByDescending(p => p.Score).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var p = ordered[i];
            float lineH = Mathf.Min(52f, 330f / ordered.Count);
            Text(R(0, 670 + i * lineH, VW, lineH), $"{i + 1}º   {p.FullName}   {p.Score} pts", Mathf.RoundToInt(34 * lineH / 52f), p.Color);
        }
        if (PhaseTime > 2f)
        {
            float blink = 0.6f + 0.4f * Mathf.Sin(Time.time * 5f);
            Keys(R(0, 960, VW, 50), $"{AnyConfirm}  voltar ao lobby", 28, new Color(1f, 0.9f, 0.5f, blink));
        }
    }

    void DrawPause()
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.72f));
        Text(R(0, 110, VW, 110), "PAUSADO", 84, Color.white);
        Keys(R(0, 228, VW, 46), $"[ESC] {{START}} ou {AnyConfirm}  continuar", 26, new Color(0.6f, 1f, 0.5f));
        Keys(R(0, 280, VW, 46), "[Q] [BACKSPACE] {B}  sair para o lobby", 26, new Color(1f, 0.6f, 0.5f));
        float h = DrawControls(350f);
        Text(R(0, 350 + h + 20, VW, 36), "Dicas: segure o pulo para ir mais alto  •  encoste na parede e pule para escalar  •  o primeiro a chegar ganha +1", 21, new Color(0.78f, 0.76f, 0.86f));
    }
}
