using UnityEngine;

/// <summary>Marca um chão especial: gelo escorregadio ou esteira (empurra na direção de transform.right).</summary>
public class Surface : MonoBehaviour
{
    public bool Slippery;
    public float ConveyorSpeed;

    public Vector2 ConveyorVelocity => ConveyorSpeed == 0f ? Vector2.zero : (Vector2)transform.right * ConveyorSpeed;
}
