using System.Globalization;
using System.Linq;
using UnityEngine;

/// <summary>Criador de fases (montar, salvar e copiar o código) e o desafio solo contra o relógio. Só no jogo offline.</summary>
public partial class GameManager
{
    enum CreatorScreen { Slots, Edit, Menu }

    static readonly (char ch, string name)[] Pieces =
    {
        ('.', "Apagar"), ('#', "Terra"), ('S', "Pedra"), ('i', "Gelo"), ('=', "Trilho"), ('a', "Ácido"), ('^', "Espinhos"),
        ('X', "Serra"), (MovingSawPiece, "Serra móvel"), ('1', "Largada"), ('G', "Cérebro"), ('t', "Lápide"), ('T', "Árvore"),
    };
    const char MovingSawPiece = '*';
    const float EditTopH = 64f, EditBottomH = 132f;   // barras do editor, em pixels da tela de 1080
    const int MenuItemCount = 6;

    CreatorScreen creatorScreen;
    int creatorSel;
    int clickedItem = -1;        // item clicado com o mouse no OnGUI, tratado no Update seguinte
    bool creatorClicked, mouseMoved;
    Vector2 lastMouse;
    readonly MapDef[] slotMaps = new MapDef[MapCode.Slots];
    readonly int[] slotTimes = new int[MapCode.Slots];

    int editSlot;
    MapDef editMap;
    int editTimeMs;              // tempo de quem criou desde a última mudança (0 = ainda não passou na fase)
    Vector2Int editCell, lastPainted = NoCell, moveDir;
    static readonly Vector2Int NoCell = new Vector2Int(-1, -1);
    int piece = 1;
    Vector2Int? sawStart;
    bool levelDirty;
    float rebuildTimer, moveRepeat, clearArmed = -10f;
    string toast = "";
    float toastTime = -10f;

    MapDef challengeMap;
    int challengeTargetMs, challengeBestMs, challengeLastMs, challengeDeaths;
    bool challengeTesting, challengeDone;
    float challengeTime, challengeDoneAt, respawnTimer;
    Zombie challengeZombie;
    InputSource challengeInput;

    /// <summary>Editando ou no menu do editor: a câmera abre espaço para as barras de cima e de baixo.</summary>
    bool EditorCamera => Phase == Phase.Editor && creatorScreen != CreatorScreen.Slots;
    static bool CanCreate => NetGame.Instance == null || NetGame.Instance.Mode == NetMode.Offline;

    void Toast(string s)
    {
        toast = s;
        toastTime = Time.time;
    }

    static string Secs(int ms) => (ms / 1000f).ToString("0.00", CultureInfo.InvariantCulture).Replace('.', ',') + " s";

    // ================================================================== lista de fases

    void OpenCreator()
    {
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
    }

