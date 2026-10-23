using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public enum NetMode { Offline, Connecting, Host, Client }

/// <summary>
/// Multiplayer online. O host roda o jogo de verdade; cada cliente manda os botões dos seus teclados e
/// controles (que viram jogadores no lobby do host, como um controle a mais) e recebe de volta a cena
/// (SceneSync), o estado da interface, os sons e os textos que sobem na tela.
/// </summary>
[DefaultExecutionOrder(-95)] // antes do GameManager, dos bots e dos zumbis: os botões remotos valem no mesmo quadro
public class NetGame : MonoBehaviour
{
    /// <summary>Muda quando as mensagens mudam: host e cliente precisam da mesma versão.</summary>
    public const int Protocol = 4;
    const float SendInterval = 1f / 30f;
    const float PingInterval = 0.5f;
    const float ConnectTimeout = 20f;

    public static NetGame Instance { get; private set; }

    public NetMode Mode { get; private set; }
    /// <summary>Andamento ou erro, para mostrar no painel online.</summary>
    public string Status { get; private set; } = "";
    public bool StatusIsError { get; private set; }
    public string JoinCode => transport != null ? transport.JoinCode : null;
    public bool ViaRelay { get; private set; }
    public string HostAddress { get; private set; }
    /// <summary>No cliente: o número desta conexão no host (para saber quais jogadores são "você").</summary>
    public int MyId { get; private set; }
    /// <summary>Computadores conectados à sala, contando o host.</summary>
    public int Computers { get; set; } = 1;
    /// <summary>No cliente: tempo de ida e volta até o host, em milissegundos (0 = ainda não medido).</summary>
    public int PingMs { get; private set; }

    /// <summary>Painel online aberto: o teclado deste computador está digitando, não jogando.</summary>
    public static bool PanelOpen { get; set; }
    static int panelClosedFrame = -1;
    /// <summary>Teclas deste computador não valem no jogo (painel aberto, ou fechado neste quadro com Esc).</summary>
    public static bool LocalInputBlocked => PanelOpen || panelClosedFrame == Time.frameCount;
    /// <summary>Só para testes: um dispositivo de mentira deste computador, controlado pelo DemoDriver.</summary>
    public static InputSource TestDevice;

    NetTransport transport;
    GameManager gm;
    float sendTimer, connectTimer, pingTimer, statsTimer = 10f;
    long statsBytes;

    // host
    SceneSync sync;
    readonly HashSet<int> clients = new HashSet<int>();
    readonly Dictionary<(int, int), InputSource> remote = new Dictionary<(int, int), InputSource>();
    readonly Dictionary<InputSource, byte[]> lastCounters = new Dictionary<InputSource, byte[]>();
    readonly List<byte[]> events = new List<byte[]>();   // sons e textos: vão junto com a próxima atualização

    // cliente
    SceneMirror mirror;
    readonly Dictionary<InputSource, int> localIds = new Dictionary<InputSource, int>();
    readonly Dictionary<InputSource, byte[]> counters = new Dictionary<InputSource, byte[]>();

    void Awake()
    {
        Instance = this;
        gm = GetComponent<GameManager>();
    }

    void OnDestroy()
    {
        transport?.Dispose();
        transport = null;
        Sfx.Played -= OnSound;
        GameManager.FloatAdded -= OnFloat;
        if (Instance == this) Instance = null;
    }

    public static void ClosePanel()
    {
        if (!PanelOpen) return;
        PanelOpen = false;
        panelClosedFrame = Time.frameCount;
    }

    void SetStatus(string s, bool error = false) { Status = s; StatusIsError = error; }

    // ================================================================== criar e entrar

    public async void HostRelay()
    {
        if (Mode != NetMode.Offline) return;
        Mode = NetMode.Connecting;
        SetStatus("Criando sala...");
        try
        {
            var t = await NetTransport.HostRelay();
            if (Mode != NetMode.Connecting) { t.Dispose(); return; } // cancelou enquanto esperava
            ViaRelay = true;
            BecomeHost(t);
        }
        catch (Exception e) { Fail("Não deu para criar a sala", e); }
    }

    public void HostIp(ushort port = NetTransport.DefaultPort)
    {
        if (Mode != NetMode.Offline) return;
        try
        {
            ViaRelay = false;
            HostAddress = LocalAddress() + ":" + port;
            BecomeHost(NetTransport.HostIp(port));
        }
        catch (Exception e) { Fail("Não deu para criar a sala na rede local", e); }
    }

