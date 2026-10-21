using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Modo demonstração / teste automático. Só liga se o executável for aberto com:
///   -zumbiDemo [-zumbiBots 4] [-zumbiDiff 0|1|2|mix] [-zumbiRounds 2] [-zumbiShots pasta]
///   [-zumbiNoItems] [-zumbiClumsy] [-zumbiWin 10] [-zumbiMap 0-5] [-zumbiBox Portal,Esteira,...]
///   [-zumbiWall] [-zumbiPauseShot] [-zumbiMute]
///   [-zumbiHostIp] (abre uma sala na rede local)   [-zumbiJoinIp 127.0.0.1] (entra nela com um jogador de teste)
///   [-zumbiHostRelay] (abre uma sala com código)     [-zumbiJoinCode ABC123] (entra pelo código)
/// Coloca bots (a IA de verdade) para jogar sozinhos e salva prints de cada fase.
/// </summary>
[DefaultExecutionOrder(-100)]
public class DemoDriver : MonoBehaviour
{
    public static bool Active { get; private set; }

    GameManager gm;
    string shotDir;
    int botCount = 4, maxRounds = 2;
    string diff = "1";
    bool clumsyLastBot, wall, pauseShot;
    bool hostIp, hostRelay, hosted, connectedOnce;
    string joinIp, joinCode;
    InputSource tester;
    float jumpHold;

    readonly HashSet<string> taken = new HashSet<string>();
    float clock, phaseClock, quitAt = -1f;
    Phase lastPhase = (Phase)(-1);
    int lastRound = -1, added;

    public static void TryAttach(GameManager gm)
    {
        var args = Environment.GetCommandLineArgs();
        if (!args.Contains("-zumbiDemo")) return;
        Active = true;
        GameManager.AllowBotsOnly = true;
        var d = gm.gameObject.AddComponent<DemoDriver>();
        d.gm = gm;
        d.shotDir = Arg(args, "-zumbiShots");
        if (int.TryParse(Arg(args, "-zumbiBots"), out int b)) d.botCount = Mathf.Clamp(b, 2, 4);
        if (int.TryParse(Arg(args, "-zumbiRounds"), out int r)) d.maxRounds = Mathf.Max(1, r);
        d.diff = Arg(args, "-zumbiDiff") ?? "1";
        BotPlayer.PlaceOutOfTheWay = args.Contains("-zumbiNoItems");
        d.clumsyLastBot = args.Contains("-zumbiClumsy");
        d.wall = args.Contains("-zumbiWall");
        d.pauseShot = args.Contains("-zumbiPauseShot");
        if (args.Contains("-zumbiMute")) AudioListener.volume = 0f;
        d.hostIp = args.Contains("-zumbiHostIp");
        d.joinIp = Arg(args, "-zumbiJoinIp");
        d.hostRelay = args.Contains("-zumbiHostRelay");
        d.joinCode = Arg(args, "-zumbiJoinCode");
        if (int.TryParse(Arg(args, "-zumbiWin"), out int w)) GameManager.WinScore = Mathf.Max(1, w);
        if (int.TryParse(Arg(args, "-zumbiMap"), out int m)) gm.SetMap(m);
        var box = Arg(args, "-zumbiBox");
        if (!string.IsNullOrEmpty(box))
        {
            GameManager.ForcedBox = new List<ItemType>();
            foreach (var name in box.Split(','))
                if (Enum.TryParse(name.Trim(), true, out ItemType t)) GameManager.ForcedBox.Add(t);
        }
        Debug.Log($"[Demo] ligado: {d.botCount} bots (dificuldade {d.diff}), {d.maxRounds} rodadas, prints em '{d.shotDir}'");
    }

    static string Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    void Shot(string name)
    {
        if (string.IsNullOrEmpty(shotDir) || !taken.Add(name)) return;
        Directory.CreateDirectory(shotDir);
        ScreenCapture.CaptureScreenshot(Path.Combine(shotDir, name + ".png"));
        Debug.Log("[Demo] print " + name);
    }

    BotDifficulty DifficultyFor(int i)
    {
        if (diff == "mix") return (BotDifficulty)new[] { 2, 1, 0, 1 }[i % 4];
        return int.TryParse(diff, out int d) ? (BotDifficulty)Mathf.Clamp(d, 0, 2) : BotDifficulty.Medio;
    }

