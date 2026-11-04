using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>
/// Testes automáticos [-zumbiShots pasta]: -zumbiCreatorTest passa por todas as telas do criador de fases e
/// -zumbiLimbTest leva um zumbi às armadilhas (perder partes e morrer, e no modo UMA VIDA morrer de uma vez). Conferem cada passo no log
/// ("[Teste] OK"/"[Teste] FALHOU") e fecham. -zumbiMapFile fase.txt deixa a fase escolhida no lobby.
/// </summary>
public partial class GameManager
{
    string testShots;
    int testFails, testChecks;

    void TryStartCreatorTest()
    {
        var args = Environment.GetCommandLineArgs();
        string Arg(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        string mapFile = Arg("-zumbiMapFile");
        if (mapFile != null)
        {
            MapCode.TestMode = true;
            if (MapCode.TryParse(File.ReadAllText(mapFile), out var map, out int ms))
            {
                MapCode.AddReceived(map, ms);
                BackToLobby(map);
                Debug.Log($"[Teste] fase do arquivo escolhida no lobby: {map.Name} (mapa {MapIndex + 1}/{lobbyMaps.Count + 1})");
            }
            else Debug.LogError("[Teste] o arquivo não tem uma fase: " + mapFile);
        }

        testShots = Arg("-zumbiShots");
        if (args.Contains("-zumbiLimbTest")) { MapCode.TestMode = true; StartCoroutine(LimbTest()); return; }
        if (!args.Contains("-zumbiCreatorTest")) return;
        MapCode.TestMode = true;
        MapCode.ClearTest();
        StartCoroutine(CreatorTest());
    }

    /// <summary>Põe o zumbi do desafio com os pés em (x, y) do mundo.</summary>
    void TestPlaceZombie(Vector2 feet)
    {
        challengeZombie.transform.position = feet;
        challengeZombie.GetComponent<Rigidbody2D>().position = feet;
        challengeZombie.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        Physics2D.SyncTransforms();
    }

    IEnumerator LimbTest()
    {
        yield return new WaitForSeconds(0.5f);
        var map = MapCode.NewMap("Teste das partes");
        MapCode.Set(map, 12, 3, '^');
        MapCode.Set(map, 13, 3, '^');
        MapCode.Set(map, 30, 5, 'X');
        for (int x = 36; x <= 38; x++) MapCode.Set(map, x, 2, 'a');
        var bot = new InputSource { Kind = DeviceKind.Bot };
        challengeInput = bot;
        StartChallenge(map, 0, ChallengeReturn.Received);
        yield return TestShot("p01_inteiro");

        // ---- andando para os espinhos: cada golpe arranca uma parte sorteada; sem nada para arrancar, morre
        var z = challengeZombie;
        bot.BotMove = Vector2.right;
        float start = Time.time, firstLoss = -1f;
        var lost = new System.Collections.Generic.List<Limb>();
        Limb seen = 0;
        bool onePerHit = true, invertedOk = false;
        float until = Time.time + 40f, lastChange = Time.time;
        while (challengeDeaths == 0 && Time.time < until)
        {
            // parado sem levar golpe: registra onde está e a distância até as pontas do espinho
            if (Time.time - lastChange > 2f && z != null)
            {
                lastChange = Time.time;
                var body = z.GetComponent<CapsuleCollider2D>();
                foreach (var hz in FindObjectsByType<Hazard>(FindObjectsSortMode.None))
                {
                    var col = hz.GetComponent<Collider2D>();
                    if (col != null && Vector2.Distance(col.bounds.center, z.Position) < 2f)
                        Debug.Log($"[Teste] parado em {z.Position.x:0.000} (vel {z.Velocity.x:0.00}), distância até {hz.name}: {Physics2D.Distance(body, col).distance:0.000}");
                }
            }

            if (z != null && z.Missing != seen)
            {
                lastChange = Time.time;
                int added = 0;
                foreach (Limb l in Enum.GetValues(typeof(Limb)))
                    if ((z.Missing & l) != 0 && (seen & l) == 0) { lost.Add(l); added++; }
                onePerHit &= added == 1;
                seen = z.Missing;
                if (firstLoss < 0f) firstLoss = Time.time - start;
                Debug.Log($"[Teste] perdeu: {string.Join(", ", lost)}");
                if (lost[lost.Count - 1] == Limb.Head)
                {
                    // sem cabeça: jogador fica com os controles trocados, bot não
                    bool botOk = !z.ControlsInverted;
                    z.Controls = new InputSource { Kind = DeviceKind.Keyboard1 };
                    invertedOk = botOk && z.ControlsInverted;
                    z.Controls = bot;
                }
                // depois de 1 s o zumbi já caiu e parou de piscar
                yield return new WaitForSeconds(0.8f);
                yield return TestShot($"p0{1 + lost.Count}_sem_{lost.Count}");
            }
            yield return null;
        }
        Check(firstLoss >= 0f && firstLoss < 4f, $"andando de lado contra o espinho perde uma parte logo ({firstLoss:0.0} s)");
        Check(onePerHit && lost.Count == 5 && seen == (Limb.BackArm | Limb.FrontArm | Limb.BackLeg | Limb.FrontLeg | Limb.Head),
              "cada golpe arranca uma parte, até as 5 (ordem sorteada: " + string.Join(", ", lost) + ")");
        Check(invertedOk, "sem cabeça o jogador fica com esquerda e direita trocadas, o bot não");
        Check(challengeDeaths == 1, "sem nada para arrancar, o próximo golpe mata");
        bot.BotMove = Vector2.zero;
        yield return new WaitForSeconds(1.2f);
        Check(challengeZombie != null && challengeZombie != z && challengeZombie.Missing == 0, "renasce com o corpo inteiro");

        // ---- serra: arranca uma parte e o zumbi continua
        var z2 = challengeZombie;
        yield return new WaitForFixedUpdate();
        TestPlaceZombie(new Vector2(Level.Origin.x + 30.5f, Level.Origin.y + 4.3f));
        yield return new WaitForSeconds(0.3f);
        int bits = 0;
        foreach (Limb l in Enum.GetValues(typeof(Limb))) if ((z2.Missing & l) != 0) bits++;
        Check(challengeDeaths == 1 && z2.State == ZombieState.Alive && bits == 1, $"serra arranca uma parte e o zumbi continua ({z2.Missing})");
        yield return new WaitForSeconds(1.5f);
        yield return TestShot("p07_depois_da_serra");

        // ---- ácido: afunda e morre, sem perder mais parte
        var before = z2.Missing;
        yield return new WaitForFixedUpdate();
        TestPlaceZombie(new Vector2(Level.Origin.x + 37.5f, Level.Origin.y + 3.5f));
        until = Time.time + 2f;
        while (challengeDeaths < 2 && Time.time < until) yield return null;
        Check(challengeDeaths == 2 && z2.State == ZombieState.Dead && z2.Missing == before, "ácido mata direto");

        // ---- modo UMA VIDA (desafio recomeçado, sem depender dos passos de cima): a serra mata na hora, sem arrancar parte
        yield return new WaitForSeconds(1.2f);
        RestartChallenge();
        var z3 = challengeZombie;
        z3.OneLife = true;
        yield return new WaitForFixedUpdate();
        TestPlaceZombie(new Vector2(Level.Origin.x + 30.5f, Level.Origin.y + 4.3f));
        yield return new WaitForSeconds(0.3f);
        Check(challengeDeaths == 1 && z3.State == ZombieState.Dead && z3.Missing == 0, "UMA VIDA: a serra mata na hora, sem arrancar parte");

        Debug.Log($"[Teste] FIM: {testChecks - testFails} de {testChecks} passaram");
        Application.Quit(testFails == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        testChecks++;
        if (!ok) testFails++;
        Debug.Log((ok ? "[Teste] OK " : "[Teste] FALHOU ") + what);
    }

    IEnumerator TestShot(string name)
    {
        yield return new WaitForSeconds(0.25f);
        if (string.IsNullOrEmpty(testShots)) yield break;
        Directory.CreateDirectory(testShots);
        ScreenCapture.CaptureScreenshot(Path.Combine(testShots, name + ".png"));
        yield return null;
        yield return null;
    }

    /// <summary>Clique do mouse num item da tela atual (tratado no Update seguinte).</summary>
    IEnumerator TestClick(int item)
    {
        clickedItem = item;
        yield return null;
        yield return null;
    }

    int PieceIndex(char ch, ItemType item = ItemType.Caixa) =>
        Array.FindIndex(Pieces, p => ch == ItemPiece ? p.IsItem && p.Item == item : !p.IsItem && p.Ch == ch);

    /// <summary>Um traço com a peça: aperta na primeira célula e arrasta pelas outras.</summary>
    void TestStroke(int pieceIndex, params Vector2Int[] cells)
    {
        piece = pieceIndex;
        undoPushed = false;
        for (int i = 0; i < cells.Length; i++) Paint(cells[i], i == 0);
        undoPushed = false;
    }

    /// <summary>Abre o menu do editor e espera a conferência do caminho dos bots.</summary>
    IEnumerator TestPath()
    {
        yield return null;
        OpenEditMenu();
        float until = Time.time + 3f;
        while (pathState == PathState.Unknown && Time.time < until) yield return null;
        creatorScreen = CreatorScreen.Edit;
    }

    IEnumerator TestReachGoal()
    {
        yield return new WaitForSeconds(0.6f);
        if (challengeZombie == null) yield break;
        Vector2 p = Level.GoalPosition + Vector2.down * 0.5f;
        challengeZombie.transform.position = p;
        challengeZombie.GetComponent<Rigidbody2D>().position = p;
        float until = Time.time + 2f;
        while (!challengeDone && Time.time < until) yield return null;
    }

    IEnumerator CreatorTest()
    {
        yield return new WaitForSeconds(0.5f);

        // ---- lista de fases
        OpenCreator();
        yield return TestShot("01_lista");
        Check(Phase == Phase.Editor && creatorScreen == CreatorScreen.Slots && slotMaps.All(m => m == null), "criador abre na lista com as 6 fases vazias");

        // ---- editor: blocos, largadas, itens, serra móvel, desfazer
        yield return TestClick(0);
        Check(creatorScreen == CreatorScreen.Edit && editMap != null && editMap.Name == "Fase 1", "abrir a FASE 1 cria uma fase nova");

        // ---- aviso de caminho: fase lisa tem, paredão até o teto não tem
        yield return TestPath();
        Check(pathState == PathState.Ok, "fase nova: os bots acham caminho até o cérebro");
        TestStroke(PieceIndex('S'), Enumerable.Range(3, Level.H - 3).Select(y => new Vector2Int(30, y)).ToArray());
        yield return TestPath();
        Check(pathState == PathState.None, "paredão até o teto: aviso de que não tem caminho");
        Undo();
        Check(Enumerable.Range(3, Level.H - 3).All(y => editMap.At(30, y) == '.'), "desfazer tira o paredão inteiro (um traço só)");

        TestStroke(PieceIndex('#'), Enumerable.Range(10, 5).Select(x => new Vector2Int(x, 6)).ToArray());
        Check(Enumerable.Range(10, 5).All(x => editMap.At(x, 6) == '#'), "pintar arrastando faz uma plataforma de terra");
        TestStroke(PieceIndex('='), Enumerable.Range(14, 9).Select(x => new Vector2Int(x, 12)).ToArray());
        MapCode.BuildTrains(editMap);
        Check(editMap.Trains.Length == 1, "trilho de 9 células ganha um trem");

        TestStroke(PieceIndex('1'), new Vector2Int(4, 3));
        TestStroke(PieceIndex('2'), new Vector2Int(6, 3));
        string all = string.Concat(editMap.Rows);
        Check(editMap.At(4, 3) == '1' && editMap.At(2, 3) == '.' && all.Count(c => c == '1') == 1 && editMap.At(6, 3) == '2', "largada 1 muda de lugar e a largada 2 aparece");
        TestStroke(PieceIndex('.'), new Vector2Int(4, 3));
        Check(editMap.At(4, 3) == '1', "apagar não tira a largada 1");

        TestStroke(PieceIndex(ItemPiece, ItemType.Mola), new Vector2Int(16, 3));
        TestStroke(PieceIndex(ItemPiece, ItemType.Portal), new Vector2Int(20, 3));
        Check(editMap.Items.Length == 1 && pendingPortal != null, "primeiro portal espera o segundo");
        TestStroke(PieceIndex(ItemPiece, ItemType.Portal), new Vector2Int(34, 3));
        TestStroke(PieceIndex(ItemPiece, ItemType.Gangorra), new Vector2Int(28, 4));
        piece = PieceIndex(ItemPiece, ItemType.PlataformaMovel);
        itemRot = 1;
        TestStroke(piece, new Vector2Int(38, 4));
        Check(editMap.Items.Length == 5 && editMap.Items.Count(i => i.Type == ItemType.Portal) == 2 && editMap.Items.Last().Rotation == 1,
              "mola, par de portais, gangorra e plataforma girada entram na fase");
        int before = editMap.Items.Length;
        TestStroke(PieceIndex(ItemPiece, ItemType.Caixa), new Vector2Int(5, 1));
        TestStroke(PieceIndex(ItemPiece, ItemType.Caixa), new Vector2Int(16, 3));
        Check(editMap.Items.Length == before, "item não entra em cima da pedra nem de outro item");

        TestStroke(PieceIndex(MovingSawPiece), new Vector2Int(12, 10));
        TestStroke(PieceIndex(MovingSawPiece), new Vector2Int(18, 11));
        Check(editMap.Saws.Length == 1 && editMap.Saws[0].To == new Vector2Int(18, 10), "serra móvel em dois toques, presa na mesma linha");
        Undo();
        Check(editMap.Saws.Length == 0 && editMap.Items.Length == before, "desfazer tira só a serra");
        TestStroke(PieceIndex(MovingSawPiece), new Vector2Int(12, 10));
        TestStroke(PieceIndex(MovingSawPiece), new Vector2Int(18, 11));

        TestStroke(PieceIndex('.'), new Vector2Int(20, 4));
        Check(editMap.Items.Count(i => i.Type == ItemType.Portal) == 0, "apagar um portal leva o par junto");
        Undo();
        Check(editMap.Items.Count(i => i.Type == ItemType.Portal) == 2, "desfazer traz os dois portais de volta");

        yield return new WaitForSeconds(0.5f);
        Check(Level.Map == editMap && ItemInstance.AllPlaced.Count == editMap.Items.Length, $"a fase montada tem os {editMap.Items.Length} itens");
        Check(MapCode.LoadSlot(0, out _) != null, "a fase já está salva");
        yield return TestShot("02_editor");

        // ---- menu: código bloqueado antes de testar
        yield return TestPath();
        Debug.Log("[Teste] caminho dos bots com mola, portais, gangorra e plataforma no chão: " + pathState);
        OpenEditMenu();
        string clipBefore = Share.Paste();
        yield return TestClick(2);
        Check(Share.Paste() == clipBefore && editTimeMs == 0, "copiar código antes de testar não deixa");
        yield return TestShot("03_menu");

        // ---- testar: morrer, renascer, chegar
        yield return TestClick(1);
        Check(Phase == Phase.Challenge && ChallengeTesting, "TESTAR começa a corrida");
        yield return new WaitForSeconds(0.5f);
        yield return TestShot("04_testando");
        challengeZombie.Die(-1);
        yield return new WaitForSeconds(1.2f);
        Check(challengeDeaths == 1 && challengeZombie != null && challengeZombie.State == ZombieState.Alive, "morreu e voltou para a largada");
        yield return TestReachGoal();
        Check(challengeDone && challengePassed && editTimeMs > 0 && MapCode.Record(editMap) > 0, $"chegou no cérebro: fase liberada com {Secs(editTimeMs)} e recorde salvo");
        yield return new WaitForSeconds(0.7f);
        yield return TestShot("05_liberada");

        // ---- desistir
        yield return TestClick(0);
        Check(Phase == Phase.Challenge && !challengeDone, "tentar de novo recomeça");
        yield return new WaitForSeconds(0.5f);
        EndChallenge(false);
        Check(challengeDone && !challengePassed, "desistir conta como não passou");
        yield return TestShot("06_nao_passou");
        yield return new WaitForSeconds(0.9f);
        yield return TestClick(1);
        Check(Phase == Phase.Editor && creatorScreen == CreatorScreen.Edit, "sair do teste volta a editar");

        // ---- copiar e exportar
        OpenEditMenu();
        yield return TestClick(2);
        string code = Share.Paste();
        Check(MapCode.TryParse(code, out var copied, out int copiedMs) && MapCode.Hash(copied) == MapCode.Hash(editMap) && copiedMs == editTimeMs,
              $"código copiado volta igual ({code.Length} caracteres)");
        yield return TestClick(3);
        string file = Path.Combine(MapCode.Folder, MapCode.FileName(editMap));
        Check(File.Exists(file) && MapCode.TryParse(File.ReadAllText(file), out var exported, out _) && MapCode.Hash(exported) == MapCode.Hash(editMap), "arquivo exportado tem a fase");
        int myTime = editTimeMs;
        yield return TestClick(7);
        Check(creatorScreen == CreatorScreen.Slots && slotMaps[0] != null && slotTimes[0] == myTime, "salvar e sair: a FASE 1 aparece na lista com o tempo");
        yield return TestShot("07_lista_com_fase");

        // ---- amigo cola o código
        yield return TestClick(MapCode.Slots + 1);
        Check(Phase == Phase.Challenge && !ChallengeTesting && challengeTargetMs == myTime && received.Count == 1, "colar o código começa o desafio e guarda nas recebidas");
        yield return TestShot("08_desafio_amigo");
        yield return TestReachGoal();
        Check(challengeDone && challengePassed, "o amigo chega no cérebro");
        yield return TestShot("09_resultado_amigo");
        yield return new WaitForSeconds(0.9f);
        yield return TestClick(1);
        Check(creatorScreen == CreatorScreen.Received, "sair do desafio volta para as recebidas");
        yield return TestShot("10_recebidas");

        // ---- importar um arquivo de outra fase
        var other = MapCode.NewMap("Fase do Amigo");
        for (int x = 15; x < 30; x++) MapCode.Set(other, x, 2, 'a');
        File.WriteAllText(Path.Combine(MapCode.Folder, "amigo.txt"), MapCode.ToText(other, 7777));
        creatorScreen = CreatorScreen.Slots;
        yield return TestClick(MapCode.Slots + 2);
        Check(creatorScreen == CreatorScreen.Received && received.Count == 2 && received[0].Map.Name == "Fase do Amigo" && received[0].TimeMs == 7777,
              "importar lê o arquivo do amigo e ignora a própria fase exportada");

        // ---- recebida: jogar em grupo no lobby
        creatorSel = 1;
        yield return TestClick(1);
        Check(creatorScreen == CreatorScreen.ReceivedMenu && receivedSel == 1, "abrir uma recebida mostra as opções");
        yield return TestShot("11_opcoes_recebida");
        var groupMap = received[1].Map;
        yield return TestClick(1);
        Check(Phase == Phase.Lobby && MapIndex >= Maps.All.Length && MapCode.Hash(lobbyMaps[MapIndex]) == MapCode.Hash(groupMap), "jogar em grupo deixa a fase escolhida no lobby");
        Check(lobbyMaps.Count == Maps.All.Length + 2, $"o lobby tem os {Maps.All.Length} mapas do jogo e as 2 fases (mesma fase não repete)");
        yield return TestShot("12_lobby");

        AllowBotsOnly = true;
        for (int i = 0; i < 3; i++) AddBot(BotDifficulty.Medio);
        StartMatch();
        Check(Level.Map != null && MapCode.Hash(Level.Map) == MapCode.Hash(groupMap) && ItemInstance.AllPlaced.Count >= groupMap.Items.Length, "a partida em grupo usa a fase criada, com os itens");
        float wait = Time.time + 60f;
        while (Phase != Phase.Run && Time.time < wait) yield return null;
        Check(Phase == Phase.Run, "os bots escolhem e colocam itens e a corrida começa");
        yield return new WaitForSeconds(3f);
        yield return TestShot("13_partida");
        wait = Time.time + 90f;
        while (Phase != Phase.Score && Time.time < wait) yield return null;
        Check(Phase == Phase.Score, "a rodada termina no placar");
        yield return TestShot("14_placar");

        Debug.Log($"[Teste] FIM: {testChecks - testFails} de {testChecks} passaram");
        MapCode.ClearTest();
        Application.Quit(testFails == 0 ? 0 : 1);
    }
}
