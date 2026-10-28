using UnityEditor;
using UnityEngine;

/// <summary>Confere que uma fase volta igual depois de virar código, e que código alterado é recusado.</summary>
public static class MapCodeCheck
{
    [MenuItem("Zumbi Horse/Conferir código de fase", priority = 40)]
    public static void Run()
    {
        var map = MapCode.NewMap();
        map.Theme = 3;
        for (int x = 5; x < 20; x++) MapCode.Set(map, x, 10, '=');
        MapCode.Set(map, 30, 12, '^');
        MapCode.Set(map, 31, 12, 'i');
        map.Saws = new[] { new SawPathDef { From = new Vector2Int(10, 15), To = new Vector2Int(10, 20) } };

        string code = MapCode.Encode(map, 12345);
        bool ok = MapCode.TryDecode(code, out var back, out int ms)
                  && ms == 12345 && back.Theme == 3 && back.Saws.Length == 1
                  && back.Saws[0].From == map.Saws[0].From && back.Saws[0].To == map.Saws[0].To
                  && string.Concat(back.Rows) == string.Concat(map.Rows)
                  && back.Trains.Length == 1 && back.Trains[0].FromX == 5 && back.Trains[0].ToX == 19;

        // trocar uma letra no meio tem que ser pego
        char[] broken = code.ToCharArray();
        int i = code.Length / 2;
        broken[i] = broken[i] == 'A' ? 'B' : 'A';
        ok &= !MapCode.TryDecode(new string(broken), out _, out _);
        ok &= !MapCode.TryDecode("ZH1-", out _, out _) && !MapCode.TryDecode("qualquer coisa", out _, out _);

        if (ok) Debug.Log($"Código de fase OK ({code.Length} caracteres): {code}");
        else Debug.LogError("Código de fase: a fase não voltou igual ou um código estragado foi aceito");
    }
}
