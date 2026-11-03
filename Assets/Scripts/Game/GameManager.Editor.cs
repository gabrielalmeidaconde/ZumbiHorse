using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

/// <summary>Criador de fases (montar, salvar, compartilhar e importar) e o desafio solo contra o relógio. Só no jogo offline.</summary>
public partial class GameManager
{
    enum CreatorScreen { Slots, Received, ReceivedMenu, Edit, Menu }
    enum ChallengeReturn { Editor, Received }
    enum PathState { Unknown, Ok, None }

    struct Piece
    {
        public char Ch;
        public ItemType Item;
        public string Name;
        public bool IsItem => Ch == ItemPiece;
    }

    const char MovingSawPiece = '*', ItemPiece = '@';
    static readonly Piece[] Pieces = BuildPieces();
    static readonly int BlockPieces = Pieces.Count(p => !p.IsItem);

    static Piece[] BuildPieces()
    {
        var list = new List<Piece>();
        foreach (var (ch, name) in new (char, string)[]
        {
            ('.', "Apagar"), ('#', "Terra"), ('S', "Pedra"), ('i', "Gelo"), ('=', "Trilho"), ('a', "Ácido"), ('^', "Espinhos"), ('X', "Serra"),
            (MovingSawPiece, "Serra móvel"), ('1', "Largada 1"), ('2', "Largada 2"), ('3', "Largada 3"), ('4', "Largada 4"),
            ('G', "Cérebro"), ('t', "Lápide"), ('T', "Árvore"),
        })
            list.Add(new Piece { Ch = ch, Name = name });
        foreach (var d in ItemDefs.All)
            if (MapCode.CanBeMapItem(d.Type)) list.Add(new Piece { Ch = ItemPiece, Item = d.Type, Name = d.Name });
        return list.ToArray();
    }

    /// <summary>Largadas e cérebro: só existe um de cada, colocar em outro lugar muda ele de lugar.</summary>
    static bool IsMarker(char ch) => ch == 'G' || (ch >= '1' && ch <= '4');
    /// <summary>A largada 1 e o cérebro nunca somem (sem eles não dá para jogar).</summary>
    static bool IsRequired(char ch) => ch == '1' || ch == 'G';

    const float EditTopH = 64f, EditBottomH = 178f;   // barras do editor, em pixels da tela de 1080
    const int SlotsItems = MapCode.Slots + 4, MenuItemCount = 8, ReceivedMenuCount = 6, ReceivedVisible = 8, MaxUndo = 60;

    CreatorScreen creatorScreen;
    int creatorSel;
    int clickedItem = -1;        // item clicado com o mouse no OnGUI, tratado no Update seguinte
    bool creatorClicked, mouseMoved;
    bool creatorFromMenu;        // aberto pelo menu principal: sair volta para ele (senão, para o lobby)
    Vector2 lastMouse;
    readonly MapDef[] slotMaps = new MapDef[MapCode.Slots];
    readonly int[] slotTimes = new int[MapCode.Slots];
    List<MapCode.Received> received = new List<MapCode.Received>();
    int receivedSel, receivedScroll;

    int editSlot;
    MapDef editMap;
    int editTimeMs;              // tempo de quem criou desde a última mudança (0 = ainda não passou na fase)
    Vector2Int editCell, lastPainted = NoCell, moveDir;
    static readonly Vector2Int NoCell = new Vector2Int(-1, -1);
    int piece = 1, itemRot;
    Vector2Int? sawStart;
    MapItemDef pendingPortal;    // primeiro portal do par, esperando o segundo
    readonly List<string> undo = new List<string>();
    bool undoPushed;             // já guardou a fase de antes deste traço
    bool levelDirty;
    int rebuiltFrame;
    PathState pathState;
    float rebuildTimer, moveRepeat, armedAt = -10f;
    string toast = "";
    float toastTime = -10f;

    MapDef challengeMap;
    int challengeTargetMs, challengeBestMs, challengeLastMs, challengeDeaths, challengeRecordMs;
    bool challengeDone, challengePassed, challengeNewRecord;
    ChallengeReturn challengeReturn;
    float challengeTime, challengeDoneAt, respawnTimer;
    Zombie challengeZombie;
    InputSource challengeInput;

    bool ChallengeTesting => challengeReturn == ChallengeReturn.Editor;
    /// <summary>Editando ou no menu do editor: a câmera abre espaço para as barras de cima e de baixo.</summary>
    bool EditorCamera => Phase == Phase.Editor && (creatorScreen == CreatorScreen.Edit || creatorScreen == CreatorScreen.Menu);
    static bool CanCreate => NetGame.Instance == null || NetGame.Instance.Mode == NetMode.Offline;
    Piece CurrentPiece => Pieces[piece];

    void Toast(string s)
    {
        toast = s;
        toastTime = Time.time;
    }

    /// <summary>Ações perigosas (apagar) pedem o segundo toque em até 2 segundos.</summary>
    bool Armed(string warning)
    {
        if (Time.time - armedAt < 2f) { armedAt = -10f; return true; }
        armedAt = Time.time;
        Toast(warning);
        Sfx.Play("error");
        return false;
    }

