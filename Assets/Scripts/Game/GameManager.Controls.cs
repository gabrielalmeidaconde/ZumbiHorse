using UnityEngine;
using K = InputSource.K;

/// <summary>
/// Tela de controles: o controle 8-bit e os dois lados do teclado desenhados, com a legenda de cada botão puxada
/// por uma linha até ele. O que está sendo apertado acende no desenho (dá para testar o controle), e a aba
/// muda sozinha para o aparelho que está sendo usado.
/// </summary>
public partial class GameManager
{
    // abas: 0 controle, 1 teclado WASD, 2 teclado setas
    static readonly string[] ControlTabs = { "CONTROLE", "TECLADO  ·  WASD", "TECLADO  ·  SETAS" };
    int controlsTab;
    DeviceKind menuDevice = DeviceKind.Gamepad;   // quem mexeu no menu por último: a tela de controles abre na aba dele

    static int TabOf(DeviceKind k) => k == DeviceKind.Keyboard1 ? 1 : k == DeviceKind.Keyboard2 ? 2 : 0;

    enum Route { Side, Top, Bottom }   // a linha vai reta para o lado, ou entra na peça por cima / por baixo

    struct Callout
    {
        public string Keys;   // botão ou tecla, na marcação do Keys() ({A}, [Q]...)
        public string Text;
        public bool Right;    // legenda do lado direito do desenho
        public Route Route;
        public int Row;       // fileira das legendas de cima/de baixo (0 = a mais perto do desenho)
        public int[] Parts;   // peças que acendem esta legenda; a linha encosta na primeira
    }

    static Callout Co(string keys, string text, bool right, Route route, int row, params int[] parts) =>
        new Callout { Keys = keys, Text = text, Right = right, Route = route, Row = row, Parts = parts };

    struct KeyDef
    {
        public string Label;
        public float X, Y, W;   // em teclas (posição do canto de cima à esquerda e largura)
        public K[] Keys;        // vazio = tecla só de enfeite (não faz nada no jogo)
    }

    static KeyDef Kd(string label, float x, float y, float w, params K[] keys) => new KeyDef { Label = label, X = x, Y = y, W = w, Keys = keys };

    // ---- controle: peças 0-7 os botões (A B X Y LB RB Start Select), 8 direcional, 9 analógico
    static readonly Callout[] PadCallouts =
    {
        Co("{SELECT}", "Criar fase (lobby)  •  testar (criador)", false, Route.Top, 1, 7),
        Co("{LB}", "Girar item  •  trocar o zumbi (lobby)", false, Route.Top, 0, 4),
        Co("{D-PAD}", "Mover  •  escolher nos menus", false, Route.Side, 0, 8),
        Co("{ANALÓGICO}", "Mover", false, Route.Bottom, 0, 9),
        Co("{START}", "Pausar", true, Route.Top, 1, 6),
        Co("{RB}", "Girar item  •  dificuldade do bot (lobby)", true, Route.Top, 0, 5),
        Co("{Y}", "Girar item  •  modo de jogo (lobby)", true, Route.Side, 0, 3),
        Co("{B}", "Voltar  •  desfazer (criador)", true, Route.Side, 0, 1),
        Co("{A}", "Pular  •  confirmar", true, Route.Side, 0, 0),
        Co("{X}", "Dançar  •  girar item", true, Route.Bottom, 0, 2),
    };

