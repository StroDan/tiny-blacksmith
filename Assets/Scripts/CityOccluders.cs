using System.Collections.Generic;
using UnityEngine;

// Boxes that hide things from the seat: every building the city and village builders place (they have no colliders,
// on purpose - nothing may block the forging stations' mouse raycasts). CityLife tests a sight line against these to
// know whether a walker could be seen before it appears or leaves.
// Lives in its own file: a MonoBehaviour saved into a scene must sit in a file of the same name, or the scene keeps
// only a broken embedded script reference (it crashed the player build).
public class CityOccluders : MonoBehaviour
{
    public List<Bounds> boxes = new List<Bounds>();

    // True when something solid lies on the straight line between the two points.
    public bool Blocked(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.01f) return false;
        var ray = new Ray(from, d / len);
        foreach (var b in boxes)
            if (b.IntersectRay(ray, out float hit) && hit < len - 0.3f && !b.Contains(to)) return true;
        return false;
    }
}
