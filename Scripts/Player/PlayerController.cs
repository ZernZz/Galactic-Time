using UnityEngine;
using Unity.Netcode;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour
{
    #region Movement Settings
    [Header("Movement Settings")]
    public float walkSpeed = 5f;
    public float runSpeed = 10f;
    public float jumpForce = 3f;
    public float gravity = -9.81f;
    public float rotationSpeed = 10f;
    #endregion

    #region Running / Stamina Settings
    [Header("Running / Stamina Settings")]
    public float maxRunTime = 3f;
    public float runCooldownTime = 5f;
    private float currentRunTime = 0f;
    private float runCooldownTimer = 0f;
    private bool isSprinting = false;
    private bool forceIdleByRpc = false;//เคนเพิ่ม
    #endregion

    #region Camera Settings
    [Header("Camera Settings")]
    public Transform cameraPivot;
    public float cameraDistance = 5f;
    public float cameraSensitivity = 2f;
    private float yaw = 0f;
    private float pitch = 10f;
    #endregion

    //เคนเพิ่ม
    [Header("Camera Collision")]
    [SerializeField] private LayerMask collisionLayerMask; // กำหนด Layer ของวัตถุที่กล้องควรจะชน
    [SerializeField] private float cameraCollisionOffset = 0.2f; // ระยะห่างเล็กน้อยเพื่อไม่ให้กล้องติดขอบวัตถุ


    #region Network Synchronization
    [Header("Network Synchronization")]
    private NetworkVariable<Vector3> netPosition = new NetworkVariable<Vector3>(
        Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<Quaternion> netRotation = new NetworkVariable<Quaternion>(
        Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    // ใช้สำหรับซิงค์สถานะ animation (เช่น idle, run)
    private NetworkVariable<bool> netIsGrounded = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private NetworkVariable<bool> netIsRunning = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private struct StateSnapshot
    {
        public float timestamp;
        public Vector3 position;
        public Quaternion rotation;
    }
    private List<StateSnapshot> stateBuffer = new List<StateSnapshot>();

    [Header("Interpolation Settings")]
    public float interpolationDelay = 0.01f;
    public float interpolationSmoothFactor = 60f;
    #endregion

    private CharacterController controller;
    private Vector3 velocity;
    private Animator animator;

    void Start()
    {
        controller = GetComponent<CharacterController>();

        // หากไม่ใช่ Owner ปิดการควบคุม CharacterController
        if (!IsOwner)
        {
            controller.enabled = false;
        }

        // ค้นหา Animator จากลูกของ GameObject
        animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        // ตรวจสอบ Scene ปัจจุบัน เพื่อให้ทำงานเฉพาะใน Scene ที่เกี่ยวข้อง
        string currentScene = SceneManager.GetActiveScene().name;
        bool isMinigameA = (currentScene == "Minigame_A");
        bool isMinigameB = (currentScene == "Minigame_B");
        bool isMinigameC = (currentScene == "Minigame_C");
        bool isMinigameD = (currentScene == "Minigame_D");

        if (!(isMinigameA || isMinigameB || isMinigameC || isMinigameD))
            return;

        // สำหรับ non-owner ใช้ Interpolation และ update Animator จาก Network
        if (!IsOwner)
        {
            UpdateAnimatorFromNetwork();
            InterpolateState();
            return;
        }

        // ใน Crystal_Rush ให้ตรวจสอบว่ามีการอนุญาตให้เคลื่อนที่หรือไม่
        if (isMinigameA)
        {
            if (CrystalRushGameManager.Instance == null || !CrystalRushGameManager.Instance.canMove.Value)
            {
                ProcessCamera();
                return;
            }
        }
        
        if (isMinigameB)
        {
            if (MinigameBManager.Instance == null || !MinigameBManager.Instance.canMove.Value)
            {
                ProcessCamera(); // ให้หมุนกล้องได้
                return;
            }
        }

        if (isMinigameC)
        {

            cameraDistance = 10f;


        }

        if (isMinigameD)
        {

        }

        ProcessMovement();
        ProcessCamera();
        UpdateAnimator();
    }

    void FixedUpdate()
    {
        if (IsOwner)
        {
            // อัปเดตตำแหน่งและการหมุนใน NetworkVariables
            netPosition.Value = transform.position;
            netRotation.Value = transform.rotation;
        }
        else
        {
            CollectSnapshot();
        }
    }

    #region Movement Processing
    void ProcessMovement()
    {
        if (netIsIdle.Value) return;

        bool isGrounded = controller.isGrounded;
        if (isGrounded && velocity.y < 0)
        {
            velocity.y = -2f;
        }

        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        // คำนวณทิศทางการเคลื่อนที่โดยอ้างอิงจากการหมุนของกล้อง
        Vector3 camForward = cameraPivot.forward;
        camForward.y = 0f;
        camForward.Normalize();
        Vector3 camRight = cameraPivot.right;
        camRight.y = 0f;
        camRight.Normalize();
        Vector3 moveDir = (camForward * vertical + camRight * horizontal).normalized;

        if (moveDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        // จัดการการวิ่ง (sprinting)
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        bool hasInput = (horizontal != 0f || vertical != 0f);
        if (shiftHeld && hasInput && runCooldownTimer <= 0f)
        {
            currentRunTime += Time.deltaTime;
            if (currentRunTime <= maxRunTime)
            {
                isSprinting = true;
            }
            else
            {
                isSprinting = false;
                runCooldownTimer = runCooldownTime;
            }
        }
        else
        {
            isSprinting = false;
            if (runCooldownTimer <= 0f)
            {
                currentRunTime = 0f;
            }
        }
        if (runCooldownTimer > 0f)
        {
            runCooldownTimer -= Time.deltaTime;
            if (runCooldownTimer <= 0f)
            {
                currentRunTime = 0f;
            }
        }

        float speed = isSprinting ? runSpeed : walkSpeed;
        controller.Move(moveDir * speed * Time.deltaTime);

        // กระโดด
        if (Input.GetKeyDown(KeyCode.Space) && isGrounded)
        {
            velocity.y = Mathf.Sqrt(jumpForce * -2f * gravity);
            if (animator != null)
            {
                animator.SetTrigger("Jump");
            }
            RequestJumpServerRpc();
        }

        velocity.y += gravity * Time.deltaTime;
        controller.Move(velocity * Time.deltaTime);
    }
    #endregion

    public void LockCameraAndSetIdle()
    {
        if (IsOwner)
        {
            canRotateCamera = true; //เคนเพิ่ม
        }
        
        if (animator != null)
        {
            animator.SetBool("isRunning", false); // ❌ หยุดสถานะวิ่ง
            animator.SetBool("isGrounded", true); // ✅ ทำให้แน่ใจว่าเป็นสถานะยืน
            animator.SetFloat("RunSpeedMultiplier", 1.0f); // ✅ รีเซ็ต speed

            animator.Play("Idle", 0, 0f); // ✅ บังคับให้กลับมาเล่น Idle ทันที
        }
    }

    //เคนเพิ่ม
    private bool canRotateCamera = true;

    #region Camera Processing
    void ProcessCamera()
    {
        if (!canRotateCamera) return; // 🚫 ถ้าเกมจบ ห้ามหมุนกล้อง

        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * cameraSensitivity;
            pitch -= Input.GetAxis("Mouse Y") * cameraSensitivity;
            pitch = Mathf.Clamp(pitch, -20f, 80f);
        }

        if (cameraPivot != null && Camera.main != null)
        {
            cameraPivot.rotation = Quaternion.Euler(pitch, yaw, 0);
            Vector3 desiredCameraPosition = cameraPivot.position - (cameraPivot.forward * cameraDistance); // ตำแหน่งกล้องที่ต้องการ
            Vector3 cameraDirection = desiredCameraPosition - cameraPivot.position;
            float actualCameraDistance = cameraDistance; // เริ่มต้นด้วยระยะทางเต็ม

            // Raycast จาก cameraPivot ไปยังตำแหน่งกล้องที่ต้องการ
            RaycastHit hit;
            if (Physics.Raycast(cameraPivot.position, cameraDirection.normalized, out hit, cameraDistance, collisionLayerMask))
            {
                // ถ้าชนวัตถุ ให้ปรับระยะกล้องเข้ามา
                actualCameraDistance = hit.distance - cameraCollisionOffset;
            }

            // กำหนดตำแหน่งกล้องสุดท้าย
            Vector3 finalCameraPosition = cameraPivot.position - (cameraPivot.forward * actualCameraDistance);
            Camera.main.transform.position = finalCameraPosition;
            Camera.main.transform.LookAt(cameraPivot.position);
        }
    }
    #endregion

    

    #region Animator Processing
    private NetworkVariable<bool> netIsIdle = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server
    );
    //เคนเพิ่ม
    void UpdateAnimator()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            if(animator == null) return;
        }

        if (netIsIdle.Value || forceIdleByRpc) // เพิ่มการตรวจสอบ forceIdleByRpc
        {
            animator.SetBool("isRunning", false);
            animator.Play("Idle", 0, 0f);
            // ถ้าถูกบังคับ Idle โดย RPC หรือ netIsIdle, Owner ก็ไม่ควรเขียน netIsRunning.Value ที่ขัดแย้ง
            if (IsOwner && netIsIdle.Value) { // Server บอกให้ Idle
                netIsRunning.Value = false; // ทำให้แน่ใจว่า Owner ส่งค่า isRunning เป็น false ด้วย
            }
            return;
        }

        bool isGrounded = controller.isGrounded;
        bool isMoving = (Input.GetAxis("Horizontal") != 0 || Input.GetAxis("Vertical") != 0);
        bool isRunning = isMoving && isSprinting && currentRunTime < maxRunTime; // ✅ ตรวจว่ากำลังวิ่งและมี stamina

        animator.SetBool("isGrounded", isGrounded);
        animator.SetBool("isRunning", isMoving);

        animator.SetFloat("RunSpeedMultiplier", isRunning ? 1.5f : 1.0f);

        netIsGrounded.Value = isGrounded;
        netIsRunning.Value = isMoving;
    }

    void UpdateAnimatorFromNetwork()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
            if(animator == null) return;
        }

        if (netIsIdle.Value)
        {
            animator.SetBool("isRunning", false);
            animator.Play("Idle", 0, 0f);
        }
        else
        {
            animator.SetBool("isGrounded", netIsGrounded.Value);
            animator.SetBool("isRunning", netIsRunning.Value);
        }
    }
    #endregion

    #region Network Interpolation (for non-owners)
    void CollectSnapshot()
    {
        StateSnapshot snapshot;
        snapshot.timestamp = Time.time;
        snapshot.position = netPosition.Value;
        snapshot.rotation = netRotation.Value;
        stateBuffer.Add(snapshot);

        while (stateBuffer.Count > 0 && Time.time - stateBuffer[0].timestamp > 1f)
        {
            stateBuffer.RemoveAt(0);
        }
    }

    void InterpolateState()
    {
        float targetTime = Time.time - interpolationDelay;
        if (stateBuffer.Count >= 2)
        {
            int i = 0;
            while (i < stateBuffer.Count - 1 && stateBuffer[i + 1].timestamp < targetTime)
            {
                i++;
            }
            StateSnapshot older = stateBuffer[i];
            StateSnapshot newer = stateBuffer[Mathf.Min(i + 1, stateBuffer.Count - 1)];

            float interval = newer.timestamp - older.timestamp;
            float t = (interval > 0.0001f) ? (targetTime - older.timestamp) / interval : 0f;

            Vector3 targetPos = Vector3.Lerp(older.position, newer.position, t);
            Quaternion targetRot = Quaternion.Slerp(older.rotation, newer.rotation, t);

            float posDelta = Vector3.Distance(transform.position, targetPos);
            if (posDelta > 0.5f)
            {
                transform.position = targetPos;
            }
            else
            {
                transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * interpolationSmoothFactor);
            }
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * interpolationSmoothFactor);
        }
        else
        {
            transform.position = netPosition.Value;
            transform.rotation = netRotation.Value;
        }
    }
    #endregion

    #region RPC for Syncing Jump Animation
    [ServerRpc]
    void RequestJumpServerRpc(ServerRpcParams rpcParams = default)
    {
        PlayJumpAnimationClientRpc();
    }

    [ClientRpc]
    void PlayJumpAnimationClientRpc(ClientRpcParams rpcParams = default)
    {
        if (!IsOwner && animator != null)
        {
            animator.SetTrigger("Jump");
        }
    }
    #endregion

    #region Force Repositioning
    // ฟังก์ชันนี้ถูกเรียกจากเซิร์ฟเวอร์เพื่อบังคับให้ผู้เล่นเปลี่ยนตำแหน่งทันที
    [ClientRpc]
    public void ForceRepositionClientRpc(Vector3 newPosition, Quaternion newRotation)
    {
        transform.position = newPosition;
        transform.rotation = newRotation;
        netPosition.Value = newPosition;
        netRotation.Value = newRotation;
    }
    #endregion

    public void RefreshAnimator()
    {
        animator = GetComponentInChildren<Animator>();
    }

    //เคนเพิ่ม
    [ClientRpc]
    public void SetIdleAnimationClientRpc()
    {
        forceIdleByRpc = true;
        if (animator != null)
        {
            animator.SetBool("isRunning", false); // ❌ หยุดสถานะวิ่ง
            animator.SetBool("isGrounded", true); // ✅ ทำให้แน่ใจว่าเป็นสถานะยืน
            animator.SetFloat("RunSpeedMultiplier", 1.0f); // ✅ รีเซ็ต speed
            animator.Play("Idle", 0, 0f); // ✅ บังคับให้กลับมาเล่น Idle ทันที
        }
        if (IsOwner)
        {
            // เช่น ป้องกันไม่ให้ UpdateAnimator() ไปเปลี่ยนค่า isRunning กลับ
        }
    }
    [ServerRpc(RequireOwnership = false)]
    public void SetIdleStateServerRpc()
    {
        netIsIdle.Value = true;
    }

    //เคนเพิ่ม
    [ServerRpc(RequireOwnership = false)] // Server สามารถเรียกได้สำหรับ PlayerController ใดๆ
    public void ResetIdleStateServerRpc()
    {
        netIsIdle.Value = false;
        ClearForceIdleClientRpc();
    }
    //เคนเพิ่ม
    [ClientRpc]
    private void ClearForceIdleClientRpc()
    {
        forceIdleByRpc = false;
    }

}
