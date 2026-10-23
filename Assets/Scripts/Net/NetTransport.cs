using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

/// <summary>
/// Conexão do multiplayer online (Unity Transport). O host aceita até 7 computadores.
/// Cada mensagem vai pelo canal confiável (sempre chega, em ordem) ou pelo rápido (pode se perder;
/// usado para o que é mandado o tempo todo, como os botões e o placar).
/// Liga pelo Unity Relay (código de sala: funciona pela internet e no navegador) ou por IP (rede local).
/// O Relay vai sempre por WebSocket seguro (porta 443, como um site): passa pelos firewalls de faculdade e
/// empresa que bloqueiam UDP, e o jogo baixado e o do navegador conversam do mesmo jeito.
/// </summary>
public class NetTransport : IDisposable
{
    public const int MaxClients = 7;
    public const ushort DefaultPort = 7777;
    /// <summary>Tamanho máximo de uma mensagem (cabe num pacote mesmo passando pelo Relay).</summary>
    public const int MaxMessage = 1100;

    public bool IsHost { get; private set; }
    public string JoinCode { get; private set; }
    public int ClientCount => IsHost ? conns.Count : 0;
    /// <summary>Bytes enviados e recebidos desde o começo (para medir o tráfego).</summary>
    public long BytesSent { get; private set; }
    public long BytesReceived { get; private set; }

    /// <summary>id da conexão (no cliente, o host é sempre o 1).</summary>
    public event Action<int> Connected, Disconnected;
    public event Action<int, byte[]> Received;

    NetworkDriver driver;
    NetworkPipeline reliable, fast;
    readonly List<NetworkConnection> conns = new List<NetworkConnection>();
    readonly Dictionary<NetworkConnection, int> ids = new Dictionary<NetworkConnection, int>();
    readonly Dictionary<int, NetworkConnection> byId = new Dictionary<int, NetworkConnection>();
    readonly Dictionary<int, Queue<byte[]>> pending = new Dictionary<int, Queue<byte[]>>();
    int nextId = 1;

    // ================================================================== criar / entrar

    public static async Task<NetTransport> HostRelay()
    {
        await SignIn();
        var alloc = await RelayService.Instance.CreateAllocationAsync(MaxClients);
        string code = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);
        var data = alloc.ToRelayServerData(RelayProtocol);
        LogEndpoints("sala", alloc.ServerEndpoints);
        var t = new NetTransport { IsHost = true, JoinCode = code };
        var settings = new NetworkSettings(Allocator.Temp);
        settings.WithRelayParameters(ref data);
        t.CreateDriver(settings, webSocket: RelayProtocol == "wss");
        t.Listen(NetworkEndpoint.AnyIpv4);
        return t;
    }

    public static async Task<NetTransport> JoinRelay(string code)
    {
        await SignIn();
        var join = await RelayService.Instance.JoinAllocationAsync(code.Trim().ToUpperInvariant());
        var data = join.ToRelayServerData(RelayProtocol);
        LogEndpoints("entrada", join.ServerEndpoints);
        var t = new NetTransport { JoinCode = code.Trim().ToUpperInvariant() };
        var settings = new NetworkSettings(Allocator.Temp);
        settings.WithRelayParameters(ref data);
        t.CreateDriver(settings, webSocket: RelayProtocol == "wss");
        t.Track(t.driver.Connect(data.Endpoint));
        return t;
    }

    public static NetTransport HostIp(ushort port)
    {
        var t = new NetTransport { IsHost = true };
        t.CreateDriver(new NetworkSettings(Allocator.Temp), webSocket: false);
        t.Listen(NetworkEndpoint.AnyIpv4.WithPort(port));
        return t;
    }

    public static NetTransport JoinIp(string address, ushort port)
    {
        address = address.Trim();
        if (address.Length == 0 || address == "localhost") address = "127.0.0.1";
        if (!NetworkEndpoint.TryParse(address, port, out var endpoint))
            throw new Exception($"Endereço inválido: '{address}' (use um IP, como 192.168.0.10)");
        var t = new NetTransport();
        t.CreateDriver(new NetworkSettings(Allocator.Temp), webSocket: false);
        t.Track(t.driver.Connect(endpoint));
        return t;
    }

    /// <summary>
    /// Protocolo do Relay: WebSocket seguro (padrão; o navegador só tem esse). No jogo baixado dá para usar
    /// UDP com DTLS abrindo o jogo com -relayDtls (os dois computadores precisam usar a mesma opção).
    /// </summary>
    static string RelayProtocol =>
