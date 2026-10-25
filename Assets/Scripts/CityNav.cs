using System.Collections.Generic;
using UnityEngine;

// The city's walkable street graph, laid out by VillageBuilder (Editor) as child transforms of "CityNav" and read
// by CityLife at runtime. A node is a street corner, a place people go (market, well, fountain, the smith's front),
// a door (people come out of and go into buildings there), or an edge (a road leaving the part of the city the
// player can see - where travellers arrive and leave).
public enum CityNodeKind { Street, Spot, Door, Edge }

public class CityNavNode : MonoBehaviour
{
    public CityNodeKind kind;
    public string place;      // what's here: "home", "shop", "tavern", "market", "plaza", "well", "church", "smithy", "gate"...
    public float spread = 1f; // how far from the exact point people stand / walk (streets are wider than doorways)
    public List<CityNavNode> links = new List<CityNavNode>();

    public Vector3 Position => transform.position;
}

// Boxes that hide things from the seat: every building the city and village builders place (they have no colliders,
// on purpose - nothing may block the forging stations' mouse raycasts). CityLife tests a sight line against these to
// know whether a walker could be seen before it appears or leaves.
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
