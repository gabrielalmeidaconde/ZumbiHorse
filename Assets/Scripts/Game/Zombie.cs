using System.Collections.Generic;
using UnityEngine;

public enum ZombieState { Alive, Dead, Finished }

/// <summary>
/// Controle de plataforma do zumbi: corrida, pulo variável, coyote time, buffer de pulo,
/// deslizar e pular na parede, gelo e molas. Zumbis atravessam uns aos outros.
/// </summary>
public class Zombie : MonoBehaviour
{
    // ------------------------------------------------------------------ ajustes
    public const float MaxSpeed = 7.5f;
    public const float GroundAccel = 85f, GroundDecel = 75f, AirAccel = 55f, AirDecel = 20f;
    const float IceAccel = 14f, IceDecel = 2.5f;
    public const float Gravity = 38f, FallMultiplier = 1.5f, LowJumpMultiplier = 2.4f, MaxFall = 20f;
    public const float JumpVelocity = 15.6f;
    const float WallSlideSpeed = 3f, WallJumpX = 9f, WallJumpY = 14.5f, WallJumpLock = 0.17f;
    const float CoyoteTime = 0.1f, JumpBufferTime = 0.13f;

    public const float Width = 0.6f, Height = 0.95f;

    public int Index;
    public InputSource Controls;
    public ZombieState State = ZombieState.Alive;
    public bool ControlsEnabled = true;

    public static readonly List<Zombie> All = new List<Zombie>();

    Rigidbody2D rb;
    CapsuleCollider2D body;
    SpriteRenderer sr;
    Transform visual;
    Sprite[] frames;

    float coyoteTimer, jumpBufferTimer, controlLockTimer, springCooldown, animTime, danceTime;
    int dance = -1, lastDance = -1;   // dança atual (-1 = não está dançando)
    bool grounded, wasGrounded, jumpCut;
    int wallDir;
    bool onIce;
    Vector2 groundVelocity;     // trem ou esteira embaixo dos pés
    Wind wind;                  // vento do último passo de física
    Portal portalLock;          // portal de onde acabou de sair (não teleporta de volta)
    float portalCooldown;
    bool faceRight = true;
    Vector3 squash = Vector3.one;

    static PhysicsMaterial2D noFriction;
    static readonly List<Collider2D> hits = new List<Collider2D>();

    public Vector2 Position => transform.position;
    public Vector2 Velocity => rb != null ? rb.linearVelocity : Vector2.zero;
    public bool IsGrounded => grounded;
    public bool OnIce => onIce;
    /// <summary>Trem embaixo dos pés (pegando carona), ou null.</summary>
    public Train GroundTrain { get; private set; }
    /// <summary>Gangorra embaixo dos pés (ela usa isso para saber o peso de cada lado), ou null.</summary>
    public Seesaw GroundSeesaw { get; private set; }

    public static Zombie Spawn(int index, InputSource controls, Vector2 pos)
    {
        var go = new GameObject($"Zumbi P{index + 1}");
        go.transform.position = pos;
        var z = go.AddComponent<Zombie>();
        z.Index = index;
        z.Controls = controls;
        z.frames = Art.ZombieFrames(index);
        z.Build();
        foreach (var other in All)
            if (other != null && other != z) Physics2D.IgnoreCollision(z.body, other.body, true);
        All.Add(z);
        return z;
    }

    void Build()
    {
        if (noFriction == null) noFriction = new PhysicsMaterial2D("SemAtrito") { friction = 0f, bounciness = 0f };

        rb = gameObject.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;

        body = gameObject.AddComponent<CapsuleCollider2D>();
        body.direction = CapsuleDirection2D.Vertical;
        body.size = new Vector2(Width, Height);
        body.offset = new Vector2(0f, Height / 2f);
        body.sharedMaterial = noFriction;

        var v = new GameObject("Visual");
        v.transform.SetParent(transform, false);
        visual = v.transform;
        sr = v.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        sr.sortingOrder = 10 + Index;
        animTime = Random.Range(0f, 1f);
    }

    void OnDestroy() => All.Remove(this);

    public static void ClearAll()
    {
        for (int i = All.Count - 1; i >= 0; i--)
            if (All[i] != null) Destroy(All[i].gameObject);
        All.Clear();
    }