    public async void JoinRelay(string code)
    {
        if (Mode != NetMode.Offline) return;
        if (string.IsNullOrWhiteSpace(code)) { SetStatus("Digite o código da sala.", true); return; }
        Mode = NetMode.Connecting;
        SetStatus("Procurando a sala " + code.Trim().ToUpperInvariant() + "...");
        try
        {
            var t = await NetTransport.JoinRelay(code);
            if (Mode != NetMode.Connecting) { t.Dispose(); return; }
            ViaRelay = true;
            BecomeClient(t);
        }
        catch (Exception e) { Fail("Não deu para entrar", e); }
    }

    public void JoinIp(string address, ushort port = NetTransport.DefaultPort)
    {
        if (Mode != NetMode.Offline) return;
        try
        {
            ViaRelay = false;
            HostAddress = address.Trim();
            Mode = NetMode.Connecting;
            SetStatus("Conectando em " + address.Trim() + "...");
            BecomeClient(NetTransport.JoinIp(address, port));
        }
        catch (Exception e) { Fail("Não deu para conectar", e); }
    }

    void Fail(string what, Exception e)
    {
        Debug.LogWarning($"[Net] {what}: {e}");
        Leave();
        PanelOpen = true; // mostra o erro
        string msg = e.Message;
        if (msg.Contains("project") || msg.Contains("Project") || msg.Contains("cloud") || msg.Contains("Cloud"))
            msg = "este build não está ligado a um projeto do Unity Cloud (veja \"Multiplayer online\" no README). Por enquanto, use a rede local.";
        else if (msg.Contains("join code") || msg.Contains("Join code") || msg.Contains("not found") || msg.Contains("Not Found"))
            msg = "código não encontrado (confira as letras; a sala pode ter fechado).";
        SetStatus($"{what}: {msg}", true);
    }

    /// <summary>Sai da sala (ou fecha a sala, no host) e volta a jogar só neste computador.</summary>
    public void Leave()
    {
        var was = Mode;
        transport?.Dispose();
        transport = null;
        Mode = NetMode.Offline;
        Computers = 1;
        PingMs = 0;
        events.Clear();
        Sfx.Played -= OnSound;
        GameManager.FloatAdded -= OnFloat;

        if (was == NetMode.Host)
        {
            foreach (int id in clients) gm.OnRemoteLeft(id);
            clients.Clear();
            foreach (var r in remote.Values) r.RemoteAlive = false;
            remote.Clear();
            lastCounters.Clear();
            InputSource.RemoteDevices.Clear();
            sync = null;
        }
        if (mirror != null) { mirror.Clear(); mirror = null; }
        if (gm.Replica) gm.ExitReplica();
        localIds.Clear();
        counters.Clear();
        if (was != NetMode.Offline) SetStatus("");
    }

    void BecomeHost(NetTransport t)
    {
        transport = t;
        Mode = NetMode.Host;
        sync = new SceneSync();
        t.Connected += id => { };            // espera o "olá" do cliente
        t.Received += HostReceive;
        t.Disconnected += HostLost;
        Sfx.Played += OnSound;
        GameManager.FloatAdded += OnFloat;
        SetStatus("");
        Debug.Log("[Net] sala criada " + (ViaRelay ? "com o código " + t.JoinCode : "em " + HostAddress));
    }

    void BecomeClient(NetTransport t)
    {
        transport = t;
        connectTimer = ConnectTimeout;
        t.Connected += id => Send(Hello(), true);
        t.Received += ClientReceive;
        t.Disconnected += id =>
        {
            bool wasPlaying = Mode == NetMode.Client;
            Debug.Log($"[Net] desconectado do host (estava jogando: {wasPlaying})");
            Leave();
            SetStatus(wasPlaying ? "A conexão com o host caiu." : "O host não respondeu.", true);
            PanelOpen = true;
        };
    }

