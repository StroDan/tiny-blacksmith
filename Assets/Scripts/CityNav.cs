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
