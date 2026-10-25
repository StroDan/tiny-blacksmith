using UnityEngine;

// A clickable anvil point. Red = Durability, Yellow = Balance. It shrinks over
// its lifetime and disables itself when it runs out. ForgeGameManager raycasts
// against it on left-click instead of using Unity's OnMouseDown: that legacy
// mouse-picking message isn't reliably sent when the project's Active Input
// Handling is set to "Input System Package (New)" only.
public class HitZoneMarker : MonoBehaviour
{
    public PointKind kind;

    private Vector3 originalScale;
    private float elapsed;
    private float lifetime = 1f;

    private void Awake()
    {
        originalScale = transform.localScale;
    }

    public void Activate(Vector3 position, float life)
    {
        lifetime = Mathf.Max(0.1f, life);
        elapsed = 0f;
        transform.position = position;
        transform.localScale = originalScale;
        gameObject.SetActive(true);
    }

    public void Deactivate()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / lifetime);
        transform.localScale = Vector3.Lerp(originalScale, Vector3.zero, t);

        if (elapsed >= lifetime)
            gameObject.SetActive(false);
    }
}
