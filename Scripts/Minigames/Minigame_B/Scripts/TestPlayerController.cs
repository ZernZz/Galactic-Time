using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class TestPlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 720f;

    [Header("Camera Settings")]
    public Transform cameraPivot;
    public float cameraDistance = 5f;
    public float cameraSensitivity = 2f;

    private Rigidbody rb;
    private float yaw = 0f;
    private float pitch = 10f;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        RotateCamera();
    }

    private void FixedUpdate()
    {
        MovePlayer();
    }

    void MovePlayer()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");

        Vector3 camForward = cameraPivot.forward;
        camForward.y = 0;
        camForward.Normalize();

        Vector3 camRight = cameraPivot.right;
        camRight.y = 0;
        camRight.Normalize();

        Vector3 moveDir = camForward * v + camRight * h;
        rb.MovePosition(rb.position + moveDir * moveSpeed * Time.fixedDeltaTime);

        // หมุนตามทิศทางเดิน
        if (moveDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            rb.MoveRotation(Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeed * Time.fixedDeltaTime));
        }
    }

    void RotateCamera()
    {
        if (Input.GetMouseButton(1)) // คลิกขวาเพื่อหมุนกล้อง
        {
            yaw += Input.GetAxis("Mouse X") * cameraSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * cameraSensitivity;
            pitch = Mathf.Clamp(pitch, -20f, 80f);
        }

        if (cameraPivot != null && Camera.main != null)
        {
            cameraPivot.rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 offset = -cameraPivot.forward * cameraDistance;
            Camera.main.transform.position = cameraPivot.position + offset;
            Camera.main.transform.LookAt(cameraPivot.position);
        }
    }
}
