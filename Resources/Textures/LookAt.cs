using UnityEngine;

[ExecuteAlways]
public class LookAt : MonoBehaviour
{
    [SerializeField] private Transform _target;
    [SerializeField] private Quaternion desiredRotation;

    void Update() =>
    //desiredRotation = _target.rotation * Quaternion.Euler(0, 0, 0);
    desiredRotation = Quaternion.LookRotation(_target.position - transform.position, Vector3.up);

    private void LateUpdate() =>
        transform.rotation = desiredRotation;
}
