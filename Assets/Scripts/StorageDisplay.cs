using UnityEngine;

// Anchor points on the storage rack (LowPoly.StorageRack), populated by Prototype1SceneBuilder to match
// the rack's procedural geometry. Lets ForgeGameManager place/show the 3D swords and read the bin
// positions without duplicating the rack's layout numbers - and since these are plain child transforms
// of the rack, any future hand-drag of the rack in the Editor carries them along automatically (see the
// furnace/anvil "hand-dragging desyncs it from its wrapper" gotcha - this sidesteps that class of bug).
public class StorageDisplay : MonoBehaviour
{
    public Transform[] swordPegs = new Transform[8];
    public Transform[] materialBins = new Transform[3];
}