    // ------------------------------------------------------------------ loop

    void Update()
    {
        if (State != ZombieState.Alive) return;
        if (ControlsEnabled && Controls != null && Controls.JumpDown) jumpBufferTimer = JumpBufferTime;
        if (ControlsEnabled && Controls != null && Controls.DanceDown && grounded)
        {
            lastDance = (lastDance + 1) % Art.DanceCount;
            dance = lastDance;
            danceTime = 0f;
        }
        Animate(Time.deltaTime);
    }

    void FixedUpdate()
    {
        if (State != ZombieState.Alive) return;
        float dt = Time.fixedDeltaTime;
        Vector2 v = rb.linearVelocity;

        CheckGround(v);
        CheckWalls();

        Vector2 move = ControlsEnabled && Controls != null ? Controls.Move : Vector2.zero;
        float ix = Mathf.Abs(move.x) > 0.25f ? Mathf.Clamp(move.x * 1.25f, -1f, 1f) : 0f;

        if (grounded) coyoteTimer = CoyoteTime; else coyoteTimer -= dt;
        jumpBufferTimer -= dt;
        controlLockTimer -= dt;
        springCooldown -= dt;
        portalCooldown -= dt;

        // velocidade "de base": carona no trem/esteira e vento lateral
        float baseVx = grounded ? groundVelocity.x : 0f;
        if (wind != null) baseVx += wind.Direction.x * wind.SideSpeed;

        // ---- horizontal
        float target = ix * MaxSpeed;
        float accel;
        if (grounded) accel = onIce ? (ix != 0 ? IceAccel : IceDecel) : (ix != 0 ? GroundAccel : GroundDecel);
        else accel = ix != 0 ? AirAccel : AirDecel;
        if (controlLockTimer > 0f) accel *= 0.12f;
        // depois de uma mola lateral, não corta a velocidade extra de uma vez
        if (!grounded && Mathf.Abs(v.x) > MaxSpeed && Mathf.Sign(v.x) == Mathf.Sign(target)) accel = AirDecel * 0.5f;
        v.x = Mathf.MoveTowards(v.x - baseVx, target, accel * dt) + baseVx;
        // Encostado na parede, não empurra contra ela: senão a cápsula engancha nas emendas
        // entre blocos empilhados e perde a velocidade do pulo.
        if (wallDir != 0 && v.x * wallDir > 0f) v.x = 0f;

        // ---- pulo
        if (jumpBufferTimer > 0f)
        {
            if (coyoteTimer > 0f)
            {
                v.y = JumpVelocity;
                jumpCut = true;
                coyoteTimer = 0f;
                jumpBufferTimer = 0f;
                grounded = false;
                squash = new Vector3(0.75f, 1.25f, 1f);
                Sfx.PlayVar("jump", 0.7f);
                Fx.Dust(Position, 4);
                if (DemoDriver.Active) Debug.Log($"[Demo] P{Index + 1} pulou em {Position} vx={v.x:0.0}");
            }
            else if (wallDir != 0)
            {
                v.x = -wallDir * WallJumpX;
                v.y = WallJumpY;
                jumpCut = true;
                jumpBufferTimer = 0f;
                controlLockTimer = WallJumpLock;
                faceRight = wallDir < 0;
                squash = new Vector3(0.75f, 1.25f, 1f);
                Sfx.PlayVar("jump", 0.7f);
                Fx.Dust(Position + new Vector2(wallDir * Width * 0.5f, 0.5f), 4);
            }
        }

        // ---- vento para cima/baixo
        if (wind != null && Mathf.Abs(wind.Direction.y) > 0.5f)
        {
            float dirY = Mathf.Sign(wind.Direction.y);
            if (v.y * dirY < wind.MaxSpeed) v.y += dirY * wind.Strength * dt;
            jumpCut = false;
        }

        // ---- gravidade
        float g = Gravity;
        if (v.y < 0f) g *= FallMultiplier;
        else if (jumpCut && !(Controls != null && Controls.JumpHeld && ControlsEnabled)) g *= LowJumpMultiplier;
        if (v.y <= 0f) jumpCut = false;
        v.y -= g * dt;

        float maxFall = MaxFall;
        bool sliding = wallDir != 0 && !grounded && v.y < 0f && ix != 0 && Mathf.Sign(ix) == wallDir;
        if (sliding) maxFall = WallSlideSpeed;
        if (v.y < -maxFall) v.y = Mathf.MoveTowards(v.y, -maxFall, 120f * dt);

        rb.linearVelocity = v;

        CheckTouches();
        if (State == ZombieState.Alive && transform.position.y < Level.KillY) Die(-1);
    }

