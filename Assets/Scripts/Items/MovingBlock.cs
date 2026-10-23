using UnityEngine;

/// <summary>
/// Item "Plataforma Móvel": plataforma de 3 que vai e volta 3 células durante a corrida e carrega quem está em cima.
/// Fica sempre na horizontal; a rotação do item só escolhe a direção (direita, cima, esquerda, baixo).
/// </summary>
public class MovingBlock : RunBehaviour
{
    public const int Travel = 3;
    const float Speed = 2.2f;

    public Rigidbody2D Body;
    public Transform Rail;

    /// <summary>Velocidade agora: o zumbi em cima soma isso à dele (como na esteira e no trem).</summary>
    public Vector2 Velocity { get; private set; }

    Vector2 dir = Vector2.right;
    float t;
    int sign = 1;
    bool running, logged;

    public void SetDirection(int rotation)
    {
        dir = ItemDefs.Rotate(Vector2.right, rotation);
        Rail.localPosition = dir * (Travel / 2f);
        Rail.localRotation = Quaternion.Euler(0f, 0f, Mathf.Abs(dir.y) > 0.5f ? 90f : 0f);
        Park();
    }

    /// <summary>Volta para o começo do trajeto (fora da corrida ela fica parada lá).</summary>
    void Park()
    {
        t = 0f;
        sign = 1;
        Velocity = Vector2.zero;
        Body.transform.localPosition = Vector3.zero;
    }

    public override void OnRunStart() { Park(); running = true; logged = false; }
    public override void OnRunEnd() { running = false; Park(); }

    void FixedUpdate()
    {
        if (!running) return;
        t += sign * Speed * Time.fixedDeltaTime / Travel;
        if (t >= 1f)
        {
            t = 1f;
            sign = -1;
            if (DemoDriver.Active && !logged) { logged = true; Debug.Log($"[Demo] plataforma móvel chegou na ponta em {Body.position}"); }
        }
        else if (t <= 0f) { t = 0f; sign = 1; }
        float e = t * t * (3f - 2f * t); // freia nas pontas
        Vector2 target = (Vector2)transform.position + dir * (Travel * e);
        Velocity = (target - Body.position) / Time.fixedDeltaTime;
        Body.MovePosition(target);
    }
}