    // ---- teclado WASD (lado esquerdo do teclado)
    static readonly KeyDef[] WasdKeys =
    {
        Kd("ESC", 0f, 0f, 1f, K.Escape),                                                                  // 0
        Kd("Q", 0.5f, 1.2f, 1f, K.Q), Kd("W", 1.5f, 1.2f, 1f, K.W), Kd("E", 2.5f, 1.2f, 1f, K.E),         // 1 2 3
        Kd("R", 3.5f, 1.2f, 1f, K.R), Kd("T", 4.5f, 1.2f, 1f, K.T),                                       // 4 5
        Kd("A", 0.8f, 2.4f, 1f, K.A), Kd("S", 1.8f, 2.4f, 1f, K.S), Kd("D", 2.8f, 2.4f, 1f, K.D),         // 6 7 8
        Kd("F", 3.8f, 2.4f, 1f, K.F),                                                                     // 9
        Kd("Z", 1.3f, 3.6f, 1f, K.Z), Kd("X", 2.3f, 3.6f, 1f), Kd("C", 3.3f, 3.6f, 1f, K.C),              // 10 11 12
        Kd("ESPAÇO", 2.0f, 4.8f, 3.6f, K.Space),                                                          // 13
    };

    static readonly Callout[] WasdCallouts =
    {
        Co("[ESC]", "Pausar", false, Route.Side, 0, 0),
        Co("[Q]", "Voltar  •  girar item", false, Route.Side, 0, 1),
        Co("[W] [A] [S] [D]", "Mover", false, Route.Side, 0, 6, 2, 7, 8),
        Co("[Z]", "Desfazer (criador)", false, Route.Side, 0, 10),
        Co("[W]", "Pular", true, Route.Top, 2, 2),
        Co("[E]", "Girar item  •  dificuldade do bot (lobby)", true, Route.Top, 1, 3),
        Co("[R]", "Trocar o zumbi (lobby)  •  girar (criador)", true, Route.Top, 0, 4),
        Co("[T]", "Testar a fase (criador)", true, Route.Side, 0, 5),
        Co("[F]", "Dançar", true, Route.Side, 0, 9),
        Co("[C]", "Criar fase (lobby)", true, Route.Side, 0, 12),
        Co("[ESPAÇO]", "Pular  •  confirmar", true, Route.Side, 0, 13),
    };

    // ---- teclado setas (lado direito do teclado; o 0, o Enter e o . do numérico também valem)
    static readonly KeyDef[] ArrowKeys =
    {
        Kd("ESC", 0f, 0f, 1f, K.Escape),                                  // 0
        Kd("BACKSPACE", 3.0f, 0f, 2.2f, K.Backspace),                     // 1
        Kd("DELETE", 7.0f, 0f, 1.4f, K.Delete),                           // 2
        Kd("ENTER", 3.5f, 1.2f, 1.7f, K.Enter, K.NumEnter),               // 3
        Kd(".", 1.5f, 2.4f, 1f, K.Period, K.NumPeriod),                   // 4
        Kd("SHIFT", 3.0f, 2.4f, 2.2f, K.RightShift),                      // 5
        Kd("CTRL", 3.5f, 3.6f, 1.7f, K.RightCtrl),                        // 6
        Kd("^", 8.0f, 3.6f, 1f, K.Up, K.Num0),                            // 7
        Kd("<", 7.0f, 4.8f, 1f, K.Left), Kd("v", 8.0f, 4.8f, 1f, K.Down), // 8 9
        Kd(">", 9.0f, 4.8f, 1f, K.Right),                                 // 10
    };

    static readonly Callout[] ArrowCallouts =
    {
        Co("[ESC]", "Pausar", false, Route.Side, 0, 0),
        Co("[ENTER]", "Confirmar", false, Route.Side, 0, 3),
        Co("[.]", "Dançar", false, Route.Side, 0, 4),
        Co("[CTRL]", "Girar item  •  trocar o zumbi (lobby)", false, Route.Side, 0, 6),
        Co("[BACKSPACE]", "Voltar  •  desfazer (criador)", true, Route.Top, 0, 1),
        Co("[DELETE]", "Girar (criador)", true, Route.Side, 0, 2),
        Co("[SHIFT]", "Girar item  •  dificuldade do bot (lobby)", true, Route.Side, 0, 5),
        Co("[^]", "Pular", true, Route.Side, 0, 7),
        Co("[<] [^] [v] [>]", "Mover", true, Route.Side, 0, 10, 7, 8, 9),
    };