    void UpdateCreator()
    {
        Vector2 mp = InputSource.MousePosition;
        mouseMoved = (mp - lastMouse).sqrMagnitude > 1f;
        lastMouse = mp;

        switch (creatorScreen)
        {
            case CreatorScreen.Slots: UpdateSlots(); break;
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
        bool confirm = ListInput(MapCode.Slots + 2, out bool back, out _);
        if (back) { BackToLobby(); Sfx.Play("tick"); return; }
        if (!confirm) return;
        if (creatorSel < MapCode.Slots) OpenSlot(creatorSel);
        else if (creatorSel == MapCode.Slots) PasteChallenge();
        else BackToLobby();
    }

    void OpenSlot(int slot)
    {
        editSlot = slot;
        editMap = slotMaps[slot] ?? MapCode.NewMap();
        editTimeMs = slotMaps[slot] != null ? slotTimes[slot] : 0;
        editCell = new Vector2Int(Level.W / 2, Level.H / 2);
        sawStart = null;
        creatorScreen = CreatorScreen.Edit;
        levelDirty = true;
        rebuildTimer = 0f;
        Sfx.Play("pick");
    }

    void PasteChallenge()
    {
        if (!MapCode.TryDecode(Clipboard.Paste(), out var map, out int ms))
        {
            Toast("Não achei um código de desafio. Copie o código inteiro (começa com ZH1-) e tente de novo.");
            Sfx.Play("error");
            return;
        }
        StartChallenge(map, ms, false);
    }

    // ================================================================== editor

    void UpdateEdit()
    {
        var devices = ActiveDevices();
        if (devices.Any(d => d.PauseDown)) { OpenEditMenu(); return; }

        int turn = -InputSource.MouseScroll;
        foreach (var d in devices) turn += (d.RotateCWDown ? 1 : 0) - (d.RotateCCWDown ? 1 : 0);
        if (turn != 0) SelectPiece(piece + turn);

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
        if (!hold && !erase) lastPainted = NoCell;
    }

    void SelectPiece(int i)
    {
        piece = (i % Pieces.Length + Pieces.Length) % Pieces.Length;
        sawStart = null;
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
        char ch = Pieces[piece].ch;
        if (ch != MovingSawPiece && ch != '1' && ch != 'G') { PaintCell(c, ch, press); return; }
        if (!press) return;   // peças que são uma só não pintam arrastando
        if (ch == MovingSawPiece) PlaceSaw(c);
        else MoveMarker(c, ch);
    }

    void PaintCell(Vector2Int c, char ch, bool press)
    {
        char old = editMap.At(c.x, c.y);
        // a largada e o cérebro só mudam de lugar, nunca somem
        if (old == '1' || old == 'G') { if (press) Sfx.Play("error"); return; }
        int saws = editMap.Saws.Length;
        if (ch == '.') editMap.Saws = editMap.Saws.Where(s => s.From != c && s.To != c).ToArray();
        if (old == ch && saws == editMap.Saws.Length) return;
        MapCode.Set(editMap, c.x, c.y, ch);
        Changed();
        Sfx.Play("tick", 0.35f);
    }

    void MoveMarker(Vector2Int c, char ch)
    {
        if (editMap.At(c.x, c.y) == (ch == '1' ? 'G' : '1')) { Sfx.Play("error"); return; }
        for (int y = 0; y < Level.H; y++)
            for (int x = 0; x < Level.W; x++)
            {
                if (editMap.At(x, y) != ch) continue;
                MapCode.Set(editMap, x, y, '.');
                if (ch == 'G' && y > 0 && editMap.At(x, y - 1) == 'p') MapCode.Set(editMap, x, y - 1, '.');
            }
        MapCode.Set(editMap, c.x, c.y, ch);
        if (ch == 'G' && c.y > 0 && editMap.At(c.x, c.y - 1) != '1') MapCode.Set(editMap, c.x, c.y - 1, 'p');
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
        editMap.Saws = editMap.Saws.Append(new SawPathDef { From = from, To = to }).ToArray();
        Changed();
        Sfx.Play("place", 0.6f);
    }

    /// <summary>A serra anda em linha reta: o fim fica na mesma linha ou coluna do começo, a que estiver mais perto do cursor.</summary>
    static Vector2Int SawEnd(Vector2Int from, Vector2Int c) =>
        Mathf.Abs(c.x - from.x) >= Mathf.Abs(c.y - from.y) ? new Vector2Int(c.x, from.y) : new Vector2Int(from.x, c.y);

    /// <summary>Qualquer mudança na fase obriga quem criou a passar nela de novo antes de compartilhar.</summary>
    void Changed()
    {
        editTimeMs = 0;
        levelDirty = true;
        MapCode.SaveSlot(editSlot, editMap, 0);
    }

    void RebuildIfDirty()
    {
        rebuildTimer -= Time.deltaTime;
        if (!levelDirty || rebuildTimer > 0f) return;
        // remontar a fase cria centenas de objetos: no máximo algumas vezes por segundo
        levelDirty = false;
        rebuildTimer = 0.12f;
        MapCode.BuildTrains(editMap);
        Level.Build(editMap);
    }

    // ================================================================== menu do editor

    void OpenEditMenu()
    {
        creatorScreen = CreatorScreen.Menu;
        creatorSel = 0;
        sawStart = null;
        Sfx.Play("tick");
    }

    void UpdateEditMenu()
    {
        bool confirm = ListInput(MenuItemCount, out bool back, out int side);
        if (back) { creatorScreen = CreatorScreen.Edit; Sfx.Play("tick"); return; }
        if (creatorSel == 3 && side != 0) ChangeTheme(side);
        if (!confirm) return;
        switch (creatorSel)
        {
            case 0: creatorScreen = CreatorScreen.Edit; Sfx.Play("tick"); break;
            case 1: StartChallenge(editMap, editTimeMs, true); break;
            case 2: CopyCode(); break;
            case 3: ChangeTheme(1); break;
            case 4:
                if (Time.time - clearArmed > 2f) { clearArmed = Time.time; Toast("Aperte de novo para apagar a fase inteira"); Sfx.Play("error"); break; }
                int theme = editMap.Theme;
                editMap = MapCode.NewMap();
                editMap.Theme = theme;
                Changed();
                clearArmed = -10f;
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

    void CopyCode()
    {
        if (editTimeMs <= 0) { Toast("Passe na fase em TESTAR primeiro: o seu tempo vai junto no código"); Sfx.Play("error"); return; }
        Clipboard.Copy(MapCode.Encode(editMap, editTimeMs));
        Toast("Código copiado! Cole numa conversa para desafiar os amigos.");
        Sfx.Play("goal");
    }

    // ================================================================== desafio

    void StartChallenge(MapDef map, int targetMs, bool testing)
    {
        challengeMap = map;
        challengeTargetMs = targetMs;
        challengeTesting = testing;
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
        if (challengeInput == null || !challengeInput.Connected || challengeInput.Remote) challengeInput = ActiveDevices().First(d => !d.Remote);
        challengeZombie = Zombie.Spawn(0, challengeInput, Level.SpawnPoint(0));
        RunActive = true;
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
            // morreu: volta para a largada e o relógio continua
            if (respawnTimer < 0f) { respawnTimer = 0.7f; challengeDeaths++; }
            if ((respawnTimer -= Time.deltaTime) <= 0f)
            {
                respawnTimer = -1f;
                Zombie.ClearAll();
                challengeZombie = Zombie.Spawn(0, challengeInput, Level.SpawnPoint(0));
            }
        }
        else if (challengeZombie.State == ZombieState.Finished) FinishChallenge();
        else challengeZombie.Controls = challengeInput;
    }

    void FinishChallenge()
    {
        challengeDone = true;
        challengeDoneAt = Time.time;
        RunActive = false;
        challengeLastMs = Mathf.Max(1, Mathf.RoundToInt(challengeTime * 1000f));
        if (challengeBestMs == 0 || challengeLastMs < challengeBestMs) challengeBestMs = challengeLastMs;
        if (challengeTesting && (editTimeMs == 0 || challengeLastMs < editTimeMs))
        {
            editTimeMs = challengeLastMs;
            MapCode.SaveSlot(editSlot, editMap, editTimeMs);
            PlayerPrefs.Save();
        }
        bool beat = challengeTargetMs > 0 && challengeLastMs < challengeTargetMs;
        Sfx.Play(beat || challengeTesting ? "win" : "goal");
    }

    void ExitChallenge()
    {
        Zombie.ClearAll();
        RunActive = false;
        SetPhase(Phase.Editor);
        if (challengeTesting)
        {
            creatorScreen = CreatorScreen.Menu;
            creatorSel = 2;
            levelDirty = true;
            rebuildTimer = 0f;
        }
        else
        {
            LoadSlots();
            creatorScreen = CreatorScreen.Slots;
            creatorSel = MapCode.Slots;
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
        Text(R(x + 30, y, w - 60, detail == null ? h : h * 0.6f), label, 28, c, TextAnchor.MiddleLeft);
        if (detail != null) Text(R(x + 30, y + h * 0.5f, w - 60, h * 0.45f), detail, 17, new Color(0.75f, 0.73f, 0.85f), TextAnchor.MiddleLeft);
    }

    void DrawToast()
    {
        float t = Time.time - toastTime;
        if (t > 3.5f) return;
        var c = new Color(1f, 0.9f, 0.5f, Mathf.Clamp01((3.5f - t) * 2f));
        Fill(R(0, 140, VW, 50), new Color(0.05f, 0.03f, 0.09f, 0.85f * c.a));
        Text(R(0, 140, VW, 50), toast, 24, c);
    }

    void DrawCreator()
    {
        switch (creatorScreen)
        {
            case CreatorScreen.Slots: DrawSlots(); break;
            case CreatorScreen.Edit: DrawEdit(); break;
            case CreatorScreen.Menu: DrawEdit(); DrawEditMenu(); break;
        }
        DrawToast();
    }

    void DrawSlots()
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.02f, 0.06f, 0.82f));
        Text(R(0, 30, VW, 80), "CRIADOR DE FASES", 64, new Color(0.55f, 0.92f, 0.4f));
        Text(R(0, 100, VW, 36), "Monte uma fase, passe nela e mande o código para os amigos tentarem bater o seu tempo.", 22, new Color(0.9f, 0.85f, 1f));

        float y = 200f, h = 84f;
        for (int i = 0; i < MapCode.Slots; i++, y += h + 8f)
        {
            var map = slotMaps[i];
            string detail = map == null ? "vazia: aperte para criar" : slotTimes[i] > 0 ? $"seu tempo: {Secs(slotTimes[i])}" : "ainda não testada";
            DrawListItem(y, h, i, $"FASE {i + 1}", detail);
            float tx = (VW + 900f) / 2f - 140f;
            if (map != null) DrawMapThumbnail(map, tx, y + 8f, 2.5f);
        }
        y += 12f;
        DrawListItem(y, 64f, MapCode.Slots, "COLAR CÓDIGO DE DESAFIO", null);
        DrawListItem(y + 72f, 64f, MapCode.Slots + 1, "VOLTAR AO LOBBY", null);
        DrawBottomHelp($"{{ANALÓGICO}} [W] [S] [^] [v] escolher   •   {AnyConfirm} abrir   •   [Q] [BACKSPACE] {{B}} voltar   •   ou use o mouse");
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
                if (editMap.At(x, y) != '1') continue;
                // zumbi fantasma na largada (sprite de 18x20 px, 16 px por célula, pés no chão da célula)
                var r = new Rect(a.x + (x + 0.5f - 7.5f / 16f) * cw, a.y - (y + 19f / 16f) * chh, cw * 18f / 16f, chh * 20f / 16f);
                DrawSprite(r, Art.ZombieFrames(0)[0], new Color(1f, 1f, 1f, 0.75f));
                WorldText(Level.CellCenter(new Vector2Int(x, y)) + Vector2.up * 1.3f, "LARGADA", 16, new Color(0.6f, 1f, 0.5f));
            }

