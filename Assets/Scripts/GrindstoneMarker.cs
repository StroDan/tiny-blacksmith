using UnityEngine;

// Tag component on the grindstone's clickable sphere. ForgeGameManager
// raycasts against this each frame to check whether the player is holding
// left-click on it (see HitZoneMarker.cs for why we don't use Unity's
// OnMouseDown/Up/Exit here).
public class GrindstoneMarker : MonoBehaviour
{
}
