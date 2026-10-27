using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Toda a arte do jogo é pixel art gerada por código (nenhum asset externo).
/// 16 pixels = 1 unidade do mundo = 1 célula da grade.
/// </summary>
public static class Art
{
    public const int PPU = 16;

    static readonly Dictionary<string, Sprite> cache = new Dictionary<string, Sprite>();

    // ------------------------------------------------------------------ cores dos jogadores

    public static readonly Color[] PlayerColors =
    {
        new Color32(235, 75, 70, 255),   // vermelho
        new Color32(70, 145, 245, 255),  // azul
        new Color32(245, 200, 55, 255),  // amarelo
        new Color32(185, 95, 235, 255),  // roxo
        new Color32(245, 140, 45, 255),  // laranja
        new Color32(45, 200, 185, 255),  // verde-água
        new Color32(245, 110, 180, 255), // rosa
        new Color32(225, 225, 235, 255), // branco
    };

    /// <summary>Cor do jogador i (P1 = 0).</summary>
    public static Color PlayerColor(int i) => PlayerColors[Mathf.Abs(i) % PlayerColors.Length];

    static readonly Color32[] SkinColors =
    {
        new Color32(125, 185, 95, 255),
        new Color32(150, 178, 140, 255),
        new Color32(105, 178, 158, 255),
        new Color32(168, 182, 88, 255),
        new Color32(140, 190, 110, 255),
        new Color32(120, 170, 150, 255),
        new Color32(160, 175, 100, 255),
        new Color32(130, 180, 125, 255),
    };

    static readonly Color32[] HairColors =
    {
        new Color32(42, 40, 36, 255),
        new Color32(100, 62, 36, 255),
        new Color32(205, 205, 195, 255),
        new Color32(200, 95, 40, 255),
        new Color32(60, 60, 70, 255),
        new Color32(150, 110, 60, 255),
        new Color32(90, 40, 30, 255),
        new Color32(230, 200, 120, 255),
    };

    public static readonly Color32 Outline = new Color32(22, 18, 28, 255);
    static readonly Color32 Clear = new Color32(0, 0, 0, 0);

    // ------------------------------------------------------------------ canvas auxiliar

    /// <summary>Imagem RGBA simples com y = 0 na linha de CIMA (como os mapas em texto).</summary>
    class Canvas
    {
        public readonly int W, H;
        public readonly Color32[] Px;

        public Canvas(int w, int h) { W = w; H = h; Px = new Color32[w * h]; }

        public bool In(int x, int y) => x >= 0 && y >= 0 && x < W && y < H;
        public void Set(int x, int y, Color32 c) { if (In(x, y)) Px[y * W + x] = c; }
        public Color32 Get(int x, int y) => In(x, y) ? Px[y * W + x] : Clear;
        public bool Filled(int x, int y) => Get(x, y).a > 0;

        public void Rect(int x, int y, int w, int h, Color32 c)
        {
            for (int j = y; j < y + h; j++)
                for (int i = x; i < x + w; i++) Set(i, j, c);
        }

        public void Disc(float cx, float cy, float r, Color32 c)
        {
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    if (dx * dx + dy * dy <= r * r) Set(x, y, c);
                }
        }

        public void Map(string[] rows, Func<char, Color32?> palette, int ox = 0, int oy = 0)
        {
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                {
                    var c = palette(rows[y][x]);
                    if (c.HasValue) Set(x + ox, y + oy, c.Value);
                }
        }