    void Update()
    {
        if (gm == null) return;
        float dt = Time.unscaledDeltaTime;
        clock += dt;
        if (gm.Phase != lastPhase || gm.Round != lastRound)
        {
            lastPhase = gm.Phase;
            lastRound = gm.Round;
            phaseClock = 0f;
            if (gm.Phase == Phase.Run) Debug.Log($"[Demo] rodada {gm.Round} começou ({Level.Map.Name})");
            if (gm.Phase == Phase.Pick && gm.Round == 1 && wall) BuildWall();
        }
        phaseClock += dt;

        if (joinIp != null || joinCode != null) { UpdateClient(); return; }
        if ((hostIp || hostRelay) && !hosted && clock > 1f)
        {
            hosted = true;
            GameManager.AllowBotsOnly = false; // espera o jogador que vem pela rede
            if (hostRelay) NetGame.Instance.HostRelay();
            else NetGame.Instance.HostIp();
        }

        string tag = $"r{gm.Round}_";
        switch (gm.Phase)
        {
            case Phase.Lobby:
                if (clock > 0.8f) Shot("00_lobby_vazio");
                if (clock > 1.2f && added < botCount && clock > 1.2f + added * 0.3f)
                {
                    var slot = gm.AddBot(DifficultyFor(added));
                    added++;
                    if (slot != null && clumsyLastBot && added == botCount) slot.Bot.NeverJump = true;
                }
                if (added == botCount && clock > 2.6f) Shot("01_lobby_bots");
                break;
            case Phase.Pick:
                if (phaseClock > 0.6f) Shot(tag + "03_escolha");
                break;
            case Phase.Place:
                if (phaseClock > 1.2f) Shot(tag + "04_posicionar");
                break;
            case Phase.Run:
                if (phaseClock > 0.5f) Shot(tag + "05_corrida_inicio");
                if (phaseClock > 2.5f) Shot(tag + "06_corrida");
                if (phaseClock > 5f) Shot(tag + "06b_corrida");
                if (pauseShot && gm.Round == 1)
                {
                    if (phaseClock > 3f && phaseClock < 3.8f) gm.SetPaused(true);
                    if (phaseClock > 3.4f) Shot("06p_pausa");
                    if (phaseClock > 3.8f && Time.timeScale == 0f) gm.SetPaused(false);
                }
                break;
            case Phase.RoundEnd:
                if (phaseClock > 0.4f) Shot(tag + "07_fim_rodada");
                break;
            case Phase.Score:
                if (phaseClock > 2.0f) Shot(tag + "08_placar");
                if (phaseClock > 2.4f)
                {
                    if (gm.Round >= maxRounds) { if (quitAt < 0f) quitAt = clock + 0.6f; }
                    else foreach (var p in gm.Players) p.Input.BotConfirmFrame = Time.frameCount;
                }
                break;
            case Phase.Victory:
                if (phaseClock > 2.5f) Shot("09_vitoria");
                if (phaseClock > 3f && quitAt < 0f) quitAt = clock + 0.6f;
                break;
        }

        if (clock > 400f && quitAt < 0f) quitAt = clock;
        if (quitAt >= 0f && clock >= quitAt) Quit();
    }

    /// <summary>
    /// -zumbiJoinIp: entra na sala e joga com um dispositivo de mentira (aperta CONFIRMAR para entrar e escolher,
    /// corre para a direita pulando), para testar o caminho todo do online. Sai quando o host fecha.
    /// </summary>
    void UpdateClient()
    {
        var net = NetGame.Instance;
        if (net.Mode != NetMode.Client)
        {
            if (tester == null && clock > 1.5f)
            {
                tester = NetGame.TestDevice = new InputSource { Kind = DeviceKind.Bot };
                if (joinCode != null) net.JoinRelay(joinCode);
                else net.JoinIp(joinIp);
            }
            if (net.Mode == NetMode.Offline && tester != null && (connectedOnce || clock > 30f) && quitAt < 0f)
            {
                Debug.Log(connectedOnce ? "[Demo] host fechou a sala" : "[Demo] não conectou");
                quitAt = clock + 0.5f;
            }
            if (quitAt >= 0f && clock >= quitAt) Quit();
            return;
        }
        if (!connectedOnce) Debug.Log("[Demo] cliente conectado");
        connectedOnce = true;

        string tag = $"cli_r{gm.Round}_";
        float t = phaseClock;
        bool every(float period) => Mathf.Repeat(t, period) < dt();
        float dt() => Time.unscaledDeltaTime;
        tester.BotMove = Vector2.zero;
        jumpHold -= dt();
        tester.BotJumpHeld = jumpHold > 0f;
        switch (gm.Phase)
        {
            case Phase.Lobby:
                if (t > 3f) Shot("cli_01_lobby");
                if (every(1.2f)) tester.BotConfirmFrame = Time.frameCount; // entra e fica pronto
                break;
            case Phase.Pick:
                if (t > 0.6f) Shot(tag + "03_escolha");
                // sobe até a fileira de itens e varre de um lado para o outro apertando CONFIRMAR
                if (t < 0.4f) tester.BotMove = Vector2.up;
                else tester.BotMove = new Vector2(Mathf.Sign(Mathf.Sin((t - 0.4f) * 1.6f)) * 0.5f, 0f);
                if (t > 0.4f && every(0.15f)) tester.BotConfirmFrame = Time.frameCount;
                break;
            case Phase.Place:
                if (t > 1.2f) Shot(tag + "04_posicionar");
                tester.BotMove = new Vector2(Mathf.Sin(t * 1.3f), Mathf.Cos(t * 0.9f)) * 0.6f;
                if (every(0.6f)) tester.BotConfirmFrame = Time.frameCount;
                break;
            case Phase.Run:
                if (t > 0.5f) Shot(tag + "05_corrida_inicio");
                if (t > 2.5f) Shot(tag + "06_corrida");
                tester.BotMove = Vector2.right;
                if (every(0.9f)) { tester.BotJumpFrame = Time.frameCount; jumpHold = 0.35f; tester.BotJumpHeld = true; }
                break;
            case Phase.RoundEnd:
                if (t > 0.4f) Shot(tag + "07_fim_rodada");
                break;
            case Phase.Score:
                if (t > 2f) Shot(tag + "08_placar");
                break;
            case Phase.Victory:
                if (t > 2.5f) Shot("cli_09_vitoria");
                break;
        }
    }

    /// <summary>-zumbiWall: paredão de pedra na frente da chegada (fase impossível, para testar bots empacados).</summary>
    static void BuildWall()
    {
        int x = Level.GoalCell.x - 4, placed = 0;
        for (int y = 0; y < Level.H - 2; y += 2)
        {
            var item = ItemFactory.Create(ItemType.Pedra);
            var pivot = new Vector2Int(x, y);
            if (!item.CanPlaceAt(pivot)) { Destroy(item.gameObject); continue; }
            item.SnapTo(pivot);
            item.Place();
            placed++;
        }
        Physics2D.SyncTransforms();
        Debug.Log($"[Demo] paredão impossível na coluna {x} ({placed} pedras)");
    }

    void Quit()
    {
        Debug.Log("[Demo] fim");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
