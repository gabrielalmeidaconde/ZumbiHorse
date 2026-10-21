using UnityEngine;

/// <summary>Caixa podre: treme quando um zumbi pisa, some e volta alguns segundos depois.</summary>
public class Crumble : RunBehaviour
{
    const float ShakeTime = 0.45f, GoneTime = 2.5f;

    enum State { Solid, Shaking, Gone }

    public ItemInstance Item;
    public SpriteRenderer Sprite;
    public BoxCollider2D Body;

    State state;
    float timer;
    static readonly System.Collections.Generic.List<Collider2D> hits = new System.Collections.Generic.List<Collider2D>();

    public void Touch()
    {
        if (state != State.Solid || Item == null || !Item.Placed || !GameManager.RunActive) return;
        state = State.Shaking;
        timer = ShakeTime;
        Sfx.PlayVar("crack", 0.6f);
    }

    public override void OnRunStart() => Restore();
    public override void OnRunEnd() => Restore();

    void Restore()
    {
        state = State.Solid;
        if (Body != null && Item != null && Item.Placed) Body.enabled = true;
        if (Sprite != null)
        {
            Sprite.transform.localPosition = Vector3.zero;
            var c = Sprite.color; c.a = 1f; Sprite.color = c;
        }
    }

    void Update()
    {
        if (state == State.Solid) return;
        timer -= Time.deltaTime;
        if (state == State.Shaking)
        {
            Sprite.transform.localPosition = (Vector3)(Random.insideUnitCircle * 0.06f);
            if (timer > 0f) return;
            state = State.Gone;
            timer = GoneTime;
            Body.enabled = false;
            Sprite.transform.localPosition = Vector3.zero;
            var c = Sprite.color; c.a = 0.18f; Sprite.color = c;
            Fx.Burst(transform.position, new Color(0.5f, 0.45f, 0.25f), 10, 4f, 0.16f);
            Sfx.PlayVar("crack", 0.8f);
        }
        else if (timer <= 0f)
        {
            // só volta se não tiver ninguém dentro
            hits.Clear();
            Physics2D.OverlapBox(transform.position, new Vector2(0.9f, 0.9f), 0f, new ContactFilter2D { useTriggers = false }, hits);
            foreach (var h in hits)
                if (h.attachedRigidbody != null && h.attachedRigidbody.GetComponent<Zombie>() != null)
                {
                    timer = 0.2f;
                    return;
                }
            Restore();
        }
    }
}
