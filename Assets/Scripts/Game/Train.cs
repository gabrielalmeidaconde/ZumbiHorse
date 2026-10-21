using UnityEngine;

/// <summary>
/// Trem fantasma: vai e volta sobre o trilho. O teto é uma plataforma (dá para pegar carona),
/// mas os para-choques das duas pontas matam.
/// </summary>
public class Train : MonoBehaviour
{
    const float LocoWidth = 2.5f, WagonWidth = 2f, EndPause = 0.6f;

    public Vector2 Velocity { get; private set; }
    public TrainDef Def { get; private set; }
    public float Speed => speed;
    public float LeftX => x;
    public float RightX => x + length;

    Rigidbody2D rb;
    float minX, maxX, x, y, speed, pauseTimer, smokeTimer, length;
    int dir = 1;
    Transform leftLoco, rightLoco;

    public static Train Create(TrainDef def, Transform parent)
    {
        var go = new GameObject("Trem fantasma");
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<Train>();
        t.speed = def.Speed;
        t.Def = def;
        t.length = LocoWidth * 2f + WagonWidth * def.Wagons;
        t.minX = Level.Origin.x + def.FromX;
        t.maxX = Mathf.Max(t.minX, Level.Origin.x + def.ToX + 1 - t.length);
        t.x = Mathf.Lerp(t.minX, t.maxX, def.StartT);
        t.dir = def.StartT >= 0.5f ? -1 : 1;
        t.y = Level.Origin.y + def.TrackRow + 1f; // em cima do trilho
        go.transform.position = new Vector3(t.x, t.y, 0f);

        t.rb = go.AddComponent<Rigidbody2D>();
        t.rb.bodyType = RigidbodyType2D.Kinematic;
        t.rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        // locomotiva da esquerda (virada para a esquerda), vagões e locomotiva da direita
        t.leftLoco = t.AddCar(Art.Locomotive, 0f, LocoWidth, 1.35f, true);
        float cx = LocoWidth;
        for (int i = 0; i < def.Wagons; i++)
        {
            t.AddCar(Art.Wagon, cx, WagonWidth, 0.95f, false);
            cx += WagonWidth;
        }
        t.rightLoco = t.AddCar(Art.Locomotive, cx, LocoWidth, 1.35f, false);

        t.AddBumper(-0.08f);
        t.AddBumper(t.length + 0.08f);
        return t;
    }

    Transform AddCar(Sprite sprite, float x0, float width, float height, bool flip)
    {
        var car = new GameObject("Vagao");
        car.transform.SetParent(transform, false);
        car.transform.localPosition = new Vector3(flip ? x0 + width : x0, 0f, 0f);
        var sr = car.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.flipX = flip;
        sr.sortingOrder = 8;

        var col = gameObject.AddComponent<BoxCollider2D>();
        col.size = new Vector2(width - 0.05f, height);
        col.offset = new Vector2(x0 + width / 2f, height / 2f);
        return car.transform;
    }

    void AddBumper(float localX)
    {
        var b = new GameObject("Para-choque");
        b.transform.SetParent(transform, false);
        b.transform.localPosition = new Vector3(localX, 0.65f, 0f);
        var col = b.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        col.size = new Vector2(0.35f, 1.2f);
        b.AddComponent<Hazard>();
    }

    /// <summary>Posição (ponta esquerda) daqui a 'ahead' segundos.</summary>
    public float PredictX(float ahead)
    {
        float px = x, pause = pauseTimer;
        int d = dir;
        const float step = 0.05f;
        for (float t = 0f; t < ahead; t += step)
        {
            if (pause > 0f) { pause -= step; continue; }
            px += d * speed * step;
            if (px >= maxX || px <= minX) { px = Mathf.Clamp(px, minX, maxX); d = -d; pause = EndPause; }
        }
        return px;
    }

    /// <summary>Para-choques (as partes que matam) daqui a 'ahead' segundos.</summary>
    public void PredictBumpers(float ahead, out Rect left, out Rect right)
    {
        float px = PredictX(ahead);
        left = new Rect(px - 0.3f, y + 0.05f, 0.35f, 1.2f);
        right = new Rect(px + length - 0.05f, y + 0.05f, 0.35f, 1.2f);
    }

    /// <summary>Corpo do trem (o teto onde dá para pegar carona) daqui a 'ahead' segundos.</summary>
    public Rect PredictBody(float ahead) => new Rect(PredictX(ahead) + 0.7f, y, length - 1.4f, 1.15f); // sem as pontas (para-choques)

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        float prev = x;
        if (pauseTimer > 0f) pauseTimer -= dt;
        else
        {
            x += dir * speed * dt;
            if (x >= maxX || x <= minX)
            {
                x = Mathf.Clamp(x, minX, maxX);
                dir = -dir;
                pauseTimer = EndPause;
                if (GameManager.RunActive) Sfx.Play("horn", 0.3f);
            }
        }
        rb.MovePosition(new Vector2(x, y));
        Velocity = new Vector2((x - prev) / dt, 0f);
    }

    void Update()
    {
        smokeTimer -= Time.deltaTime;
        if (smokeTimer > 0f) return;
        smokeTimer = 0.22f;
        // fumaça fantasma saindo da chaminé da locomotiva da frente
        var loco = dir > 0 ? rightLoco : leftLoco;
        if (loco == null) return;
        float chimney = dir > 0 ? 1.8f : -1.8f;
        Vector2 p = (Vector2)loco.position + new Vector2(chimney, 1.45f);
        var puff = Fx.Spawn(Art.Pixel, p, new Color(0.65f, 0.95f, 0.75f, 0.45f), Random.Range(0.18f, 0.3f), 7);
        puff.Velocity = new Vector2(-dir * Random.Range(0.3f, 1f), Random.Range(0.8f, 1.6f));
        puff.Gravity = -0.5f;
        puff.Life = 1.1f;
        puff.Shrink = false;
    }
}
