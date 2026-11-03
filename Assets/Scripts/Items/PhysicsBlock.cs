using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Base dos blocos com física (caixote, plataforma frágil e pedregulho): um corpo cinemático que se move sozinho
/// durante a corrida, testando a fase com BoxCast. Não usa o motor de física para se mexer, então o movimento é
/// sempre igual e os bots conseguem prever. Antes de cada corrida (e no fim dela) o corpo volta para onde o item
/// foi colocado: na fase de posicionar, e para a bomba, ele está sempre no lugar original.
/// </summary>
public abstract class PhysicsBlock : RunBehaviour
{
    public const float Gravity = 30f, MaxFall = 18f;
    /// <summary>Caindo mais rápido que isso, machuca quem estiver embaixo.</summary>
    public const float CrushSpeed = 7f;
    const float Skin = 0.02f;

    public ItemInstance Item;
    public Rigidbody2D Body;
    /// <summary>Caixa de colisão do corpo (tamanho e centro em relação ao corpo).</summary>
    public Vector2 Size = Vector2.one, Offset;
    public Transform Visual;
    public Vector2 VisualHome;
    /// <summary>Gatilho embaixo do bloco que só liga enquanto ele cai rápido.</summary>
    public GameObject Crush;

    /// <summary>Velocidade agora (o zumbi em cima soma o x dela, como na esteira).</summary>
    public Vector2 Velocity { get; protected set; }
    /// <summary>Caiu para fora da fase (ou no ácido): some até a próxima corrida.</summary>
    public bool Gone { get; private set; }

    protected Vector2 pos, vel;
    protected bool running;
    Vector2 lastPos;
    float crushTimer;

    /// <summary>Caindo, para em cima dos zumbis (em vez de atravessar e empurrar eles para dentro do chão).</summary>
    protected virtual bool LandsOnZombies => true;

    static readonly List<RaycastHit2D> castHits = new List<RaycastHit2D>();
    protected static readonly ContactFilter2D solidFilter = new ContactFilter2D { useTriggers = false };

    public override void OnRunStart()
    {
        ResetBody();
        running = Item != null && Item.Placed;
        if (running) Body.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    public override void OnRunEnd()
    {
        running = false;
        ResetBody();
        Body.interpolation = RigidbodyInterpolation2D.None;
    }

    protected virtual void ResetBody()
    {
        Gone = false;
        crushTimer = 0f;
        vel = Vector2.zero;
        Velocity = Vector2.zero;
        Body.gameObject.SetActive(true);
        Body.transform.localPosition = Vector3.zero;
        pos = lastPos = Body.transform.position;
        Body.position = pos;
        if (Visual != null)
        {
            Visual.localPosition = VisualHome;
            Visual.localRotation = Quaternion.identity;
        }
        if (Crush != null) Crush.SetActive(false);
    }

    /// <summary>Um zumbi está em pé em cima do bloco.</summary>
    public virtual void Stood(Zombie z) { }

    /// <summary>Como o bloco entra no mapa dos bots (por padrão, onde está).</summary>
    public virtual Rect NavRect(Rect current) => current;

    // ------------------------------------------------------------------ física compartilhada

    /// <summary>Caixa usada nos testes: um pouco menor na direção que não está sendo testada (para não raspar no chão ou na parede).</summary>
    protected Vector2 CastSize(bool vertical) => vertical ? new Vector2(Size.x - 0.14f, Size.y - 0.04f) : new Vector2(Size.x - 0.04f, Size.y - 0.08f);

    /// <summary>Não tem nada logo embaixo (vai cair)?</summary>
    protected bool Unsupported() => FreeDistance(pos + Offset, CastSize(true), Vector2.down, 0.05f) > 0.03f;

    /// <summary>Quanto a caixa (centro c, tamanho s) anda na direção d até encostar em algo sólido (no máximo 'dist').</summary>
    protected float FreeDistance(Vector2 c, Vector2 s, Vector2 d, float dist, bool zombiesSolid = false)
    {
        castHits.Clear();
        Physics2D.BoxCast(c, s, 0f, d, solidFilter, castHits, dist + Skin);
        float best = dist;
        foreach (var h in castHits)
        {
            if (Ignore(h.collider, zombiesSolid)) continue;
            best = Mathf.Min(best, Mathf.Max(0f, h.distance - Skin));
        }
        return best;
    }

    protected bool Ignore(Collider2D c, bool zombiesSolid)
    {
        var rb = c.attachedRigidbody;
        if (rb != null && rb == Body) return true;
        if (rb != null && rb.GetComponent<Zombie>() != null) return !zombiesSolid;
        if (rb != null && rb.GetComponent<Projectile>() != null) return true;
        var item = c.GetComponentInParent<ItemInstance>();
        return item != null && !item.Placed; // fantasmas e itens da caixa
    }

    /// <summary>Gravidade: cai até encostar em algo. Devolve a velocidade com que bateu (0 se continua no ar).</summary>
    protected float FallStep(ref Vector2 p, ref Vector2 v, float dt)
    {
        v.y = Mathf.Max(v.y - Gravity * dt, -MaxFall);
        float want = -v.y * dt;
        float free = FreeDistance(p + Offset, CastSize(true), Vector2.down, want, LandsOnZombies);
        if (free < want - 0.0001f)
        {
            p.y -= free;
            float hit = -v.y;
            v.y = 0f;
            if (hit > CrushSpeed) crushTimer = 0.12f; // bateu rápido: quem estiver embaixo leva o golpe
            return hit;
        }
        p.y -= want;
        return 0f;
    }

    /// <summary>Aplica a posição calculada no corpo (e o gatilho de esmagar). Chamar no fim de cada FixedUpdate.</summary>
    protected void Finish(float dt)
    {
        crushTimer -= dt;
        if (Crush != null) Crush.SetActive(vel.y < -CrushSpeed || crushTimer > 0f);
        Velocity = (pos - lastPos) / dt;
        lastPos = pos;
        Body.MovePosition(pos);
        if (pos.y < Level.KillY - 1f) Vanish();
    }

    protected void Vanish()
    {
        Gone = true;
        Velocity = Vector2.zero;
        Body.gameObject.SetActive(false);
    }

    /// <summary>Barulho e poeira de quando o bloco bate no chão.</summary>
    protected void Landed(float speed)
    {
        Sfx.PlayVar("thump", Mathf.Clamp01(0.25f + speed / 20f));
        Fx.Dust(pos + Offset + Vector2.down * (Size.y / 2f), Mathf.RoundToInt(Size.x * 3f));
    }
}
