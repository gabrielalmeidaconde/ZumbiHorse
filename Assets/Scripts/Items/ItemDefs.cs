using UnityEngine;

public enum ItemType { Caixa, Prancha, Pedra, Espinhos, Serra, Canhao, Mola, Gelo, Bomba, SerraMovel, Portal, Esteira, LancaChamas, CaixaPodre, Ventilador }

public class ItemDef
{
    public ItemType Type;
    public string Name;
    public string Hint;
    /// <summary>Células ocupadas, relativas à célula pivô (rotação 0).</summary>
    public Vector2Int[] Cells;
    /// <summary>Centro visual relativo ao centro da célula pivô (rotação 0).</summary>
    public Vector2 Center;
    public float Weight;
    public bool CanRotate = true;
}

public static class ItemDefs
{
    public static readonly ItemDef[] All =
    {
        new ItemDef { Type = ItemType.Caixa, Name = "Caixa", Hint = "Bloco 1x1", Cells = new[] { V(0, 0) }, Weight = 3f },
        new ItemDef { Type = ItemType.Prancha, Name = "Prancha", Hint = "Plataforma 3x1", Cells = new[] { V(-1, 0), V(0, 0), V(1, 0) }, Weight = 3f },
        new ItemDef { Type = ItemType.Pedra, Name = "Pedra", Hint = "Bloco 2x2", Cells = new[] { V(0, 0), V(1, 0), V(0, 1), V(1, 1) }, Center = new Vector2(0.5f, 0.5f), Weight = 2f },
        new ItemDef { Type = ItemType.Gelo, Name = "Gelo", Hint = "Escorregadio", Cells = new[] { V(0, 0), V(1, 0) }, Center = new Vector2(0.5f, 0f), Weight = 1.5f },
        new ItemDef { Type = ItemType.CaixaPodre, Name = "Caixa Podre", Hint = "Desmorona ao pisar", Cells = new[] { V(0, 0) }, Weight = 2f },
        new ItemDef { Type = ItemType.Esteira, Name = "Esteira", Hint = "Empurra de lado", Cells = new[] { V(-1, 0), V(0, 0), V(1, 0) }, Weight = 2f },
        new ItemDef { Type = ItemType.Mola, Name = "Mola", Hint = "Arremessa longe", Cells = new[] { V(0, 0) }, Weight = 2f },
        new ItemDef { Type = ItemType.Ventilador, Name = "Ventilador", Hint = "Sopra os zumbis", Cells = new[] { V(0, 0) }, Weight = 1.5f },
        new ItemDef { Type = ItemType.Portal, Name = "Portais", Hint = "Coloque os dois", Cells = new[] { V(0, 0), V(0, 1) }, Center = new Vector2(0f, 0.5f), Weight = 1.5f },
        new ItemDef { Type = ItemType.Espinhos, Name = "Espinhos", Hint = "Mata quem encostar", Cells = new[] { V(0, 0) }, Weight = 3f },
        new ItemDef { Type = ItemType.Serra, Name = "Serra", Hint = "Mata quem encostar", Cells = new[] { V(0, 0) }, Weight = 2f },
        new ItemDef { Type = ItemType.SerraMovel, Name = "Serra Móvel", Hint = "Vai e volta", Cells = new[] { V(-1, 0), V(0, 0), V(1, 0) }, Weight = 2f },
        new ItemDef { Type = ItemType.Canhao, Name = "Canhão de Ossos", Hint = "Atira ossos", Cells = new[] { V(0, 0) }, Weight = 2f },
        new ItemDef { Type = ItemType.LancaChamas, Name = "Lança-Chamas", Hint = "Cospe fogo", Cells = new[] { V(0, 0) }, Weight = 2f },
        new ItemDef { Type = ItemType.Bomba, Name = "Bomba", Hint = "Explode blocos", Cells = new[] { V(0, 0) }, Weight = 1.5f, CanRotate = false },
    };

    static Vector2Int V(int x, int y) => new Vector2Int(x, y);

    public static ItemDef Get(ItemType t)
    {
        foreach (var d in All) if (d.Type == t) return d;
        return All[0];
    }

    public static ItemType Random()
    {
        float total = 0f;
        foreach (var d in All) total += d.Weight;
        float r = UnityEngine.Random.Range(0f, total);
        foreach (var d in All)
        {
            r -= d.Weight;
            if (r <= 0f) return d.Type;
        }
        return ItemType.Caixa;
    }

    /// <summary>Gira um vetor de célula 90° no sentido anti-horário, n vezes.</summary>
    public static Vector2Int Rotate(Vector2Int v, int n)
    {
        n = ((n % 4) + 4) % 4;
        for (int i = 0; i < n; i++) v = new Vector2Int(-v.y, v.x);
        return v;
    }

    public static Vector2 Rotate(Vector2 v, int n)
    {
        n = ((n % 4) + 4) % 4;
        for (int i = 0; i < n; i++) v = new Vector2(-v.y, v.x);
        return v;
    }
}
