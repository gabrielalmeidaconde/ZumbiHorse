using UnityEngine;

/// <summary>Portal: quem entra num sai no outro do par, mantendo a velocidade.</summary>
public class Portal : MonoBehaviour
{
    public ItemInstance Item;
    public Portal Partner;
    public SpriteRenderer Sprite;

    public bool Active => Item != null && Item.Placed && Partner != null && Partner.Item != null && Partner.Item.Placed;

    /// <summary>Onde ficam os pés do zumbi ao sair deste portal: embaixo, no centro.</summary>
    public Vector2 ExitPoint
    {
        get
        {
            var cells = Item.Footprint(Item.Pivot);
            int minY = int.MaxValue;
            foreach (var c in cells) minY = Mathf.Min(minY, c.y);
            Vector2 center = (Vector2)Item.transform.position + Item.RotatedCenter;
            return new Vector2(center.x, Level.Origin.y + minY + 0.05f);
        }
    }

    void Update()
    {
        if (Sprite == null || Item == null || !Item.Placed) return;
        // pisca fraco enquanto o par não foi colocado (ou foi explodido)
        var c = Sprite.color;
        c.a = Active ? 1f : 0.45f + 0.15f * Mathf.Sin(Time.time * 4f);
        Sprite.color = c;
    }
}
