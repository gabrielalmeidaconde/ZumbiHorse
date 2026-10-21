using UnityEngine;

/// <summary>Canhão de ossos: durante a corrida, atira um osso a cada poucos segundos.</summary>
public class Turret : RunBehaviour
{
    public const float Interval = 2.2f;
    public ItemInstance Item;
    public Transform Visual;
    public Collider2D Body;

    float timer;

    public override void OnRunStart() => timer = 0.7f;

    void FixedUpdate()
    {
        if (Item == null || !Item.Placed || !GameManager.RunActive) return;
        timer -= Time.fixedDeltaTime;
        if (timer <= 0f)
        {
            timer = Interval;
            Fire();
        }
    }

    void Update()
    {
        if (Visual != null) Visual.localPosition = Vector3.Lerp(Visual.localPosition, Vector3.zero, Time.deltaTime * 10f);
    }

    void Fire()
    {
        Vector2 dir = transform.right;
        Projectile.Spawn((Vector2)transform.position + dir * 0.6f, dir, Item.Owner, Body);
        if (Visual != null) Visual.localPosition = new Vector3(-0.15f, 0, 0);
        Sfx.PlayVar("shoot", 0.6f);
    }
}