    static string Secs(int ms) => (ms / 1000f).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',') + " s";

    // ================================================================== telas de lista

    void OpenCreator()
    {
        creatorFromMenu = Phase == Phase.Menu;
        lobbyCountdown = -1f;
        LoadSlots();
        creatorScreen = CreatorScreen.Slots;
        creatorSel = 0;
        SetPhase(Phase.Editor);
        Sfx.Play("pick");
    }

    void LoadSlots()
    {
        for (int i = 0; i < MapCode.Slots; i++) slotMaps[i] = MapCode.LoadSlot(i, out slotTimes[i]);
        received = MapCode.LoadReceived();
    }

    void UpdateCreator()
    {
        Vector2 mp = InputSource.MousePosition;
        mouseMoved = (mp - lastMouse).sqrMagnitude > 1f;
        lastMouse = mp;

        switch (creatorScreen)
        {
            case CreatorScreen.Slots: UpdateSlots(); break;
            case CreatorScreen.Received: UpdateReceived(); break;
            case CreatorScreen.ReceivedMenu: UpdateReceivedMenu(); break;
            case CreatorScreen.Edit: UpdateEdit(); break;
            case CreatorScreen.Menu: UpdateEditMenu(); break;
        }
        clickedItem = -1;
        RebuildIfDirty();
    }

    /// <summary>Cima/baixo de qualquer dispositivo anda na lista. Devolve true se alguém confirmou (ou clicou num item).</summary>
    bool ListInput(int count, out bool back, out int side)
    {
        int dy = 0;
        bool confirm = false;
        back = false;
        side = 0;
        foreach (var d in ActiveDevices())
        {
            dy += d.MoveYPressed();
            side += d.MoveXPressed();
            if (d.ConfirmDown) { confirm = true; challengeInput = d; }
            back |= d.BackDown || d.PauseDown;
        }
        if (dy != 0)
        {
            creatorSel = (creatorSel - System.Math.Sign(dy) + count) % count;
            Sfx.Play("tick");
        }
        if (clickedItem >= 0) { creatorSel = clickedItem; confirm = true; }
        return confirm;
    }

    void UpdateSlots()
    {
        bool confirm = ListInput(SlotsItems, out bool back, out _);
        if (back) { LeaveCreator(); return; }
        if (!confirm) return;
        switch (creatorSel - MapCode.Slots)
        {
            case 0: OpenReceived(0); break;
            case 1: PasteMap(); break;
            case 2: ImportFile(); break;
            case 3: LeaveCreator(); break;
            default: OpenSlot(creatorSel); break;
        }
    }

    void LeaveCreator()
    {
        if (creatorFromMenu) BackToMenu();
        else { BackToLobby(); Sfx.Play("tick"); }
    }

    void OpenSlot(int slot)
    {
        editSlot = slot;
        editMap = slotMaps[slot] ?? MapCode.NewMap($"Fase {slot + 1}");
        editTimeMs = slotMaps[slot] != null ? slotTimes[slot] : 0;
        editCell = new Vector2Int(Level.W / 2, Level.H / 2);
        sawStart = null;
        pendingPortal = null;
        undo.Clear();
        creatorScreen = CreatorScreen.Edit;
        levelDirty = true;
        rebuildTimer = 0f;
        pathState = PathState.Unknown;
        Sfx.Play("pick");
    }

    void PasteMap()
    {
        if (!MapCode.TryParse(Share.Paste(), out var map, out int ms))
        {
            Toast("Não achei uma fase no que foi colado. Copie o código inteiro (começa com ZH1-) e tente de novo.");
            Sfx.Play("error");
            return;
        }
        MapCode.AddReceived(map, ms);
        received = MapCode.LoadReceived();
        receivedSel = 0;
        StartChallenge(map, ms, ChallengeReturn.Received);
    }

    void ImportFile()
    {
        if (Share.Browser)
        {
            Share.PickFile(gameObject.name, nameof(OnFileImported));
            Toast("Escolha o arquivo .txt da fase");
            return;
        }
        List<MapCode.Received> found;
        try { found = MapCode.ImportFolder(); }
        catch (System.Exception e) { Toast("Não deu para ler a pasta: " + e.Message); Sfx.Play("error"); return; }
        // as fases exportadas daqui também ficam na pasta: não importa as próprias
        var mine = new HashSet<uint>(slotMaps.Where(m => m != null).Select(MapCode.Hash));
        found.RemoveAll(f => mine.Contains(MapCode.Hash(f.Map)));
        if (found.Count == 0)
        {
            try { System.IO.Directory.CreateDirectory(MapCode.Folder); } catch (System.Exception) { }
            Application.OpenURL("file:///" + MapCode.Folder.Replace('\\', '/'));
            Toast("Coloque os arquivos .txt das fases nesta pasta e importe de novo: " + MapCode.Folder);
            Sfx.Play("error");
            return;
        }
        foreach (var f in found) MapCode.AddReceived(f.Map, f.TimeMs);
        Toast(found.Count == 1 ? "1 fase importada" : $"{found.Count} fases importadas");
        Sfx.Play("goal");
        OpenReceived(0);
    }

    /// <summary>Texto do arquivo escolhido no navegador (chega pelo Share.PickFile).</summary>
    public void OnFileImported(string text)
    {
        if (!MapCode.TryParse(text, out var map, out int ms)) { Toast("Esse arquivo não tem uma fase do jogo"); Sfx.Play("error"); return; }
        MapCode.AddReceived(map, ms);
        Toast($"Fase importada: {map.Name}");
        Sfx.Play("goal");
        if (Phase == Phase.Editor) OpenReceived(0);
    }

    void OpenReceived(int sel)
    {
        received = MapCode.LoadReceived();
        creatorScreen = CreatorScreen.Received;
        creatorSel = Mathf.Clamp(sel, 0, received.Count);
    }

    void UpdateReceived()
    {
        bool confirm = ListInput(received.Count + 1, out bool back, out _);
        int shown = Mathf.Min(creatorSel, Mathf.Max(0, received.Count - 1));
        receivedScroll = Mathf.Clamp(Mathf.Clamp(receivedScroll, shown - ReceivedVisible + 1, shown), 0, Mathf.Max(0, received.Count - ReceivedVisible));
        if (back || (confirm && creatorSel == received.Count)) { creatorScreen = CreatorScreen.Slots; creatorSel = MapCode.Slots; Sfx.Play("tick"); return; }
        if (!confirm) return;
        receivedSel = creatorSel;
        creatorScreen = CreatorScreen.ReceivedMenu;
        creatorSel = 0;
        Sfx.Play("pick");
    }

    void UpdateReceivedMenu()
    {
        bool confirm = ListInput(ReceivedMenuCount, out bool back, out _);
        if (back || (confirm && creatorSel == 5)) { OpenReceived(receivedSel); Sfx.Play("tick"); return; }
        if (!confirm || receivedSel >= received.Count) return;
        var r = received[receivedSel];
        switch (creatorSel)
        {
            case 0: StartChallenge(r.Map, r.TimeMs, ChallengeReturn.Received); break;
            case 1: BackToLobby(r.Map); Sfx.Play("go"); break;
            case 2: CopyCode(r.Map, r.TimeMs); break;
            case 3: ExportMap(r.Map, r.TimeMs); break;
            case 4:
                if (!Armed("Aperte de novo para apagar esta fase")) break;
                MapCode.RemoveReceived(receivedSel);
                OpenReceived(receivedSel);
                Sfx.Play("bomb");
                break;
        }
    }

    void CopyCode(MapDef map, int timeMs)
    {
        Share.Copy(MapCode.Encode(map, timeMs));
        Toast("Código copiado! Cole numa conversa para desafiar os amigos.");
        Sfx.Play("goal");
    }

    void ExportMap(MapDef map, int timeMs)
    {
        if (Share.Browser)
        {
            Share.Download(MapCode.FileName(map), MapCode.ToText(map, timeMs));
            Toast("Arquivo da fase baixado: mande para os amigos importarem");
            Sfx.Play("goal");
            return;
        }
        try
        {
            Toast("Fase salva em " + MapCode.Export(map, timeMs));
            Sfx.Play("goal");
        }
        catch (System.Exception e) { Toast("Não deu para salvar o arquivo: " + e.Message); Sfx.Play("error"); }
    }

    // ================================================================== editor

    void UpdateEdit()
    {
        var devices = ActiveDevices();
        if (devices.Any(d => d.PauseDown)) { OpenEditMenu(); return; }
        if (InputSource.TestKeyDown || devices.Any(d => d.SelectDown)) { TestMap(); return; }
        if (devices.Any(d => d.UndoDown)) Undo();

        int turn = -InputSource.MouseScroll + devices.Sum(d => d.PieceStep);
        if (turn != 0) SelectPiece(piece + turn);
        if (devices.Any(d => d.TurnDown) && CurrentPiece.IsItem && ItemDefs.Get(CurrentPiece.Item).CanRotate && pendingPortal == null)
        {
            itemRot = ItemDefs.NextRotation(ItemDefs.Get(CurrentPiece.Item), itemRot, 1);
            Sfx.Play("rotate");
        }

        // teclado/controle: uma célula por toque, repetindo enquanto segura
        Vector2 mv = devices.Select(d => d.Move).FirstOrDefault(v => v.sqrMagnitude > 0.25f);
        var dir = new Vector2Int(Mathf.Abs(mv.x) > 0.5f ? (int)Mathf.Sign(mv.x) : 0, Mathf.Abs(mv.y) > 0.5f ? (int)Mathf.Sign(mv.y) : 0);
        if (dir == Vector2Int.zero) moveDir = dir;
        else if (dir != moveDir) { moveDir = dir; moveRepeat = 0.25f; MoveCursor(dir); }
        else if ((moveRepeat -= Time.deltaTime) <= 0f) { moveRepeat = 0.06f; MoveCursor(dir); }

        bool press = devices.Any(d => d.ConfirmDown), hold = devices.Any(d => d.ConfirmHeld), erase = false;

        // mouse: aponta a célula, o botão esquerdo pinta e o direito apaga
        bool mouseButton = InputSource.MouseHeld(0) || InputSource.MouseHeld(1);
        if ((mouseMoved || mouseButton) && MouseCell(out var mc))
        {
            editCell = mc;
            press |= InputSource.MouseDown(0);
            hold |= InputSource.MouseHeld(0);
            erase = InputSource.MouseHeld(1);
        }

        if (erase) { if (editCell != lastPainted) { lastPainted = editCell; PaintCell(editCell, '.', false); } }
        else if (press) { lastPainted = editCell; Paint(editCell, true); }
        else if (hold && editCell != lastPainted) { lastPainted = editCell; Paint(editCell, false); }
        if (!hold && !erase) { lastPainted = NoCell; undoPushed = false; }
    }

    void SelectPiece(int i)
    {
        piece = (i % Pieces.Length + Pieces.Length) % Pieces.Length;
        sawStart = null;
        pendingPortal = null;
        Sfx.Play("rotate");
    }

    void MoveCursor(Vector2Int dir)
    {
        editCell = new Vector2Int(Mathf.Clamp(editCell.x + dir.x, 0, Level.W - 1), Mathf.Clamp(editCell.y + dir.y, 0, Level.H - 1));
    }

    /// <summary>Célula embaixo do mouse; false se ele está fora da fase ou em cima das barras do editor.</summary>
    bool MouseCell(out Vector2Int c)
    {
        c = NoCell;
        if (!InputSource.HasMouse) return false;
        Vector2 mp = InputSource.MousePosition;
        float vy = (Screen.height - mp.y) / S;
        if (vy < EditTopH || vy > 1080f - EditBottomH) return false;
        c = Level.WorldToCell(cam.ScreenToWorldPoint(new Vector3(mp.x, mp.y, 10f)));
        return c.x >= 0 && c.x < Level.W && c.y >= 0 && c.y < Level.H;
    }

    void Paint(Vector2Int c, bool press)
    {
        var p = CurrentPiece;
        if (p.IsItem) { if (press) PlaceItem(c); return; }
        if (p.Ch != MovingSawPiece && !IsMarker(p.Ch)) { PaintCell(c, p.Ch, press); return; }
        if (!press) return;   // peças que são uma só não pintam arrastando
        if (p.Ch == MovingSawPiece) PlaceSaw(c);
        else MoveMarker(c, p.Ch);
    }

    /// <summary>Índice do item da fase que ocupa a célula, ou -1.</summary>
    int ItemAt(Vector2Int c)
    {
        for (int i = 0; i < editMap.Items.Length; i++)
            if (MapCode.ItemCells(editMap.Items[i]).Contains(c)) return i;
        return -1;
    }

    void PaintCell(Vector2Int c, char ch, bool press)
    {
        char old = editMap.At(c.x, c.y);
        int item = ItemAt(c);
        if (IsRequired(old) || (item >= 0 && ch != '.')) { if (press) Sfx.Play("error"); return; }
        bool sawHere = editMap.Saws.Any(s => s.From == c || s.To == c);
        if (old == ch && !(ch == '.' && (sawHere || item >= 0))) return;
        SaveUndo();
        if (ch == '.')
        {
            editMap.Saws = editMap.Saws.Where(s => s.From != c && s.To != c).ToArray();
            if (item >= 0) RemoveItem(item);
        }
        MapCode.Set(editMap, c.x, c.y, ch);
        Changed();
        Sfx.Play("tick", 0.35f);
    }

    /// <summary>Tira o item; um portal leva junto o par dele.</summary>
    void RemoveItem(int index)
    {
        var items = editMap.Items.ToList();
        if (items[index].Type == ItemType.Portal)
        {
            var portals = Enumerable.Range(0, items.Count).Where(i => items[i].Type == ItemType.Portal).ToList();
            int k = portals.IndexOf(index), partner = portals[k ^ 1];
            items.RemoveAt(Mathf.Max(index, partner));
            items.RemoveAt(Mathf.Min(index, partner));
        }
        else items.RemoveAt(index);
        editMap.Items = items.ToArray();
    }

    void MoveMarker(Vector2Int c, char ch)
    {
        char old = editMap.At(c.x, c.y);
        if ((IsMarker(old) && old != ch) || ItemAt(c) >= 0) { Sfx.Play("error"); return; }
        SaveUndo();
        for (int y = 0; y < Level.H; y++)
            for (int x = 0; x < Level.W; x++)
            {
                if (editMap.At(x, y) != ch) continue;
                MapCode.Set(editMap, x, y, '.');
                if (ch == 'G' && y > 0 && editMap.At(x, y - 1) == 'p') MapCode.Set(editMap, x, y - 1, '.');
            }
        MapCode.Set(editMap, c.x, c.y, ch);
        if (ch == 'G' && c.y > 0 && !IsMarker(editMap.At(c.x, c.y - 1)) && ItemAt(c + Vector2Int.down) < 0) MapCode.Set(editMap, c.x, c.y - 1, 'p');
        Changed();
        Sfx.Play("place", 0.6f);
    }

    void PlaceSaw(Vector2Int c)
    {
        if (sawStart == null)
        {
            if (editMap.Saws.Length >= MapCode.MaxSaws) { Toast($"Limite de {MapCode.MaxSaws} serras móveis"); Sfx.Play("error"); return; }
            sawStart = c;
            Sfx.Play("pick");
            return;
        }
        var from = sawStart.Value;
        var to = SawEnd(from, c);
        sawStart = null;
        if (to == from) { Sfx.Play("error"); return; }
        SaveUndo();
        editMap.Saws = editMap.Saws.Append(new SawPathDef { From = from, To = to }).ToArray();
        Changed();
        Sfx.Play("place", 0.6f);
    }

    /// <summary>A serra anda em linha reta: o fim fica na mesma linha ou coluna do começo, a que estiver mais perto do cursor.</summary>
    static Vector2Int SawEnd(Vector2Int from, Vector2Int c) =>
        Mathf.Abs(c.x - from.x) >= Mathf.Abs(c.y - from.y) ? new Vector2Int(c.x, from.y) : new Vector2Int(from.x, c.y);

    MapItemDef GhostItem(Vector2Int c)
    {
        var def = ItemDefs.Get(CurrentPiece.Item);
        int rot = pendingPortal != null ? pendingPortal.Rotation : def.CanRotate ? itemRot : 0;
        return new MapItemDef { Type = def.Type, Pivot = c, Rotation = rot };
    }

    /// <summary>O item cabe aqui: dentro da fase, só em células vazias e sem encostar em outro item.</summary>
    bool ItemFits(MapItemDef it)
    {
        foreach (var c in MapCode.ItemCells(it))
        {
            if (c.x < 0 || c.x >= Level.W || c.y < 0 || c.y >= Level.H) return false;
            char ch = editMap.At(c.x, c.y);
            if ((ch != '.' && ch != 't' && ch != 'T') || ItemAt(c) >= 0) return false;
            if (pendingPortal != null && MapCode.ItemCells(pendingPortal).Contains(c)) return false;
        }
        return true;
    }

    void PlaceItem(Vector2Int c)
    {
        var it = GhostItem(c);
        if (!ItemFits(it)) { Sfx.Play("error"); return; }
        bool portal = it.Type == ItemType.Portal;
        if (editMap.Items.Length + (portal && pendingPortal == null ? 2 : 1) > MapCode.MaxItems)
        {
            Toast($"Limite de {MapCode.MaxItems} itens na fase");
            Sfx.Play("error");
            return;
        }
        if (portal && pendingPortal == null)
        {
            pendingPortal = it;
            Toast("Agora coloque o segundo portal");
            Sfx.Play("pick");
            return;
        }
        SaveUndo();
        editMap.Items = portal ? editMap.Items.Append(pendingPortal).Append(it).ToArray() : editMap.Items.Append(it).ToArray();
        pendingPortal = null;
        Changed();
        Sfx.Play("place", 0.6f);
    }

    /// <summary>Guarda a fase de antes da mudança (uma vez por traço: pintar arrastando desfaz tudo de uma vez).</summary>
    void SaveUndo()
    {
        if (undoPushed) return;
        undo.Add(MapCode.ToText(editMap, editTimeMs));
        if (undo.Count > MaxUndo) undo.RemoveAt(0);
        undoPushed = true;
    }

    void Undo()
    {
        if (undo.Count == 0 || !MapCode.TryParse(undo[undo.Count - 1], out var map, out int ms)) { Sfx.Play("error"); return; }
        undo.RemoveAt(undo.Count - 1);
        editMap = map;
        editTimeMs = ms;
        sawStart = null;
        pendingPortal = null;
        levelDirty = true;
        pathState = PathState.Unknown;
        MapCode.SaveSlot(editSlot, editMap, editTimeMs);
        Sfx.Play("rotate");
    }

    /// <summary>Qualquer mudança na fase obriga quem criou a passar nela de novo antes de compartilhar.</summary>
    void Changed()
    {
        editTimeMs = 0;
        levelDirty = true;
        pathState = PathState.Unknown;
        MapCode.SaveSlot(editSlot, editMap, 0);
    }

    void RebuildIfDirty()
    {
        rebuildTimer -= Time.deltaTime;
        if (!levelDirty || rebuildTimer > 0f) return;
        // remontar a fase cria centenas de objetos: no máximo algumas vezes por segundo
        levelDirty = false;
        rebuildTimer = 0.12f;
        rebuiltFrame = Time.frameCount;
        MapCode.BuildTrains(editMap);
        Level.Build(editMap);
    }

    /// <summary>Os bots acham um caminho da largada até o cérebro? (Eles não usam todos os itens, então é só um aviso.)</summary>
    void CheckPath()
    {
        Physics2D.SyncTransforms();
        var nav = NavGrid.Build();
        var feet = Level.SpawnPoint(0);
        var start = nav.Nearest(feet, 3f, out var node) ? node : NavGrid.NodeAt(feet);
        pathState = nav.HasPath(start) ? PathState.Ok : PathState.None;
    }

    void TestMap()
    {
        sawStart = null;
        pendingPortal = null;
        StartChallenge(editMap, editTimeMs, ChallengeReturn.Editor);
    }

    // ================================================================== menu do editor

    void OpenEditMenu()
    {
        creatorScreen = CreatorScreen.Menu;
        creatorSel = 0;
        sawStart = null;
        pendingPortal = null;
        Sfx.Play("tick");
    }

    void UpdateEditMenu()
    {
        // os objetos da fase antiga só somem no fim do quadro em que ela foi remontada
        if (pathState == PathState.Unknown && !levelDirty && Time.frameCount > rebuiltFrame + 1) CheckPath();

        bool confirm = ListInput(MenuItemCount, out bool back, out int side);
        if (back) { creatorScreen = CreatorScreen.Edit; Sfx.Play("tick"); return; }
        if (creatorSel == 4 && side != 0) ChangeTheme(side);
        if (!confirm) return;
        switch (creatorSel)
        {
            case 0: creatorScreen = CreatorScreen.Edit; Sfx.Play("tick"); break;
            case 1: TestMap(); break;
            case 2:
            case 3:
                if (editTimeMs <= 0) { Toast("Passe na fase em TESTAR primeiro: o seu tempo vai junto"); Sfx.Play("error"); break; }
                if (creatorSel == 2) CopyCode(editMap, editTimeMs);
                else ExportMap(editMap, editTimeMs);
                break;
            case 4: ChangeTheme(1); break;
            case 5: PlayerPrefs.Save(); BackToLobby(editMap); Sfx.Play("go"); break;
            case 6:
                if (!Armed("Aperte de novo para apagar a fase inteira")) break;
                undoPushed = false;
                SaveUndo();
                undoPushed = false;
                int theme = editMap.Theme;
                editMap = MapCode.NewMap(editMap.Name);
                editMap.Theme = theme;
                Changed();
                creatorScreen = CreatorScreen.Edit;
                Sfx.Play("bomb");
                break;
            default:
                PlayerPrefs.Save();
                LoadSlots();
                creatorScreen = CreatorScreen.Slots;
                creatorSel = editSlot;
                Sfx.Play("tick");
                break;
        }
    }

    void ChangeTheme(int step)
    {
        int n = MapCode.ThemeCount;
        editMap.Theme = ((editMap.Theme + step) % n + n) % n;
        levelDirty = true;
        MapCode.SaveSlot(editSlot, editMap, editTimeMs);
        Sfx.Play("tick");
    }

    // ================================================================== desafio

    void StartChallenge(MapDef map, int targetMs, ChallengeReturn ret)
    {
        challengeMap = map;
        challengeTargetMs = targetMs;
        challengeReturn = ret;
        challengeBestMs = 0;
        RestartChallenge();
    }

    void RestartChallenge()
    {
        Zombie.ClearAll();
        Projectile.ClearAll();
        MapCode.BuildTrains(challengeMap);
        Level.Build(challengeMap);
        challengeTime = 0f;
        challengeDeaths = 0;
        challengeDone = false;
        respawnTimer = -1f;
        challengeRecordMs = MapCode.Record(challengeMap);
        if (challengeInput == null || !challengeInput.Connected || challengeInput.Remote) challengeInput = ActiveDevices().First(d => !d.Remote);
        challengeZombie = Zombie.Spawn(0, SkinOf(challengeInput), challengeInput, Level.SpawnPoint(0));
        RunActive = true;
        RunBehaviour.RunStarted();
        SetPhase(Phase.Challenge);
        Sfx.Play("go");
    }

    void UpdateChallenge()
    {
        var devices = ActiveDevices();
        if (challengeDone)
        {
            if (Time.time - challengeDoneAt < 0.8f) return;
            if (devices.Any(d => d.ConfirmDown) || clickedItem == 0) RestartChallenge();
            else if (devices.Any(d => d.BackDown) || clickedItem == 1) ExitChallenge();
            clickedItem = -1;
            return;
        }

        challengeTime += Time.deltaTime;
        // quem mexer controla o zumbi: dá para trocar de teclado para controle no meio
        foreach (var d in devices)
            if (d != challengeInput && !d.Remote && (d.JumpDown || d.Move.sqrMagnitude > 0.25f)) challengeInput = d;

        if (challengeZombie == null || challengeZombie.State == ZombieState.Dead)
        {
            // morreu: volta para a largada com a fase inteira de novo, e o relógio continua
            if (respawnTimer < 0f) { respawnTimer = 0.7f; challengeDeaths++; }
            if ((respawnTimer -= Time.deltaTime) <= 0f)
            {
                respawnTimer = -1f;
                Zombie.ClearAll();
                Projectile.ClearAll();
                RunBehaviour.RunEnded();
                RunBehaviour.RunStarted();
                challengeZombie = Zombie.Spawn(0, SkinOf(challengeInput), challengeInput, Level.SpawnPoint(0));
            }
        }
        else if (challengeZombie.State == ZombieState.Finished) EndChallenge(true);
        else challengeZombie.Controls = challengeInput;
    }

    /// <summary>Fim do desafio: chegou no cérebro ou desistiu pelo pause.</summary>
    void EndChallenge(bool passed)
    {
        challengeDone = true;
        challengePassed = passed;
        challengeNewRecord = false;
        challengeDoneAt = Time.time;
        RunActive = false;
        RunBehaviour.RunEnded();
        challengeLastMs = Mathf.Max(1, Mathf.RoundToInt(challengeTime * 1000f));
        if (!passed) { Zombie.ClearAll(); Sfx.Play("death"); return; }

        if (challengeBestMs == 0 || challengeLastMs < challengeBestMs) challengeBestMs = challengeLastMs;
        challengeNewRecord = MapCode.SaveRecord(challengeMap, challengeLastMs);
        challengeRecordMs = MapCode.Record(challengeMap);
        if (ChallengeTesting && (editTimeMs == 0 || challengeLastMs < editTimeMs))
        {
            editTimeMs = challengeLastMs;
            MapCode.SaveSlot(editSlot, editMap, editTimeMs);
            PlayerPrefs.Save();
        }
        bool beat = challengeTargetMs > 0 && challengeLastMs < challengeTargetMs;
        Sfx.Play(beat || ChallengeTesting || challengeNewRecord ? "win" : "goal");
    }

    void ExitChallenge()
    {
        Zombie.ClearAll();
        RunActive = false;
        RunBehaviour.RunEnded();
        SetPhase(Phase.Editor);
        if (ChallengeTesting)
        {
            creatorScreen = CreatorScreen.Edit;
            levelDirty = true;
            rebuildTimer = 0f;
            pathState = PathState.Unknown;
        }
        else
        {
            LoadSlots();
            OpenReceived(receivedSel);
        }
        Sfx.Play("tick");
    }

    // ================================================================== interface

    /// <summary>Linha clicável: o mouse em cima escolhe o item e o clique confirma.</summary>
    void MouseItem(Rect r, int i)
    {
        var e = Event.current;
        if (!r.Contains(e.mousePosition)) return;
        if (mouseMoved) creatorSel = i;
        if (e.type == EventType.MouseDown && e.button == 0) { clickedItem = i; e.Use(); }
    }

    void DrawListItem(float y, float h, int i, string label, string detail, bool enabled = true)
    {
        float w = 900f, x = (VW - w) / 2f;
        var r = R(x, y, w, h);
        bool sel = creatorSel == i;
        MouseItem(r, i);
        Fill(r, sel ? new Color(0.3f, 0.2f, 0.45f, 0.95f) : new Color(0.08f, 0.06f, 0.13f, 0.9f));
        if (sel) Fill(R(x, y, 8, h), new Color(1f, 0.9f, 0.5f));
        Color c = enabled ? Color.white : new Color(0.6f, 0.6f, 0.65f);
        Text(R(x + 30, y, w - 60, detail == null ? h : h * 0.6f), label, h < 60f ? 24 : 28, c, TextAnchor.MiddleLeft);
        if (detail != null) Text(R(x + 30, y + h * 0.5f, w - 200, h * 0.45f), detail, 17, new Color(0.75f, 0.73f, 0.85f), TextAnchor.MiddleLeft);
    }

    void DrawToast()
    {
        float t = Time.time - toastTime;
        if (t > 4f) return;
        var c = new Color(1f, 0.9f, 0.5f, Mathf.Clamp01((4f - t) * 2f));
        // no editor fica embaixo da barra de cima; nas listas, em cima do rodapé (longe dos títulos)
        float y = creatorScreen == CreatorScreen.Edit ? EditTopH + 40f : 972f;
        Fill(R(0, y, VW, 50), new Color(0.05f, 0.03f, 0.09f, 0.88f * c.a));
        Text(R(0, y, VW, 50), toast, 22, c);
    }

    void DrawCreator()
    {
        switch (creatorScreen)
        {
            case CreatorScreen.Slots: DrawSlots(); break;
            case CreatorScreen.Received: DrawReceived(); break;
            case CreatorScreen.ReceivedMenu: DrawReceivedMenu(); break;
            case CreatorScreen.Edit: DrawEdit(); break;
            case CreatorScreen.Menu: DrawEdit(); DrawEditMenu(); break;
        }
        DrawToast();
    }

    void DrawTitle(string title, string subtitle)
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.02f, 0.06f, 0.82f));
        Text(R(0, 24, VW, 80), title, 60, new Color(0.55f, 0.92f, 0.4f));
        Text(R(0, 96, VW, 36), subtitle, 22, new Color(0.9f, 0.85f, 1f));
    }

    const string ListHelp = "{ANALÓGICO} [W] [S] [^] [v] escolher   •   " + AnyConfirm + " abrir   •   [Q] [BACKSPACE] {B} voltar   •   ou use o mouse";

    void DrawSlots()
    {
        DrawTitle("CRIADOR DE FASES", "Monte uma fase, passe nela e mande o código ou o arquivo para os amigos tentarem bater o seu tempo.");
        float y = 196f, h = 80f;
        for (int i = 0; i < MapCode.Slots; i++, y += h + 6f)
        {
            var map = slotMaps[i];
            string detail = map == null ? "vazia: aperte para criar" : slotTimes[i] > 0 ? $"seu tempo: {Secs(slotTimes[i])}" : "ainda não testada";
            DrawListItem(y, h, i, map != null ? map.Name.ToUpper() : $"FASE {i + 1}", detail);
            if (map != null) DrawMapThumbnail(map, (VW + 900f) / 2f - 140f, y + 8f, 2.5f);
        }
        y += 8f;
        DrawListItem(y, 52f, MapCode.Slots, $"FASES RECEBIDAS ({received.Count})", null);
        DrawListItem(y += 58f, 52f, MapCode.Slots + 1, "COLAR CÓDIGO DE DESAFIO", null);
        DrawListItem(y += 58f, 52f, MapCode.Slots + 2, Share.Browser ? "IMPORTAR ARQUIVO" : "IMPORTAR ARQUIVOS DA PASTA", null);
        DrawListItem(y += 58f, 52f, MapCode.Slots + 3, creatorFromMenu ? "VOLTAR AO MENU" : "VOLTAR AO LOBBY", null);
        DrawBottomHelp(ListHelp);
    }

    void DrawReceived()
    {
        DrawTitle("FASES RECEBIDAS", received.Count == 0 ? "Nenhuma ainda: cole um código ou importe um arquivo no criador." : "Fases dos amigos: jogue sozinho contra o relógio ou leve para o lobby e jogue em grupo.");
        float y = 196f, h = 80f;
        for (int i = receivedScroll; i < Mathf.Min(received.Count, receivedScroll + ReceivedVisible); i++, y += h + 6f)
        {
            var r = received[i];
            int rec = MapCode.Record(r.Map);
            string detail = (r.TimeMs > 0 ? $"tempo de quem criou: {Secs(r.TimeMs)}" : "sem tempo de quem criou") + (rec > 0 ? $"   ·   seu recorde: {Secs(rec)}" : "   ·   você ainda não passou");
            DrawListItem(y, h, i, r.Map.Name.ToUpper(), detail);
            DrawMapThumbnail(r.Map, (VW + 900f) / 2f - 140f, y + 8f, 2.5f);
        }
        if (received.Count > ReceivedVisible) Text(R(0, y, VW, 24), $"{receivedScroll + 1}-{Mathf.Min(received.Count, receivedScroll + ReceivedVisible)} de {received.Count}", 16, new Color(0.7f, 0.7f, 0.8f));
        DrawListItem(y + 26f, 52f, received.Count, "VOLTAR", null);
        if (creatorScreen == CreatorScreen.Received) DrawBottomHelp(ListHelp);
    }

    void DrawReceivedMenu()
    {
        if (receivedSel >= received.Count) return;
        var r = received[receivedSel];
        int rec = MapCode.Record(r.Map);
        DrawTitle(r.Map.Name.ToUpper(), rec > 0 ? $"Seu recorde nesta fase: {Secs(rec)}" : "Você ainda não passou nesta fase");
        float y = 196f, h = 72f;
        DrawListItem(y, h, 0, "JOGAR SOZINHO (CONTRA O RELÓGIO)", r.TimeMs > 0 ? $"Tempo a bater: {Secs(r.TimeMs)}" : null);
        DrawListItem(y += h + 8f, h, 1, "JOGAR EM GRUPO NO LOBBY", "A fase fica escolhida no lobby: partida normal, local ou online");
        DrawListItem(y += h + 8f, h, 2, "COPIAR CÓDIGO", "Para mandar para mais alguém");
        DrawListItem(y += h + 8f, h, 3, "EXPORTAR ARQUIVO", Share.Browser ? "Baixa um arquivo .txt" : "Salva em " + MapCode.Folder);
        DrawListItem(y += h + 8f, h, 4, "APAGAR", Time.time - armedAt < 2f ? "Aperte de novo para confirmar" : "Tira da lista de fases recebidas");
        DrawListItem(y += h + 8f, h, 5, "VOLTAR", null);
        DrawBottomHelp(ListHelp);
    }

    void DrawEdit()
    {
        Vector2 a = ToGui(Level.Origin), b = ToGui(Level.Origin + new Vector2(Level.W, Level.H));
        float cw = (b.x - a.x) / Level.W, chh = (a.y - b.y) / Level.H;
        Rect Cell(Vector2Int c) => new Rect(a.x + c.x * cw, a.y - (c.y + 1) * chh, cw, chh);

        var line = new Color(1f, 1f, 1f, 0.07f);
        for (int x = 0; x <= Level.W; x++) Fill(new Rect(a.x + x * cw, b.y, 1f, a.y - b.y), line);
        for (int y = 0; y <= Level.H; y++) Fill(new Rect(a.x, b.y + y * chh, b.x - a.x, 1f), line);

        for (int y = 0; y < Level.H; y++)
            for (int x = 0; x < Level.W; x++)
            {
                char ch = editMap.At(x, y);
                if (ch < '1' || ch > '4') continue;
                // zumbi fantasma na largada (sprite de 18x20 px, 16 px por célula, pés no chão da célula)
                var r = new Rect(a.x + (x + 0.5f - 7.5f / 16f) * cw, a.y - (y + 19f / 16f) * chh, cw * 18f / 16f, chh * 20f / 16f);
                DrawSprite(r, Art.ZombieFrames(ch - '1', 0)[0], new Color(1f, 1f, 1f, 0.75f));
                WorldText(Level.CellCenter(new Vector2Int(x, y)) + Vector2.up * 1.3f, "P" + ch, 16, Art.PlayerColor(ch - '1'));
            }

        var sawColor = new Color(0.95f, 0.3f, 0.3f, 0.35f);
        foreach (var s in editMap.Saws) FillPath(s.From, s.To, Cell, sawColor);
        if (sawStart.HasValue) FillPath(sawStart.Value, SawEnd(sawStart.Value, editCell), Cell, new Color(1f, 0.9f, 0.5f, 0.35f));
        if (pendingPortal != null) foreach (var c in MapCode.ItemCells(pendingPortal)) Fill(Cell(c), new Color(0.6f, 0.4f, 1f, 0.45f));

        if (CurrentPiece.IsItem && creatorScreen == CreatorScreen.Edit)
        {
            // contorno do item onde ele vai ficar: verde cabe, vermelho não cabe
            var ghost = GhostItem(editCell);
            var gc = ItemFits(ghost) ? new Color(0.5f, 1f, 0.45f, 0.4f) : new Color(1f, 0.3f, 0.3f, 0.45f);
            foreach (var c in MapCode.ItemCells(ghost)) Fill(Cell(c), gc);
        }

        var cur = Cell(editCell);
        Color pc = PieceColor(CurrentPiece);
        Fill(cur, new Color(pc.r, pc.g, pc.b, 0.45f));
        float t = Mathf.Max(2f, 3f * S);
        var edge = new Color(1f, 0.9f, 0.5f, 0.6f + 0.4f * Mathf.Sin(Time.time * 8f));
        Fill(new Rect(cur.x - t, cur.y - t, cur.width + 2 * t, t), edge);
        Fill(new Rect(cur.x - t, cur.yMax, cur.width + 2 * t, t), edge);
        Fill(new Rect(cur.x - t, cur.y, t, cur.height), edge);
        Fill(new Rect(cur.xMax, cur.y, t, cur.height), edge);

        if (creatorScreen != CreatorScreen.Edit) return;
        var e = Event.current;
        bool Clicked(Rect r)
        {
            if (e.type != EventType.MouseDown || e.button != 0 || !r.Contains(e.mousePosition)) return false;
            e.Use();
            return true;
        }

        // barra de cima: nome e estado da fase, peça escolhida e botões
        Fill(R(0, 0, VW, EditTopH), new Color(0.05f, 0.03f, 0.09f, 0.88f));
        string status = editTimeMs > 0 ? $"seu tempo: {Secs(editTimeMs)}" : "ainda não testada";
        string mapLabel = $"{editMap.Name.ToUpper()}  ·  {status}";
        Text(R(24, 0, 600, EditTopH), mapLabel, 22, new Color(0.55f, 0.92f, 0.4f), TextAnchor.MiddleLeft);
        string pieceName = CurrentPiece.Name.ToUpper();
        if (CurrentPiece.IsItem && ItemDefs.Get(CurrentPiece.Item).CanRotate) pieceName += $"  ·  giro {itemRot * 90}°";
        // a peça vai no espaço entre o nome da fase e os botões (em tela estreita o meio da tela fica atrás deles)
        float left = 24f + TextWidth(mapLabel, 22) / S + 24f, right = VW - 760f - 24f;
        float half = TextWidth(pieceName, 26) / S / 2f;
        if (VW / 2f - half >= left && VW / 2f + half <= right) Text(R(0, 0, VW, EditTopH), pieceName, 26, Color.white);
        else Text(R(left, 0, right - left, EditTopH), pieceName, FitSize(pieceName, 26, right - left, 14), Color.white);
        var undoBtn = R(VW - 760, 8, 220, EditTopH - 16);
        var testBtn = R(VW - 530, 8, 210, EditTopH - 16);
        var menuBtn = R(VW - 310, 8, 290, EditTopH - 16);
        if (Clicked(undoBtn)) Undo();
        if (Clicked(testBtn)) { TestMap(); return; }
        if (Clicked(menuBtn)) { OpenEditMenu(); return; }
        foreach (var btn in new[] { undoBtn, testBtn, menuBtn }) Fill(btn, new Color(0.3f, 0.2f, 0.45f, 0.95f));
        Keys(undoBtn, "DESFAZER  [Z] {B}", 16, undo.Count > 0 ? Color.white : new Color(0.6f, 0.6f, 0.65f));
        Keys(testBtn, "TESTAR  [T] {SELECT}", 16, Color.white);
        Keys(menuBtn, "MENU  [ESC] {START}", 16, Color.white);

        char pch = CurrentPiece.Ch;
        string tip = pch == MovingSawPiece ? (sawStart.HasValue ? "Agora marque onde a serra para (na mesma linha ou coluna)" : "Marque onde a serra começa")
                   : CurrentPiece.IsItem && CurrentPiece.Item == ItemType.Portal ? (pendingPortal != null ? "Agora coloque o segundo portal" : "Coloque o primeiro portal")
                   : CurrentPiece.IsItem ? (ItemDefs.Get(CurrentPiece.Item).CanRotate ? "[R] [DELETE] {Y} girar o item" : "")
                   : IsMarker(pch) ? "Só existe um: colocar em outro lugar muda ele de lugar" + (IsRequired(pch) ? "" : " (apagar tira ele)")
                   : pch == '=' ? "Trilho com 7 células ou mais ganha um trem" : "";
        if (tip.Length > 0) Keys(R(0, EditTopH + 4, VW, 32), tip, 18, new Color(1f, 0.9f, 0.5f));

        // barra de peças: blocos em cima, itens embaixo
        float by = 1080f - EditBottomH;
        Fill(R(0, by, VW, EditBottomH - 52f), new Color(0.05f, 0.03f, 0.09f, 0.88f));
        for (int row = 0; row < 2; row++)
        {
            int first = row == 0 ? 0 : BlockPieces, count = row == 0 ? BlockPieces : Pieces.Length - BlockPieces;
            float gap = 5f, chipW = Mathf.Min(118f, (VW - 30f) / count - gap), x0 = (VW - count * (chipW + gap) + gap) / 2f;
            for (int k = 0; k < count; k++)
            {
                int i = first + k;
                var r = R(x0 + k * (chipW + gap), by + 6f + row * 60f, chipW, 54f);
                if (Clicked(r)) SelectPiece(i);
                Fill(r, i == piece ? new Color(0.3f, 0.2f, 0.45f, 0.95f) : new Color(1f, 1f, 1f, 0.05f));
                if (i == piece) Fill(R(x0 + k * (chipW + gap), by + 6f + row * 60f, chipW, 4f), new Color(1f, 0.9f, 0.5f));
                Fill(R(x0 + k * (chipW + gap) + chipW / 2f - 12f, by + 14f + row * 60f, 24f, 16f), PieceColor(Pieces[i]));
                DrawChipName(R(x0 + k * (chipW + gap), by + 31f + row * 60f, chipW, 26f), Pieces[i].Name, 13, i == piece ? Color.white : new Color(0.75f, 0.73f, 0.85f));
            }
        }
        DrawBottomHelp("[W] [A] [S] [D]  [<] [^] [v] [>]  {ANALÓGICO} mover   •   [ESPAÇO] [ENTER] {A} pintar (segure)   •   [Q] [E]  [CTRL] [SHIFT]  {LB} {RB}  rodinha: peça   •   botão direito apaga");
    }

    void FillPath(Vector2Int from, Vector2Int to, System.Func<Vector2Int, Rect> cell, Color c)
    {
        var step = new Vector2Int(System.Math.Sign(to.x - from.x), System.Math.Sign(to.y - from.y));
        for (var p = from; ; p += step)
        {
            Fill(cell(p), c);
            if (p == to || step == Vector2Int.zero) break;
        }
    }

    static Color PieceColor(Piece p)
    {
        if (p.IsItem) return new Color(0.72f, 0.5f, 0.95f);
        switch (p.Ch)
        {
            case '.': return new Color(0.9f, 0.25f, 0.25f);
            case 't': return new Color(0.55f, 0.55f, 0.62f);
            case 'T': return new Color(0.4f, 0.3f, 0.25f);
            case MovingSawPiece: return new Color(1f, 0.55f, 0.3f);
            default: return CellColor(p.Ch) ?? Color.white;
        }
    }

    void DrawEditMenu()
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.7f));
        Text(R(0, 90, VW, 80), editMap.Name.ToUpper(), 56, new Color(0.85f, 0.75f, 1f));
        string time = editTimeMs > 0 ? $"Seu tempo: {Secs(editTimeMs)}" : "Ainda não testada";
        Text(R(0, 160, VW, 32), time, 22, editTimeMs > 0 ? new Color(0.6f, 1f, 0.5f) : new Color(1f, 0.75f, 0.5f));
        string path = pathState == PathState.None ? "Atenção: os bots não acham caminho da largada até o cérebro (eles não usam mola nem portal; se você passa, tudo certo)"
                    : pathState == PathState.Ok ? "Os bots acham caminho até o cérebro" : "Conferindo o caminho...";
        Text(R(0, 192, VW, 30), path, 18, pathState == PathState.None ? new Color(1f, 0.55f, 0.35f) : new Color(0.7f, 0.85f, 1f));

        bool tested = editTimeMs > 0;
        float y = 232f, h = 70f;
        DrawListItem(y, h, 0, "CONTINUAR EDITANDO", null);
        DrawListItem(y += h + 8f, h, 1, "TESTAR", "Passe na fase: o seu melhor tempo é o que os amigos vão tentar bater");
        DrawListItem(y += h + 8f, h, 2, "COPIAR CÓDIGO DO DESAFIO", tested ? "Vai para a área de transferência: cole numa conversa" : "Liberado depois que você passar na fase em TESTAR", tested);
        DrawListItem(y += h + 8f, h, 3, "EXPORTAR ARQUIVO", !tested ? "Liberado depois que você passar na fase em TESTAR" : Share.Browser ? "Baixa um arquivo .txt" : "Salva em " + MapCode.Folder, tested);
        DrawListItem(y += h + 8f, h, 4, $"TEMA:   <  {MapCode.ThemeName(editMap.Theme)}  >", "Esquerda/direita troca o fundo");
        DrawListItem(y += h + 8f, h, 5, "JOGAR EM GRUPO NO LOBBY", "A fase fica escolhida no lobby: partida normal, local ou online");
        DrawListItem(y += h + 8f, h, 6, "COMEÇAR DO ZERO", Time.time - armedAt < 2f ? "Aperte de novo para confirmar" : "Apaga a fase inteira (dá para desfazer)");
        DrawListItem(y += h + 8f, h, 7, "SALVAR E SAIR", "A fase já é salva a cada mudança");
        DrawBottomHelp($"{{ANALÓGICO}} [W] [S] [^] [v] escolher   •   {AnyConfirm} confirmar   •   [ESC] {{START}} [Q] {{B}} voltar a editar");
    }

    void DrawChallenge()
    {
        Fill(R(0, 0, VW, 64), new Color(0.05f, 0.03f, 0.09f, 0.75f));
        Text(R(24, 0, 600, 64), ChallengeTesting ? $"TESTANDO  ·  {challengeMap.Name.ToUpper()}" : $"DESAFIO  ·  {challengeMap.Name.ToUpper()}", 24, new Color(0.85f, 0.75f, 1f), TextAnchor.MiddleLeft);
        int now = Mathf.RoundToInt(challengeTime * 1000f);
        Text(R(0, 0, VW, 64), Secs(challengeDone ? challengeLastMs : now), 40, Color.white);
        string right = (challengeTargetMs > 0 ? $"a bater: {Secs(challengeTargetMs)}   ·   " : "") + (challengeRecordMs > 0 ? $"recorde: {Secs(challengeRecordMs)}   ·   " : "") + $"mortes: {challengeDeaths}";
        Text(R(VW - 824, 0, 800, 64), right, 22, new Color(1f, 0.85f, 0.5f), TextAnchor.MiddleRight);

        if (!challengeDone)
        {
            if (PhaseTime < 1.1f) Text(R(0, 380, VW, 200), "VAI!", 120, new Color(0.55f, 1f, 0.45f, Mathf.Clamp01((1.1f - PhaseTime) * 3f)));
            DrawBottomHelp("PULAR  [W] [ESPAÇO]  [^]  {A}   •   morreu: volta para a largada e o tempo continua   •   [ESC] {START} pausar ou desistir");
            return;
        }

        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.01f, 0.05f, 0.72f));
        bool beat = challengePassed && challengeTargetMs > 0 && challengeLastMs < challengeTargetMs;
        string title = !challengePassed ? "NÃO PASSOU" : ChallengeTesting ? "FASE LIBERADA!" : beat ? "VOCÊ BATEU O TEMPO!" : "PASSOU!";
        Color tc = !challengePassed ? new Color(1f, 0.45f, 0.4f) : beat || ChallengeTesting ? new Color(1f, 0.85f, 0.3f) : new Color(0.55f, 1f, 0.45f);
        Text(R(0, 190, VW, 120), title, 84, tc);
        Text(R(0, 320, VW, 50), $"{(challengePassed ? "Seu tempo" : "Tempo jogado")}: {Secs(challengeLastMs)}   •   Mortes: {challengeDeaths}", 34, Color.white);
        string sub = "";
        if (!challengePassed) sub = challengeTargetMs > 0 ? $"Quem criou passou em {Secs(challengeTargetMs)}: tente de novo!" : "Tente de novo!";
        else if (ChallengeTesting) sub = $"Tempo que vai no código: {Secs(editTimeMs)}. Copie o código ou exporte o arquivo no menu do criador.";
        else if (beat) sub = $"Quem criou fez {Secs(challengeTargetMs)}: você foi {Secs(challengeTargetMs - challengeLastMs)} mais rápido!";
        else if (challengeTargetMs > 0) sub = $"Quem criou fez {Secs(challengeTargetMs)}: faltaram {Secs(challengeLastMs - challengeTargetMs)}";
        Text(R(0, 380, VW, 40), sub, 26, new Color(0.9f, 0.85f, 1f));
        string rec = challengeNewRecord ? $"NOVO RECORDE: {Secs(challengeRecordMs)}!" : challengeRecordMs > 0 ? $"Seu recorde nesta fase: {Secs(challengeRecordMs)}" : "Você ainda não tem recorde nesta fase";
        Text(R(0, 426, VW, 36), rec, 24, challengeNewRecord ? new Color(1f, 0.85f, 0.3f) : new Color(0.7f, 0.9f, 1f));

        if (Time.time - challengeDoneAt < 0.8f) return;
        float blink = 0.6f + 0.4f * Mathf.Sin(Time.time * 5f);
        var again = R(VW / 2f - 420f, 520, 400, 60);
        var exit = R(VW / 2f + 20f, 520, 400, 60);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (again.Contains(e.mousePosition)) { clickedItem = 0; e.Use(); }
            else if (exit.Contains(e.mousePosition)) { clickedItem = 1; e.Use(); }
        }
        Fill(again, new Color(0.3f, 0.2f, 0.45f, 0.95f));
        Fill(exit, new Color(0.3f, 0.2f, 0.45f, 0.95f));
        Keys(again, $"{AnyConfirm}  tentar de novo", 20, new Color(1f, 0.9f, 0.5f, blink));
        Keys(exit, ChallengeTesting ? "[Q] [BACKSPACE] {B}  voltar a editar" : "[Q] [BACKSPACE] {B}  sair", 20, Color.white);
    }
}