        /// <summary>Contorno de 1 pixel em volta de tudo que está preenchido.</summary>
        public void AddOutline(Color32 c)
        {
            var copy = (Color32[])Px.Clone();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    if (copy[y * W + x].a > 0) continue;
                    bool n = (x > 0 && copy[y * W + x - 1].a > 0) || (x < W - 1 && copy[y * W + x + 1].a > 0) ||
                             (y > 0 && copy[(y - 1) * W + x].a > 0) || (y < H - 1 && copy[(y + 1) * W + x].a > 0);
                    if (n) Px[y * W + x] = c;
                }
        }

        /// <summary>Linha grossa (pincel 2x2), usada nos braços das danças.</summary>
        public void Line(int x0, int y0, int x1, int y1, Color32 c)
        {
            int n = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0), 1);
            for (int i = 0; i <= n; i++)
                Rect(x0 + Mathf.RoundToInt((x1 - x0) * i / (float)n), y0 + Mathf.RoundToInt((y1 - y0) * i / (float)n), 2, 2, c);
        }

        public void MirrorX()
        {
            for (int y = 0; y < H; y++) Array.Reverse(Px, y * W, W);
        }

        public Texture2D ToTexture(FilterMode filter = FilterMode.Point)
        {
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false)
            {
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
            };
            var flipped = new Color32[W * H];
            for (int y = 0; y < H; y++)
                Array.Copy(Px, y * W, flipped, (H - 1 - y) * W, W);
            tex.SetPixels32(flipped);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>pivotPx em pixels a partir do canto INFERIOR esquerdo.</summary>
        public Sprite ToSprite(Vector2 pivotPx, int ppu = PPU, FilterMode filter = FilterMode.Point)
        {
            var tex = ToTexture(filter);
            return Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(pivotPx.x / W, pivotPx.y / H), ppu, 0, SpriteMeshType.FullRect);
        }

        public Sprite ToSpriteCentered(int ppu = PPU) => ToSprite(new Vector2(W / 2f, H / 2f), ppu);
    }

    static Sprite Cached(string key, Func<Sprite> make)
    {
        if (!cache.TryGetValue(key, out var s) || s == null)
        {
            s = make();
            s.name = key;
            cache[key] = s;
        }
        return s;
    }

    /// <summary>
    /// Recria um sprite a partir do nome (a chave do cache). Usado no multiplayer online: o host manda só o nome
    /// do sprite e o cliente gera a mesma imagem. Devolve null se o nome não for conhecido.
    /// </summary>
    public static Sprite FromKey(string key)
    {
        if (cache.TryGetValue(key, out var s) && s != null) return s;
        var p = key.Split('_');
        int I(int i) => i < p.Length && int.TryParse(p[i], out int v) ? v : 0;
        bool B(int i) => i < p.Length && p[i] == bool.TrueString;
        switch (p[0])
        {
            case "pixel": return Pixel;
            case "cursor": return Cursor;
            case "crate": return Crate;
            case "plank": return Plank;
            case "bigstone": return BigStone;
            case "ice": return Ice;
            case "spikes": return Spikes;
            case "saw": return Saw;
            case "cannon": return Cannon;
            case "bone": return Bone;
            case "bomb": return Bomb;
            case "brain": return Brain;
            case "pedestal": return Pedestal;
            case "tree": return DeadTree;
            case "moon": return Moon;
            case "glow": return Glow;
            case "dot": return Dot;
            case "fog": return Fog;
            case "rail": return Rail;
            case "loco": return Locomotive;
            case "wagon": return Wagon;
            case "icetile": return IceTile;
            case "flamethrower": return Flamethrower;
            case "rottencrate": return RottenCrate;
            case "fanbase": return FanBase;
            case "fanblades": return FanBlades;
            case "platform": return MovingPlatform;
            case "seesaw": return SeesawPlank;
            case "seesawbase": return SeesawBase;
            case "zombie": return ZombieFrames(I(1))[Mathf.Clamp(I(2), 0, 7)];
            case "dance": return DanceFrame(I(1), I(2), I(3));
            case "ground": return Ground(B(1), I(2));
            case "stone": return Stone(B(1));
            case "spring": return Spring(B(1));
            case "tomb": return Tombstone(I(1));
            case "arrow": return Arrow(I(1));
            case "sky": return Sky(I(1));
            case "hills": return Hills(I(1), new Color32((byte)I(2), (byte)I(3), (byte)I(4), 255));
            case "acid": return Acid(B(1), I(2));
            case "sawrail": return SawRail(I(1));
            case "portal": return Portal(I(1), I(2));
            case "conveyor": return Conveyor(I(1));
            case "flame": return Flame(I(1));
            default: return null;
        }
    }

    static float Hash(int x, int y, int seed)
    {
        unchecked
        {
            int h = x * 374761393 + y * 668265263 + seed * 1442695041;
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }
    }

    static Color32 Shade(Color32 c, float k) =>
        new Color32((byte)Mathf.Clamp(c.r * k, 0, 255), (byte)Mathf.Clamp(c.g * k, 0, 255), (byte)Mathf.Clamp(c.b * k, 0, 255), c.a);

    static Color32 C(int r, int g, int b, int a = 255) => new Color32((byte)r, (byte)g, (byte)b, (byte)a);

    // ------------------------------------------------------------------ básicos

    /// <summary>Quadrado branco de 1x1 unidade (overlays, partículas, painéis).</summary>
    public static Sprite Pixel => Cached("pixel", () =>
    {
        var c = new Canvas(4, 4);
        c.Rect(0, 0, 4, 4, C(255, 255, 255));
        return c.ToSprite(new Vector2(2, 2), 4);
    });

    // ------------------------------------------------------------------ zumbis

    static readonly string[] ZombieUpper =
    {
        "..................",
        "..................",
        ".....HHHHHH.......",
        "....HHHHHHHH......",
        "....HGGHGGGHG.....",
        "....GGGGWWGWW.....",
        "....gGGGWRGWR.....",
        "....gGGGGGGGGG....",
        "....gGGGMMMMG.....",
        "....ggGGMTMTG.....",
        ".....gGGGGGG......",
        "....SSSSSSSssGGG..",
        "....SSSSSSSssGGGG.",
        "....sSSSBSS.......",
        "....sSSSSSS.......",
        "....sSSSSSs.......",
    };

    static readonly string[][] ZombieLegs =
    {
        new[] { ".....PPPPP........", ".....PP.PP........", ".....FF.FFF.......", ".................." }, // 0 parado
        new[] { ".....PPPPP........", "....pp...PP.......", "...ff.....FF......", ".................." }, // 1 passo A
        new[] { ".....PPPPP........", "......PPP.........", "......FFFF........", ".................." }, // 2 cruzando
        new[] { ".....PPPPP........", "....PP...pp.......", "...FF.....ff......", ".................." }, // 3 passo B
        new[] { ".....PPPPP........", "....PP..PP........", "....F....FF.......", ".................." }, // 4 pulo
        new[] { "....PPPPPPP.......", "...PP.....PP......", "..FF.......FF.....", ".................." }, // 5 base aberta
        new[] { ".....PPPPPPPP.....", ".....PP.....FF....", ".....FF...........", ".................." }, // 6 chute
        new[] { ".....PPPPP........", ".....PP.PPP.......", ".....FF...FF......", ".................." }, // 7 joelho para cima
        new[] { ".....PPPPP........", "......PP.PP.......", "......F...F.......", ".................." }, // 8 ponta do pé
        new[] { "...PPPPPPPPP......", "..PP.......PP.....", "..FF.......FF.....", ".................." }, // 9 cavalinho
    };

    /// <summary>Corpo do zumbi sem os braços (nas danças os braços são desenhados à parte).</summary>
    static readonly string[] ZombieBody =
    {
        "..................",
        "..................",
        ".....HHHHHH.......",
        "....HHHHHHHH......",
        "....HGGHGGGHG.....",
        "....GGGGWWGWW.....",
        "....gGGGWRGWR.....",
        "....gGGGGGGGGG....",
        "....gGGGMMMMG.....",
        "....ggGGMTMTG.....",
        ".....gGGGGGG......",
        "....SSSSSSSss.....",
        "....SSSSSSSss.....",
        "....sSSSBSS.......",
        "....sSSSSSS.......",
        "....sSSSSSs.......",
    };

    /// <summary>Pose de dança: pernas, tronco abaixado/deslocado e braços (cotovelo e mão em pixels do quadro).</summary>
    struct DancePose
    {
        public int Legs, Bob, Dx;
        public (int ex, int ey, int hx, int hy) Back, Front;
        public bool Flip;
    }

    static DancePose P(int legs, int bob, (int, int, int, int) back, (int, int, int, int) front, bool flip = false, int dx = 0) =>
        new DancePose { Legs = legs, Bob = bob, Dx = dx, Back = back, Front = front, Flip = flip };

    /// <summary>Danças das raças de World of Warcraft: quadros por segundo e poses.</summary>
    static readonly (float fps, DancePose[] poses)[] Dances =
    {
        // Morto-vivo
        (5f, new[]
        {
            P(1, 0, (7, 9, 12, 8), (14, 9, 16, 7)),
            P(3, 1, (7, 10, 12, 9), (14, 10, 16, 8)),
            P(1, 0, (7, 9, 12, 8), (14, 9, 16, 7), true),
            P(3, 1, (7, 10, 12, 9), (14, 10, 16, 8), true),
        }),
        // Humano
        (3f, new[]
        {
            P(5, 0, (1, 13, 3, 15), (13, 8, 15, 3)),
            P(5, 1, (1, 13, 3, 15), (13, 13, 15, 16)),
        }),
        // Orc
        (6f, new[]
        {
            P(5, 1, (1, 11, 0, 8), (14, 12, 16, 14), dx: -1),
            P(5, 1, (1, 12, 0, 15), (14, 11, 16, 8), dx: 1),
        }),
        // Anão
        (8f, new[]
        {
            P(6, 0, (3, 12, 3, 14), (11, 12, 11, 14)),
            P(0, 1, (3, 12, 3, 14), (11, 12, 11, 14)),
            P(7, 0, (3, 12, 3, 14), (11, 12, 11, 14)),
            P(0, 1, (3, 12, 3, 14), (11, 12, 11, 14)),
        }),
        // Elfo Noturno
        (4f, new[]
        {
            P(0, 0, (2, 13, 2, 15), (14, 7, 12, 2)),
            P(7, 0, (1, 11, 0, 9), (13, 13, 15, 15)),
            P(8, 0, (2, 13, 2, 15), (14, 7, 12, 2), true),
            P(7, 0, (1, 11, 0, 9), (13, 13, 15, 15), true),
        }),
        // Gnomo
        (3f, new[]
        {
            P(0, 0, (0, 11, 0, 14), (15, 11, 15, 8)),
            P(0, 0, (0, 11, 0, 8), (15, 11, 15, 14)),
        }),
        // Tauren
        (4f, new[]
        {
            P(1, 0, (2, 7, 1, 2), (14, 7, 16, 2)),
            P(2, 1, (2, 7, 3, 2), (14, 7, 14, 2)),
            P(3, 0, (2, 7, 1, 2), (14, 7, 16, 2)),
            P(2, 1, (2, 7, 3, 2), (14, 7, 14, 2)),
        }),
        // Troll
        (6f, new[]
        {
            P(1, 0, (2, 13, 1, 15), (12, 13, 13, 15)),
            P(2, 1, (2, 13, 1, 15), (12, 13, 13, 15)),
            P(3, 0, (2, 13, 1, 15), (12, 13, 13, 15), true),
            P(2, 1, (2, 13, 1, 15), (12, 13, 13, 15), true),
        }),
        // Draenei
        (4f, new[]
        {
            P(1, 0, (1, 9, 2, 5), (14, 9, 15, 5), dx: -1),
            P(3, 1, (1, 9, 0, 5), (14, 9, 16, 5), dx: 1),
        }),
        // Elfo Sangrento
        (5f, new[]
        {
            P(6, 0, (1, 10, 1, 7), (14, 10, 15, 7)),
            P(0, 1, (1, 12, 0, 14), (14, 12, 16, 14)),
            P(7, 0, (1, 10, 1, 7), (14, 10, 15, 7), true),
            P(0, 1, (1, 12, 0, 14), (14, 12, 16, 14), true),
        }),
        // Worgen
        (8f, new[]
        {
            P(5, 0, (8, 9, 15, 8), (13, 13, 12, 14)),
            P(5, 1, (8, 10, 15, 9), (13, 13, 14, 15)),
        }),
        // Goblin
        (5f, new[]
        {
            P(7, 0, (2, 13, 2, 15), (14, 9, 16, 7)),
            P(0, 1, (1, 11, 0, 9), (14, 13, 16, 15)),
            P(7, 0, (2, 13, 2, 15), (14, 9, 16, 7), true),
            P(0, 1, (1, 11, 0, 9), (14, 13, 16, 15), true),
        }),
        // Pandaren
        (6f, new[]
        {
            P(9, 0, (7, 13, 12, 13), (13, 13, 14, 12)),
            P(9, 1, (7, 13, 12, 13), (13, 13, 14, 12)),
            P(9, 0, (1, 13, 3, 15), (13, 6, 10, 1)),
            P(9, 1, (1, 13, 3, 15), (13, 6, 16, 1)),
        }),
    };

    public static int DanceCount => Dances.Length;

    /// <summary>Quadro da dança no tempo t (segundos desde que começou).</summary>
    public static Sprite DanceSprite(int variant, int dance, float t)
    {
        var d = Dances[Mathf.Abs(dance) % Dances.Length];
        return DanceFrame(variant, dance, (int)(t * d.fps) % d.poses.Length);
    }

    static Sprite DanceFrame(int variant, int dance, int frame)
    {
        variant = Mathf.Abs(variant) % PlayerColors.Length;
        dance = Mathf.Abs(dance) % Dances.Length;
        var pose = Dances[dance].poses[Mathf.Clamp(frame, 0, Dances[dance].poses.Length - 1)];
        return Cached($"dance_{variant}_{dance}_{frame}", () => BuildZombie(variant, pose.Legs, pose.Bob, pose));
    }

    /// <summary>Quadros do zumbi: 0-1 parado, 2-5 andando, 6 pulando, 7 caindo.</summary>
    public static Sprite[] ZombieFrames(int variant)
    {
        variant = Mathf.Abs(variant) % PlayerColors.Length;
        // (pernas, deslocamento do tronco para baixo)
        var spec = new (int legs, int bob)[] { (0, 0), (0, 1), (1, 0), (2, 1), (3, 0), (2, 1), (4, 0), (1, 0) };
        var frames = new Sprite[spec.Length];
        for (int i = 0; i < spec.Length; i++)
        {
            int idx = i;
            frames[i] = Cached($"zombie_{variant}_{i}", () => BuildZombie(variant, spec[idx].legs, spec[idx].bob));
        }
        return frames;
    }

    static Sprite BuildZombie(int variant, int legs, int bob, DancePose? dance = null)
    {
        Color32 skin = SkinColors[variant], skinD = Shade(skin, 0.75f);
        Color32 shirt = PlayerColors[variant], shirtD = Shade(shirt, 0.7f);
        Color32 hair = HairColors[variant];
        Color32 pants = C(62, 58, 92), pantsD = C(44, 40, 66);
        Color32 shoe = C(58, 44, 38), shoeD = C(38, 30, 26);

        Color32? Pal(char ch)
        {
            switch (ch)
            {
                case 'H': return hair;
                case 'G': return skin;
                case 'g': return skinD;
                case 'W': return C(240, 236, 175);
                case 'R': return C(205, 30, 30);
                case 'M': return C(75, 14, 22);
                case 'T': return C(232, 226, 200);
                case 'S': return shirt;
                case 's': return shirtD;
                case 'B': return C(130, 22, 26);
                case 'P': return pants;
                case 'p': return pantsD;
                case 'F': return shoe;
                case 'f': return shoeD;
                default: return null;
            }
        }

        var c = new Canvas(18, 20);
        c.Map(ZombieLegs[legs], Pal, 0, 16);
        if (dance is DancePose d)
        {
            // braço de trás atrás do corpo, o da frente por cima; manga até o cotovelo e pele até a mão
            void Arm(int sx, (int ex, int ey, int hx, int hy) a, Color32 sleeve, Color32 hand)
            {
                c.Line(sx + d.Dx, 11 + bob, a.ex + d.Dx, a.ey + bob, sleeve);
                c.Line(a.ex + d.Dx, a.ey + bob, a.hx + d.Dx, a.hy + bob, hand);
            }
            Arm(3, d.Back, shirtD, skinD);
            c.Map(ZombieBody, Pal, d.Dx, bob);
            Arm(11, d.Front, shirt, skin);
        }
        else c.Map(ZombieUpper, Pal, 0, bob);
        c.AddOutline(Outline);
        bool flip = dance.HasValue && dance.Value.Flip;
        if (flip) c.MirrorX();
        // pivô no centro do corpo, na sola dos pés (1 px acima da borda inferior)
        return c.ToSprite(new Vector2(flip ? 10.5f : 7.5f, 1f));
    }

    /// <summary>Mão/seta do cursor (branca, para ser tingida com a cor do jogador).</summary>
    public static Sprite Cursor => Cached("cursor", () =>
    {
        string[] map =
        {
            ".............",
            ".W...........",
            ".WW..........",
            ".WWW.........",
            ".WWWW........",
            ".WWWWW.......",
            ".WWWWWW......",
            ".WWWWWWW.....",
            ".WWWWWWWW....",
            ".WWWWW.......",
            ".WW.WWW......",
            ".W...WW......",
            "......WW.....",
            ".............",
        };
        var c = new Canvas(13, 14);
        c.Map(map, ch => ch == 'W' ? C(255, 255, 255) : (Color32?)null);
        c.AddOutline(Outline);
        return c.ToSprite(new Vector2(1, 13));
    });

    // ------------------------------------------------------------------ terreno

    /// <summary>Terra do cemitério. top = tem grama seca em cima.</summary>
    public static Sprite Ground(bool top, int seed) => Cached($"ground_{top}_{seed % 4}", () =>
    {
        int s = seed % 4;
        var c = new Canvas(16, 16);
        Color32 dirt = C(84, 62, 54), dirtD = C(64, 46, 42), dirtL = C(104, 80, 66);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                float h = Hash(x, y, 11 + s);
                c.Set(x, y, h < 0.12f ? dirtD : h > 0.92f ? dirtL : dirt);
            }
        // pedrinhas
        for (int k = 0; k < 2; k++)
        {
            int px = 2 + (int)(Hash(k, s, 3) * 11), py = (top ? 7 : 2) + (int)(Hash(s, k, 5) * 6);
            c.Rect(px, py, 2, 2, C(112, 106, 112));
            c.Set(px, py, C(140, 134, 140));
        }
        // um ossinho de vez em quando
        if (s == 2)
        {
            int bx = 5, by = top ? 10 : 6;
            c.Rect(bx, by, 6, 1, C(222, 215, 195));
            c.Set(bx - 1, by - 1, C(222, 215, 195)); c.Set(bx - 1, by + 1, C(222, 215, 195));
            c.Set(bx + 6, by - 1, C(222, 215, 195)); c.Set(bx + 6, by + 1, C(222, 215, 195));
        }
        if (top)
        {
            Color32 grass = C(98, 122, 62), grassL = C(134, 156, 80), grassD = C(70, 92, 48);
            for (int x = 0; x < 16; x++)
            {
                int depth = 3 + (Hash(x, 0, 21 + s) > 0.5f ? 1 : 0);
                for (int y = 0; y < depth; y++) c.Set(x, y, y == 0 ? grassL : grass);
                c.Set(x, depth, grassD);
            }
        }
        return c.ToSpriteCentered();
    });

    /// <summary>Tijolos de pedra (base indestrutível da largada e da chegada).</summary>
    public static Sprite Stone(bool top) => Cached($"stone_{top}", () =>
    {
        var c = new Canvas(16, 16);
        Color32 mortar = C(58, 52, 70), brick = C(108, 102, 122), brickL = C(128, 122, 142), brickD = C(90, 84, 104);
        c.Rect(0, 0, 16, 16, mortar);
        for (int row = 0; row < 2; row++)
        {
            int y0 = row * 8;
            int off = row == 0 ? 0 : 4;
            for (int b = -1; b < 2; b++)
            {
                int x0 = b * 8 + off;
                for (int y = y0 + 1; y < y0 + 8; y++)
                    for (int x = x0 + 1; x < x0 + 8; x++)
                    {
                        float h = Hash(x, y, 77);
                        c.Set(x, y, y == y0 + 1 ? brickL : h < 0.15f ? brickD : brick);
                    }
            }
        }
        if (top)
        {
            for (int x = 0; x < 16; x++)
            {
                int d = 2 + (Hash(x, 1, 90) > 0.6f ? 1 : 0);
                for (int y = 0; y < d; y++) c.Set(x, y, y == 0 ? C(120, 160, 90) : C(86, 126, 70));
            }
        }
        return c.ToSpriteCentered();
    });

    // ------------------------------------------------------------------ itens

    public static Sprite Crate => Cached("crate", () =>
    {
        var c = new Canvas(16, 16);
        Color32 dark = C(78, 46, 24), wood = C(164, 108, 58), woodD = C(132, 84, 44), woodL = C(196, 138, 76);
        c.Rect(0, 0, 16, 16, dark);
        c.Rect(1, 1, 14, 14, wood);
        for (int y = 1; y < 15; y++)
            for (int x = 1; x < 15; x++)
                if ((y - 1) % 5 == 4) c.Set(x, y, woodD);
        for (int i = 2; i < 14; i++) { c.Set(i, i, woodL); c.Set(i, i + 1 < 15 ? i + 1 : i, woodL); }
        c.Rect(1, 1, 14, 1, woodL);
        c.Rect(2, 2, 2, 2, dark); c.Rect(12, 2, 2, 2, dark); c.Rect(2, 12, 2, 2, dark); c.Rect(12, 12, 2, 2, dark);
        return c.ToSpriteCentered();
    });

    /// <summary>Prancha de 3 células de largura e meia célula de altura.</summary>
    public static Sprite Plank => Cached("plank", () =>
    {
        var c = new Canvas(48, 8);
        Color32 dark = C(78, 46, 24), wood = C(152, 100, 56), woodL = C(188, 132, 76), woodD = C(110, 70, 38);
        c.Rect(0, 0, 48, 8, dark);
        c.Rect(1, 1, 46, 6, wood);
        c.Rect(1, 1, 46, 1, woodL);
        c.Rect(1, 6, 46, 1, woodD);
        for (int x = 1; x < 47; x++) if (Hash(x, 3, 4) < 0.2f) c.Set(x, 3 + (x % 2), woodD);
        for (int seg = 0; seg < 3; seg++)
        {
            c.Set(seg * 16 + 3, 3, C(60, 60, 66)); c.Set(seg * 16 + 12, 3, C(60, 60, 66));
            if (seg > 0) c.Rect(seg * 16, 1, 1, 6, woodD);
        }
        return c.ToSpriteCentered();
    });

    /// <summary>Bloco de pedra 2x2 com cara de lápide rachada.</summary>
    public static Sprite BigStone => Cached("bigstone", () =>
    {
        var c = new Canvas(32, 32);
        Color32 mortar = C(62, 58, 74), stone = C(122, 118, 134), stoneL = C(146, 142, 158), stoneD = C(98, 94, 110);
        c.Rect(0, 0, 32, 32, mortar);
        for (int row = 0; row < 4; row++)
        {
            int y0 = row * 8, off = row % 2 == 0 ? 0 : 8;
            for (int b = -1; b < 3; b++)
            {
                int x0 = b * 16 + off;
                for (int y = y0 + 1; y < y0 + 8; y++)
                    for (int x = x0 + 1; x < x0 + 16; x++)
                    {
                        float h = Hash(x, y, 33);
                        c.Set(x, y, y == y0 + 1 ? stoneL : h < 0.12f ? stoneD : stone);
                    }
            }
        }
        // rachadura
        int cx = 20;
        for (int y = 3; y < 20; y++) { cx += Hash(y, 0, 8) > 0.5f ? 1 : -1; c.Set(cx, y, mortar); }
        // musgo
        for (int x = 0; x < 32; x++)
        {
            int d = Hash(x, 0, 41) > 0.55f ? 2 : 1;
            for (int y = 0; y < d; y++) c.Set(x, y, C(92, 132, 74));
        }
        return c.ToSpriteCentered();
    });

    public static Sprite Ice => Cached("ice", () =>
    {
        var c = new Canvas(32, 16);
        Color32 edge = C(84, 150, 205), ice = C(160, 215, 245), iceD = C(128, 192, 232), white = C(240, 250, 255);
        c.Rect(0, 0, 32, 16, edge);
        c.Rect(1, 1, 30, 14, ice);
        for (int y = 1; y < 15; y++)
            for (int x = 1; x < 31; x++)
                if (Hash(x, y, 55) < 0.1f) c.Set(x, y, iceD);
        for (int i = 0; i < 6; i++) { c.Set(4 + i, 3 + i, white); c.Set(5 + i, 3 + i, white); }
        for (int i = 0; i < 4; i++) c.Set(20 + i, 4 + i, white);
        c.Rect(1, 1, 30, 1, white);
        return c.ToSpriteCentered();
    });

    /// <summary>Espinhos apontando para cima (base embaixo).</summary>
    public static Sprite Spikes => Cached("spikes", () =>
    {
        var c = new Canvas(16, 16);
        Color32 baseC = C(72, 70, 84), baseL = C(100, 98, 112), metalL = C(210, 210, 222), metalD = C(140, 140, 158), blood = C(185, 30, 34);
        c.Rect(0, 11, 16, 5, baseC);
        c.Rect(0, 11, 16, 1, baseL);
        c.Set(3, 13, metalD); c.Set(12, 13, metalD);
        for (int s = 0; s < 3; s++)
        {
            int cx = 2 + s * 5 + (s == 2 ? 1 : 0);
            for (int y = 2; y <= 10; y++)
            {
                int half = (y - 2) / 3;
                for (int x = cx - half; x <= cx + half + 1; x++)
                    c.Set(x, y, x <= cx ? metalL : metalD);
            }
            c.Set(cx, 2, blood); c.Set(cx + 1, 2, blood); c.Set(cx, 3, blood);
        }
        return c.ToSprite(new Vector2(8, 8));
    });

    public static Sprite Saw => Cached("saw", () =>
    {
        int n = 22;
        var c = new Canvas(n, n);
        float cx = n / 2f, cy = n / 2f;
        Color32 metal = C(176, 176, 190), metalD = C(128, 128, 144), hub = C(80, 78, 92), red = C(190, 40, 40);
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Atan2(dy, dx) / (Mathf.PI * 2f) * 10f;
                float frac = a - Mathf.Floor(a);
                if (r < 7.5f) c.Set(x, y, r < 3f ? hub : (dx + dy < 0 ? metal : metalD));
                else if (r < 7.5f + 3f * (1f - frac)) c.Set(x, y, metalD);
            }
        c.Disc(cx, cy, 1.6f, red);
        // brilho
        c.Set(7, 6, C(235, 235, 245)); c.Set(8, 6, C(235, 235, 245)); c.Set(6, 7, C(235, 235, 245));
        c.AddOutline(Outline);
        return c.ToSpriteCentered();
    });

    /// <summary>Canhão de ossos (caveira com cano virado para a direita).</summary>
    public static Sprite Cannon => Cached("cannon", () =>
    {
        var c = new Canvas(16, 16);
        Color32 box = C(82, 62, 96), boxL = C(110, 86, 126), bone = C(232, 226, 204), boneD = C(186, 178, 156), barrel = C(48, 44, 56);
        c.Rect(0, 0, 16, 16, Outline);
        c.Rect(1, 1, 14, 14, box);
        c.Rect(1, 1, 14, 1, boxL);
        // caveira
        c.Rect(3, 3, 7, 6, bone);
        c.Rect(4, 9, 5, 2, bone);
        c.Rect(4, 5, 2, 2, Outline); c.Rect(7, 5, 2, 2, Outline);
        c.Set(6, 8, Outline);
        c.Set(4, 10, boneD); c.Set(6, 10, boneD); c.Set(8, 10, boneD);
        // cano
        c.Rect(10, 5, 6, 6, barrel);
        c.Rect(10, 5, 6, 1, C(80, 76, 90));
        c.Rect(15, 6, 1, 4, C(20, 16, 24));
        return c.ToSpriteCentered();
    });

    public static Sprite Bone => Cached("bone", () =>
    {
        var c = new Canvas(12, 7);
        Color32 bone = C(238, 232, 212), boneD = C(196, 188, 166);
        c.Rect(3, 3, 6, 1, bone);
        c.Rect(1, 1, 2, 2, bone); c.Rect(1, 4, 2, 2, bone);
        c.Rect(9, 1, 2, 2, bone); c.Rect(9, 4, 2, 2, bone);
        c.Set(3, 2, boneD); c.Set(8, 4, boneD);
        c.AddOutline(Outline);
        return c.ToSpriteCentered();
    });

    public static Sprite Spring(bool compressed) => Cached($"spring_{compressed}", () =>
    {
        var c = new Canvas(16, 16);
        Color32 baseC = C(70, 70, 82), coil = C(190, 190, 204), coilD = C(128, 128, 146), plate = C(214, 54, 54), plateL = C(245, 110, 100);
        c.Rect(1, 13, 14, 3, baseC);
        c.Rect(1, 13, 14, 1, C(100, 100, 114));
        int top = compressed ? 9 : 3;
        int coilTop = top + 3;
        for (int y = coilTop; y < 13; y++)
        {
            bool even = ((y - coilTop) / (compressed ? 1 : 2)) % 2 == 0;
            c.Rect(4, y, 8, 1, even ? coil : coilD);
            c.Set(even ? 4 : 11, y, coilD);
        }
        c.Rect(1, top, 14, 3, plate);
        c.Rect(1, top, 14, 1, plateL);
        c.AddOutline(Outline);
        return c.ToSprite(new Vector2(8, 8));
    });

    public static Sprite Bomb => Cached("bomb", () =>
    {
        var c = new Canvas(16, 16);
        c.Disc(7.5f, 9.5f, 5.6f, C(40, 38, 50));
        c.Disc(6f, 8f, 1.6f, C(110, 108, 130));
        c.Rect(9, 3, 2, 2, C(90, 88, 100));
        c.Set(11, 2, C(150, 100, 50)); c.Set(12, 1, C(150, 100, 50));
        c.AddOutline(Outline);
        c.Set(13, 0, C(255, 220, 80)); c.Set(14, 1, C(255, 150, 40)); c.Set(12, 0, C(255, 150, 40));
        return c.ToSprite(new Vector2(8, 8));
    });

    // ------------------------------------------------------------------ chegada

    public static Sprite Brain => Cached("brain", () =>
    {
        var c = new Canvas(20, 16);
        Color32 pink = C(240, 142, 170), pinkD = C(196, 88, 128), pinkL = C(255, 196, 214);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 20; x++)
            {
                float dx = (x + 0.5f - 10f) / 8.6f, dy = (y + 0.5f - 8.2f) / 6.4f;
                if (dx * dx + dy * dy > 1f) continue;
                float fold = Mathf.Sin(x * 0.95f + Mathf.Sin(y * 1.25f) * 1.8f);
                c.Set(x, y, fold > 0.78f ? pinkD : pink);
            }
        for (int y = 3; y < 14; y++) c.Set(10, y, pinkD); // divisão dos hemisférios
        c.Set(5, 4, pinkL); c.Set(6, 3, pinkL); c.Set(7, 3, pinkL); c.Set(13, 3, pinkL); c.Set(14, 4, pinkL);
        c.AddOutline(Outline);
        return c.ToSprite(new Vector2(10, 8));
    });

    public static Sprite Pedestal => Cached("pedestal", () =>
    {
        var c = new Canvas(16, 16);
        Color32 stone = C(126, 120, 140), stoneL = C(156, 150, 170), stoneD = C(92, 86, 106), gold = C(230, 190, 70);
        c.Rect(0, 0, 16, 4, stone); c.Rect(0, 0, 16, 1, stoneL); c.Rect(0, 3, 16, 1, gold);
        c.Rect(3, 4, 10, 9, stoneD); c.Rect(4, 4, 3, 9, stone);
        c.Rect(1, 13, 14, 3, stone); c.Rect(1, 13, 14, 1, stoneL);
        return c.ToSpriteCentered();
    });

    // ------------------------------------------------------------------ cenário

    public static Sprite Tombstone(int kind) => Cached($"tomb_{kind % 3}", () =>
    {
        var c = new Canvas(14, 16);
        Color32 st = C(118, 114, 130), stD = C(88, 84, 100), stL = C(150, 146, 162);
        int k = kind % 3;
        if (k == 0)
        {
            c.Disc(7, 5.5f, 5f, st);
            c.Rect(2, 5, 10, 10, st);
            c.Rect(6, 4, 2, 7, stD); c.Rect(4, 6, 6, 2, stD);
            c.Rect(3, 2, 3, 1, stL);
        }
        else if (k == 1)
        {
            c.Rect(5, 1, 4, 14, st); c.Rect(2, 4, 10, 3, st);
            c.Rect(5, 1, 1, 14, stL);
        }
        else
        {
            c.Rect(3, 6, 8, 9, st); c.Rect(4, 5, 6, 1, st);
            c.Rect(5, 8, 4, 1, stD); c.Rect(5, 10, 4, 1, stD);
        }
        c.Rect(1, 14, 12, 2, C(86, 120, 62));
        c.AddOutline(Outline);
        return c.ToSprite(new Vector2(7, 1));
    });

    public static Sprite DeadTree => Cached("tree", () =>
    {
        var c = new Canvas(28, 40);
        Color32 bark = C(46, 34, 44);
        c.Rect(12, 14, 4, 26, bark);
        c.Rect(11, 36, 6, 4, bark);
        void Branch(int x, int y, int dx, int len)
        {
            for (int i = 0; i < len; i++) { c.Set(x + dx * i, y - i, bark); c.Set(x + dx * i, y - i + 1, bark); }
        }
        Branch(12, 22, -1, 9); Branch(15, 18, 1, 10); Branch(13, 14, -1, 7); Branch(14, 12, 1, 6);
        Branch(6, 16, -1, 4); Branch(22, 11, 1, 4); Branch(13, 14, 0, 8);
        return c.ToSprite(new Vector2(14, 0));
    });

    public static Sprite Moon => Cached("moon", () =>
    {
        var c = new Canvas(36, 36);
        c.Disc(18, 18, 16, C(242, 236, 205));
        c.Disc(12, 13, 3.5f, C(218, 210, 180));
        c.Disc(23, 22, 4.5f, C(218, 210, 180));
        c.Disc(20, 10, 2f, C(218, 210, 180));
        return c.ToSpriteCentered();
    });

    public static Sprite Glow => Cached("glow", () =>
    {
        var c = new Canvas(64, 64);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32, 32)) / 32f;
                float a = Mathf.Clamp01(1f - d);
                c.Set(x, y, C(255, 255, 255, (int)(a * a * 255)));
            }
        return c.ToSprite(new Vector2(32, 32), 16, FilterMode.Bilinear);
    });

    // ------------------------------------------------------------------ plataforma móvel e gangorra

    /// <summary>Plataforma de metal 3x0,5 com faixas de aviso (item "Plataforma Móvel").</summary>
    public static Sprite MovingPlatform => Cached("platform", () =>
    {
        var c = new Canvas(48, 8);
        Color32 steel = C(120, 126, 140), steelL = C(160, 166, 180), steelD = C(78, 82, 96);
        Color32 yellow = C(240, 196, 60), black = C(36, 34, 40);
        c.Rect(0, 0, 48, 8, steel);
        c.Rect(0, 0, 48, 1, steelL);
        c.Rect(0, 7, 48, 1, steelD);
        for (int x = 0; x < 48; x++)
            if (x < 6 || x >= 42) for (int y = 1; y < 7; y++) c.Set(x, y, (x + y) / 3 % 2 == 0 ? yellow : black);
        for (int x = 10; x < 40; x += 10) { c.Set(x, 3, steelD); c.Set(x, 4, steelL); }
        return c.ToSpriteCentered();
    });

    /// <summary>Prancha da gangorra (5 x 0,44), com o parafuso no meio.</summary>
    public static Sprite SeesawPlank => Cached("seesaw", () =>
    {
        var c = new Canvas(80, 7);
        Color32 wood = C(176, 120, 70), woodL = C(206, 152, 98), woodD = C(128, 84, 48), metal = C(150, 150, 165);
        c.Rect(0, 0, 80, 7, wood);
        c.Rect(0, 0, 80, 1, woodL);
        c.Rect(0, 6, 80, 1, woodD);
        for (int x = 16; x < 80; x += 16) c.Rect(x, 1, 1, 5, woodD);
        c.Rect(0, 0, 3, 7, woodD);
        c.Rect(77, 0, 3, 7, woodD);
        c.Disc(40f, 3.5f, 2.6f, metal);
        c.Set(40, 3, C(90, 90, 105));
        return c.ToSpriteCentered();
    });

    /// <summary>Apoio triangular de pedra embaixo da gangorra.</summary>
    public static Sprite SeesawBase => Cached("seesawbase", () =>
    {
        var c = new Canvas(16, 16);
        Color32 stone = C(126, 120, 140), stoneL = C(156, 150, 170), stoneD = C(92, 86, 106);
        for (int y = 0; y < 16; y++)
        {
            int half = 1 + y * 7 / 15;
            for (int x = 8 - half; x < 8 + half; x++) c.Set(x, y, x < 8 ? stoneL : stone);
        }
        c.Rect(1, 14, 14, 2, stoneD);
        c.AddOutline(Outline);
        return c.ToSpriteCentered();
    });

    // ------------------------------------------------------------------ interface (teclas e botões)

    /// <summary>Seta branca: 0 cima, 1 esquerda, 2 baixo, 3 direita.</summary>
    public static Sprite Arrow(int dir) => Cached($"arrow_{dir & 3}", () =>
    {
        string[] up =
        {
            "....#....",
            "...###...",
            "..#####..",
            ".#######.",
            "#########",
            "...###...",
            "...###...",
            "...###...",
            "...###...",
        };
        var c = new Canvas(9, 9);
        for (int y = 0; y < 9; y++)
            for (int x = 0; x < 9; x++)
            {
                if (up[y][x] != '#') continue;
                int px = x, py = y;
                for (int k = 0; k < (dir & 3); k++) { int t = px; px = py; py = 8 - t; } // 90° anti-horário
                c.Set(px, py, C(255, 255, 255));
            }
        return c.ToSprite(new Vector2(4.5f, 4.5f), 9);
    });

    /// <summary>Círculo branco liso (botões A/B/X/Y do controle).</summary>
    public static Sprite Dot => Cached("dot", () =>
    {
        var c = new Canvas(48, 48);
        for (int y = 0; y < 48; y++)
            for (int x = 0; x < 48; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(24, 24));
                c.Set(x, y, C(255, 255, 255, (int)(Mathf.Clamp01(23.5f - d) * 255)));
            }
        return c.ToSprite(new Vector2(24, 24), 48, FilterMode.Bilinear);
    });

    // ------------------------------------------------------------------ temas (cor do céu por mapa)

    public struct ThemeColors
    {
        public Color Top, Mid, Bottom, Moon;
        public Color32 HillFar, HillNear;
    }

    public static ThemeColors Theme(int theme)
    {
        switch (theme)
        {
            case 1: // azul (estação)
                return new ThemeColors { Top = C(10, 14, 36), Mid = C(24, 40, 84), Bottom = C(64, 84, 132), Moon = new Color(0.85f, 0.92f, 1f), HillFar = C(30, 42, 78), HillNear = C(20, 28, 54) };
            case 2: // vermelho (serraria)
                return new ThemeColors { Top = C(26, 10, 20), Mid = C(74, 26, 38), Bottom = C(146, 62, 50), Moon = new Color(1f, 0.6f, 0.45f), HillFar = C(72, 30, 40), HillNear = C(44, 18, 28) };
            case 3: // verde (pântano)
                return new ThemeColors { Top = C(8, 20, 18), Mid = C(22, 54, 44), Bottom = C(64, 104, 72), Moon = new Color(0.85f, 1f, 0.8f), HillFar = C(24, 56, 46), HillNear = C(14, 36, 30) };
            case 4: // azul-arroxeado (ferrovia)
                return new ThemeColors { Top = C(12, 12, 28), Mid = C(32, 36, 70), Bottom = C(86, 72, 116), Moon = Color.white, HillFar = C(42, 40, 76), HillNear = C(26, 26, 50) };
            default: // roxo (cemitério)
                return new ThemeColors { Top = C(16, 12, 34), Mid = C(46, 28, 70), Bottom = C(96, 56, 92), Moon = Color.white, HillFar = C(54, 36, 74), HillNear = C(36, 25, 52) };
        }
    }

    /// <summary>Gradiente do céu noturno (esticado para cobrir a tela).</summary>
    public static Sprite Sky(int theme) => Cached($"sky_{theme}", () =>
    {
        var t = Theme(theme);
        var c = new Canvas(1, 64);
        for (int y = 0; y < 64; y++)
        {
            float k = y / 63f;
            Color col = k < 0.6f ? Color.Lerp(t.Top, t.Mid, k / 0.6f) : Color.Lerp(t.Mid, t.Bottom, (k - 0.6f) / 0.4f);
            c.Set(0, y, col);
        }
        return c.ToSprite(new Vector2(0.5f, 32), 1, FilterMode.Bilinear);
    });

    /// <summary>Morros distantes com cruzes e lápides em silhueta.</summary>
    public static Sprite Hills(int seed, Color32 color) => Cached($"hills_{seed}_{color.r}_{color.g}_{color.b}", () =>
    {
        int w = 320, h = 48;
        var c = new Canvas(w, h);
        float phase = seed * 3.1f;
        for (int x = 0; x < w; x++)
        {
            float t = x / (float)w * Mathf.PI * 2f;
            float hh = 22 + Mathf.Sin(t * 2 + phase) * 7 + Mathf.Sin(t * 5 + phase * 2) * 3 + Mathf.Sin(t * 11) * 1.5f;
            for (int y = h - (int)hh; y < h; y++) c.Set(x, y, color);
        }
        for (int k = 0; k < 14; k++)
        {
            int x = 10 + (int)(Hash(k, seed, 9) * (w - 20));
            int ground = h;
            for (int y = 0; y < h; y++) if (c.Filled(x, y)) { ground = y; break; }
            if (Hash(k, seed, 13) > 0.5f)
            {
                c.Rect(x, ground - 7, 1, 7, color); c.Rect(x - 2, ground - 5, 5, 1, color);
            }
            else
            {
                c.Rect(x - 2, ground - 5, 4, 5, color); c.Rect(x - 1, ground - 6, 2, 1, color);
            }
        }
        return c.ToSprite(new Vector2(w / 2f, 0), 8);
    });

    public static Sprite Fog => Cached("fog", () =>
    {
        int w = 64, h = 16;
        var c = new Canvas(w, h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float vy = 1f - Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                float vx = Mathf.Sin((x + 0.5f) / w * Mathf.PI);
                float n = 0.6f + 0.4f * Hash(x / 4, y / 4, 61);
                c.Set(x, y, C(220, 210, 235, (int)(vy * vy * vx * n * 70)));
            }
        return c.ToSprite(new Vector2(w / 2f, h / 2f), 4, FilterMode.Bilinear);
    });
    // ------------------------------------------------------------------ elementos dos mapas

    /// <summary>Trilho de trem em cima e uma ponte de madeira (treliça) embaixo.</summary>
    public static Sprite Rail => Cached("rail", () =>
    {
        var c = new Canvas(16, 16);
        Color32 wood = C(76, 54, 40), woodD = C(56, 38, 28);
        for (int y = 5; y < 16; y++)
        {
            int x1 = Mathf.RoundToInt((y - 5) * 15f / 10f);
            c.Set(x1, y, wood); c.Set(15 - x1, y, wood);
        }
        c.Rect(0, 5, 2, 11, woodD);
        for (int x = 0; x < 16; x++)
            if (x % 4 != 3) { c.Set(x, 3, C(104, 72, 46)); c.Set(x, 4, C(84, 58, 38)); }
        c.Rect(0, 1, 16, 1, C(204, 204, 218));
        c.Rect(0, 2, 16, 1, C(124, 124, 140));
        return c.ToSpriteCentered();
    });

    /// <summary>Locomotiva do trem fantasma (frente para a direita).</summary>
    public static Sprite Locomotive => Cached("loco", () =>
    {
        var c = new Canvas(40, 24);
        Color32 iron = C(54, 48, 66), ironL = C(82, 74, 98), ironD = C(34, 30, 44), trim = C(150, 90, 205),
                glow = C(130, 255, 150), red = C(176, 42, 42), gold = C(226, 186, 84), bone = C(232, 226, 206);
        c.Rect(2, 3, 13, 15, iron);
        c.Rect(1, 2, 16, 2, ironL);
        c.Rect(5, 6, 7, 5, glow);
        c.Rect(5, 6, 7, 1, C(200, 255, 210));
        c.Rect(6, 12, 4, 3, bone); c.Set(6, 13, ironD); c.Set(8, 13, ironD); c.Rect(7, 15, 2, 1, bone);
        c.Rect(14, 9, 21, 10, iron);
        c.Rect(14, 9, 21, 1, ironL);
        c.Rect(20, 9, 1, 10, trim); c.Rect(27, 9, 1, 10, trim);
        c.Rect(27, 2, 5, 8, ironD);
        c.Rect(26, 1, 7, 2, ironL);
        c.Rect(34, 10, 3, 3, gold);
        for (int y = 16; y <= 21; y++)
            for (int x = 33; x <= 33 + (y - 15); x++) c.Set(Mathf.Min(x, 39), y, red);
        c.Rect(1, 18, 35, 2, ironD);
        foreach (int wx in new[] { 8, 19, 29 })
        {
            c.Disc(wx, 20.5f, 2.6f, red);
            c.Set(wx, 20, ironL);
        }
        c.AddOutline(Outline);
        return c.ToSprite(new Vector2(0, 1));
    });

    /// <summary>Vagão carregando um caixão.</summary>
    public static Sprite Wagon => Cached("wagon", () =>
    {
        var c = new Canvas(32, 20);
        Color32 bed = C(112, 74, 46), bedL = C(142, 98, 62), bedD = C(80, 50, 30), coffin = C(72, 46, 32), lid = C(98, 64, 42), gold = C(226, 186, 84);
        c.Rect(1, 11, 30, 5, bed);
        c.Rect(1, 11, 30, 1, bedL);
        c.Rect(1, 15, 30, 1, bedD);
        c.Rect(5, 5, 22, 6, coffin);
        c.Rect(7, 4, 18, 1, coffin);
        c.Rect(5, 5, 22, 1, lid);
        c.Rect(15, 5, 2, 5, gold); c.Rect(13, 6, 6, 1, gold);
        c.Rect(0, 13, 1, 1, C(34, 30, 44)); c.Rect(31, 13, 1, 1, C(34, 30, 44));
        foreach (int wx in new[] { 8, 23 })
        {
            c.Disc(wx, 16.5f, 2.4f, C(150, 36, 36));
            c.Set(wx, 16, C(82, 74, 98));
        }
        c.AddOutline(Outline);
        return c.ToSprite(new Vector2(0, 1));
    });

    /// <summary>Ácido verde (2 quadros de animação). top = superfície com ondinhas.</summary>
    public static Sprite Acid(bool top, int frame) => Cached($"acid_{top}_{frame}", () =>
    {
        var c = new Canvas(16, 16);
        Color32 acid = C(92, 200, 60, 235), acidD = C(62, 156, 44, 235), acidL = C(176, 244, 116, 245);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                c.Set(x, y, Hash(x, y + frame * 7, 71) < 0.12f ? acidD : acid);
        for (int b = 0; b < 2; b++)
        {
            int bx = 3 + (int)(Hash(b, frame, 5) * 10), by = 5 + (int)(Hash(frame, b, 9) * 8);
            c.Set(bx, by, acidL); c.Set(bx + 1, by, acidL); c.Set(bx, by + 1, acidL); c.Set(bx + 1, by + 1, acidL);
        }
        if (top)
            for (int x = 0; x < 16; x++)
            {
                int h = Mathf.Sin((x + frame * 4) * 0.8f) > 0f ? 2 : 3;
                for (int y = 0; y < h; y++) c.Set(x, y, Clear);
                c.Set(x, h, acidL);
            }
        return c.ToSpriteCentered();
    });

    public static Sprite IceTile => Cached("icetile", () =>
    {
        var c = new Canvas(16, 16);
        Color32 ice = C(160, 215, 245), iceD = C(128, 192, 232), white = C(240, 250, 255);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++) c.Set(x, y, Hash(x, y, 56) < 0.1f ? iceD : ice);
        c.Rect(0, 0, 16, 1, white);
        for (int i = 0; i < 5; i++) c.Set(3 + i, 4 + i, white);
        c.Rect(0, 15, 16, 1, iceD);
        return c.ToSpriteCentered();
    });

    /// <summary>Trilho da serra móvel com 'cells' células de comprimento.</summary>
    public static Sprite SawRail(int cells) => Cached($"sawrail_{cells}", () =>
    {
        int w = cells * 16;
        var c = new Canvas(w, 6);
        c.Rect(1, 1, w - 2, 4, C(66, 64, 78));
        c.Rect(2, 2, w - 4, 2, C(30, 28, 38));
        c.Rect(0, 0, 3, 6, C(110, 108, 124)); c.Rect(w - 3, 0, 3, 6, C(110, 108, 124));
        for (int x = 8; x < w - 4; x += 16) c.Set(x, 1, C(140, 138, 150));
        return c.ToSpriteCentered();
    });

    // ------------------------------------------------------------------ itens novos

    /// <summary>Portal oval (variant 0 = verde, 1 = roxo), 2 quadros de redemoinho.</summary>
    public static Sprite Portal(int variant, int frame) => Cached($"portal_{variant}_{frame}", () =>
    {
        var c = new Canvas(16, 32);
        Color32 ring = variant == 0 ? C(80, 235, 130) : C(196, 96, 255);
        Color32 ringL = variant == 0 ? C(190, 255, 210) : C(236, 196, 255);
        Color32 swirl = variant == 0 ? C(30, 120, 70) : C(100, 40, 150);
        Color32 dark = C(18, 10, 30);
        float cx = 8f, cy = 16f, rx = 7f, ry = 15f;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 16; x++)
            {
                float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                float d = dx * dx + dy * dy;
                if (d > 1f) continue;
                if (d > 0.6f) { c.Set(x, y, (dx < 0 && dy < 0) ? ringL : ring); continue; }
                float ang = Mathf.Atan2(dy, dx);
                float band = Mathf.Sin(ang * 2f + Mathf.Sqrt(d) * 9f + frame * Mathf.PI);
                c.Set(x, y, band > 0.35f ? swirl : dark);
            }
        c.AddOutline(Outline);
        return c.ToSpriteCentered();
    });

    /// <summary>Esteira 3x1 (meia altura) com setas para a direita, 2 quadros.</summary>
    public static Sprite Conveyor(int frame) => Cached($"conveyor_{frame}", () =>
    {
        var c = new Canvas(48, 8);
        c.Rect(0, 0, 48, 8, Outline);
        c.Rect(1, 1, 46, 6, C(50, 48, 60));
        c.Rect(1, 1, 46, 1, C(96, 94, 108));
        Color32 arrow = C(236, 196, 70);
        for (int ax = 3 + frame * 4; ax < 46; ax += 8)
        {
            c.Set(ax, 2, arrow); c.Set(ax + 1, 3, arrow); c.Set(ax + 1, 4, arrow); c.Set(ax, 5, arrow);
        }
        c.Disc(3.5f, 4f, 2.2f, C(130, 128, 144));
        c.Disc(44.5f, 4f, 2.2f, C(130, 128, 144));
        return c.ToSpriteCentered();
    });

    /// <summary>Lança-chamas (bico para a direita).</summary>
    public static Sprite Flamethrower => Cached("flamethrower", () =>
    {
        var c = new Canvas(16, 16);
        c.Rect(0, 0, 16, 16, Outline);
        c.Rect(1, 1, 14, 14, C(70, 66, 80));
        c.Rect(2, 3, 8, 10, C(150, 46, 40));
        c.Rect(2, 3, 8, 2, C(196, 80, 64));
        for (int x = 2; x < 10; x++) c.Set(x, 12, (x / 2) % 2 == 0 ? C(236, 196, 70) : C(30, 26, 34));
        c.Rect(10, 6, 6, 4, C(52, 50, 62));
        c.Rect(10, 6, 6, 1, C(100, 98, 112));
        c.Rect(15, 7, 1, 2, C(255, 160, 60));
        return c.ToSpriteCentered();
    });

    /// <summary>Labareda de 2,5 células (40x12 px), 2 quadros.</summary>
    public static Sprite Flame(int frame) => Cached($"flame_{frame}", () =>
    {
        var c = new Canvas(40, 12);
        Color32 core = C(255, 244, 170), mid = C(255, 156, 44), outer = C(222, 64, 30);
        for (int x = 0; x < 40; x++)
        {
            float half = 1.5f + x / 39f * 4.5f + (Hash(x, frame, 17) - 0.5f) * 1.6f;
            for (int y = 0; y < 12; y++)
            {
                float d = Mathf.Abs(y + 0.5f - 6f);
                if (d > half) continue;
                float k = d / half;
                c.Set(x, y, k < 0.35f ? core : k < 0.7f ? mid : outer);
            }
        }
        return c.ToSprite(new Vector2(0, 6));
    });

    /// <summary>Caixa podre: desmorona quando pisam nela.</summary>
    public static Sprite RottenCrate => Cached("rottencrate", () =>
    {
        var c = new Canvas(16, 16);
        Color32 dark = C(60, 52, 30), wood = C(126, 112, 62), woodD = C(96, 84, 46), moss = C(92, 146, 62);
        c.Rect(0, 0, 16, 16, dark);
        c.Rect(1, 1, 14, 14, wood);
        for (int y = 1; y < 15; y++)
            for (int x = 1; x < 15; x++)
            {
                if ((y - 1) % 5 == 4) c.Set(x, y, woodD);
                if (Hash(x, y, 31) < 0.1f) c.Set(x, y, moss);
            }
        // rachaduras
        int cx = 5;
        for (int y = 2; y < 14; y++) { cx += Hash(y, 1, 3) > 0.5f ? 1 : 0; c.Set(cx, y, dark); }
        c.Rect(11, 3, 2, 1, dark); c.Rect(12, 4, 1, 2, dark);
        c.Rect(1, 1, 3, 2, moss); c.Rect(10, 13, 4, 2, moss);
        return c.ToSpriteCentered();
    });

    /// <summary>Carcaça do ventilador (sopra para cima, setas no topo).</summary>
    public static Sprite FanBase => Cached("fanbase", () =>
    {
        var c = new Canvas(16, 16);
        c.Rect(0, 0, 16, 16, Outline);
        c.Rect(1, 1, 14, 14, C(92, 96, 110));
        c.Rect(1, 1, 14, 1, C(130, 134, 150));
        c.Disc(8, 9, 5.5f, C(40, 40, 52));
        c.Rect(4, 1, 1, 2, C(160, 220, 255)); c.Rect(3, 2, 3, 1, C(160, 220, 255));
        c.Rect(11, 1, 1, 2, C(160, 220, 255)); c.Rect(10, 2, 3, 1, C(160, 220, 255));
        return c.ToSpriteCentered();
    });

    public static Sprite FanBlades => Cached("fanblades", () =>
    {
        var c = new Canvas(12, 12);
        Color32 blade = C(196, 200, 214), bladeD = C(150, 154, 170);
        for (int k = 0; k < 3; k++)
        {
            float a = k * Mathf.PI * 2f / 3f;
            for (float r = 1f; r < 5.2f; r += 0.5f)
                for (float w = -0.9f; w <= 0.9f; w += 0.45f)
                {
                    float ang = a + w * 0.35f;
                    c.Set((int)(6 + Mathf.Cos(ang) * r), (int)(6 + Mathf.Sin(ang) * r), w < 0 ? blade : bladeD);
                }
        }
        c.Disc(6, 6, 1.4f, C(226, 186, 84));
        return c.ToSpriteCentered();
    });
}
