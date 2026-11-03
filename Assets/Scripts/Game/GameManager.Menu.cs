using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Menu principal (a tela de título), menu de pausa e as telas de configurações e de controles que os dois abrem.
/// Qualquer teclado ou controle mexe (cima/baixo escolhe, esquerda/direita muda o valor, CONFIRMAR aperta e
/// VOLTAR/ESC volta) e o mouse também. As configurações ficam salvas com PlayerPrefs.
/// </summary>
public partial class GameManager
{
    enum MenuScreen { Main, Pause, Settings, Controls }
    enum MenuAction { Play, Online, Creator, Settings, Controls, Quit, Resume, Leave, Volume, Fullscreen, Resolution, WinPoints, ScreenShake, Back }

    struct MenuItem { public MenuAction Action; public string Label, Value, Detail; public bool Enabled; }

    MenuScreen menuScreen = MenuScreen.Main;
    MenuScreen menuParent;                  // quem abriu as configurações/controles: o menu principal ou a pausa
    int menuSel, menuClicked = -1, menuClickDx;
    float transitionTime = -10f;            // última troca de tela (a tela escurece e volta)
    readonly List<MenuItem> menuItems = new List<MenuItem>();

    // ================================================================== configurações

    const string PrefVolume = "config_volume", PrefWinScore = "config_pontos", PrefShake = "config_tremer";
    const int MinWinScore = 3, MaxWinScore = 30;
    int volume = 10;                        // 0 a 10 (de 10 em 10%)
    bool shakeOn = true;

    void LoadSettings()
    {
        volume = Mathf.Clamp(PlayerPrefs.GetInt(PrefVolume, 10), 0, 10);
        AudioListener.volume = volume / 10f;
        WinScore = Mathf.Clamp(PlayerPrefs.GetInt(PrefWinScore, WinScore), MinWinScore, MaxWinScore);
        shakeOn = PlayerPrefs.GetInt(PrefShake, 1) != 0;
    }

    static bool CanQuit => Application.platform != RuntimePlatform.WebGLPlayer;
    static bool CanChangeVideo => Application.platform != RuntimePlatform.WebGLPlayer;
    bool Online => NetGame.Instance != null && NetGame.Instance.Mode != NetMode.Offline;

    static int Step(int value, int dir, int min, int max, bool wrap)
    {
        value += dir;
        if (wrap) return value > max ? min : value < min ? max : value;
        return Mathf.Clamp(value, min, max);
    }

