# Smoke Test Checklist - ServerAbility Refactor

**Date:** 2026-06-22
**Branch:** release
**Build:** bb5bd64d1f294b9514af9dea40476e855722db81

## Test Environment
- Launch: Run project in Godot editor (F5)
- Character: Manki (Pyromaniac Monkey Bomber)
- Mode: Training arena / Sandbox

## Tests

### 1. LMB Combo (MankiLmbCombo)
- [ ] Click LMB 3 times rapidly
- [ ] Expected: 3-hit combo animation plays
- [ ] Expected: Character lunges forward during each hit (5m range closing)
- [ ] Expected: Each hit spawns hitbox at correct timing
- [ ] Test combo chaining: Click during animation vs after
- [ ] Test cooldown: None expected for LMB

**Pass/Fail:** _____
**Notes:** _____

### 2. Q - Round Bomb (MankiRoundBomb)
- [ ] Press Q
- [ ] Expected: Character plays throw animation
- [ ] Expected: Bomb projectile spawns and follows parabolic arc
- [ ] Expected: Aim indicator shows target distance
- [ ] Expected: Bomb explodes on impact (ground or enemy)
- [ ] Test cooldown: 90 ticks (1.5 seconds)
- [ ] Test range: Up to 12m

**Pass/Fail:** _____
**Notes:** _____

### 3. RMB - Aerosol Flamethrower (MankiAerosolFlame)
- [ ] **Tap RMB** (quick burst)
  - [ ] Expected: Short flamethrower cone
  - [ ] Expected: ~58 tick duration
- [ ] **Hold RMB for 1 second, release** (charged)
  - [ ] Expected: Longer flamethrower cone
  - [ ] Expected: More damage than tap version
  - [ ] Expected: ~50 tick duration
- [ ] Test cooldown: 30 ticks (0.5 seconds)

**Pass/Fail:** _____
**Notes:** _____

### 4. Warp Movement (ServerAbility Integration)
- [ ] If any ability has WarpRange > 0: Test warp-to-target
- [ ] Expected: Character warps toward target before attacking
- [ ] Expected: Collision detection works during warp
- [ ] Expected: Gravity applies if airborne during warp

**Pass/Fail:** _____
**Notes:** _____

### 5. Edge Cases
- [ ] Spam abilities on cooldown (should reject silently)
- [ ] Try to activate ability during hitstun (should reject)
- [ ] Try to activate ability during animation lock (should reject)
- [ ] Interrupt ability with different ability (should not orphan old ability)

**Pass/Fail:** _____
**Notes:** _____

## Console Checks
- [ ] No critical errors in console
- [ ] No NullReferenceException
- [ ] No "ERROR: Ability slot X has AbilityTypeId=0"

## Issues Found
_List any bugs, visual glitches, or unexpected behavior_

## Tester Sign-off
**Tester:** _____
**Date:** _____
**Overall Result:** PASS / FAIL / NEEDS_FIXES
