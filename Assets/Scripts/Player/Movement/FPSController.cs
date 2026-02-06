using UnityEngine;

public class FPSControllerMobile : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public FloatingJoystick moveJoystick;
    public CharacterController controller;

    [Header("Gravity & Jump")]
    public bool enableJump = true;
    public float gravity = -9.81f;
    public float jumpForce = 1.6f;

    [Header("Look Settings")]
    public Transform cameraHolder;
    public LookArea lookArea;

    [Range(0.05f, 1f)]
    public float mobileLookSensitivity = 0.2f;

    [Range(30f, 89f)]
    public float maxLookAngle = 80f;

    float xRotation = 0f;
    float verticalVelocity;

    void Start()
    {
        if (!controller)
            controller = GetComponent<CharacterController>();
    }

    void Update()
    {
        MobileMovement();
        MobileLook();
        ApplyGravity();
    }

    // ================= MOVEMENT =================
    void MobileMovement()
    {
        Vector3 move =
            transform.right * moveJoystick.Horizontal +
            transform.forward * moveJoystick.Vertical;

        controller.Move(move * moveSpeed * Time.deltaTime);
    }

    // ================= LOOK =================
    void MobileLook()
    {
        if (!lookArea) return;

        Vector2 delta = lookArea.LookDelta;
        if (delta.sqrMagnitude < 0.01f) return;

        float lookX = delta.x * mobileLookSensitivity;
        float lookY = delta.y * mobileLookSensitivity;

        xRotation -= lookY;
        xRotation = Mathf.Clamp(xRotation, -maxLookAngle, maxLookAngle);

        cameraHolder.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * lookX);

        lookArea.ResetDelta();
    }

    // ================= GRAVITY & JUMP =================
    void ApplyGravity()
    {
        if (controller.isGrounded)
        {
            if (verticalVelocity < 0)
                verticalVelocity = -2f;

            // if (enableJump && Input.GetKeyDown(KeyCode.Space))
            // {
            //     verticalVelocity = Mathf.Sqrt(jumpForce * -2f * gravity);
            // }
        }

        verticalVelocity += gravity * Time.deltaTime;
        controller.Move(Vector3.up * verticalVelocity * Time.deltaTime);
    }

    public void Jump()
    {
        if (enableJump && controller.isGrounded)
            verticalVelocity = Mathf.Sqrt(jumpForce * -2f * gravity);
    }

}
