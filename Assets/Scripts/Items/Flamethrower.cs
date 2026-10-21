using UnityEngine;

/// <summary>Lança-chamas: durante a corrida, cospe fogo à frente (2,5 células) de tempos em tempos.</summary>
public class Flamethrower : RunBehaviour
{
    const float OnTime = 1.1f, OffTime = 1.7f, WarnTime = 0.4f;

    public ItemInstance Item;
    public GameObject Flame;

    float timer;
    bool on;

    public override void OnRunStart()
    {
        timer = 0.9f;
        SetOn(false);
    }

    public override void OnRunEnd() => SetOn(false);

    void Start() => SetOn(false);

    /// <summary>Área do fogo no mundo (mesmo com o fogo apagado).</summary>
    public Rect FlameRect
    {
        get
        {
            Vector2 a = transform.TransformPoint(new Vector2(0.5f, -0.3f));
            Vector2 b = transform.TransformPoint(new Vector2(3.0f, 0.3f));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }
    }

    /// <summary>O fogo vai estar aceso daqui a 'ahead' segundos?</summary>
    public bool PredictOn(float ahead)
    {
        if (Item == null || !Item.Placed || !GameManager.RunActive) return false;
        bool state = on;
        float t = timer;
        while (ahead > t)
        {
            ahead -= Mathf.Max(0.01f, t);
            state = !state;
            t = state ? OnTime : OffTime;
        }
        return state;
    }

    void SetOn(bool value)
    {
        on = value;
        if (Flame != null) Flame.SetActive(value);
    }

    void Update()
    {
        if (Item == null || !Item.Placed || !GameManager.RunActive)
        {
            if (on) SetOn(false);
            return;
        }
        timer -= Time.deltaTime;
        if (!on && timer < WarnTime && Random.value < 0.3f)
        {
            var p = Fx.Spawn(Art.Pixel, (Vector2)transform.position + (Vector2)transform.right * 0.55f, new Color(1f, 0.6f, 0.2f), 0.1f, 30);
            p.Velocity = (Vector2)transform.right * 2f + Random.insideUnitCircle;
            p.Gravity = 0f;
            p.Life = 0.25f;
        }
        if (timer > 0f) return;
        SetOn(!on);
        timer = on ? OnTime : OffTime;
        if (on) Sfx.PlayVar("flame", 0.5f);
    }
}
