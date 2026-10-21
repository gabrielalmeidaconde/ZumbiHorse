using UnityEngine;

/// <summary>Gira, balança e/ou desliza um objeto (serra, cérebro, neblina...).</summary>
public class Spinner : MonoBehaviour
{
    public float DegreesPerSecond;
    public float BobAmplitude;
    public float BobSpeed = 2f;
    public float DriftX;
    public float WrapHalfWidth = 30f;

    Vector3 basePos;
    float phase;

    void Start()
    {
        basePos = transform.localPosition;
        phase = Random.Range(0f, 10f);
    }

    void Update()
    {
        if (DegreesPerSecond != 0f) transform.Rotate(0, 0, DegreesPerSecond * Time.deltaTime);
        if (DriftX != 0f)
        {
            basePos.x += DriftX * Time.deltaTime;
            if (basePos.x > WrapHalfWidth) basePos.x -= WrapHalfWidth * 2f;
            if (basePos.x < -WrapHalfWidth) basePos.x += WrapHalfWidth * 2f;
        }
        Vector3 p = basePos;
        if (BobAmplitude != 0f) p += Vector3.up * Mathf.Sin((Time.time + phase) * BobSpeed) * BobAmplitude;
        if (BobAmplitude != 0f || DriftX != 0f) transform.localPosition = p;
    }
}
