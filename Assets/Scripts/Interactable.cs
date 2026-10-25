using System.Collections.Generic;
using UnityEngine;

public enum InteractionKind { EndDay, OpenJournal, MaterialOrders, OpenDelivery }

// Something in the Main Room the player can use with E just by looking at it (the house door, the journal, the
// Material Order Book and the delivery crate by the storage).
// The player never walks, and the camera only snap-turns between a few seated views, so "looking at" means:
// the mouse pointer resting on it, or else the highest-priority one near the centre of the view (within
// maxViewAngle degrees, the closest to the centre winning a tie). No collider needed - that also means none of
// these can ever block the mouse raycasts the forging stations rely on.
// ForgeGameManager decides what each InteractionKind does (see RunMainRoom).
public class Interactable : MonoBehaviour
{
    public InteractionKind kind;
    public string prompt = "Interact";
    public float maxViewAngle = 32f;
    public int priority;   // among several in view with nothing pointed at, the highest wins (set at runtime)

    private static readonly List<Interactable> active = new List<Interactable>();
    private Renderer[] renderers;

    private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
    private void OnDisable() => active.Remove(this);

    public static Interactable Focused(Camera cam) => Focused(cam, null);

    public static Interactable Focused(Camera cam, Vector2? pointer)
    {
        if (cam == null) return null;
        Interactable best = null;
        float bestAngle = float.MaxValue;
        Vector3 eye = cam.transform.position, forward = cam.transform.forward;
        foreach (var i in active)
        {
            if (i == null || !i.isActiveAndEnabled) continue;
            float angle = Vector3.Angle(forward, i.transform.position - eye);
            if (angle > i.maxViewAngle) continue;
            if (pointer.HasValue && i.ScreenRect(cam).Contains(pointer.Value)) return i; // pointed at: nothing else matters
            if (best == null || i.priority > best.priority || (i.priority == best.priority && angle < bestAngle)) { best = i; bestAngle = angle; }
        }
        return best;
    }

    // Where it is on screen (pixels): the box around all its visible parts.
    public Rect ScreenRect(Camera cam)
    {
        if (renderers == null) renderers = GetComponentsInChildren<Renderer>();
        bool any = false;
        Bounds b = default;
        foreach (var r in renderers)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        if (!any) return Rect.zero;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        for (int c = 0; c < 8; c++)
        {
            Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1));
            Vector3 s = cam.WorldToScreenPoint(corner);
            if (s.z <= 0f) return Rect.zero; // partly behind the camera: not something you're pointing at
            min = Vector2.Min(min, s); max = Vector2.Max(max, s);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
}
