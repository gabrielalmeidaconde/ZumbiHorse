using System.Collections.Generic;
using UnityEngine;

/// <summary>Qualquer coisa que ocupa células da grade: terreno ou item posicionado.</summary>
public class GridObject : MonoBehaviour
{
    public readonly List<Vector2Int> Cells = new List<Vector2Int>();
    public bool Indestructible;
    public bool IsTerrain;
    /// <summary>Para terreno: '#' terra, 'S' pedra, 'i' gelo.</summary>
    public char TerrainKind = '.';

    public void Register()
    {
        foreach (var c in Cells) Level.Occupied[c] = this;
    }

    public void Unregister()
    {
        foreach (var c in Cells)
            if (Level.Occupied.TryGetValue(c, out var o) && o == this) Level.Occupied.Remove(c);
    }

    void OnDestroy() => Unregister();
}
