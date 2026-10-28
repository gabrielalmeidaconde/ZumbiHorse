using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Fases criadas pelos jogadores: código de texto para compartilhar e espaços salvos no computador.</summary>
public static class MapCode
{
    const string Prefix = "ZH1-";
    const byte Version = 1;
    /// <summary>Caracteres que podem aparecer numa fase criada (o código guarda o índice de cada um).</summary>
    const string Cells = ".#Si=a^XpGtT1";
    public const int MaxSaws = 20;
    public const int Slots = 6;

    public static int ThemeCount => Maps.All.Max(m => m.Theme) + 1;
    public static string ThemeName(int theme) => Maps.All.First(m => m.Theme == theme).Name;

    /// <summary>Fase nova: chão de pedra, largada à esquerda e cérebro no pedestal à direita.</summary>
    public static MapDef NewMap()
    {
        var rows = new string[Level.H];
        for (int r = 0; r < Level.H; r++) rows[r] = new string(r >= Level.H - 3 ? 'S' : '.', Level.W);
        var map = new MapDef { Name = "Fase criada", Description = "", Theme = 0, Rows = rows };
        Set(map, 2, 3, '1');
        Set(map, Level.W - 4, 3, 'p');
        Set(map, Level.W - 4, 4, 'G');
        return map;
    }

    /// <summary>Troca o caractere da célula (x, y), com y = 0 na linha de baixo.</summary>
    public static void Set(MapDef map, int x, int y, char ch)
    {
        int row = Level.H - 1 - y;
        var r = map.Rows[row].ToCharArray();
        r[x] = ch;
        map.Rows[row] = new string(r);
    }

    /// <summary>Um trem em cada trilho reto com espaço para ele (7 células ou mais).</summary>
    public static void BuildTrains(MapDef map)
    {
        var trains = new List<TrainDef>();
        for (int y = 0; y < Level.H; y++)
            for (int x = 0; x < Level.W; x++)
            {
                if (map.At(x, y) != '=') continue;
                int end = x;
                while (end + 1 < Level.W && map.At(end + 1, y) == '=') end++;
                int len = end - x + 1;
                if (len >= 7) trains.Add(new TrainDef { TrackRow = y, FromX = x, ToX = end, Wagons = len >= 15 ? 2 : 1, Speed = 4.5f });
                x = end;
            }
        map.Trains = trains.ToArray();
    }

    // ------------------------------------------------------------------ código

    /// <summary>Código da fase com o tempo de quem criou (em milissegundos).</summary>
    public static string Encode(MapDef map, int timeMs)
    {
        var b = new List<byte> { Version, (byte)map.Theme };
        b.AddRange(BitConverter.GetBytes(timeMs));
        b.Add((byte)map.Saws.Length);
        foreach (var s in map.Saws) b.AddRange(new[] { (byte)s.From.x, (byte)s.From.y, (byte)s.To.x, (byte)s.To.y });

        // células linha por linha, de cima para baixo, em pares (peça, repetições)
        string all = string.Concat(map.Rows);
        for (int i = 0; i < all.Length;)
        {
            int run = 1;
            while (i + run < all.Length && all[i + run] == all[i] && run < 255) run++;
            b.Add((byte)Mathf.Max(0, Cells.IndexOf(all[i])));
            b.Add((byte)run);
            i += run;
        }
        b.Add(Checksum(b, b.Count));
        return Prefix + Convert.ToBase64String(b.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Lê um código colado pelo jogador. Falha se ele estiver incompleto, alterado ou com uma fase impossível de montar.</summary>
    public static bool TryDecode(string code, out MapDef map, out int timeMs)
    {
        map = null;
        timeMs = 0;
        if (string.IsNullOrWhiteSpace(code)) return false;
        // apps de conversa às vezes quebram a linha no meio do código
        string s = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        s = s.Substring(Prefix.Length).Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');

        byte[] b;
        try { b = Convert.FromBase64String(s); }
        catch (FormatException) { return false; }
        if (b.Length < 8 || b[0] != Version || Checksum(b, b.Length - 1) != b[b.Length - 1]) return false;

        int theme = b[1];
        timeMs = BitConverter.ToInt32(b, 2);
        int saws = b[6], p = 7;
        if (theme >= ThemeCount || timeMs < 0 || saws > MaxSaws || p + saws * 4 > b.Length - 1) return false;

        var sawDefs = new SawPathDef[saws];
        for (int i = 0; i < saws; i++, p += 4)
        {
            var from = new Vector2Int(b[p], b[p + 1]);
            var to = new Vector2Int(b[p + 2], b[p + 3]);
            if (!InMap(from) || !InMap(to) || from == to || (from.x != to.x && from.y != to.y)) return false;
            sawDefs[i] = new SawPathDef { From = from, To = to };
        }

        var cells = new System.Text.StringBuilder(Level.W * Level.H);
        for (; p + 1 < b.Length - 1; p += 2)
        {
            if (b[p] >= Cells.Length || b[p + 1] == 0 || cells.Length + b[p + 1] > Level.W * Level.H) return false;
            cells.Append(Cells[b[p]], b[p + 1]);
        }
        if (p != b.Length - 1 || cells.Length != Level.W * Level.H) return false;

        string all = cells.ToString();
        if (all.Count(c => c == '1') != 1 || all.Count(c => c == 'G') != 1) return false;

        var rows = new string[Level.H];
        for (int r = 0; r < Level.H; r++) rows[r] = all.Substring(r * Level.W, Level.W);
        map = new MapDef { Name = "Desafio", Description = "", Theme = theme, Rows = rows, Saws = sawDefs };
        BuildTrains(map);
        return true;
    }

    static bool InMap(Vector2Int c) => c.x >= 0 && c.x < Level.W && c.y >= 0 && c.y < Level.H;

    static byte Checksum(IList<byte> b, int count)
    {
        int sum = 0;
        for (int i = 0; i < count; i++) sum = (sum * 31 + b[i]) & 0xFF;
        return (byte)sum;
    }

    // ------------------------------------------------------------------ espaços salvos

    static string SlotKey(int slot) => "fase_criada_" + slot;

    /// <summary>Fase salva no espaço (null = vazio) e o tempo de quem criou (0 = ainda não passou nela).</summary>
    public static MapDef LoadSlot(int slot, out int timeMs)
    {
        timeMs = 0;
        return TryDecode(PlayerPrefs.GetString(SlotKey(slot), ""), out var map, out timeMs) ? map : null;
    }

    public static void SaveSlot(int slot, MapDef map, int timeMs) => PlayerPrefs.SetString(SlotKey(slot), Encode(map, timeMs));
}
