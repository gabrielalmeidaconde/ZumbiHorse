using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Parte do GameManager ligada ao multiplayer online: o modo cópia do cliente (não roda o jogo, só mostra
/// o que o host manda), o estado da interface que vai pela rede e o painel "JOGAR ONLINE" (tecla O).
/// </summary>
public partial class GameManager
{
    /// <summary>Cliente online: a partida roda no host; aqui só se desenha.</summary>
    public bool Replica { get; private set; }

    /// <summary>Avisado a cada texto que sobe na tela (o host repassa para os clientes).</summary>
    public static event System.Action<Vector2, string, Color, int, float> FloatAdded;

    struct BoxView { public ItemType Type; public int Owner; }
    readonly List<BoxView> boxView = new List<BoxView>();

    bool replicaBotsOnly;
    int savedWinScore;
    bool BotsOnlyAllowed => Replica ? replicaBotsOnly : AllowBotsOnly;

    /// <summary>Com o painel online aberto, o teclado deste computador está digitando: só valem os dispositivos remotos.</summary>
    static bool Listening(InputSource d) => d.Remote || !NetGame.LocalInputBlocked;
    static List<InputSource> ActiveDevices() => InputSource.AvailableDevices().Where(Listening).ToList();

    bool CanOpenOnline => Replica || Phase == Phase.Lobby;

    void HandleOnlineKeys()
    {
        if (!CanOpenOnline) { NetGame.ClosePanel(); return; }
        if (InputSource.OnlineKeyDown && !onlineTextFocused)
        {
            if (NetGame.PanelOpen) NetGame.ClosePanel();
            else { NetGame.PanelOpen = true; Sfx.Play("tick"); }
        }
        else if (NetGame.PanelOpen && InputSource.EscapeKeyDown) NetGame.ClosePanel();
    }

    /// <summary>"Teclado (WASD)" para quem joga aqui; no online diz de qual computador é.</summary>
    string DeviceLabel(PlayerSlot p)
    {
        var i = p.Input;
        if (!Replica) return i.Remote ? "Online: " + i.Label : i.Label;
        if (!i.Remote) return "Host: " + i.Label;
        return (i.RemoteConn == NetGame.Instance.MyId ? "Você: " : "Online: ") + i.Label;
    }

    // ================================================================== modo cópia (cliente)

    public void EnterReplica()
    {
        ClearRoundObjects();
        Players.Clear();
        boxView.Clear();
        floats.Clear();
        Level.ClearAll();
        paused = false;
        Time.timeScale = 1f;
        savedWinScore = WinScore;
        Replica = true;
        SetPhase(Phase.Lobby);
    }

    public void ExitReplica()
    {
        Replica = false;
        Players.Clear();
        boxView.Clear();
        floats.Clear();
        WinScore = savedWinScore;
        paused = false;
        winner = -1;
        lobbyCountdown = -1f;
        shake = 0f;
        Level.Build(MapIndex < Maps.All.Length ? Maps.All[MapIndex] : Maps.All[0]);
        SetPhase(Phase.Lobby);
    }

    void ReplicaUpdate()
    {
        PhaseTime += Time.unscaledDeltaTime; // anda sozinho entre um envio e outro (animações lisas)
        floats.RemoveAll(f => Time.time - f.Start > f.Duration);
    }

    public void AddFloatFromNet(Vector2 pos, string text, Color color, int size, float duration) =>
        floats.Add(new FloatText { Pos = pos, Text = text, Color = color, Size = size, Start = Time.time, Duration = duration });

    /// <summary>Um computador saiu da sala: no lobby os jogadores dele saem; no meio da partida viram CPU.</summary>
    public void OnRemoteLeft(int conn)
    {
        foreach (var p in Players.ToList())
        {
            if (!p.Input.Remote || p.Input.RemoteConn != conn) continue;
            if (Phase == Phase.Lobby) { Players.Remove(p); continue; }
            p.Bot = new BotPlayer(BotDifficulty.Medio);
            p.Bot.ResetPhase();
            p.Input = new InputSource { Kind = DeviceKind.Bot };
            p.Ready = true;
            if (p.Zombie != null) p.Zombie.Controls = p.Input;
            AddFloat(new Vector2(0f, Level.H / 2f - 3f), $"{p.Name} saiu da sala: agora é CPU", p.Color, 26, 3f);
        }
    }

    /// <summary>Atualiza o que a interface mostra a partir dos objetos do jogo (host / offline).</summary>
    void RefreshViews()
    {
        foreach (var p in Players)
        {
            p.ZombieView = p.Zombie != null ? p.Zombie.State : (ZombieState?)null;
            if (p.Zombie != null) p.ZombiePos = p.Zombie.Position;
            p.CursorView = p.Cursor != null ? p.Cursor.Position : (Vector2?)null;
            p.HeldView = p.Item != null ? p.Item.Def.Type : (ItemType?)null;
            p.HeldSecondPortal = p.PortalFirst != null;
        }
        boxView.Clear();
        foreach (var it in boxItems) if (it != null) boxView.Add(new BoxView { Type = it.Def.Type, Owner = it.Owner });
    }

    // ================================================================== estado da interface pela rede

    static void WriteV(BinaryWriter w, Vector2 v) { w.Write(SceneSync.Q(v.x)); w.Write(SceneSync.Q(v.y)); }
    static Vector2 ReadV(BinaryReader r) => new Vector2(SceneSync.UnQ(r.ReadInt16()), SceneSync.UnQ(r.ReadInt16()));

    public void WriteUi(BinaryWriter w)
    {
        RefreshViews();
        w.Write((byte)Phase);
        w.Write(PhaseTime);
        w.Write((byte)Round);
        w.Write((byte)MapIndex);
        w.Write((byte)System.Array.IndexOf(Maps.All, Level.Map));
        w.Write(lobbyCountdown);
        w.Write(pickTimer);
        w.Write(placeTimer);
        w.Write(runTimer);
        w.Write((byte)((paused ? 1 : 0) | (botsOnly ? 2 : 0) | (nobodyLastRound ? 4 : 0) | (AllowBotsOnly ? 8 : 0)));
        w.Write((byte)menuScreen);   // menu de pausa: qual tela e qual item (quem mexe é o host)
        w.Write((byte)menuSel);
        w.Write((byte)WinScore);
        w.Write(shake);
        w.Write(roundMessage ?? "");
        Color32 rc = roundMessageColor;
        w.Write(rc.r); w.Write(rc.g); w.Write(rc.b); w.Write(rc.a);
        w.Write((sbyte)winner);

        w.Write((byte)boxView.Count);
        foreach (var b in boxView) { w.Write((byte)b.Type); w.Write((sbyte)b.Owner); }

        w.Write((byte)Players.Count);
        foreach (var p in Players)
        {
            w.Write((byte)p.Index);
            w.Write((byte)p.Input.Kind);
            w.Write((byte)p.Input.PadNumber);
            w.Write((byte)(p.Input.Remote ? p.Input.RemoteConn : 0));
            w.Write((byte)((p.Ready ? 1 : 0) | (p.Picked ? 2 : 0) | (p.Placed ? 4 : 0) | (p.IsBot ? 8 : 0) | (p.HeldSecondPortal ? 16 : 0)));
            w.Write((sbyte)p.Dance);
            w.Write((byte)p.Skin);
            w.Write((short)p.Score);
            w.Write((short)p.PrevScore);
            w.Write(p.JoinFrame);
            w.Write((byte)(p.IsBot ? (int)p.Bot.Difficulty : 0));
            w.Write((byte)(p.ZombieView.HasValue ? (int)p.ZombieView.Value + 1 : 0));
            WriteV(w, p.ZombiePos);
            w.Write(p.CursorView.HasValue);
            WriteV(w, p.CursorView ?? Vector2.zero);
            w.Write((sbyte)(p.HeldView.HasValue ? (int)p.HeldView.Value : -1));
            w.Write((byte)p.RoundPoints.Count);
            foreach (var kv in p.RoundPoints) { w.Write(kv.Key); w.Write((sbyte)kv.Value); }
        }
    }

    public void ReadUi(BinaryReader r)
    {
        var phase = (Phase)r.ReadByte();
        float phaseTime = r.ReadSingle();
        if (phase != Phase || Mathf.Abs(phaseTime - PhaseTime) > 0.25f) PhaseTime = phaseTime;
        Phase = phase;
        Round = r.ReadByte();
        MapIndex = r.ReadByte();
        int levelMap = r.ReadByte();
        if (levelMap < Maps.All.Length && Level.Map != Maps.All[levelMap]) Level.LoadInfo(Maps.All[levelMap]);
        lobbyCountdown = r.ReadSingle();
        pickTimer = r.ReadSingle();
        placeTimer = r.ReadSingle();
        runTimer = r.ReadSingle();
        int flags = r.ReadByte();
        paused = (flags & 1) != 0;
        botsOnly = (flags & 2) != 0;
        nobodyLastRound = (flags & 4) != 0;
        replicaBotsOnly = (flags & 8) != 0;
        var screen = (MenuScreen)r.ReadByte();
        if (screen != menuScreen) transitionTime = Time.unscaledTime;
        menuScreen = screen;
        menuSel = r.ReadByte();
        WinScore = r.ReadByte();
        shake = r.ReadSingle();
        roundMessage = r.ReadString();
        roundMessageColor = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
        winner = r.ReadSByte();

        boxView.Clear();
        int boxCount = r.ReadByte();
        for (int i = 0; i < boxCount; i++) boxView.Add(new BoxView { Type = (ItemType)r.ReadByte(), Owner = r.ReadSByte() });

        int n = r.ReadByte();
        while (Players.Count > n) Players.RemoveAt(Players.Count - 1);
        while (Players.Count < n) Players.Add(new PlayerSlot());
        foreach (var p in Players)
        {
            p.Index = r.ReadByte();
            var kind = (DeviceKind)r.ReadByte();
            int pad = r.ReadByte(), owner = r.ReadByte();
            if (p.Input == null || p.Input.Kind != kind || p.Input.RemoteConn != owner || p.Input.PadNumber != pad)
                p.Input = new InputSource { Kind = kind, PadNumber = pad, Remote = owner != 0, RemoteConn = owner };
            int f = r.ReadByte();
            p.Ready = (f & 1) != 0;
            p.Picked = (f & 2) != 0;
            p.Placed = (f & 4) != 0;
            p.HeldSecondPortal = (f & 16) != 0;
            p.Dance = r.ReadSByte();
            p.Skin = r.ReadByte();
            p.Score = r.ReadInt16();
            p.PrevScore = r.ReadInt16();
            p.JoinFrame = r.ReadInt32();
            var difficulty = (BotDifficulty)r.ReadByte();
            if ((f & 8) == 0) p.Bot = null;
            else if (p.Bot == null) p.Bot = new BotPlayer(difficulty);
            else p.Bot.Difficulty = difficulty;
            int z = r.ReadByte();
            p.ZombieView = z == 0 ? (ZombieState?)null : (ZombieState)(z - 1);
            p.ZombiePos = ReadV(r);
            bool hasCursor = r.ReadBoolean();
            var cursor = ReadV(r);
            p.CursorView = hasCursor ? cursor : (Vector2?)null;
            int held = r.ReadSByte();
            p.HeldView = held < 0 ? (ItemType?)null : (ItemType)held;
            p.RoundPoints.Clear();
            int points = r.ReadByte();
            for (int i = 0; i < points; i++) p.RoundPoints.Add(new KeyValuePair<string, int>(r.ReadString(), r.ReadSByte()));
        }
        Players.Sort((a, b) => a.Index.CompareTo(b.Index));
    }

    // ================================================================== painel "JOGAR ONLINE"

    string codeField = "", ipField = "127.0.0.1";
    bool onlineTextFocused;
    GUIStyle buttonStyle, fieldStyle;

    /// <summary>Faixa no lobby com a sala online atual. Devolve false se não está online.</summary>
    bool DrawOnlineBanner(Rect r)
    {
        var net = NetGame.Instance;
        if (net == null || net.Mode == NetMode.Offline) return false;
        string where = net.ViaRelay ? $"código [{net.JoinCode}]" : net.HostAddress;
        string s;
        Color c = new Color(0.55f, 0.92f, 0.4f);
        switch (net.Mode)
        {
            case NetMode.Connecting: s = net.Status; c = new Color(1f, 0.9f, 0.5f); break;
            case NetMode.Host: s = $"SALA ONLINE   •   {where}   •   {net.Computers} {(net.Computers == 1 ? "computador" : "computadores")}   •   [O] menu online"; break;
            default: s = $"ONLINE na sala {where}   •   {net.Computers} computadores   •   [O] sair"; break;
        }
        Keys(r, s, 22, c);
        return true;
    }

    bool Button(float x, float y, float w, float h, string label) => GUI.Button(R(x, y, w, h), label, buttonStyle);

    /// <summary>No cliente online: o ping até o host no canto de baixo (verde bom, amarelo ok, vermelho ruim).</summary>
    void DrawPing()
    {
        var net = NetGame.Instance;
        if (!Replica || net == null || net.PingMs <= 0) return;
        int ms = net.PingMs;
        Color c = ms < 90 ? new Color(0.55f, 1f, 0.45f) : ms < 180 ? new Color(1f, 0.85f, 0.35f) : new Color(1f, 0.45f, 0.4f);
        Text(R(14, 1050, 260, 28), $"ping {ms} ms", 17, c, TextAnchor.MiddleLeft);
    }

    void DrawOnline()
    {
        var net = NetGame.Instance;
        onlineTextFocused = false;
        DrawPing();
        if (net == null || !NetGame.PanelOpen) return;
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, wordWrap = true };
            fieldStyle = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }
        buttonStyle.fontSize = Mathf.RoundToInt(22 * S);
        fieldStyle.fontSize = Mathf.RoundToInt(30 * S);

        // Enter num campo de texto = botão "ENTRAR"
        var e = Event.current;
        bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
        string focused = GUI.GetNameOfFocusedControl();

        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.6f));
        float w = 1000f, h = 640f, x = (VW - w) / 2f, y = 220f;
        Fill(R(x, y, w, h), new Color(0.08f, 0.06f, 0.13f, 0.97f));
        Fill(R(x, y, w, 5), new Color(0.72f, 0.5f, 0.95f));
        Text(R(x, y + 14, w, 60), "JOGAR ONLINE", 44, new Color(0.55f, 0.92f, 0.4f));
        var light = new Color(0.85f, 0.83f, 0.92f);
        float cy = y + 95f;

        switch (net.Mode)
        {
            case NetMode.Offline:
                Text(R(x + 40, cy, w - 80, 34), "PELA INTERNET (com código)", 24, new Color(0.85f, 0.7f, 1f), TextAnchor.MiddleLeft);
                if (Button(x + 40, cy + 42, w - 80, 62, "CRIAR SALA  (gera um código para seus amigos)")) net.HostRelay();
                Text(R(x + 40, cy + 118, 300, 60), "Código da sala:", 22, light, TextAnchor.MiddleLeft);
                GUI.SetNextControlName("code");
                codeField = GUI.TextField(R(x + 260, cy + 118, 380, 60), codeField.ToUpperInvariant(), 8, fieldStyle);
                if (Button(x + 660, cy + 118, w - 700, 60, "ENTRAR") || (enter && focused == "code")) net.JoinRelay(codeField);
                cy += 210f;
                if (NetGame.IpSupported)
                {
                    Text(R(x + 40, cy, w - 80, 34), "REDE LOCAL (mesmo Wi-Fi, sem internet)", 24, new Color(0.85f, 0.7f, 1f), TextAnchor.MiddleLeft);
                    if (Button(x + 40, cy + 42, w - 80, 56, "CRIAR SALA NA REDE LOCAL")) net.HostIp();
                    Text(R(x + 40, cy + 110, 300, 60), "IP do host:", 22, light, TextAnchor.MiddleLeft);
                    GUI.SetNextControlName("ip");
                    ipField = GUI.TextField(R(x + 260, cy + 110, 380, 60), ipField, 40, fieldStyle);
                    if (Button(x + 660, cy + 110, w - 700, 60, "ENTRAR") || (enter && focused == "ip")) net.JoinIp(ipField);
                }
                else Text(R(x + 40, cy + 20, w - 80, 34), "A rede local só funciona no jogo baixado (não no navegador).", 20, light, TextAnchor.MiddleLeft);
                break;

            case NetMode.Connecting:
                Text(R(x, cy + 60, w, 50), net.Status, 30, new Color(1f, 0.9f, 0.5f));
                if (Button(x + w / 2 - 150, cy + 160, 300, 60, "CANCELAR")) net.Leave();
                break;

            case NetMode.Host:
                Text(R(x, cy, w, 40), "SALA ABERTA!", 30, Color.white);
                if (net.ViaRelay)
                {
                    Text(R(x, cy + 50, w, 34), "Código da sala:", 22, light);
                    Keys(R(x, cy + 88, w, 90), $"[{net.JoinCode}]", 56, Color.white);
                    WrapText(R(x + 60, cy + 190, w - 120, 64), "Seus amigos abrem o jogo (ou a página no itch.io), apertam O e digitam esse código.", 21, light);
                    if (Button(x + w / 2 - 330, cy + 270, 300, 56, "COPIAR CÓDIGO")) GUIUtility.systemCopyBuffer = net.JoinCode;
                    if (Button(x + w / 2 + 30, cy + 270, 300, 56, "FECHAR A SALA")) net.Leave();
                }
                else
                {
                    Text(R(x, cy + 50, w, 34), "Endereço deste computador na rede local:", 22, light);
                    Text(R(x, cy + 92, w, 80), net.HostAddress, 52, Color.white);
                    WrapText(R(x + 60, cy + 190, w - 120, 64), "Quem estiver no mesmo Wi-Fi aperta O, digita esse IP (sem o :7777) em \"IP do host\" e entra.", 21, light);
                    if (Button(x + w / 2 - 150, cy + 270, 300, 56, "FECHAR A SALA")) net.Leave();
                }
                Text(R(x, cy + 345, w, 34), $"{net.Computers - 1} {(net.Computers == 2 ? "computador conectado" : "computadores conectados")}. Cada jogador entra apertando CONFIRMAR no lobby.", 20, new Color(0.6f, 1f, 0.5f));
                break;

            case NetMode.Client:
                Text(R(x, cy, w, 40), "CONECTADO!", 30, Color.white);
                Keys(R(x, cy + 50, w, 60), net.ViaRelay ? $"Sala [{net.JoinCode}]" : "Host " + net.HostAddress, 30, light);
                WrapText(R(x + 60, cy + 130, w - 120, 64), "No lobby, aperte CONFIRMAR no seu teclado ou controle para entrar como jogador. Cada teclado/controle deste computador pode entrar.", 21, light);
                Text(R(x, cy + 205, w, 34), $"{net.Computers} computadores na sala", 21, new Color(0.6f, 1f, 0.5f));
                if (Button(x + w / 2 - 150, cy + 260, 300, 56, "SAIR DA SALA")) net.Leave();
                break;
        }

        if (!string.IsNullOrEmpty(net.Status) && net.Mode != NetMode.Connecting)
            WrapText(R(x + 40, y + h - 128, w - 80, 70), net.Status, 20, net.StatusIsError ? new Color(1f, 0.5f, 0.45f) : new Color(1f, 0.9f, 0.5f));
        Keys(R(x, y + h - 52, w, 40), "[ESC] ou [O] fechar", 18, new Color(0.6f, 0.6f, 0.66f));

        onlineTextFocused = GUI.GetNameOfFocusedControl() == "code" || GUI.GetNameOfFocusedControl() == "ip";
    }

    void WrapText(Rect r, string s, int size, Color c)
    {
        style.wordWrap = true;
        Text(r, s, size, c);
        style.wordWrap = false;
    }
}