#if UNITY_WEBGL && !UNITY_EDITOR
        "wss";
#else
        Array.IndexOf(Environment.GetCommandLineArgs(), "-relayDtls") >= 0 ? "dtls" : "wss";
#endif

    /// <summary>Servidores do Relay que a Unity escolheu (ajuda a ver se a rede bloqueia algum).</summary>
    static void LogEndpoints(string what, List<RelayServerEndpoint> endpoints)
    {
        var parts = new List<string>();
        foreach (var e in endpoints) parts.Add($"{e.ConnectionType}={e.Host}:{e.Port}");
        Debug.Log($"[Net] Relay ({what}): {string.Join("  ", parts)}");
    }

    static async Task SignIn()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            // um perfil por execução: dois jogos abertos no mesmo computador viram jogadores diferentes
            var options = new InitializationOptions().SetProfile("p" + UnityEngine.Random.Range(0, 1_000_000));
            await UnityServices.InitializeAsync(options);
        }
        if (!AuthenticationService.Instance.IsSignedIn) await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    /// <summary>Só para testes (-zumbiLag ms): atrasa cada pacote enviado e recebido e perde 2% deles.</summary>
    static int SimulatedLagMs
    {
        get
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-zumbiLag");
            return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int ms) ? ms : 0;
        }
    }

    void CreateDriver(NetworkSettings settings, bool webSocket)
    {
        // Pelo Relay a ida e volta passa fácil de 200 ms (o padrão). Com pouca paciência, o canal confiável
        // reenvia pacotes que nem se perderam, a rede congestiona e o atraso só cresce.
        settings.WithReliableStageParameters(windowSize: 64, minimumResendTime: 150, maximumResendTime: 1500);
        settings.WithNetworkConfigParameters(disconnectTimeoutMS: 15000);
        int lag = SimulatedLagMs;
        if (lag > 0) settings.WithSimulatorStageParameters(maxPacketCount: 1000, mode: ApplyMode.AllPackets, packetDelayMs: lag, packetDropPercentage: 2);
#if UNITY_WEBGL && !UNITY_EDITOR
        driver = NetworkDriver.Create(new WebSocketNetworkInterface(), settings); // o navegador só tem WebSocket
#else
        driver = webSocket ? NetworkDriver.Create(new WebSocketNetworkInterface(), settings)
                           : NetworkDriver.Create(new UDPNetworkInterface(), settings);
#endif
        if (lag > 0)
        {
            reliable = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
            fast = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage), typeof(SimulatorPipelineStage));
            Debug.Log($"[Net] simulando {lag} ms de atraso e 2% de perda");
        }
        else
        {
            reliable = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
            fast = driver.CreatePipeline(typeof(UnreliableSequencedPipelineStage));
        }
    }

    void Listen(NetworkEndpoint endpoint)
    {
        if (driver.Bind(endpoint) != 0) throw new Exception("Não deu para abrir a porta " + endpoint.Port + " (outro jogo já está usando?)");
        if (driver.Listen() != 0) throw new Exception("Não deu para receber conexões");
    }

    int Track(NetworkConnection c)
    {
        int id = IsHost ? nextId++ : 1;
        conns.Add(c);
        ids[c] = id;
        byId[id] = c;
        pending[id] = new Queue<byte[]>();
        return id;
    }

    void Forget(NetworkConnection c)
    {
        if (!ids.TryGetValue(c, out int id)) return;
        conns.Remove(c);
        ids.Remove(c);
        byId.Remove(id);
        pending.Remove(id);
    }

    // ================================================================== loop

    /// <summary>Recebe e envia tudo o que está pendente. Chamar uma vez por quadro.</summary>
    public void Update()
    {
        if (!driver.IsCreated) return;
        driver.ScheduleUpdate().Complete();

        if (IsHost)
        {
            NetworkConnection c;
            while ((c = driver.Accept()) != default)
            {
                if (conns.Count >= MaxClients) { driver.Disconnect(c); continue; }
                Connected?.Invoke(Track(c));
            }
        }

        foreach (var c in conns.ToArray())
        {
            if (!ids.TryGetValue(c, out int id)) continue;
            NetworkEvent.Type ev;
            while ((ev = driver.PopEventForConnection(c, out var reader)) != NetworkEvent.Type.Empty)
            {
                if (ev == NetworkEvent.Type.Connect) Connected?.Invoke(id);
                else if (ev == NetworkEvent.Type.Data)
                {
                    var bytes = new byte[reader.Length];
                    reader.ReadBytes(new Span<byte>(bytes));
                    BytesReceived += bytes.Length;
                    Received?.Invoke(id, bytes);
                }
                else if (ev == NetworkEvent.Type.Disconnect)
                {
                    string reason = reader.Length > 0 ? ((Unity.Networking.Transport.Error.DisconnectReason)reader.ReadByte()).ToString() : "?";
                    Debug.Log($"[Net] conexão {id} caiu ({reason}) em t={Time.realtimeSinceStartup:0.0}s");
                    Forget(c);
                    Disconnected?.Invoke(id);
                    break;
                }
                if (!ids.ContainsKey(c)) break; // desconectado durante o Received
            }
        }

        foreach (var kv in pending)
        {
            var q = kv.Value;
            while (q.Count > 0 && TrySend(byId[kv.Key], reliable, q.Peek())) q.Dequeue();
        }
    }

    /// <summary>Envia para uma conexão. Pelo canal confiável, o que não couber agora fica na fila.</summary>
    public void Send(int id, byte[] data, bool reliableChannel)
    {
        if (!byId.TryGetValue(id, out var c)) return;
        if (data.Length > MaxMessage) Debug.LogWarning($"[Net] mensagem grande demais ({data.Length} bytes)");
        if (!reliableChannel) { TrySend(c, fast, data); return; }
        var q = pending[id];
        if (q.Count > 0 || !TrySend(c, reliable, data)) q.Enqueue(data);
    }

    /// <summary>Quantas mensagens confiáveis ainda esperam para sair (rede lenta).</summary>
    public int Backlog(int id) => pending.TryGetValue(id, out var q) ? q.Count : 0;

    bool TrySend(NetworkConnection c, NetworkPipeline pipe, byte[] data)
    {
        if (driver.BeginSend(pipe, c, out var w) != 0) return false;
        w.WriteBytes(new Span<byte>(data));
        if (w.HasFailedWrites)
        {
            driver.AbortSend(w);
            Debug.LogError($"[Net] mensagem de {data.Length} bytes não cabe no pacote; descartada");
            return true;
        }
        if (driver.EndSend(w) < 0) return false;
        BytesSent += data.Length;
        return true;
    }

    public void Kick(int id)
    {
        if (!byId.TryGetValue(id, out var c)) return;
        driver.Disconnect(c);
        Forget(c);
    }

    public void Dispose()
    {
        if (!driver.IsCreated) return;
        foreach (var c in conns) driver.Disconnect(c);
        driver.ScheduleUpdate().Complete(); // manda os avisos de desconexão
        driver.Dispose();
        conns.Clear();
        ids.Clear();
        byId.Clear();
        pending.Clear();
    }
}
