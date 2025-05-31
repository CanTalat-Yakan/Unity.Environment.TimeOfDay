using UnityEngine;

[ExecuteAlways]
public class LookAt : MonoBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField] private Quaternion desiredRotation;

    void Update() =>
        desiredRotation = _target.rotation * Quaternion.Euler(0, 180f, 0);

    private void LateUpdate() =>
        transform.rotation = desiredRotation;
}
