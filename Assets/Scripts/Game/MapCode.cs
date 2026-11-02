using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Fases criadas pelos jogadores: código e texto para compartilhar, fases salvas, recebidas, recordes e arquivos.</summary>
public static class MapCode
{
    const string Prefix = "ZH1-";
    const byte Version = 2;
    /// <summary>Caracteres que podem aparecer numa fase criada (o código guarda o índice de cada um; a versão 1 ia até o '1').</summary>
    const string Cells = ".#Si=a^XpGtT1234";
    public const int MaxSaws = 20, MaxItems = 40, MaxName = 24;
    public const int Slots = 6, MaxReceived = 20;

    public static int ThemeCount => Maps.All.Max(m => m.Theme) + 1;
    public static string ThemeName(int theme) => Maps.All.First(m => m.Theme == theme).Name;

    /// <summary>A bomba explode na hora, então não dá para deixar ela pronta na fase.</summary>
    public static bool CanBeMapItem(ItemType t) => t != ItemType.Bomba;

    /// <summary>Fase nova: chão de pedra, largada à esquerda e cérebro no pedestal à direita.</summary>
    public static MapDef NewMap(string name)
    {
        var rows = new string[Level.H];
        for (int r = 0; r < Level.H; r++) rows[r] = new string(r >= Level.H - 3 ? 'S' : '.', Level.W);
        var map = new MapDef { Name = name, Description = "", Theme = 0, Rows = rows };
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

    /// <summary>Células que o item da fase ocupa.</summary>
    public static IEnumerable<Vector2Int> ItemCells(MapItemDef it) => ItemDefs.Footprint(ItemDefs.Get(it.Type), it.Rotation).Select(c => it.Pivot + c);

    static bool InMap(Vector2Int c) => c.x >= 0 && c.x < Level.W && c.y >= 0 && c.y < Level.H;

    /// <summary>Fase que dá para montar: largada 1 e cérebro (um de cada), no máximo uma de cada largada 2-4, serras e itens dentro da fase.</summary>
    static bool Valid(MapDef map)
    {
        string all = string.Concat(map.Rows);
        if (map.Rows.Length != Level.H || map.Rows.Any(r => r.Length != Level.W) || all.Any(c => Cells.IndexOf(c) < 0)) return false;
        if (all.Count(c => c == '1') != 1 || all.Count(c => c == 'G') != 1 || "234".Any(n => all.Count(c => c == n) > 1)) return false;
        if (map.Theme < 0 || map.Theme >= ThemeCount || map.Saws.Length > MaxSaws || map.Items.Length > MaxItems) return false;
        foreach (var s in map.Saws)
            if (!InMap(s.From) || !InMap(s.To) || s.From == s.To || (s.From.x != s.To.x && s.From.y != s.To.y)) return false;
        foreach (var it in map.Items)
            if (!Enum.IsDefined(typeof(ItemType), it.Type) || !CanBeMapItem(it.Type) || it.Rotation < 0 || it.Rotation > 3 || !ItemCells(it).All(InMap)) return false;
        return map.Items.Count(i => i.Type == ItemType.Portal) % 2 == 0;
    }

    // ------------------------------------------------------------------ código

    /// <summary>Código da fase com o tempo de quem criou (em milissegundos).</summary>
    public static string Encode(MapDef map, int timeMs)
    {
        var b = new List<byte> { Version, (byte)map.Theme };
        b.AddRange(BitConverter.GetBytes(timeMs));
        var name = Encoding.UTF8.GetBytes(ShortName(map.Name));
        b.Add((byte)name.Length);
        b.AddRange(name);
        b.Add((byte)map.Saws.Length);
        foreach (var s in map.Saws) b.AddRange(new[] { (byte)s.From.x, (byte)s.From.y, (byte)s.To.x, (byte)s.To.y });
        b.Add((byte)map.Items.Length);
        foreach (var it in map.Items) b.AddRange(new[] { (byte)it.Type, (byte)it.Pivot.x, (byte)it.Pivot.y, (byte)it.Rotation });

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

    static string ShortName(string name)
    {
        name = (name ?? "").Trim();
        while (Encoding.UTF8.GetByteCount(name) > MaxName) name = name.Substring(0, name.Length - 1);
        return name;
    }

    /// <summary>Lê um código. Falha se ele estiver incompleto, alterado ou com uma fase impossível de montar.</summary>
    public static bool TryDecode(string code, out MapDef map, out int timeMs)
    {
        map = null;
        timeMs = 0;
        if (string.IsNullOrWhiteSpace(code)) return false;
        int start = code.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return false;
        // o código vai até o primeiro espaço; se falhar, tenta juntando as linhas (apps de conversa quebram códigos longos)
        string rest = code.Substring(start + Prefix.Length);
        string word = new string(rest.TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
        return Decode(word, out map, out timeMs) || Decode(new string(rest.Where(c => !char.IsWhiteSpace(c)).ToArray()), out map, out timeMs);
    }

    static bool Decode(string s, out MapDef map, out int timeMs)
    {
        map = null;
        timeMs = 0;
        s = s.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        byte[] b;
        try { b = Convert.FromBase64String(s); }
        catch (FormatException) { return false; }
        if (b.Length < 8 || (b[0] != 1 && b[0] != Version) || Checksum(b, b.Length - 1) != b[b.Length - 1]) return false;

        int end = b.Length - 1, p = 0;
        bool Need(int n) => p + n <= end;
        int version = b[p++], theme = b[p++];
        timeMs = BitConverter.ToInt32(b, p);
        p += 4;
        string name = "Desafio";
        if (version >= 2)
        {
            if (!Need(1) || !Need(1 + b[p])) return false;
            name = Encoding.UTF8.GetString(b, p + 1, b[p]);
            p += 1 + b[p];
        }
        if (!Need(1) || !Need(1 + b[p] * 4)) return false;
        var saws = new SawPathDef[b[p++]];
        for (int i = 0; i < saws.Length; i++, p += 4)
            saws[i] = new SawPathDef { From = new Vector2Int(b[p], b[p + 1]), To = new Vector2Int(b[p + 2], b[p + 3]) };
        var items = new MapItemDef[0];
        if (version >= 2)
        {
            if (!Need(1) || !Need(1 + b[p] * 4)) return false;
            items = new MapItemDef[b[p++]];
            for (int i = 0; i < items.Length; i++, p += 4)
                items[i] = new MapItemDef { Type = (ItemType)b[p], Pivot = new Vector2Int(b[p + 1], b[p + 2]), Rotation = b[p + 3] };
        }

        var cells = new StringBuilder(Level.W * Level.H);
        for (; p + 1 < end; p += 2)
        {
            if (b[p] >= Cells.Length || b[p + 1] == 0 || cells.Length + b[p + 1] > Level.W * Level.H) return false;
            cells.Append(Cells[b[p]], b[p + 1]);
        }
        if (p != end || cells.Length != Level.W * Level.H || timeMs < 0) return false;

        string all = cells.ToString();
        var rows = new string[Level.H];
        for (int r = 0; r < Level.H; r++) rows[r] = all.Substring(r * Level.W, Level.W);
        map = new MapDef { Name = name.Length > 0 ? name : "Desafio", Description = "", Theme = theme, Rows = rows, Saws = saws, Items = items };
        if (!Valid(map)) { map = null; return false; }
        BuildTrains(map);
        return true;
    }

    static byte Checksum(IList<byte> b, int count)
    {
        int sum = 0;
        for (int i = 0; i < count; i++) sum = (sum * 31 + b[i]) & 0xFF;
        return (byte)sum;
    }

    // ------------------------------------------------------------------ texto (mesmo desenho do MapDefs.cs)

    /// <summary>A fase em texto: o desenho das 27 linhas (legenda do MapDefs.cs) mais nome, tema, tempo, serras e itens.</summary>
    public static string ToText(MapDef map, int timeMs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Ultimate Zumbi Horse: fase criada (importe este arquivo ou cole o texto no jogo)");
        sb.AppendLine("nome: " + ShortName(map.Name));
        sb.AppendLine("tema: " + map.Theme);
        sb.AppendLine("tempo: " + timeMs);
        foreach (var r in map.Rows) sb.AppendLine(r);
        foreach (var s in map.Saws) sb.AppendLine($"serra: {s.From.x} {s.From.y} {s.To.x} {s.To.y}");
        foreach (var it in map.Items) sb.AppendLine($"item: {it.Type} {it.Pivot.x} {it.Pivot.y} {it.Rotation}");
        return sb.ToString();
    }

    static bool TryParseText(string text, out MapDef map, out int timeMs)
    {
        map = null;
        timeMs = 0;
        var rows = new List<string>();
        var saws = new List<SawPathDef>();
        var items = new List<MapItemDef>();
        string name = "Fase recebida";
        int theme = 0;
        foreach (var raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == Level.W && line.All(c => Cells.IndexOf(c) >= 0)) { rows.Add(line); continue; }
            int colon = line.IndexOf(':');
            if (line.StartsWith("#") || colon < 0) continue;
            string key = line.Substring(0, colon).Trim().ToLowerInvariant(), value = line.Substring(colon + 1).Trim();
            var nums = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int N(int i) => i < nums.Length && int.TryParse(nums[i], out int v) ? v : -1;
            switch (key)
            {
                case "nome": name = value; break;
                case "tema": theme = N(0); break;
                case "tempo": timeMs = Mathf.Max(0, N(0)); break;
                case "serra": saws.Add(new SawPathDef { From = new Vector2Int(N(0), N(1)), To = new Vector2Int(N(2), N(3)) }); break;
                case "item":
                    if (nums.Length < 4 || !Enum.TryParse(nums[0], true, out ItemType type)) return false;
                    items.Add(new MapItemDef { Type = type, Pivot = new Vector2Int(N(1), N(2)), Rotation = N(3) });
                    break;
            }
        }
        if (rows.Count != Level.H) return false;
        map = new MapDef { Name = ShortName(name), Description = "", Theme = theme, Rows = rows.ToArray(), Saws = saws.ToArray(), Items = items.ToArray() };
        if (!Valid(map)) { map = null; return false; }
        BuildTrains(map);
        return true;
    }

    /// <summary>Aceita o código (ZH1-...) ou o texto da fase, inclusive no meio de uma mensagem.</summary>
    public static bool TryParse(string s, out MapDef map, out int timeMs)
    {
        map = null;
        timeMs = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        return TryDecode(s, out map, out timeMs) || TryParseText(s.Replace("\r", ""), out map, out timeMs);
    }

    // ------------------------------------------------------------------ fases salvas, recebidas e recordes

    /// <summary>Testes automáticos: fases, recordes e arquivos ficam separados dos do jogador.</summary>
    public static bool TestMode;
    static string Key(string key) => TestMode ? "teste_" + key : key;
    static string SlotKey(int slot) => Key("fase_criada_" + slot);

    /// <summary>Fase salva no espaço (null = vazio) e o tempo de quem criou (0 = ainda não passou nela).</summary>
    public static MapDef LoadSlot(int slot, out int timeMs) =>
        TryParse(PlayerPrefs.GetString(SlotKey(slot), ""), out var map, out timeMs) ? map : null;

    /// <summary>Apaga as fases e a pasta do modo de teste (começar o teste do zero).</summary>
    public static void ClearTest()
    {
        if (!TestMode) return;
        for (int i = 0; i < Slots; i++) PlayerPrefs.DeleteKey(SlotKey(i));
        SaveReceived(new List<Received>());
        if (Directory.Exists(Folder)) Directory.Delete(Folder, true);
    }

    public static void SaveSlot(int slot, MapDef map, int timeMs) => PlayerPrefs.SetString(SlotKey(slot), ToText(map, timeMs));

    public struct Received { public MapDef Map; public int TimeMs; }

    /// <summary>Fases de amigos (coladas ou importadas), da mais nova para a mais antiga.</summary>
    public static List<Received> LoadReceived()
    {
        var list = new List<Received>();
        int n = PlayerPrefs.GetInt(Key("fases_recebidas"), 0);
        for (int i = 0; i < n; i++)
            if (TryParse(PlayerPrefs.GetString(Key("fase_recebida_" + i), ""), out var map, out int ms)) list.Add(new Received { Map = map, TimeMs = ms });
        return list;
    }

    static void SaveReceived(List<Received> list)
    {
        for (int i = 0; i < list.Count; i++) PlayerPrefs.SetString(Key("fase_recebida_" + i), ToText(list[i].Map, list[i].TimeMs));
        for (int i = list.Count; i < PlayerPrefs.GetInt(Key("fases_recebidas"), 0); i++) PlayerPrefs.DeleteKey(Key("fase_recebida_" + i));
        PlayerPrefs.SetInt(Key("fases_recebidas"), list.Count);
        PlayerPrefs.Save();
    }

    /// <summary>Guarda a fase no topo da lista (a mesma fase recebida de novo só sobe para o topo).</summary>
    public static void AddReceived(MapDef map, int timeMs)
    {
        var list = LoadReceived();
        list.RemoveAll(r => Hash(r.Map) == Hash(map));
        list.Insert(0, new Received { Map = map, TimeMs = timeMs });
        if (list.Count > MaxReceived) list.RemoveRange(MaxReceived, list.Count - MaxReceived);
        SaveReceived(list);
    }

    public static void RemoveReceived(int index)
    {
        var list = LoadReceived();
        if (index < 0 || index >= list.Count) return;
        list.RemoveAt(index);
        SaveReceived(list);
    }

    /// <summary>Identifica a fase pelo que dá para jogar (células, serras e itens), sem o nome, o tema ou o tempo.</summary>
    public static uint Hash(MapDef map)
    {
        string s = string.Concat(map.Rows) + string.Concat(map.Saws.Select(x => $"{x.From}{x.To}")) + string.Concat(map.Items.Select(x => $"{x.Type}{x.Pivot}{x.Rotation}"));
        uint h = 2166136261;
        foreach (char c in s) h = (h ^ c) * 16777619;
        return h;
    }

    /// <summary>Melhor tempo neste computador (0 = nunca passou).</summary>
    public static int Record(MapDef map) => PlayerPrefs.GetInt(Key("recorde_" + Hash(map)), 0);

    /// <summary>Guarda o tempo se for o melhor. Devolve true se é recorde novo.</summary>
    public static bool SaveRecord(MapDef map, int ms)
    {
        int old = Record(map);
        if (old > 0 && old <= ms) return false;
        PlayerPrefs.SetInt(Key("recorde_" + Hash(map)), ms);
        PlayerPrefs.Save();
        return true;
    }

    // ------------------------------------------------------------------ arquivos (no computador)

    /// <summary>Pasta das fases exportadas e de onde as importadas são lidas.</summary>
    public static string Folder
    {
        get
        {
            if (TestMode) return Path.Combine(Application.temporaryCachePath, "Fases");
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return Path.Combine(string.IsNullOrEmpty(docs) ? Application.persistentDataPath : docs, "Ultimate Zumbi Horse", "Fases");
        }
    }

    public static string FileName(MapDef map)
    {
        string name = new string(ShortName(map.Name).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return (name.Length > 0 ? name : "fase") + ".txt";
    }

    /// <summary>Grava a fase em texto na pasta e devolve o caminho do arquivo.</summary>
    public static string Export(MapDef map, int timeMs)
    {
        Directory.CreateDirectory(Folder);
        string path = Path.Combine(Folder, FileName(map));
        File.WriteAllText(path, ToText(map, timeMs));
        return path;
    }

    /// <summary>Lê todas as fases da pasta (arquivos de texto com uma fase ou um código dentro).</summary>
    public static List<Received> ImportFolder()
    {
        var list = new List<Received>();
        if (!Directory.Exists(Folder)) return list;
        foreach (string path in Directory.GetFiles(Folder, "*.txt"))
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { continue; }
            if (TryParse(text, out var map, out int ms)) list.Add(new Received { Map = map, TimeMs = ms });
        }
        return list;
    }
}