    /// <summary>Esquerda/direita (dir = -1/+1) ou CONFIRMAR (wrap: passa do último para o primeiro).</summary>
    void ChangeSetting(MenuAction a, int dir, bool wrap)
    {
        switch (a)
        {
            case MenuAction.Volume:
                volume = Step(volume, dir, 0, 10, wrap);
                AudioListener.volume = volume / 10f;
                PlayerPrefs.SetInt(PrefVolume, volume);
                break;
            case MenuAction.Fullscreen:
                if (Screen.fullScreen) Screen.SetResolution(1280, 720, FullScreenMode.Windowed);
                else Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, FullScreenMode.FullScreenWindow);
                break;
            case MenuAction.Resolution:
                StepResolution(dir);
                break;
            case MenuAction.WinPoints:
                WinScore = Step(WinScore, dir, MinWinScore, MaxWinScore, wrap);
                PlayerPrefs.SetInt(PrefWinScore, WinScore);
                break;
            case MenuAction.ScreenShake:
                shakeOn = !shakeOn;
                PlayerPrefs.SetInt(PrefShake, shakeOn ? 1 : 0);
                if (shakeOn) Shake(0.15f);
                break;
            default: return;
        }
        PlayerPrefs.Save();
        Sfx.Play("tick");
    }

    /// <summary>Próxima/anterior resolução do monitor (o Unity guarda a escolhida sozinho).</summary>
    static void StepResolution(int dir)
    {
        var list = Screen.resolutions.Select(r => new Vector2Int(r.width, r.height)).Distinct().OrderBy(v => v.x * v.y).ToList();
        if (list.Count == 0) return;
        int cur = list.FindIndex(v => v.x == Screen.width && v.y == Screen.height);
        if (cur < 0) cur = list.FindLastIndex(v => v.x * v.y <= Screen.width * Screen.height);
        int next = Step(cur, dir, 0, list.Count - 1, true);
        Screen.SetResolution(list[next].x, list[next].y, Screen.fullScreenMode);
    }

    // ================================================================== itens de cada tela

    void AddItem(MenuAction action, string label, string detail = null, string value = null, bool enabled = true) =>
        menuItems.Add(new MenuItem { Action = action, Label = label, Detail = detail, Value = value, Enabled = enabled });

    void BuildMenu()
    {
        menuItems.Clear();
        switch (menuScreen)
        {
            case MenuScreen.Main:
                AddItem(MenuAction.Play, "JOGAR", "De 2 a 8 jogadores neste computador, com bots se quiser");
                AddItem(MenuAction.Online, "JOGAR ONLINE", "Crie uma sala ou entre com o código de um amigo");
                AddItem(MenuAction.Creator, "CRIAR FASE", "Monte uma fase e desafie os amigos a bater o seu tempo");
                AddItem(MenuAction.Settings, "CONFIGURAÇÕES", "Volume, tela e pontos para vencer");
                AddItem(MenuAction.Controls, "CONTROLES", "As teclas de cada teclado e do controle");
                if (CanQuit) AddItem(MenuAction.Quit, "SAIR");
                break;

            case MenuScreen.Pause:
                AddItem(MenuAction.Resume, "CONTINUAR");
                // no online cada computador tem as suas configurações, e quem mexe na pausa é o host
                AddItem(MenuAction.Settings, "CONFIGURAÇÕES", Online ? "só no jogo offline (ou no menu principal)" : null, enabled: !Online);
                AddItem(MenuAction.Controls, "CONTROLES");
                bool challenge = Phase == Phase.Challenge;
                AddItem(MenuAction.Leave, challenge ? "SAIR DO DESAFIO" : "SAIR DA PARTIDA",
                        challenge ? "volta para o criador de fases" : "volta para o lobby (a partida acaba para todos)");
                break;

            case MenuScreen.Settings:
                AddItem(MenuAction.Volume, "VOLUME", value: volume == 0 ? "MUDO" : $"{volume * 10}%");
                AddItem(MenuAction.Fullscreen, "TELA CHEIA", CanChangeVideo ? null : "no navegador, use o botão de tela cheia da página",
                        Screen.fullScreen ? "SIM" : "NÃO", CanChangeVideo);
                AddItem(MenuAction.Resolution, "RESOLUÇÃO", CanChangeVideo ? (Screen.fullScreen ? "nitidez da imagem na tela cheia" : "tamanho da janela") : "só no jogo baixado",
                        $"{Screen.width} x {Screen.height}", CanChangeVideo);
                AddItem(MenuAction.WinPoints, "PONTOS PARA VENCER", "o primeiro a chegar nesses pontos vence a partida", WinScore.ToString());
                AddItem(MenuAction.ScreenShake, "TREMER A TELA", "quando alguém morre ou algo explode", shakeOn ? "SIM" : "NÃO");
                AddItem(MenuAction.Back, "VOLTAR");
                break;

            case MenuScreen.Controls:
                AddItem(MenuAction.Back, "VOLTAR");
                break;
        }
    }

    // ================================================================== navegação

    void OpenMenuScreen(MenuScreen screen, MenuAction? select = null)
    {
        if ((screen == MenuScreen.Settings || screen == MenuScreen.Controls) && (menuScreen == MenuScreen.Main || menuScreen == MenuScreen.Pause))
            menuParent = menuScreen;
        menuScreen = screen;
        BuildMenu();
        menuSel = select.HasValue ? Mathf.Max(0, menuItems.FindIndex(i => i.Action == select.Value)) : 0;
        transitionTime = Time.unscaledTime;
    }

    void OpenMainMenu()
    {
        SetPhase(Phase.Menu);
        OpenMenuScreen(MenuScreen.Main);
    }

    /// <summary>Do lobby ou do criador de fases para o menu principal (quem entrou no lobby continua lá).</summary>
    void BackToMenu()
    {
        BackToLobby();
        OpenMainMenu();
        Sfx.Play("tick");
    }

    /// <summary>Menu principal ou pausa aberta: qualquer dispositivo (e o mouse) mexe.</summary>
    void UpdateMenu()
    {
        Vector2 mp = InputSource.MousePosition;
        mouseMoved = (mp - lastMouse).sqrMagnitude > 1f;
        lastMouse = mp;

        BuildMenu();
        int dy = 0, dx = 0;
        bool confirm = false, back = false;
        foreach (var d in ActiveDevices())
        {
            dy += d.MoveYPressed();
            dx += d.MoveXPressed();
            confirm |= d.ConfirmDown;
            back |= d.BackDown || d.PauseDown;
        }
        if (menuClicked >= 0 && menuClicked < menuItems.Count)
        {
            menuSel = menuClicked;
            if (menuClickDx != 0) dx = menuClickDx;
            else confirm = true;
        }
        menuClicked = -1;
        menuClickDx = 0;

        int n = menuItems.Count;
        if (dy != 0)
        {
            menuSel = (menuSel - System.Math.Sign(dy) + n) % n;
            Sfx.Play("tick");
        }
        menuSel = Mathf.Clamp(menuSel, 0, n - 1);
        var item = menuItems[menuSel];

        if (back) { MenuBack(); return; }
        if (dx != 0 && item.Value != null)
        {
            if (item.Enabled) ChangeSetting(item.Action, System.Math.Sign(dx), false);
            return;
        }
        if (!confirm) return;
        if (!item.Enabled) { Sfx.Play("error"); return; }
        RunMenuAction(item.Action);
    }

    void MenuBack()
    {
        switch (menuScreen)
        {
            case MenuScreen.Settings: OpenMenuScreen(menuParent, MenuAction.Settings); break;
            case MenuScreen.Controls: OpenMenuScreen(menuParent, MenuAction.Controls); break;
            case MenuScreen.Pause: SetPaused(false); break;
            default: return; // no menu principal não há para onde voltar
        }
        Sfx.Play("tick");
    }

    void RunMenuAction(MenuAction a)
    {
        switch (a)
        {
            case MenuAction.Play:
            case MenuAction.Online:
                SetPhase(Phase.Lobby);
                transitionTime = Time.unscaledTime;
                if (a == MenuAction.Online) NetGame.PanelOpen = true;
                Sfx.Play("pick");
                break;
            case MenuAction.Creator:
                OpenCreator();
                transitionTime = Time.unscaledTime;
                break;
            case MenuAction.Settings: OpenMenuScreen(MenuScreen.Settings); Sfx.Play("tick"); break;
            case MenuAction.Controls: OpenMenuScreen(MenuScreen.Controls); Sfx.Play("tick"); break;
            case MenuAction.Quit:
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                break;
            case MenuAction.Resume: SetPaused(false); Sfx.Play("tick"); break;
            case MenuAction.Leave:
                if (Phase == Phase.Challenge) { SetPaused(false); ExitChallenge(); }
                else BackToLobby();
                transitionTime = Time.unscaledTime;
                break;
            case MenuAction.Back: MenuBack(); break;
            default: ChangeSetting(a, 1, true); break; // CONFIRMAR numa configuração passa para o próximo valor
        }
    }

    // ================================================================== desenho

    /// <summary>Lista de itens; com valor, mostra "&lt; valor &gt;" à direita (clicar nas setas muda).</summary>
    void DrawMenuItems(float y, float w, float h, float gap)
    {
        float k = Mathf.Clamp01((Time.unscaledTime - transitionTime) / 0.22f);
        float x = (VW - w) / 2f;
        y += (1f - k) * (1f - k) * 30f; // ao abrir a tela a lista sobe um pouquinho
        var e = Event.current;
        for (int i = 0; i < menuItems.Count; i++, y += h + gap)
        {
            var it = menuItems[i];
            var r = R(x, y, w, h);
            float vx = x + w - 330f;
            if (!Replica && r.Contains(e.mousePosition))
            {
                if (mouseMoved) menuSel = i;
                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    menuClicked = i;
                    if (it.Value != null && R(vx, y, 60, h).Contains(e.mousePosition)) menuClickDx = -1;
                    else if (it.Value != null && R(vx + 240, y, 60, h).Contains(e.mousePosition)) menuClickDx = 1;
                    e.Use();
                }
            }

            bool sel = menuSel == i;
            Fill(r, sel ? new Color(0.3f, 0.2f, 0.45f, 0.95f) : new Color(0.08f, 0.06f, 0.13f, 0.9f));
            if (sel) Fill(R(x, y, 8, h), new Color(1f, 0.9f, 0.5f));
            Color c = it.Enabled ? Color.white : new Color(0.55f, 0.55f, 0.6f);
            bool detail = it.Detail != null;
            Text(R(x + 30, y, w - 60, detail ? h * 0.62f : h), it.Label, 28, c, TextAnchor.MiddleLeft);
            if (detail) Text(R(x + 30, y + h * 0.52f, w - 60, h * 0.42f), it.Detail, 17, new Color(0.75f, 0.73f, 0.85f), TextAnchor.MiddleLeft);
            if (it.Value != null)
            {
                Color ac = sel && it.Enabled ? new Color(1f, 0.9f, 0.5f) : new Color(0.55f, 0.55f, 0.6f);
                Text(R(vx, y, 60, h), "<", 32, ac);
                Text(R(vx + 60, y, 180, h), it.Value, 26, c);
                Text(R(vx + 240, y, 60, h), ">", 32, ac);
            }
        }
    }

    void DrawMenuFooter(string keys)
    {
        Fill(R(0, 1010, VW, 70), new Color(0.03f, 0.02f, 0.06f, 0.95f)); // cobre a ajuda da partida embaixo da pausa
        Keys(R(0, 1018, VW, 44), keys, 18, new Color(0.7f, 0.68f, 0.78f));
    }

    void DrawMainMenu()
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.02f, 0.06f, 0.62f));
        if (menuScreen != MenuScreen.Main) { DrawMenuSubScreen(); return; }

        float wob = Mathf.Sin(Time.unscaledTime * 2f) * 5f;
        Text(R(0, 50 + wob, VW, 130), "ULTIMATE ZUMBI HORSE", 104, new Color(0.55f, 0.92f, 0.4f));
        Text(R(0, 176, VW, 40), "Monte a fase, arme as armadilhas e seja o único zumbi a comer o cérebro!", 26, new Color(0.9f, 0.85f, 1f));
        BuildMenu();
        DrawMenuItems(250f, 760f, 80f, 10f);
        DrawZombieParade(900f);
        DrawMenuFooter("[W] [S]  [^] [v]  ou analógico: escolher   •   [ESPAÇO] [ENTER] {A} ou clique: confirmar");
    }

    /// <summary>Fila de zumbis (um de cada visual) andando pela parte de baixo da tela de título.</summary>
    void DrawZombieParade(float y)
    {
        float w = 72f, h = 80f, spacing = 150f, span = Art.SkinCount * spacing;
        float t = Time.unscaledTime;
        for (int i = 0; i < Art.SkinCount; i++)
        {
            float x = Mathf.Repeat(t * 55f + i * spacing, Mathf.Max(span, VW + w)) - w;
            var frames = Art.ZombieFrames(i, i);
            float bob = Mathf.Abs(Mathf.Sin(t * 7f + i)) * 3f;
            DrawSprite(R(x, y - bob, w, h), frames[2 + (int)(t * 8f + i) % 4], Color.white);
        }
    }

    void DrawPauseMenu()
    {
        // nas configurações e nos controles a partida quase some, para os textos dela não brigarem com a tabela
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, menuScreen == MenuScreen.Pause ? 0.78f : 0.94f));
        if (menuScreen != MenuScreen.Pause) { DrawMenuSubScreen(); return; }

        Text(R(0, 110, VW, 110), "PAUSADO", 84, Color.white);
        BuildMenu();
        DrawMenuItems(260f, 760f, 80f, 10f);
        Text(R(0, 680, VW, 36), "Dicas: segure o pulo para ir mais alto  •  encoste na parede e pule para escalar  •  o primeiro a chegar ganha +1", 21, new Color(0.78f, 0.76f, 0.86f));
        DrawMenuFooter($"[W] [S]  [^] [v]: escolher   •   {AnyConfirm} confirmar   •   [ESC] {{START}} continuar");
    }

    /// <summary>Configurações ou controles (abertos pelo menu principal ou pela pausa).</summary>
    void DrawMenuSubScreen()
    {
        BuildMenu();
        if (menuScreen == MenuScreen.Settings)
        {
            Text(R(0, 60, VW, 100), "CONFIGURAÇÕES", 72, new Color(0.85f, 0.7f, 1f));
            DrawMenuItems(200f, 900f, 84f, 10f);
            DrawMenuFooter("[W] [S]  [^] [v]: escolher   •   [A] [D]  [<] [>]: mudar   •   [Q] [BACKSPACE] {B}: voltar");
            return;
        }

        Text(R(0, 40, VW, 90), "CONTROLES", 64, new Color(0.85f, 0.7f, 1f));
        float th = DrawControls(140f);
        float y = 140f + th + 22f;
        var hint = new Color(0.7f, 0.85f, 1f);
        Text(R(0, y, VW, 32), "NO LOBBY", 22, new Color(0.85f, 0.7f, 1f));
        Keys(R(0, y + 34, VW, 32), "[W] [^] ou CIMA coloca bot  •  [S] [v] ou BAIXO tira  •  [E] [SHIFT] {RB} muda a dificuldade do último bot", 18, hint);
        Keys(R(0, y + 68, VW, 32), "[A] [D]  [<] [>] troca o mapa  •  [R] [CTRL] {LB} troca o visual do zumbi  •  [O] jogar online  •  [C] {SELECT} criar fase", 18, hint);
        Text(R(0, y + 112, VW, 32), "NA CORRIDA", 22, new Color(0.85f, 0.7f, 1f));
        Text(R(0, y + 146, VW, 32), "Segure o pulo para ir mais alto  •  encoste na parede e pule para escalar  •  dance parado no chão", 18, hint);
        DrawMenuItems(y + 200f, 600f, 64f, 0f);
        DrawMenuFooter("[Q] [BACKSPACE] {B} ou [ESC] {START}: voltar");
    }

    /// <summary>Escurece a tela por um instante logo depois de trocar de tela.</summary>
    void DrawTransition()
    {
        float t = (Time.unscaledTime - transitionTime) / 0.25f;
        if (t >= 1f) return;
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.02f, 0.01f, 0.05f, (1f - t) * 0.6f));
    }
}