    static byte[] Hello()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Msg.Hello);
        w.Write(Protocol);
        return ms.ToArray();
    }

    void Send(byte[] data, bool reliable, int id = 1) => transport?.Send(id, data, reliable);

    void Broadcast(byte[] data, bool reliable)
    {
        foreach (int id in clients) transport.Send(id, data, reliable);
    }

    // ================================================================== loop

    void Update()
    {
        if (transport == null) return;
        try { transport.Update(); }
        catch (Exception e) { Fail("Erro de rede", e); return; }
        if (transport == null) return; // desconectou durante a leitura
        if (DemoDriver.Active) LogStats();

        if (Mode == NetMode.Connecting)
        {
            connectTimer -= Time.unscaledDeltaTime;
            if (connectTimer <= 0f) { Leave(); SetStatus("O host não respondeu (tempo esgotado).", true); PanelOpen = true; }
            return;
        }
        if (Mode == NetMode.Client)
        {
            mirror?.Update(Time.unscaledDeltaTime);
            SendInput();
            pingTimer -= Time.unscaledDeltaTime;
            if (pingTimer <= 0f)
            {
                pingTimer = PingInterval;
                var ms = new MemoryStream();
                var pw = new BinaryWriter(ms);
                pw.Write(Msg.Ping);
                pw.Write(Time.realtimeSinceStartup);
                Send(ms.ToArray(), false);
            }
            return;
        }

        // host: manda a cena e a interface 30 vezes por segundo
        Computers = 1 + clients.Count;
        sendTimer -= Time.unscaledDeltaTime;
        if (sendTimer > 0f || clients.Count == 0) return;
        sendTimer = SendInterval;
        TickSync();
        var ui = new MemoryStream();
        var w = new BinaryWriter(ui);
        w.Write(Msg.Ui);
        w.Write((byte)Computers);
        gm.WriteUi(w);
        Broadcast(ui.ToArray(), false);

        foreach (int id in new List<int>(clients))
            if (transport.Backlog(id) > 600) // não está dando conta de receber: melhor desconectar do que travar
            {
                Debug.LogWarning($"[Net] cliente {id} lento demais, desconectado");
                transport.Kick(id);
                HostLost(id);
            }
    }

    void TickSync()
    {
        var msgs = new List<byte[]>();
        sync.Capture(msgs);
        msgs.AddRange(events);
        events.Clear();
        foreach (var b in Pack(msgs)) Broadcast(b, true);
    }

    /// <summary>
    /// Junta várias mensagens em pacotes de até ~1 KB ([Bundle][quantidade][tamanho + mensagem]...): menos
    /// pacotes na rede e menos confirmações esperando no canal confiável.
    /// </summary>
    static List<byte[]> Pack(List<byte[]> msgs)
    {
        var packs = new List<byte[]>();
        MemoryStream ms = null;
        BinaryWriter w = null;
        int n = 0;
        void Close()
        {
            if (ms == null) return;
            var bytes = ms.ToArray();
            bytes[1] = (byte)n;
            packs.Add(bytes);
            ms = null;
        }
        foreach (var m in msgs)
        {
            if (ms != null && (ms.Length + m.Length + 2 > NetTransport.MaxMessage || n == 255)) Close();
            if (ms == null)
            {
                ms = new MemoryStream();
                w = new BinaryWriter(ms);
                w.Write(Msg.Bundle);
                w.Write((byte)0);
                n = 0;
            }
            w.Write((ushort)m.Length);
            w.Write(m);
            n++;
        }
        Close();
        return packs;
    }

    // ================================================================== host

    void HostReceive(int id, byte[] data)
    {
        if (data.Length == 0) return;
        var r = new BinaryReader(new MemoryStream(data));
        byte type = r.ReadByte();
        if (type == Msg.Hello)
        {
            int protocol = data.Length >= 5 ? r.ReadInt32() : -1;
            if (protocol != Protocol)
            {
                var ms = new MemoryStream();
                var w = new BinaryWriter(ms);
                w.Write(Msg.Reject);
                w.Write("Versões diferentes do jogo: atualize o jogo nos dois computadores.");
                transport.Send(id, ms.ToArray(), true);
                return;
            }
            var welcome = new MemoryStream();
            var ww = new BinaryWriter(welcome);
            ww.Write(Msg.Welcome);
            ww.Write(id);
            transport.Send(id, welcome.ToArray(), true);
            // os outros clientes recebem o que mudou; o novo recebe a cena inteira
            if (clients.Count > 0) TickSync();
            else sync.Capture(new List<byte[]>());
            var all = new List<byte[]>();
            sync.FullState(all);
            foreach (var b in Pack(all)) transport.Send(id, b, true);
            clients.Add(id);
            Sfx.Play("join");
            Debug.Log($"[Net] cliente {id} entrou ({all.Count} mensagens de cena)");
        }
        else if (type == Msg.Input && clients.Contains(id)) ReadInput(id, r);
        else if (type == Msg.Ping && clients.Contains(id))
        {
            data[0] = Msg.Pong; // devolve o mesmo horário: o cliente calcula a ida e volta
            transport.Send(id, data, false);
        }
    }

    void HostLost(int id)
    {
        if (!clients.Remove(id)) return;
        foreach (var kv in new List<KeyValuePair<(int, int), InputSource>>(remote))
        {
            if (kv.Key.Item1 != id) continue;
            kv.Value.RemoteAlive = false;
            InputSource.RemoteDevices.Remove(kv.Value);
            lastCounters.Remove(kv.Value);
            remote.Remove(kv.Key);
        }
        gm.OnRemoteLeft(id);
        Debug.Log($"[Net] cliente {id} saiu");
    }

    /// <summary>Botões de um cliente: [quantidade] e, por dispositivo: id, tipo, nº do controle, eixo x/y, pulo segurado, contadores de apertos.</summary>
    void ReadInput(int conn, BinaryReader r)
    {
        int n = r.ReadByte();
        for (int i = 0; i < n; i++)
        {
            int devId = r.ReadByte();
            var kind = (DeviceKind)r.ReadByte();
            int pad = r.ReadByte();
            float mx = r.ReadSByte() / 127f, my = r.ReadSByte() / 127f;
            bool jumpHeld = r.ReadByte() != 0;
            var c = r.ReadBytes(6);

            if (!remote.TryGetValue((conn, devId), out var src))
            {
                src = new InputSource { Kind = kind, PadNumber = pad, Remote = true, RemoteConn = conn, RemoteId = devId };
                remote[(conn, devId)] = src;
                InputSource.RemoteDevices.Add(src);
                lastCounters[src] = c; // os apertos antigos não contam
            }
            src.RemoteMove = new Vector2(mx, my);
            src.RemoteJumpHeld = jumpHeld;
            var last = lastCounters[src];
            for (int b = 0; b < 6; b++)
                if (c[b] != last[b]) src.RemotePressFrame[b] = Time.frameCount;
            lastCounters[src] = c;
        }
    }

    void OnSound(string name, float volume, float pitch)
    {
        if (clients.Count == 0) return;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Msg.Sound);
        w.Write(name);
        w.Write(volume);
        w.Write(pitch);
        events.Add(ms.ToArray());
    }

    void OnFloat(Vector2 pos, string text, Color color, int size, float duration)
    {
        if (clients.Count == 0) return;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Msg.Float);
        w.Write(pos.x); w.Write(pos.y);
        w.Write(text);
        Color32 c = color;
        w.Write(c.r); w.Write(c.g); w.Write(c.b); w.Write(c.a);
        w.Write((byte)size);
        w.Write(duration);
        events.Add(ms.ToArray());
    }

    // ================================================================== cliente

    void ClientReceive(int id, byte[] data)
    {
        if (data.Length == 0) return;
        var r = new BinaryReader(new MemoryStream(data));
        byte type = r.ReadByte();
        switch (type)
        {
            case Msg.Welcome:
                MyId = r.ReadInt32();
                Mode = NetMode.Client;
                mirror = new SceneMirror();
                gm.EnterReplica();
                ClosePanel();
                SetStatus("");
                Debug.Log($"[Net] conectado na sala (sou o cliente {MyId})");
                break;
            case Msg.Reject:
                string why = r.ReadString();
                Leave();
                SetStatus(why, true);
                PanelOpen = true;
                break;
            case Msg.Bundle:
                int n = r.ReadByte();
                for (int i = 0; i < n; i++) ClientReceive(id, r.ReadBytes(r.ReadUInt16()));
                break;
            case Msg.Pong:
                int rtt = Mathf.RoundToInt((Time.realtimeSinceStartup - r.ReadSingle()) * 1000f);
                PingMs = PingMs == 0 ? rtt : Mathf.RoundToInt(Mathf.Lerp(PingMs, rtt, 0.3f));
                break;
            case Msg.SpriteKey:
            case Msg.Objects:
            case Msg.Remove:
                mirror?.Apply(type, r);
                break;
            case Msg.Sound:
                string name = r.ReadString();
                float volume = r.ReadSingle(), pitch = r.ReadSingle();
                Sfx.Play(name, volume, pitch);
                break;
            case Msg.Float:
                var pos = new Vector2(r.ReadSingle(), r.ReadSingle());
                string text = r.ReadString();
                var color = new Color32(r.ReadByte(), r.ReadByte(), r.ReadByte(), r.ReadByte());
                int size = r.ReadByte();
                float duration = r.ReadSingle();
                gm.AddFloatFromNet(pos, text, color, size, duration);
                break;
            case Msg.Ui:
                if (Mode != NetMode.Client) break;
                Computers = r.ReadByte();
                gm.ReadUi(r);
                break;
        }
    }

    /// <summary>Manda o estado dos teclados e controles deste computador (todo quadro, pelo canal rápido).</summary>
    void SendInput()
    {
        var devices = InputSource.AvailableDevices();
        if (TestDevice != null) devices.Add(TestDevice);
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Msg.Input);
        w.Write((byte)devices.Count);
        bool blocked = LocalInputBlocked;
        foreach (var d in devices)
        {
            if (!localIds.TryGetValue(d, out int devId))
            {
                devId = d == TestDevice ? 99 : d.Kind == DeviceKind.Keyboard1 ? 0 : d.Kind == DeviceKind.Keyboard2 ? 1 : 2 + localIds.Count;
                localIds[d] = devId;
                counters[d] = new byte[6];
            }
            var c = counters[d];
            bool test = d == TestDevice;
            if (!blocked || test)
            {
                if (d.JumpDown) c[InputSource.BtnJump]++;
                if (d.ConfirmDown) c[InputSource.BtnConfirm]++;
                if (d.BackDown) c[InputSource.BtnBack]++;
                if (d.RotateCWDown) c[InputSource.BtnCW]++;
                if (d.RotateCCWDown) c[InputSource.BtnCCW]++;
                if (d.PauseDown) c[InputSource.BtnPause]++;
            }
            Vector2 m = blocked && !test ? Vector2.zero : d.Move;
            w.Write((byte)devId);
            w.Write((byte)(test ? DeviceKind.Keyboard1 : d.Kind));
            w.Write((byte)d.PadNumber);
            w.Write((sbyte)Mathf.RoundToInt(m.x * 127f));
            w.Write((sbyte)Mathf.RoundToInt(m.y * 127f));
            w.Write((byte)(!blocked || test ? (d.JumpHeld ? 1 : 0) : 0));
            w.Write(c);
        }
        Send(ms.ToArray(), false);
    }

    // ================================================================== utilidades

    /// <summary>Modo de teste: a cada 10 s, o tráfego e quantos sprites estão sendo copiados.</summary>
    void LogStats()
    {
        statsTimer -= Time.unscaledDeltaTime;
        if (statsTimer > 0f) return;
        statsTimer = 10f;
        long total = Mode == NetMode.Host ? transport.BytesSent : transport.BytesReceived;
        float kbps = (total - statsBytes) / 1024f / 10f;
        statsBytes = total;
        if (Mode == NetMode.Host)
        {
            int sprites = 0;
            foreach (var sr in FindObjectsByType<SpriteRenderer>(FindObjectsSortMode.None))
                if (sr.enabled && sr.sprite != null && sr.gameObject.activeInHierarchy) sprites++;
            int backlog = 0;
            foreach (int id in clients) backlog = Mathf.Max(backlog, transport.Backlog(id));
            Debug.Log($"[Net] host: {kbps:0.0} KB/s enviados, {sprites} sprites na cena, fila {backlog}, fase {gm.Phase}");
        }
        else if (Mode == NetMode.Client)
            Debug.Log($"[Net] cliente: {kbps:0.0} KB/s recebidos, {mirror?.Count ?? 0} sprites copiados, ping {PingMs} ms, fase {gm.Phase}");
    }

    /// <summary>IP deste computador na rede local (para mostrar a quem vai entrar pela rede local).</summary>
    public static string LocalAddress()
    {
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up ||
                    ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                foreach (var a in ni.GetIPProperties().UnicastAddresses)
                {
                    if (a.Address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork) continue;
                    string s = a.Address.ToString();
                    if (s.StartsWith("192.168.") || s.StartsWith("10.") || s.StartsWith("172.")) return s;
                }
            }
        }
        catch (Exception) { }
        return "127.0.0.1";
    }

    public static bool IpSupported =>
#if UNITY_WEBGL && !UNITY_EDITOR
        false; // o navegador não abre conexões diretas
#else
        true;
#endif
}
