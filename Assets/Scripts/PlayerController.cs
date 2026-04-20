using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class PlayerController : MonoBehaviour
{
    // -------------------------
    // CONFIGURATION
    // -------------------------

    [Header("Horizontal Movement")]
    [SerializeField] private float maxSpeed = 8f;
    [SerializeField] private float acceleration = 60f;
    [SerializeField] private float deceleration = 70f;
    [SerializeField] private float inputDeadzone = 0.1f;

    [Header("Jump")]
    [SerializeField] private float jumpSpeed = 14f;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [SerializeField] private float coyoteTime = 0.1f;
    [SerializeField] private float jumpBufferTime = 0.1f;

    [Header("Gravity")]
    [SerializeField] private float baseGravityScale = 3f;
    [SerializeField] private float fallMultiplier = 2.2f;
    [SerializeField] private float lowJumpMultiplier = 3f;
    [SerializeField] private float maxFallSpeed = 25f;

    [Header("Ground Check")]
    [SerializeField] private LayerMask groundMask;
    [SerializeField] private float groundCheckPadding = 0.02f;
    [SerializeField] private float groundCheckExtra = 0.04f;

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 18f;
    [SerializeField] private float dashDuration = 0.15f;
    [SerializeField] private KeyCode dashKey = KeyCode.LeftShift;

    [Header("Wall Slide & Wall Jump")]
    [SerializeField] private float wallCheckDistance = 0.05f;
    [SerializeField] private float wallSlideSpeed = 2f;
    [SerializeField] private float wallJumpHorizontalSpeed = 10f;
    [SerializeField] private float wallJumpVerticalSpeed = 14f;
    [SerializeField] private float wallJumpControlLockTime = 0.12f;

    // -------------------------
    // RUNTIME VARIABLES
    // -------------------------

    private Rigidbody2D rb;
    private Collider2D col;
    private Animator anim;
    private SpriteRenderer sr;   // NEW

    // Input
    private float xInput;
    private float yInput;
    private bool jumpHeldRuntime = false;
    private bool dashPressed = false;

    // Movement state
    private float facing = 1f;
    private bool isGrounded;
    private bool wasGrounded = false;
    private float coyoteTimeCounter = 0f;
    private float jumpBufferCounter = 0f;

    // Dash state
    private bool isDashing = false;
    private bool hasDashed = false;
    private float dashStartTime = 0f;
    private Vector2 dashDir = Vector2.zero;

    // Wall state
    private bool isOnLeftWall;
    private bool isOnRightWall;
    private bool isOnWall;
    private bool isWallSliding;
    private float wallDirX;

    // Wall jump control lock
    private float wallJumpLockCounter = 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        anim = GetComponent<Animator>();
        sr = GetComponent<SpriteRenderer>();   // NEW

        rb.gravityScale = baseGravityScale;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    private void Update()
    {
        xInput = Input.GetAxisRaw("Horizontal");
        if (Mathf.Abs(xInput) < inputDeadzone) xInput = 0f;
        if (xInput != 0) facing = Mathf.Sign(xInput);

        // Flip sprite based on facing
        UpdateSpriteFacing();   // NEW

        yInput = Input.GetAxisRaw("Vertical");

        if (Input.GetKeyDown(jumpKey) || Input.GetButtonDown("Jump"))
            jumpBufferCounter = jumpBufferTime;
        else if (jumpBufferCounter > 0f)
            jumpBufferCounter -= Time.deltaTime;

        jumpHeldRuntime = Input.GetKey(jumpKey) || Input.GetButton("Jump");

        if (Input.GetKeyDown(dashKey))
            dashPressed = true;
    }

    private void FixedUpdate()
    {
        if (wallJumpLockCounter > 0f)
            wallJumpLockCounter -= Time.fixedDeltaTime;

        isGrounded = CheckGrounded();

        if (isGrounded)
            hasDashed = false;

        if (isGrounded)
            coyoteTimeCounter = coyoteTime;
        else if (coyoteTimeCounter > 0f)
            coyoteTimeCounter -= Time.fixedDeltaTime;

        DetectWalls();
        UpdateWallSlideState();

        Vector2 v = rb.linearVelocity;

        float effectiveXInput = (wallJumpLockCounter > 0f) ? 0f : xInput;

        float targetSpeed = effectiveXInput * maxSpeed;
        float accelRate = (Mathf.Abs(effectiveXInput) > 0.01f) ? acceleration : deceleration;
        float newX = Mathf.MoveTowards(v.x, targetSpeed, accelRate * Time.fixedDeltaTime);

        // DASH START
        if (dashPressed && !isDashing && !hasDashed)
        {
            dashPressed = false;
            hasDashed = true;
            isDashing = true;
            dashStartTime = Time.time;

            Vector2 dir = new Vector2(xInput, yInput);
            if (dir == Vector2.zero)
                dir = new Vector2(facing, 0f);

            dashDir = dir.normalized;

            rb.gravityScale = 0f;
            rb.linearVelocity = dashDir * dashSpeed;
            return;
        }

        // DASH ACTIVE
        if (isDashing)
        {
            rb.linearVelocity = dashDir * dashSpeed;

            if (Time.time >= dashStartTime + dashDuration)
            {
                isDashing = false;
                rb.gravityScale = baseGravityScale;

                Vector2 endVel = rb.linearVelocity;
                if (endVel.y > 3f) endVel.y = 3f;
                rb.linearVelocity = endVel;
            }

            wasGrounded = isGrounded;
            return;
        }

        // WALL / GROUND JUMP
        bool wantsJump = jumpBufferCounter > 0f;

        if (wantsJump)
        {
            if (isOnWall && !isGrounded)
            {
                float dir = -wallDirX;
                if (dir == 0f) dir = facing;

                v.x = dir * wallJumpHorizontalSpeed;
                v.y = wallJumpVerticalSpeed;

                jumpBufferCounter = 0f;
                coyoteTimeCounter = 0f;
                isWallSliding = false;

                wallJumpLockCounter = wallJumpControlLockTime;

                newX = v.x;
            }
            else if (coyoteTimeCounter > 0f)
            {
                v.y = jumpSpeed;
                jumpBufferCounter = 0f;
                coyoteTimeCounter = 0f;
            }
        }

        // WALL SLIDE
        if (isWallSliding)
        {
            if (v.y < -wallSlideSpeed)
                v.y = -wallSlideSpeed;
        }

        // VARIABLE GRAVITY
        if (v.y < 0f)
        {
            v.y += Physics2D.gravity.y * (fallMultiplier - 1f) * baseGravityScale * Time.fixedDeltaTime;
        }
        else if (v.y > 0f && !jumpHeldRuntime)
        {
            v.y += Physics2D.gravity.y * (lowJumpMultiplier - 1f) * baseGravityScale * Time.fixedDeltaTime;
        }

        if (v.y < -maxFallSpeed)
            v.y = -maxFallSpeed;

        // Apply velocity
        rb.linearVelocity = new Vector2(newX, v.y);

        wasGrounded = isGrounded;

        // update animator
        UpdateAnimator();
    }

    // -------------------------
    // ANIMATOR UPDATE
    // -------------------------
    private void UpdateAnimator()
    {
        if (anim == null) return;

        Vector2 vel = rb.linearVelocity;

        anim.SetFloat("Speed", Mathf.Abs(vel.x));
        anim.SetFloat("VelocityY", vel.y);
        anim.SetBool("IsGrounded", isGrounded);
        anim.SetBool("IsWallSliding", isWallSliding);
        anim.SetBool("IsDashing", isDashing);
    }

    // -------------------------
    // SPRITE FACING (NEW)
    // -------------------------
    private void UpdateSpriteFacing()
    {
        if (sr == null) return;

        // facing > 0 → look right, facing < 0 → look left
        sr.flipX = facing < 0f;
    }

    // -------------------------
    // GROUND CHECK
    // -------------------------
    private bool CheckGrounded()
    {
        Bounds b = col.bounds;
        Vector2 boxSize = new Vector2(b.size.x * 0.98f, groundCheckPadding);
        Vector2 boxOrigin = new Vector2(b.center.x, b.min.y + (groundCheckPadding * 0.5f));

        RaycastHit2D hit = Physics2D.BoxCast(boxOrigin, boxSize, 0f, Vector2.down, groundCheckExtra, groundMask);
        return hit.collider != null;
    }

    // -------------------------
    // WALL DETECTION
    // -------------------------
    private void DetectWalls()
    {
        Bounds b = col.bounds;
        Vector2 center = b.center;
        float rayLength = b.extents.x + wallCheckDistance;

        RaycastHit2D leftHit = Physics2D.Raycast(center, Vector2.left, rayLength, groundMask);
        RaycastHit2D rightHit = Physics2D.Raycast(center, Vector2.right, rayLength, groundMask);

        isOnLeftWall = leftHit.collider != null;
        isOnRightWall = rightHit.collider != null;

        isOnWall = !isGrounded && (isOnLeftWall || isOnRightWall);

        if (isOnLeftWall) wallDirX = -1f;
        else if (isOnRightWall) wallDirX = 1f;
        else wallDirX = 0f;
    }

    // -------------------------
    // WALL SLIDE
    // -------------------------
    private void UpdateWallSlideState()
    {
        bool pressingIntoWall =
            (wallDirX == -1f && xInput < 0f) ||
            (wallDirX == 1f && xInput > 0f);

        isWallSliding =
            isOnWall &&
            pressingIntoWall &&
            rb.linearVelocity.y <= 0f;
    }

    public int FacingSign => (int)facing;
}