    static readonly Color CalloutColor = new Color(0.78f, 0.74f, 0.92f), LitColor = new Color(1f, 0.89f, 0.47f);

    // ================================================================== estado (o que está apertado)

    /// <summary>Tudo o que está apertado nos controles deste computador, nos bits do Art.Gamepad.</summary>
    static int PadLitMask()
    {
        int mask = 0;
        foreach (var d in InputSource.AvailableDevices())
        {
            if (d.Kind != DeviceKind.Gamepad || d.Remote || !d.Connected) continue;
            for (int b = 0; b < 8; b++) if (d.PadButtonHeld(b)) mask |= 1 << b;
            mask |= Dirs(d.PadDpad) << 8;
            mask |= Dirs(d.PadStick) << 12;
        }
        return mask;
    }

    /// <summary>Direção em 4 bits: cima, baixo, esquerda, direita.</summary>
    static int Dirs(Vector2 v) => (v.y > 0.5f ? 1 : 0) | (v.y < -0.5f ? 2 : 0) | (v.x < -0.5f ? 4 : 0) | (v.x > 0.5f ? 8 : 0);

    static bool PadPartLit(int mask, int part) => part < 8 ? (mask & (1 << part)) != 0 : (mask & (part == 8 ? 0xF00 : 0xF000)) != 0;

    static bool KeyLit(KeyDef k)
    {
        foreach (var key in k.Keys) if (InputSource.KeyHeld(key)) return true;
        return false;
    }

    /// <summary>Alguma tecla daquele lado do teclado apertada (o Esc é dos dois lados, então não conta).</summary>
    static bool AnyKeyLit(KeyDef[] keys)
    {
        foreach (var k in keys) if (k.Label != "ESC" && KeyLit(k)) return true;
        return false;
    }

    // ================================================================== desenho

