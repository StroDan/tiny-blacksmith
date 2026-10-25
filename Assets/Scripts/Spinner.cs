using UnityEngine;

// Turns a part slowly round its own local axis: windmill sails, the water wheel. Placed by VillageBuilder.
public class Spinner : MonoBehaviour
{
    public Vector3 axis = Vector3.forward;
    public float degreesPerSecond = 12f;

    private void Update() => transform.Rotate(axis, degreesPerSecond * Time.deltaTime, Space.Self);
}