        var sawColor = new Color(0.95f, 0.3f, 0.3f, 0.35f);
        foreach (var s in editMap.Saws) FillPath(s.From, s.To, Cell, sawColor);
        if (sawStart.HasValue) FillPath(sawStart.Value, SawEnd(sawStart.Value, editCell), Cell, new Color(1f, 0.9f, 0.5f, 0.35f));

        var cur = Cell(editCell);
        Color pc = PieceColor(Pieces[piece].ch);
        Fill(cur, new Color(pc.r, pc.g, pc.b, 0.45f));
        float t = Mathf.Max(2f, 3f * S);
        var edge = new Color(1f, 0.9f, 0.5f, 0.6f + 0.4f * Mathf.Sin(Time.time * 8f));
        Fill(new Rect(cur.x - t, cur.y - t, cur.width + 2 * t, t), edge);
        Fill(new Rect(cur.x - t, cur.yMax, cur.width + 2 * t, t), edge);
        Fill(new Rect(cur.x - t, cur.y, t, cur.height), edge);
        Fill(new Rect(cur.xMax, cur.y, t, cur.height), edge);

        if (creatorScreen != CreatorScreen.Edit) return;

        // barra de cima
        Fill(R(0, 0, VW, EditTopH), new Color(0.05f, 0.03f, 0.09f, 0.88f));
        Text(R(24, 0, 600, EditTopH), $"CRIADOR  ·  FASE {editSlot + 1}", 26, new Color(0.55f, 0.92f, 0.4f), TextAnchor.MiddleLeft);
        string status = editTimeMs > 0 ? $"seu tempo: {Secs(editTimeMs)}" : "ainda não testada";
        Text(R(0, 0, VW, EditTopH), $"{Pieces[piece].name.ToUpper()}   ·   {status}", 26, Color.white);
        var menu = R(VW - 330, 8, 306, EditTopH - 16);
        Fill(menu, new Color(0.3f, 0.2f, 0.45f, 0.95f));
        Keys(menu, "MENU  [ESC] {START}", 18, Color.white);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && menu.Contains(e.mousePosition)) { e.Use(); OpenEditMenu(); }

        string tip = Pieces[piece].ch == MovingSawPiece ? (sawStart.HasValue ? "Agora marque onde a serra para (na mesma linha ou coluna)" : "Marque onde a serra começa")
                   : Pieces[piece].ch == '1' || Pieces[piece].ch == 'G' ? "Só existe uma: colocar em outro lugar muda ela de lugar"
                   : Pieces[piece].ch == '=' ? "Trilho com 7 células ou mais ganha um trem" : "";
        if (tip.Length > 0) Text(R(0, EditTopH + 4, VW, 32), tip, 20, new Color(1f, 0.9f, 0.5f));

        // barra de peças
        float by = 1080f - EditBottomH;
        Fill(R(0, by, VW, EditBottomH - 52f), new Color(0.05f, 0.03f, 0.09f, 0.88f));
        float gap = 6f, chipW = Mathf.Min(136f, (VW - 40f) / Pieces.Length - gap), x0 = (VW - Pieces.Length * (chipW + gap) + gap) / 2f;
        for (int i = 0; i < Pieces.Length; i++)
        {
            var r = R(x0 + i * (chipW + gap), by + 8f, chipW, 64f);
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition)) { e.Use(); SelectPiece(i); }
            Fill(r, i == piece ? new Color(0.3f, 0.2f, 0.45f, 0.95f) : new Color(1f, 1f, 1f, 0.05f));
            if (i == piece) Fill(R(x0 + i * (chipW + gap), by + 8f, chipW, 4f), new Color(1f, 0.9f, 0.5f));
            Fill(R(x0 + i * (chipW + gap) + chipW / 2f - 14f, by + 16f, 28f, 22f), PieceColor(Pieces[i].ch));
            Text(R(x0 + i * (chipW + gap), by + 40f, chipW, 28f), Pieces[i].name, 15, i == piece ? Color.white : new Color(0.75f, 0.73f, 0.85f));
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

    static Color PieceColor(char ch)
    {
        switch (ch)
        {
            case '.': return new Color(0.9f, 0.25f, 0.25f);
            case 't': return new Color(0.55f, 0.55f, 0.62f);
            case 'T': return new Color(0.4f, 0.3f, 0.25f);
            case MovingSawPiece: return new Color(1f, 0.55f, 0.3f);
            default: return CellColor(ch) ?? Color.white;
        }
    }

    void DrawEditMenu()
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.7f));
        Text(R(0, 120, VW, 80), $"FASE {editSlot + 1}", 60, new Color(0.85f, 0.75f, 1f));
        string time = editTimeMs > 0 ? $"Seu tempo: {Secs(editTimeMs)}" : "Ainda não testada";
        Text(R(0, 196, VW, 36), time, 24, editTimeMs > 0 ? new Color(0.6f, 1f, 0.5f) : new Color(1f, 0.75f, 0.5f));

        float y = 260f, h = 82f;
        DrawListItem(y, h, 0, "CONTINUAR EDITANDO", null);
        DrawListItem(y += h + 10f, h, 1, "TESTAR", "Passe na fase: o seu melhor tempo é o que os amigos vão tentar bater");
        DrawListItem(y += h + 10f, h, 2, "COPIAR CÓDIGO DO DESAFIO", editTimeMs > 0 ? "Vai para a área de transferência: cole numa conversa" : "Liberado depois que você passar na fase em TESTAR", editTimeMs > 0);
        DrawListItem(y += h + 10f, h, 3, $"TEMA:   <  {MapCode.ThemeName(editMap.Theme)}  >", "Esquerda/direita troca o fundo");
        DrawListItem(y += h + 10f, h, 4, "COMEÇAR DO ZERO", Time.time - clearArmed < 2f ? "Aperte de novo para confirmar" : "Apaga a fase inteira", true);
        DrawListItem(y += h + 10f, h, 5, "SALVAR E SAIR", "A fase já é salva a cada mudança");
        DrawBottomHelp($"{{ANALÓGICO}} [W] [S] [^] [v] escolher   •   {AnyConfirm} confirmar   •   [ESC] {{START}} [Q] {{B}} voltar a editar");
    }

    void DrawChallenge()
    {
        Fill(R(0, 0, VW, 64), new Color(0.05f, 0.03f, 0.09f, 0.75f));
        Text(R(24, 0, 600, 64), challengeTesting ? $"TESTANDO A FASE {editSlot + 1}" : "DESAFIO", 26, new Color(0.85f, 0.75f, 1f), TextAnchor.MiddleLeft);
        int now = Mathf.RoundToInt(challengeTime * 1000f);
        Text(R(0, 0, VW, 64), Secs(challengeDone ? challengeLastMs : now), 40, Color.white);
        string right = (challengeTargetMs > 0 ? $"a bater: {Secs(challengeTargetMs)}   ·   " : "") + $"mortes: {challengeDeaths}";
        Text(R(VW - 624, 0, 600, 64), right, 24, new Color(1f, 0.85f, 0.5f), TextAnchor.MiddleRight);

        if (!challengeDone)
        {
            if (PhaseTime < 1.1f) Text(R(0, 380, VW, 200), "VAI!", 120, new Color(0.55f, 1f, 0.45f, Mathf.Clamp01((1.1f - PhaseTime) * 3f)));
            DrawBottomHelp("PULAR  [W] [ESPAÇO]  [^]  {A}   •   morreu: volta para a largada e o tempo continua   •   [ESC] {START} pausar");
            return;
        }

        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.01f, 0.05f, 0.72f));
        bool beat = challengeTargetMs > 0 && challengeLastMs < challengeTargetMs;
        string title = challengeTesting ? "FASE LIBERADA!" : beat ? "VOCÊ BATEU O TEMPO!" : "PASSOU!";
        Text(R(0, 200, VW, 120), title, 84, beat || challengeTesting ? new Color(1f, 0.85f, 0.3f) : new Color(0.55f, 1f, 0.45f));
        Text(R(0, 340, VW, 50), $"Seu tempo: {Secs(challengeLastMs)}   •   Mortes: {challengeDeaths}", 34, Color.white);
        string sub;
        if (challengeTesting) sub = $"Tempo que vai no código: {Secs(editTimeMs)}. Copie o código no menu do criador.";
        else if (challengeTargetMs <= 0) sub = "";
        else if (beat) sub = $"Quem criou fez {Secs(challengeTargetMs)}: você foi {Secs(challengeTargetMs - challengeLastMs)} mais rápido!";
        else sub = $"Quem criou fez {Secs(challengeTargetMs)}: faltaram {Secs(challengeLastMs - challengeTargetMs)}";
        Text(R(0, 400, VW, 40), sub, 26, new Color(0.9f, 0.85f, 1f));
        if (!challengeTesting && challengeBestMs > 0) Text(R(0, 444, VW, 36), $"Seu melhor: {Secs(challengeBestMs)}", 22, new Color(0.7f, 0.9f, 1f));

        if (Time.time - challengeDoneAt < 0.8f) return;
        float blink = 0.6f + 0.4f * Mathf.Sin(Time.time * 5f);
        var again = R(VW / 2f - 420f, 540, 400, 60);
        var exit = R(VW / 2f + 20f, 540, 400, 60);
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0)
        {
            if (again.Contains(e.mousePosition)) { clickedItem = 0; e.Use(); }
            else if (exit.Contains(e.mousePosition)) { clickedItem = 1; e.Use(); }
        }
        Fill(again, new Color(0.3f, 0.2f, 0.45f, 0.95f));
        Fill(exit, new Color(0.3f, 0.2f, 0.45f, 0.95f));
        Keys(again, $"{AnyConfirm}  tentar de novo", 20, new Color(1f, 0.9f, 0.5f, blink));
        Keys(exit, challengeTesting ? "[Q] [BACKSPACE] {B}  voltar ao criador" : "[Q] [BACKSPACE] {B}  sair", 20, Color.white);
    }
}