    void DrawControlsScreen()
    {
        var e = Event.current;
        int padMask = PadLitMask();
        // a aba segue o aparelho que está sendo usado
        if (e.type == EventType.Repaint)
        {
            if (padMask != 0) controlsTab = 0;
            else if (AnyKeyLit(WasdKeys)) controlsTab = 1;
            else if (AnyKeyLit(ArrowKeys)) controlsTab = 2;
        }

        // a fase atrás quase some, para as legendas não brigarem com o cenário
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0.03f, 0.02f, 0.06f, 0.6f));
        Text(R(0, 18, VW, 84), "CONTROLES", 60, new Color(0.85f, 0.7f, 1f));
        DrawControlTabs(108f);

        float cy = 530f;
        if (controlsTab == 0)
        {
            float k = Mathf.Clamp(Mathf.Floor((VW - 1120f) / Art.GamepadW), 4f, 7f);
            float w = Art.GamepadW * k, h = Art.GamepadH * k;
            var box = new Rect((VW - w) / 2f, cy - h / 2f, w, h);
            DrawSprite(R(box.x, box.y, w, h), Art.Gamepad(padMask), Color.white);
            foreach (var c in PadCallouts)
            {
                Vector2 a = Art.GamepadAnchor(c.Parts[0]);
                bool lit = false;
                foreach (int p in c.Parts) lit |= PadPartLit(padMask, p);
                DrawCallout(c, box, new Vector2(box.x + a.x * k, box.y + a.y * k), lit);
            }
            bool anyPad = InputSource.GamepadCount > 0;
            Text(R(0, 790, VW, 34), anyPad ? "Aperte os botões do controle: eles acendem no desenho" : "Nenhum controle conectado: conecte um e aperte os botões para testar",
                 21, anyPad ? new Color(0.7f, 1f, 0.6f) : new Color(1f, 0.75f, 0.55f));
        }
        else
        {
            var keys = controlsTab == 1 ? WasdKeys : ArrowKeys;
            var callouts = controlsTab == 1 ? WasdCallouts : ArrowCallouts;
            float u = VW >= 1700f ? 72f : 58f, gap = u / 9f;
            float cols = 0f, rows = 0f;
            foreach (var key in keys) { cols = Mathf.Max(cols, key.X + key.W); rows = Mathf.Max(rows, key.Y + 1f); }
            float w = cols * u - gap, h = rows * u - gap;
            var box = new Rect((VW - w) / 2f, cy - h / 2f, w, h);
            Rect KeyRect(KeyDef d) => new Rect(box.x + d.X * u, box.y + d.Y * u, d.W * u - gap, u - gap);

            foreach (var key in keys) DrawKeyDef(KeyRect(key), key, KeyLit(key));
            foreach (var c in callouts)
            {
                var r = KeyRect(keys[c.Parts[0]]);
                Vector2 a = c.Route == Route.Top ? new Vector2(r.center.x, r.y)
                          : c.Route == Route.Bottom ? new Vector2(r.center.x, r.yMax)
                          : new Vector2(c.Right ? r.xMax : r.x, r.center.y);
                bool lit = false;
                foreach (int p in c.Parts) lit |= KeyLit(keys[p]);
                DrawCallout(c, box, a, lit);
            }
            Text(R(0, 790, VW, 34), controlsTab == 1 ? "Aperte as teclas: elas acendem no desenho"
                                                     : "Aperte as teclas: elas acendem no desenho  •  no teclado numérico, 0 pula, Enter confirma e . dança",
                 21, new Color(0.7f, 1f, 0.6f));
        }

        var hint = new Color(0.7f, 0.85f, 1f);
        Text(R(0, 832, VW, 30), "No lobby: cima e baixo colocam e tiram bots  •  esquerda e direita trocam o mapa  •  M troca o modo  •  O abre o jogo online", 18, hint);
        Text(R(0, 864, VW, 30), "Na corrida: segure o pulo para ir mais alto  •  encoste na parede e pule para escalar  •  dance parado no chão", 18, hint);
        DrawMenuItems(912f, 420f, 60f, 0f);
        DrawMenuFooter("[Q] [BACKSPACE] {B} ou [ESC] {START}: voltar   •   clique nas abas para ver os outros");
    }

    void DrawControlTabs(float y)
    {
        var e = Event.current;
        float w = 360f, h = 48f, gap = 14f, x = (VW - (ControlTabs.Length * w + (ControlTabs.Length - 1) * gap)) / 2f;
        for (int t = 0; t < ControlTabs.Length; t++, x += w + gap)
        {
            var r = R(x, y, w, h);
            bool sel = controlsTab == t;
            if (r.Contains(e.mousePosition) && e.type == EventType.MouseDown && e.button == 0)
            {
                if (!sel) Sfx.Play("tick");
                controlsTab = t;
                e.Use();
            }
            Fill(r, sel ? new Color(0.3f, 0.2f, 0.45f, 0.95f) : new Color(0.08f, 0.06f, 0.13f, 0.9f));
            if (sel) Fill(R(x, y + h - 5, w, 5), new Color(1f, 0.9f, 0.5f));

            // quem está jogando com este aparelho: as cores dos jogadores ao lado do nome
            DeviceKind kind = t == 0 ? DeviceKind.Gamepad : t == 1 ? DeviceKind.Keyboard1 : DeviceKind.Keyboard2;
            int users = 0;
            foreach (var p in Players) if (!p.IsBot && p.Input.Kind == kind) users++;
            float titleW = TextWidth(ControlTabs[t], 22) / S, chipW = 40f, groupW = titleW + users * (chipW + 6f) + (users > 0 ? 8f : 0f);
            float tx = x + (w - groupW) / 2f;
            Text(R(tx, y, titleW, h), ControlTabs[t], 22, sel ? Color.white : new Color(0.72f, 0.7f, 0.8f), TextAnchor.MiddleLeft);
            float chipX = tx + titleW + 8f;
            foreach (var p in Players)
            {
                if (p.IsBot || p.Input.Kind != kind) continue;
                Fill(R(chipX, y + 11, chipW, 26), p.Color);
                Text(R(chipX, y + 11, chipW, 26), p.Name, 16, Color.white, TextAnchor.MiddleCenter, false);
                chipX += chipW + 6f;
            }
        }
    }

    /// <summary>Uma tecla do teclado desenhado: acesa quando apertada, apagada quando não faz nada no jogo.</summary>
    void DrawKeyDef(Rect virt, KeyDef d, bool lit)
    {
        var b = R(virt.x, virt.y, virt.width, virt.height);
        Color face = lit ? LitColor : d.Keys.Length == 0 ? new Color(0.36f, 0.34f, 0.44f) : CapFace;
        int dir = ArrowDir(d.Label);
        KeyCap(b, dir >= 0 ? "" : d.Label, d.Label.Length > 1 ? 22 : 32, face);
        if (dir >= 0)
        {
            float a = b.height * 0.42f, depth = Mathf.Max(2f, 4f * S);
            DrawSprite(new Rect(b.center.x - a / 2f, b.center.y - a / 2f - depth / 2f, a, a), Art.Arrow(dir), CapInk);
        }
    }

    /// <summary>
    /// Legenda ao lado do desenho com uma linha até a peça: reta para o lado, ou com uma dobra para entrar
    /// por cima/por baixo. Tudo em coordenadas virtuais (1080 de altura).
    /// </summary>
    void DrawCallout(Callout c, Rect box, Vector2 anchor, bool lit)
    {
        const float RowGap = 46f, Margin = 44f;
        float y = c.Route == Route.Side ? anchor.y
                : c.Route == Route.Top ? box.y - 42f - c.Row * RowGap
                : box.yMax + 42f + c.Row * RowGap;
        float startX = c.Right ? box.xMax + Margin : box.x - Margin;
        Color col = lit ? LitColor : CalloutColor;

        // contorno escuro primeiro, depois a cor (as duas pernas da dobra se encontram sem emenda)
        var corner = new Vector2(anchor.x, y);
        for (int pass = 0; pass < 2; pass++)
        {
            float t = pass == 0 ? 6f : 3f;
            Color lc = pass == 0 ? new Color(0.04f, 0.03f, 0.07f, 0.9f) : col;
            Segment(new Vector2(startX, y), corner, t, lc);
            if (c.Route != Route.Side) Segment(corner, anchor, t, lc);
            float dot = pass == 0 ? 12f : 8f;
            Fill(R(anchor.x - dot / 2f, anchor.y - dot / 2f, dot, dot), lc);
            Fill(R(startX - dot / 2f, y - dot / 2f, dot, dot), lc);
        }

        int size = VW >= 1700f ? 20 : 17;
        float labelW = (c.Right ? VW - startX : startX) - 24f;
        string s = c.Right ? $"{c.Keys}  {c.Text}" : $"{c.Text}  {c.Keys}";
        var r = c.Right ? R(startX + 14f, y - 20f, labelW, 40f) : R(startX - 14f - labelW, y - 20f, labelW, 40f);
        Keys(r, s, size, lit ? LitColor : new Color(0.9f, 0.88f, 0.97f), c.Right ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
    }

    /// <summary>Segmento reto (horizontal ou vertical) de espessura t.</summary>
    void Segment(Vector2 a, Vector2 b, float t, Color c)
    {
        float x = Mathf.Min(a.x, b.x) - t / 2f, y = Mathf.Min(a.y, b.y) - t / 2f;
        Fill(R(x, y, Mathf.Abs(b.x - a.x) + t, Mathf.Abs(b.y - a.y) + t), c);
    }
}