    void CheckGround(Vector2 v)
    {
        wasGrounded = grounded;
        grounded = false;
        onIce = false;
        groundVelocity = Vector2.zero;
        GroundTrain = null;
        GroundSeesaw = null;
        if (v.y > 0.5f) return;

        hits.Clear();
        var filter = new ContactFilter2D { useTriggers = false };
        Physics2D.OverlapBox(Position + new Vector2(0f, -0.03f), new Vector2(Width * 0.8f, 0.08f), 0f, filter, hits);
        foreach (var h in hits)
        {
            if (IsZombie(h)) continue;
            grounded = true;
            var s = h.GetComponent<Surface>();
            if (s != null)
            {
                if (s.Slippery) onIce = true;
                groundVelocity += s.ConveyorVelocity;
            }
            var train = h.GetComponentInParent<Train>();
            if (train != null) { groundVelocity += train.Velocity; GroundTrain = train; }
            var mover = h.GetComponentInParent<MovingBlock>();
            if (mover != null && h.attachedRigidbody == mover.Body) groundVelocity += mover.Velocity;
            var seesaw = h.GetComponentInParent<Seesaw>();
            if (seesaw != null && h.attachedRigidbody == seesaw.Plank) GroundSeesaw = seesaw;
            var crumble = h.GetComponent<Crumble>();
            if (crumble != null) crumble.Touch();
        }

        if (grounded && !wasGrounded)
        {
            squash = new Vector3(1.25f, 0.78f, 1f);
            Fx.Dust(Position, 3);
            Sfx.Play("land", 0.5f);
        }
    }

    void CheckWalls()
    {
        wallDir = 0;
        var filter = new ContactFilter2D { useTriggers = false };
        for (int side = -1; side <= 1; side += 2)
        {
            hits.Clear();
            Physics2D.OverlapBox(Position + new Vector2(side * (Width / 2f + 0.04f), Height * 0.55f), new Vector2(0.06f, Height * 0.5f), 0f, filter, hits);
            foreach (var h in hits)
            {
                if (IsZombie(h)) continue;
                wallDir = side;
                break;
            }
            if (wallDir != 0) break;
        }
    }

    void CheckTouches()
    {
        hits.Clear();
        var filter = new ContactFilter2D { useTriggers = true };
        body.Overlap(filter, hits);
        wind = null;
        bool touchingPortal = false;
        foreach (var h in hits)
        {
            if (h == null || IsZombie(h)) continue;

            var w = h.GetComponent<Wind>();
            if (w != null && w.Item != null && w.Item.Placed) wind = w;

            var portal = h.GetComponent<Portal>();
            if (portal != null)
            {
                touchingPortal = true;
                if (portal != portalLock && portal.Active && portalCooldown <= 0f)
                {
                    Teleport(portal.Partner);
                    return;
                }
                continue;
            }

            var hazard = h.GetComponent<Hazard>();
            if (hazard != null && hazard.isActiveAndEnabled)
            {
                Die(hazard.Owner);
                return;
            }

            var spring = h.GetComponent<Spring>();
            if (spring != null && springCooldown <= 0f)
            {
                rb.linearVelocity = spring.Launch();
                springCooldown = 0.2f;
                jumpCut = false;
                coyoteTimer = 0f;
                if (Mathf.Abs(spring.Direction.y) < 0.5f) controlLockTimer = 0.3f;
                squash = new Vector3(0.7f, 1.3f, 1f);
                continue;
            }

            if (h.GetComponent<Goal>() != null)
            {
                Finish();
                return;
            }
        }
        if (!touchingPortal) portalLock = null;
    }

