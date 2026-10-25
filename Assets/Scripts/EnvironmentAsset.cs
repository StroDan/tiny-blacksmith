using System.Collections.Generic;
using UnityEngine;

// Placement data for one prefab of the Blender-built environment library (Assets/TinyBlacksmith/Environment),
// filled in by EnvironmentLibrary from the metadata the Blender build writes next to the models. All positions are
// in the prefab's own frame: front toward +X (VillageBuilder's house convention), width along Z, ground at y = 0.
// Nothing here runs in the game - it only tells the builders where the door, chimneys and lanterns of a model are.
public class EnvironmentAsset : MonoBehaviour
{
    public string kind;                 // building / prop / tree / pine / bush / rock / ground
    public string style;                // town / cottage / tavern / workshop / warehouse / mill / blacksmith
    public Bounds walls;                // the building's wall block (x = depth, z = frontage width)
    public Bounds extent;               // everything, props and awnings included
    public float ridge;                 // top of the highest roof ridge (chimneys not counted)
    public int floors;
    public Vector3 door;                // on the ground in front of the main door (a street-graph door node)
    public List<Vector3> chimneys = new List<Vector3>();   // chimney tops (CityAmbience smoke markers)
    public List<Vector3> lanterns = new List<Vector3>();   // lantern flames (DayCycle LanternLights markers)
    public List<Vector3> glows = new List<Vector3>();      // forge fires
    public Vector3 wheelHub;            // watermill: the wheel's axle centre (the wheel is its own child)

    // Width along the street (Z) and depth back from it (X) of the whole model.
    public float Frontage => extent.size.z;
    public float Depth => extent.size.x;
    // How far in front of the wall line the model reaches (steps, porches, verandas).
    public float FrontReach => extent.max.x - walls.max.x;
}
