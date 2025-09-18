using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-only container for patrol points. Add child transforms as waypoints.
/// </summary>
public class PatrolRoute : MonoBehaviour
{
    [SerializeField] private List<Transform> points = new();

    public IReadOnlyList<Transform> Points => points;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Quality-of-life: if list is empty, auto-pull children once
        if (points == null || points.Count == 0)
        {
            points = new List<Transform>();
            foreach (Transform t in transform)
                points.Add(t);
        }
    }
#endif
}
