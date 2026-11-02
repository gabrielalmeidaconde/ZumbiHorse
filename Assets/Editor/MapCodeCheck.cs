using UnityEditor;
using UnityEngine;

/// <summary>Confere que uma fase volta igual depois de virar código ou texto, e que código alterado ou fase impossível é recusado.</summary>
public static class MapCodeCheck
{
    [MenuItem("Zumbi Horse/Conferir código de fase", priority = 40)]
    public static void Run()
    {
        var map = MapCode.NewMap("Teste ação");
        map.Theme = 3;
        for (int x = 5; x < 20; x++) MapCode.Set(map, x, 10, '=');
        MapCode.Set(map, 30, 12, '^');
        MapCode.Set(map, 31, 12, 'i');
        MapCode.Set(map, 6, 3, '2');
        map.Saws = new[] { new SawPathDef { From = new Vector2Int(10, 15), To = new Vector2Int(10, 20) } };
        map.Items = new[]
        {
            new MapItemDef { Type = ItemType.Mola, Pivot = new Vector2Int(12, 4), Rotation = 0 },
            new MapItemDef { Type = ItemType.Portal, Pivot = new Vector2Int(20, 4), Rotation = 1 },
            new MapItemDef { Type = ItemType.Portal, Pivot = new Vector2Int(26, 4), Rotation = 1 },
        };

        string code = MapCode.Encode(map, 12345);
        bool ok = MapCode.TryParse("olha essa: " + code + " consegue?", out var fromCode, out int ms) && ms == 12345 && Same(map, fromCode);
        ok &= MapCode.TryParse(MapCode.ToText(map, 999), out var fromText, out ms) && ms == 999 && Same(map, fromText);
        ok &= fromCode.Trains.Length == 1 && fromCode.Trains[0].FromX == 5 && fromCode.Trains[0].ToX == 19;

        // trocar uma letra no meio tem que ser pego
        char[] broken = code.ToCharArray();
        int i = code.Length / 2;
        broken[i] = broken[i] == 'A' ? 'B' : 'A';
        ok &= !MapCode.TryParse(new string(broken), out _, out _);
        ok &= !MapCode.TryParse("ZH1-", out _, out _) && !MapCode.TryParse("qualquer coisa", out _, out _);

        // fase sem cérebro ou com item fora da fase não vale
        var noGoal = MapCode.NewMap("x");
        MapCode.Set(noGoal, Level.W - 4, 4, '.');
        ok &= !MapCode.TryParse(MapCode.ToText(noGoal, 0), out _, out _);
        var outside = MapCode.NewMap("x");
        outside.Items = new[] { new MapItemDef { Type = ItemType.Prancha, Pivot = new Vector2Int(0, 10), Rotation = 0 } };
        ok &= !MapCode.TryParse(MapCode.Encode(outside, 0), out _, out _);

        // código da primeira versão (sem nome nem itens): fase nova padrão, tema 2, 4,321 s
        ok &= MapCode.TryParse("ZH1-AQLhEAAAAAD_AP8A_wD_AFAJAQAFDAEAKQgBAAMCkOE", out var v1, out ms) && ms == 4321 && v1.Theme == 2
              && string.Concat(v1.Rows) == string.Concat(MapCode.NewMap("x").Rows);

        if (ok) Debug.Log($"Código de fase OK ({code.Length} caracteres): {code}");
        else Debug.LogError("Código de fase: a fase não voltou igual ou uma fase estragada foi aceita");
    }

    static bool Same(MapDef a, MapDef b) =>
        b != null && a.Name == b.Name && a.Theme == b.Theme && string.Concat(a.Rows) == string.Concat(b.Rows) && MapCode.Hash(a) == MapCode.Hash(b);
}
