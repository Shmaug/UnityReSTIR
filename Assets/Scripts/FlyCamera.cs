using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FlyCamera : MonoBehaviour {
    public float _MouseSensitivity = 0.1f;
    public float _MoveSpeed = 1.3f;
    public float _ShiftMultiplier = 1.5f;

    void Update() {
        if (Input.GetMouseButton(1)) {
            Vector3 angles = transform.eulerAngles;
            angles.x -= _MouseSensitivity*Input.mousePositionDelta.y;
            angles.y += _MouseSensitivity*Input.mousePositionDelta.x;
            transform.eulerAngles = angles;
        }

        Vector3 dir = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) dir += transform.forward;
        if (Input.GetKey(KeyCode.S)) dir -= transform.forward;
        if (Input.GetKey(KeyCode.D)) dir += transform.right;
        if (Input.GetKey(KeyCode.A)) dir -= transform.right;
        if (Input.GetKey(KeyCode.Space))       dir += transform.up;
        if (Input.GetKey(KeyCode.LeftControl)) dir -= transform.up;
        if (Input.GetKey(KeyCode.LeftShift)) dir *= _ShiftMultiplier;
        transform.position += dir * _MoveSpeed * Time.deltaTime;
    }
}
