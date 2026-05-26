using SlopArena.Shared

namespace SlopArena.Shared
{
    public static class PhysicsConfig
    {
        public const int TickRate = 60;
using SlopArena.Shared

        // Dash Settings
        public const ushort DashDurationTicks = 10;
using SlopArena.Shared

        // Hitstun & Knockback
using SlopArena.Shared

        // 3D Height & Jump
        public const float Gravity = 1400f;
        public const float JumpForce = 600f;
        public const float WallJumpForce = 800f;
using SlopArena.Shared

        // Arena Bounds
        public const float ArenaMinX = 50f;
        public const float ArenaMaxX = 4950f;
        public const float ArenaMinY = 50f;
using SlopArena.Shared

        // Heightmap
        public static float[] Heightmap = null;
        public static int GridCount = 0;
using SlopArena.Shared

        public static void Initialize()
        {
            string path = "heightmap.bin";
            if (!System.IO.File.Exists(path))
                path = "../heightmap.bin";
            if (!System.IO.File.Exists(path))
                path = "../../heightmap.bin";
            if (!System.IO.File.Exists(path))
                path = "Shared/heightmap.bin";
            LoadHeightmap(path);
using SlopArena.Shared

        public static void LoadHeightmap(string path)
        {
            if (System.IO.File.Exists(path))
            {
                try
                {
                    using (var file = System.IO.File.OpenRead(path))
                    using (var reader = new System.IO.BinaryReader(file))
                    {
                        GridCount = reader.ReadInt32();
                        GridSpacing = reader.ReadSingle();
                        Heightmap = new float[GridCount * GridCount];
                        for (int i = 0; i < Heightmap.Length; i++)
                            Heightmap[i] = reader.ReadSingle();
                    }
                    Console.WriteLine($"PhysicsConfig: Loaded heightmap from {path} ({GridCount}x{GridCount})");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"PhysicsConfig: Error loading heightmap: {ex.Message}");
                }
            }
            else
            {
                Console.WriteLine($"PhysicsConfig: Heightmap file not found at: {path}");
            }
using SlopArena.Shared

        public static float GetGroundHeight(float x, float y)
        {
using SlopArena.Shared

            float gx = x / GridSpacing;
using SlopArena.Shared

            int ix = (int)MathF.Floor(gx);
using SlopArena.Shared

            ix = Math.Clamp(ix, 0, GridCount - 2);
using SlopArena.Shared

            float tx = gx - ix;
using SlopArena.Shared

            float h00 = Heightmap[iy * GridCount + ix];
            float h10 = Heightmap[iy * GridCount + (ix + 1)];
            float h01 = Heightmap[(iy + 1) * GridCount + ix];
using SlopArena.Shared

            float h0 = h00 * (1f - tx) + h10 * tx;
using SlopArena.Shared

            return h0 * (1f - ty) + h1 * ty;
using SlopArena.Shared

        public static void SimulateStep(
            ref float posX, ref float posY, ref float posZ,
            ref float velX, ref float velY, ref float velZ,
            ref ActionState actionState, ref ushort stateTicksRemaining, ref ushort dashCooldown,
            ref float dashDirX, ref float dashDirY,
            ref ushort combatLockoutTicks, ref bool slideMomentumActive,
            ClientInputPacket input, float radius = 15f,
            bool? nearWallOverride = null)
        {
            MovementProfiles.ApplyFromActionFlags(input.ActionFlags);
using SlopArena.Shared

            // Respawn Flag
            bool inputRespawn = (input.MovementFlags & 0x40) != 0;
            if (inputRespawn)
            {
                posX = 2500f; posY = 2500f;
                posZ = GetGroundHeight(2500f, 2500f) + 10f;
                velX = 0f; velY = 0f; velZ = 0f;
                actionState = ActionState.Idle;
                stateTicksRemaining = 0; dashCooldown = 0;
                combatLockoutTicks = 0; slideMomentumActive = false;
                return;
using SlopArena.Shared

            // Parse inputs
            bool inputUp = (input.MovementFlags & 0x01) != 0;
            bool inputLeft = (input.MovementFlags & 0x02) != 0;
            bool inputDown = (input.MovementFlags & 0x04) != 0;
            bool inputRight = (input.MovementFlags & 0x08) != 0;
            bool inputJump = (input.MovementFlags & 0x10) != 0;
            bool inputDash = (input.MovementFlags & 0x20) != 0;
            bool inputCrouch = (input.MovementFlags & 0x80) != 0;
using SlopArena.Shared

using SlopArena.Shared

using SlopArena.Shared

            ResolveMovementInput(
                input, posX, posY,
                inputUp, inputLeft, inputDown, inputRight,
using SlopArena.Shared

using SlopArena.Shared

            float dt = TimeStep;
            float groundHeight = GetGroundHeight(posX, posY);
            bool isGrounded = posZ <= groundHeight + 0.1f;
            if (!isGrounded && actionState != ActionState.Dashing && posZ <= groundHeight + 3.0f && velZ <= 0.1f)
using SlopArena.Shared
using SlopArena.Shared

            // === STATE MACHINE ===
            if (actionState == ActionState.Hitstun)
            {
                velX -= velX * KnockbackDecay * dt;
                velY -= velY * KnockbackDecay * dt;
using SlopArena.Shared

                // DI
                float diX = 0f, diY = 0f;
                if (inputUp) diY -= 1f;
                if (inputDown) diY += 1f;
                if (inputLeft) diX -= 1f;
                if (inputRight) diX += 1f;
                if (diX != 0f || diY != 0f)
                {
                    float diLen = MathF.Sqrt(diX * diX + diY * diY);
                    diX /= diLen; diY /= diLen;
                    float currentSpeed = MathF.Sqrt(velX * velX + velY * velY);
                    float diForce = currentSpeed * 0.15f + 50f;
                    velX += diX * diForce * dt;
                    velY += diY * diForce * dt;
using SlopArena.Shared

                if (stateTicksRemaining > 0) stateTicksRemaining--;
                if (stateTicksRemaining == 0) actionState = ActionState.Idle;
            }
            else if (actionState == ActionState.Attacking)
            {
                velX *= 0.94f;
                velY *= 0.94f;
                if (stateTicksRemaining > 0) stateTicksRemaining--;
                if (stateTicksRemaining == 0)
                {
                    actionState = ActionState.Idle;
                    combatLockoutTicks = prof.PostAttackSlideLockoutTicks;
                    slideMomentumActive = false;
                }
            }
            else if (actionState == ActionState.Dashing)
            {
                if (inputAttack)
                {
                    actionState = ActionState.Attacking;
                    stateTicksRemaining = prof.AttackDurationTicks;
                    slideMomentumActive = false;
                }
                else if (inputCrouch && isGrounded)
                {
                    BeginSlide(ref actionState, ref stateTicksRemaining, ref slideMomentumActive,
                        CanMomentumSlide(prof, combatLockoutTicks, speed2D));
                }
                else
                {
                    velX = dashDirX * prof.DashSpeed;
                    velY = dashDirY * prof.DashSpeed;
                    velZ = 0f;
using SlopArena.Shared

                    stateTicksRemaining--;
                    if (stateTicksRemaining == 0) actionState = ActionState.Idle;
                }
            }
            else if (actionState == ActionState.Sliding)
            {
                if (inputAttack)
                {
                    actionState = ActionState.Attacking;
                    stateTicksRemaining = prof.AttackDurationTicks;
                    slideMomentumActive = false;
                }
                else if (inputDash && dashCooldown == 0 && (moveX != 0 || moveY != 0))
                {
                    actionState = ActionState.Dashing;
                    stateTicksRemaining = DashDurationTicks;
                    dashCooldown = DashCooldownTicks;
                    dashDirX = moveX; dashDirY = moveY;
                    velX = dashDirX * prof.DashSpeed;
                    velY = dashDirY * prof.DashSpeed;
                    velZ = 0f;
                    slideMomentumActive = false;
                }
                else
                {
                    float drag = slideMomentumActive ? prof.SlideMomentumDrag : prof.SlideNormalDrag;
                    velX -= velX * drag * dt;
                    velY -= velY * drag * dt;
using SlopArena.Shared

                    if (moveX != 0 || moveY != 0)
                    {
                        float steer = slideMomentumActive ? 220f : 300f;
                        velX += moveX * steer * dt;
                        velY += moveY * steer * dt;
                        ClampSlideSpeed(ref velX, ref velY, prof, slideMomentumActive);
using SlopArena.Shared

                    if (inputJump && isGrounded)
                    {
                        velZ = JumpForce;
                        posZ += velZ * dt;
                        isGrounded = false;
                        actionState = ActionState.Idle;
                        slideMomentumActive = false;
                    }
                    else if (!inputCrouch)
                    {
                        speed2D = MathF.Sqrt(velX * velX + velY * velY);
                        if (!slideMomentumActive || speed2D < prof.SlideMomentumMinSpeed * 0.45f)
                        {
                            actionState = ActionState.Idle;
                            slideMomentumActive = false;
                        }
                    }
                    else if (!slideMomentumActive && speed2D < 90f)
                    {
                        actionState = ActionState.Idle;
                    }
                }
            }
            else // Idle / Jogging
            {
                if (inputAttack && isGrounded)
                {
                    actionState = ActionState.Attacking;
                    stateTicksRemaining = prof.AttackDurationTicks;
                    slideMomentumActive = false;
                }
                else if (inputDash && dashCooldown == 0 && (moveX != 0 || moveY != 0))
                {
                    actionState = ActionState.Dashing;
                    stateTicksRemaining = DashDurationTicks;
                    dashCooldown = DashCooldownTicks;
                    dashDirX = moveX; dashDirY = moveY;
                    velX = dashDirX * prof.DashSpeed;
                    velY = dashDirY * prof.DashSpeed;
                    velZ = 0f;
                }
                else
                {
                    // Jogging physics
                    float accel = isGrounded ? prof.Acceleration : prof.AirAcceleration;
                    float drag = isGrounded
                        ? ((moveX == 0 && moveY == 0) ? prof.DragWhenStopped : prof.DragWhenMoving)
using SlopArena.Shared

                    velX -= velX * drag * dt;
using SlopArena.Shared

                    if (moveX != 0 || moveY != 0)
                    {
                        float projSpeed = velX * moveX + velY * moveY;
                        if (projSpeed < moveMaxSpeed)
                        {
                            float addedSpeed = accel * dt;
                            if (projSpeed + addedSpeed > moveMaxSpeed)
                                addedSpeed = moveMaxSpeed - projSpeed;
                            velX += moveX * addedSpeed;
                            velY += moveY * addedSpeed;
                        }
using SlopArena.Shared

                    // Jump
                    if (inputJump && isGrounded)
                    {
                        velZ = JumpForce;
                        isGrounded = false;
using SlopArena.Shared

                    // Slide from idle
                    if (isGrounded && inputCrouch)
                    {
                        speed2D = MathF.Sqrt(velX * velX + velY * velY);
                        if (speed2D < 100f && (moveX != 0 || moveY != 0) && !CanMomentumSlide(prof, combatLockoutTicks, speed2D))
                        {
                            velX = moveX * 300f;
                            velY = moveY * 300f;
                        }
                        BeginSlide(ref actionState, ref stateTicksRemaining, ref slideMomentumActive,
                            CanMomentumSlide(prof, combatLockoutTicks, speed2D));
                    }
                }
using SlopArena.Shared

            // Gravity
            if (!isGrounded && actionState != ActionState.Dashing && actionState != ActionState.Attacking)
using SlopArena.Shared

            // Position update with heightmap collision
            float nextX = posX + velX * dt;
            float nextY = posY + velY * dt;
using SlopArena.Shared

            float eps = 8f;
            float hL = GetGroundHeight(nextX - eps, nextY);
            float hR = GetGroundHeight(nextX + eps, nextY);
            float hD = GetGroundHeight(nextX, nextY - eps);
using SlopArena.Shared

            float nx = hL - hR;
            float ny = hD - hU;
            float nz = 2f * eps;
            float len3D = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
using SlopArena.Shared

            if (isGrounded)
            {
                float verticalDelta = nextGroundHeight - posZ;
using SlopArena.Shared

                if (isWall)
                {
                    float len = MathF.Sqrt(nx * nx + ny * ny);
                    if (len > 0.001f)
                    {
                        float normalX = nx / len;
                        float normalY = ny / len;
                        float wallSpeed = MathF.Sqrt(velX * velX + velY * velY);
                        if (wallSpeed > 10f)
                        {
                            float normX = velX / wallSpeed;
                            float normY = velY / wallSpeed;
                            float dot = normX * normalX + normY * normalY;
                            if (dot < 0f)
                            {
                                if (dot < -0.7f) { velX = 0f; velY = 0f; }
                                else
                                {
                                    float dotProj = velX * normalX + velY * normalY;
                                    float bounceX = velX - 2.0f * dotProj * normalX;
                                    float bounceY = velY - 2.0f * dotProj * normalY;
                                    velX = bounceX * 0.7f;
                                    velY = bounceY * 0.7f;
                                    posX += normalX * 2.0f;
                                    posY += normalY * 2.0f;
                                }
                            }
                        }
                    }
                    else { velX = 0f; velY = 0f; }
                }
                else
                {
                    posX = nextX; posY = nextY;
                    posZ = nextGroundHeight;
                    velZ = verticalDelta / dt;
                }
            }
            else
            {
                // Mid-air: wall-jump from heightmap walls
                float curHL = GetGroundHeight(posX - eps, posY);
                float curHR = GetGroundHeight(posX + eps, posY);
                float curHD = GetGroundHeight(posX, posY - eps);
                float curHU = GetGroundHeight(posX, posY + eps);
                float curNX = curHL - curHR;
                float curNY = curHD - curHU;
                float curLen = MathF.Sqrt(curNX * curNX + curNY * curNY);
                float maxGroundAround = MathF.Max(MathF.Max(curHL, curHR), MathF.Max(curHD, curHU));
using SlopArena.Shared

                bool wallDetected = false;
                bool hasNearWall = nearWallOverride ?? nearWall;
                if (hasNearWall && !isGrounded && actionState != ActionState.Dashing && inputCrouch)
                {
                    float curNormalX = curNX / curLen;
                    float curNormalY = curNY / curLen;
                    float wallDot = velX * curNormalX + velY * curNormalY;
                    if (wallDot < 0f)
                    {
                        wallDetected = true;
                        // Wall-jump (no cling, just bounce off)
                        if (inputJump)
                        {
                            velX = curNormalX * WallJumpHorizontal;
                            velY = curNormalY * WallJumpHorizontal;
                            velZ = WallJumpForce;
                            posZ += velZ * dt;
                            isGrounded = false;
                            dashCooldown = 0;
                            posX += velX * dt;
                            posY += velY * dt;
                        }
                        else
                        {
                            // Slide down the wall
                            velX = 0f; velY = 0f;
                            velZ = -400f;
                            if (posZ <= groundHeight + 0.1f)
                            {
                                posZ = groundHeight; velZ = 0f;
                                isGrounded = true;
                            }
                        }
                    }
using SlopArena.Shared

                if (!wallDetected)
                {
                    if (posZ < nextGroundHeight)
                    {
                        bool isWall = (slopeUp < 0.14f);
                        if (isWall)
                        {
                            float len = MathF.Sqrt(nx * nx + ny * ny);
                            if (len > 0.001f)
                            {
                                float normalX = nx / len;
                                float normalY = ny / len;
                                float wallSpeed = MathF.Sqrt(velX * velX + velY * velY);
                                if (wallSpeed > 10f)
                                {
                                    float normX = velX / wallSpeed;
                                    float normY = velY / wallSpeed;
                                    float dot = normX * normalX + normY * normalY;
                                    if (dot < 0f)
                                    {
                                        if (dot < -0.7f) { velX = 0f; velY = 0f; }
                                        else
                                        {
                                            float dotProj = velX * normalX + velY * normalY;
                                            float bounceX = velX - 2.0f * dotProj * normalX;
                                            float bounceY = velY - 2.0f * dotProj * normalY;
                                            velX = bounceX * 0.7f;
                                            velY = bounceY * 0.7f;
                                            posX += normalX * 2.0f;
                                            posY += normalY * 2.0f;
                                        }
                                    }
                                }
                            }
                            else { velX = 0f; velY = 0f; }
                        }
                        else
                        {
                            posX = nextX; posY = nextY;
                            posZ = nextGroundHeight; velZ = 0f;
                            isGrounded = true;
                            speed2D = MathF.Sqrt(velX * velX + velY * velY);
                            if (inputCrouch && speed2D > 100f)
                            {
                                BeginSlide(ref actionState, ref stateTicksRemaining, ref slideMomentumActive,
                                    CanMomentumSlide(prof, combatLockoutTicks, speed2D));
                            }
                            else
                            {
                                velX *= prof.LandingSpeedRetain;
                                velY *= prof.LandingSpeedRetain;
                                actionState = ActionState.Idle;
                                stateTicksRemaining = 0;
                            }
                        }
                    }
                    else
                    {
                        posX = nextX; posY = nextY;
                        posZ += velZ * dt;
                    }
                }
using SlopArena.Shared

            // Floor clamp
            groundHeight = GetGroundHeight(posX, posY);
            if (posZ < groundHeight)
            {
                posZ = groundHeight; velZ = 0f;
                bool wasAirborne = !isGrounded;
                isGrounded = true;
                speed2D = MathF.Sqrt(velX * velX + velY * velY);
                if (inputCrouch && speed2D > 100f)
                {
                    BeginSlide(ref actionState, ref stateTicksRemaining, ref slideMomentumActive,
                        CanMomentumSlide(prof, combatLockoutTicks, speed2D));
                }
                else if (wasAirborne)
                {
                    velX *= prof.LandingSpeedRetain;
                    velY *= prof.LandingSpeedRetain;
                    actionState = ActionState.Idle;
                    stateTicksRemaining = 0;
                }
using SlopArena.Shared

            // Jump check
            if (isGrounded && inputJump && actionState != ActionState.Dashing && actionState != ActionState.Attacking)
            {
                velZ = JumpForce;
                posZ += velZ * dt;
                isGrounded = false;
using SlopArena.Shared

            // Arena bounds
            if (posX < ArenaMinX) { posX = ArenaMinX; velX = 0; }
            if (posX > ArenaMaxX) { posX = ArenaMaxX; velX = 0; }
            if (posY < ArenaMinY) { posY = ArenaMinY; velY = 0; }
            if (posY > ArenaMaxY) { posY = ArenaMaxY; velY = 0; }
using SlopArena.Shared

        private static bool CanMomentumSlide(MovementProfile prof, ushort combatLockoutTicks, float speed2D)
        {
            return prof.EnableMomentumSlide
                && combatLockoutTicks == 0
                && speed2D >= prof.SlideMomentumMinSpeed;
using SlopArena.Shared

        private static void BeginSlide(
            ref ActionState actionState, ref ushort stateTicksRemaining, ref bool slideMomentumActive,
            bool momentum)
        {
            actionState = ActionState.Sliding;
            slideMomentumActive = momentum;
            stateTicksRemaining = 0;
using SlopArena.Shared

        private static void ClampSlideSpeed(
            ref float velX, ref float velY, MovementProfile prof, bool slideMomentumActive)
        {
            if (!slideMomentumActive) return;
            float sp = MathF.Sqrt(velX * velX + velY * velY);
            if (sp > prof.SlideMaxSpeed)
            {
                float scale = prof.SlideMaxSpeed / sp;
                velX *= scale; velY *= scale;
            }
using SlopArena.Shared

        public static void ApplyKnockback(
            ref float velX, ref float velY, ref float velZ,
            ref ActionState actionState, ref ushort stateTicksRemaining,
            float knockbackX, float knockbackY, float force, ushort durationTicks,
            ClientInputPacket input)
        {
            actionState = ActionState.Hitstun;
            stateTicksRemaining = durationTicks;
            velX = knockbackX * force;
            velY = knockbackY * force;
using SlopArena.Shared

            bool inputUp = (input.MovementFlags & 0x01) != 0;
            bool inputLeft = (input.MovementFlags & 0x02) != 0;
            bool inputDown = (input.MovementFlags & 0x04) != 0;
using SlopArena.Shared

            float diX = 0f, diY = 0f;
            if (inputUp) diY -= 1f;
            if (inputDown) diY += 1f;
            if (inputLeft) diX -= 1f;
using SlopArena.Shared

            if (diX != 0f || diY != 0f)
            {
                float diLen = MathF.Sqrt(diX * diX + diY * diY);
                diX /= diLen; diY /= diLen;
                float diStrength = 0.3f;
                velX += diX * force * diStrength;
                velY += diY * force * diStrength;
            }
using SlopArena.Shared

        private static void ResolveMovementInput(
            ClientInputPacket input, float posX, float posY,
            bool inputUp, bool inputLeft, bool inputDown, bool inputRight,
            out float moveX, out float moveY, out float moveMaxSpeed)
        {
            var prof = MovementProfiles.Active;
            moveMaxSpeed = prof.MaxSpeed;
            moveX = 0f; moveY = 0f;
            if (inputUp) moveY -= 1f;
            if (inputDown) moveY += 1f;
            if (inputLeft) moveX -= 1f;
using SlopArena.Shared

            if (moveX != 0f || moveY != 0f)
            {
                float len = MathF.Sqrt(moveX * moveX + moveY * moveY);
                moveX /= len; moveY /= len;
                if (inputDown && !inputUp && !inputLeft && !inputRight)
                    moveMaxSpeed = prof.BackwardMaxSpeed;
            }
        }
    }
using SlopArena.Shared
