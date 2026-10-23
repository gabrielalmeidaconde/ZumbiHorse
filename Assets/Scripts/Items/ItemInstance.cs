using System.Collections.Generic;
using UnityEngine;

/// <summary>Um item na mão de um jogador (fantasma) ou já posicionado na fase.</summary>
public class ItemInstance : MonoBehaviour
{
    public ItemDef Def;
    public int Owner = -1;
    public int Rotation;
    public bool Placed;
    public Vector2Int Pivot;

    SpriteRenderer[] renderers;
    int[] baseOrders;
    Color[] baseColors;
    Collider2D[] colliders;

    public static readonly List<ItemInstance> AllPlaced = new List<ItemInstance>();

    public void CacheParts()
    {
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseOrders = new int[renderers.Length];
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            baseOrders[i] = renderers[i].sortingOrder;
            baseColors[i] = renderers[i].color;
        }
        colliders = GetComponentsInChildren<Collider2D>(true);
    }

    public void SetSortingBoost(int boost)
    {
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].sortingOrder = baseOrders[i] + boost;
    }

    public void SetTint(Color tint)
    {
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i] != null) renderers[i].color = baseColors[i] * tint;
    }

    public void SetCollidersEnabled(bool on)
    {
        foreach (var c in colliders) if (c != null) c.enabled = on;
    }

    public Vector2 RotatedCenter => ItemDefs.Rotate(Def.Center, Rotation);

    public void ApplyRotation()
    {
        transform.rotation = Def.KeepUpright ? Quaternion.identity : Quaternion.Euler(0, 0, Rotation * 90f);
        var mover = GetComponent<MovingBlock>();
        if (mover != null) mover.SetDirection(Rotation);
    }

    /// <summary>Posiciona livremente (sem grade) com o centro visual em p.</summary>
    public void ShowAt(Vector2 p) => transform.position = p - RotatedCenter;

    public List<Vector2Int> Footprint(Vector2Int pivot)
    {
        var list = new List<Vector2Int>();
        foreach (var c in ItemDefs.Footprint(Def, Rotation)) list.Add(pivot + c);
        return list;
    }

    /// <summary>Célula pivô para que o centro visual fique o mais perto possível de p.</summary>
    public Vector2Int PivotFor(Vector2 p) => Level.WorldToCell(p - RotatedCenter);

    public void SnapTo(Vector2Int pivot)
    {
        Pivot = pivot;
        transform.position = Level.CellCenter(pivot);
    }

    public bool CanPlaceAt(Vector2Int pivot) => CanPlace(Def, pivot, Rotation);

    /// <summary>Dá para colocar o item 'def' com esta rotação nesta célula?</summary>
    public static bool CanPlace(ItemDef def, Vector2Int pivot, int rotation)
    {
        if (def.Type == ItemType.Bomba) return Level.InBounds(pivot);
        foreach (var cell in ItemDefs.Footprint(def, rotation))
        {
            var c = pivot + cell;
            if (!Level.InBounds(c) || Level.IsProtected(c)) return false;
            if (Level.Occupied.TryGetValue(c, out var o) && o != null) return false;
        }
        return true;
    }

    /// <summary>Fixa o item na fase. Retorna false para a bomba (que explode e some).</summary>
    public bool Place()
    {
        if (Def.Type == ItemType.Bomba)
        {
            Explode();
            return false;
        }
        Placed = true;
        SetTint(Color.white);
        SetSortingBoost(0);
        SetCollidersEnabled(true);
        var g = gameObject.AddComponent<GridObject>();
        g.Cells.AddRange(Footprint(Pivot));
        g.Register();
        AllPlaced.Add(this);
        if (Level.Root != null) transform.SetParent(Level.Root, true);
        Sfx.PlayVar("place");
        Fx.Dust((Vector2)transform.position + RotatedCenter + Vector2.down * 0.5f, 6);
        return true;
    }

    /// <summary>O que uma bomba nesta célula destrói (área 3x3, menos o que é indestrutível).</summary>
    public static HashSet<GridObject> BlastVictims(Vector2Int pivot)
    {
        var victims = new HashSet<GridObject>();
        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            {
                var c = pivot + new Vector2Int(dx, dy);
                if (Level.Occupied.TryGetValue(c, out var o) && o != null && !o.Indestructible) victims.Add(o);
            }
        return victims;
    }

    void Explode()
    {
        Vector2 center = Level.CellCenter(Pivot);
        var victims = BlastVictims(Pivot);
        foreach (var v in victims)
        {
            v.Unregister();
            Fx.Burst(v.transform.position, new Color(0.45f, 0.35f, 0.3f), 6, 6f, 0.2f);
            var item = v.GetComponent<ItemInstance>();
            if (item != null) AllPlaced.Remove(item);
            Destroy(v.gameObject);
        }
        Level.RefreshTerrainSprites();
        Fx.Explosion(center);
        Sfx.Play("bomb");
        GameManager.Shake(0.35f);
        Destroy(gameObject);
    }

    void OnDestroy() => AllPlaced.Remove(this);

    /// <summary>Dono do item (para dar ponto de armadilha) a partir de qualquer filho.</summary>
    public static int OwnerOf(Component c)
    {
        var item = c.GetComponentInParent<ItemInstance>();
        return item != null ? item.Owner : -1;
    }
}
