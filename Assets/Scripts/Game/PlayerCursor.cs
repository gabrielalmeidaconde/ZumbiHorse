using UnityEngine;

/// <summary>Cursor (mãozinha colorida) usado para escolher e posicionar itens.</summary>
public class PlayerCursor : MonoBehaviour
{
    const float Speed = 11f;

    public int Index;
    SpriteRenderer sr;

    public Vector2 Position
    {
        get => transform.position;
        set => transform.position = new Vector3(value.x, value.y, 0f);
    }

    public static PlayerCursor Create(int index, Vector2 pos)
    {
        var go = new GameObject($"Cursor P{index + 1}");
        var c = go.AddComponent<PlayerCursor>();
        c.Index = index;
        c.sr = go.AddComponent<SpriteRenderer>();
        c.sr.sprite = Art.Cursor;
        c.sr.color = Art.PlayerColors[index % 4];
        c.sr.sortingOrder = 200 + index;
        c.Position = pos;
        return c;
    }

    public void Move(Vector2 input, Rect bounds)
    {
        if (input.sqrMagnitude < 0.01f) return;
        Vector2 p = Position + input * Speed * Time.unscaledDeltaTime;
        p.x = Mathf.Clamp(p.x, bounds.xMin, bounds.xMax);
        p.y = Mathf.Clamp(p.y, bounds.yMin, bounds.yMax);
        Position = p;
    }

    public void SetVisible(bool on) => sr.enabled = on;
}