    void Teleport(Portal exit)
    {
        Fx.Burst(Position + Vector2.up * 0.5f, new Color(0.4f, 1f, 0.6f), 10, 4f, 0.12f, 0f);
        Vector2 to = exit.ExitPoint;
        rb.position = to;
        transform.position = to;
        portalLock = exit;
        portalCooldown = 0.25f;
        Fx.Burst(to + Vector2.up * 0.5f, new Color(0.8f, 0.5f, 1f), 10, 4f, 0.12f, 0f);
        Sfx.PlayVar("portal", 0.7f);
    }

    static bool IsZombie(Collider2D c) => c.attachedRigidbody != null && c.attachedRigidbody.GetComponent<Zombie>() != null;

    // ------------------------------------------------------------------ fim da corrida

    public void Die(int killer)
    {
        if (State != ZombieState.Alive) return;
        State = ZombieState.Dead;
        if (DemoDriver.Active) Debug.Log($"[Demo] P{Index + 1} morreu em {Position} (armadilha de {killer})");

        Vector2 center = Position + Vector2.up * 0.5f;
        Fx.Burst(center, new Color(0.55f, 0.08f, 0.1f), 14, 7f, 0.16f);
        Fx.Burst(center, Art.PlayerColor(Index), 8, 6f, 0.14f);
        var corpse = Fx.Spawn(sr.sprite, Position, Color.white, 1f, 15);
        corpse.GetComponent<SpriteRenderer>().flipX = sr.flipX;
        corpse.Velocity = new Vector2(Random.Range(-3f, 3f), 11f);
        corpse.Gravity = 30f;
        corpse.Spin = Random.Range(300f, 600f) * (Random.value < 0.5f ? -1f : 1f);
        corpse.Life = 1.4f;
        corpse.Shrink = false;
        Sfx.PlayVar("death");
        GameManager.Shake(0.12f);

        if (GameManager.Instance != null) GameManager.Instance.OnZombieDied(this, killer);
        gameObject.SetActive(false);
    }

    void Finish()
    {
        if (State != ZombieState.Alive) return;
        State = ZombieState.Finished;
        if (DemoDriver.Active) Debug.Log($"[Demo] P{Index + 1} chegou no cérebro");
        Fx.Burst(Level.GoalPosition, new Color(1f, 0.6f, 0.75f), 18, 6f, 0.16f, 8f);
        Fx.Burst(Level.GoalPosition, Art.PlayerColor(Index), 12, 5f, 0.14f, 8f);
        Sfx.Play("goal");
        if (GameManager.Instance != null) GameManager.Instance.OnZombieFinished(this);
        gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ animação

    void Animate(float dt)
    {
        animTime += dt;
        Vector2 v = rb.linearVelocity;
        Vector2 move = ControlsEnabled && Controls != null ? Controls.Move : Vector2.zero;
        if (controlLockTimer <= 0f)
        {
            if (move.x > 0.25f) faceRight = true;
            else if (move.x < -0.25f) faceRight = false;
        }
        else if (Mathf.Abs(v.x) > 0.5f) faceRight = v.x > 0f;

        // dança sem parar até andar ou sair do chão (pular, mola, vento...)
        if (Mathf.Abs(move.x) > 0.25f || !grounded) dance = -1;
        if (dance >= 0)
        {
            danceTime += dt;
            sr.sprite = Art.DanceSprite(Index, dance, danceTime);
            sr.flipX = !faceRight;
            squash = Vector3.Lerp(squash, Vector3.one, dt * 12f);
            visual.localScale = squash;
            visual.localRotation = Quaternion.identity;
            return;
        }

        int frame;
        if (!grounded) frame = v.y > 0f ? 6 : 7;
        else if (Mathf.Abs(v.x) > 0.6f) frame = 2 + (int)(animTime * (6f + Mathf.Abs(v.x))) % 4;
        else frame = (int)(animTime * 2f) % 2;

        sr.sprite = frames[frame];
        sr.flipX = !faceRight;

        squash = Vector3.Lerp(squash, Vector3.one, dt * 12f);
        // balanço de zumbi andando
        float lean = grounded && Mathf.Abs(v.x) > 0.6f ? Mathf.Sin(animTime * 9f) * 4f : 0f;
        visual.localScale = squash;
        visual.localRotation = Quaternion.Euler(0, 0, lean);
    }
}
