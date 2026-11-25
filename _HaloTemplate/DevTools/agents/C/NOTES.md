# C notes (Characters: campaign bipeds)

## Files
- Data/Characters (Unity-free, harness DevTools/agents/C/harness: `list`, `anims`, `nums`, `poses`, `boxes`):
  - `HaloCharacterDef` (bipd + actv: model / graph / coll, team, biped flags (Flying ...), turning, heights, ping / death thresholds,
    grenades, melee damage, unit weapons, change colours; actv: weapon, body / shield vitality override (absolute points), rate of fire,
    projectile error, burst, grenades, drop numbers, colours). `ColorA(rng)` = change colour A.
  - `HaloCollisionDef` (coll: vitality, shield material type, stun / recharge, effects, materials (type, head, multipliers, leak),
    regions (threshold, flags, destroyed effect, permutations), nodes with per-permutation BSP vertex bounds + main material).
  - `HaloUnitAnimator` (the graph state machine on D's HaloPose: seat (stand / crouch / alert / flee / vehicle seat) + weapon class by label,
    idle / move-front/back/left/right blended by direction at the unit's pace (root speed), turn-left/right, airborne, cross-fades by pose
    snapshot; aim-still / aim-move grid, seat look grid, fire-1 overlay, one action (reload, melee, throw (key frame -> release), gesture,
    dive / evade (root motion), land, ready, pings), deaths (s/h-kill by dir + region, airborne-dead / landing-dead), permutations "%n" by
    weight, custom animations of any graph (scripts / cinematics) with root motion).
- Game/Characters: `HaloUnit` (partial: .cs control / Init / Update, .Body model + permutations + colour + hit boxes + pose write,
  .Move kinematic mover, .Weapon weapon in hand / firing / grenades / melee / drop, .Damage TakeHit / shields / regions / pings / death /
  IHaloHitInfo, .Control gestures / dive / evade / CustomAnimation, .Net co-op puppet + checkpoints), `HaloBipeds` (Spawn, Clear, Erase,
  All, caches per map, events Died / Damaged / Fired, body sweep).
- Editor/HaloLabCharacters.cs: labs `characters` (-halomap a10 / a30: line-up pictures idle / aim / fire / walk / strafe / dying / dead +
  close-ups) and `characters-damage` (pistol bullets on a grunt and an elite).

## Contract (for I, K, N, S, Q)
- `HaloBipeds.Spawn(bipd or actv, pos, yawDeg (Unity), team)`; team None = the unit's default. `HaloBipeds.All`, `Erase(u)`, `Clear()`.
- Controls: MoveVelocity (m/s world; walkers horizontal, flying 3D), LookPoint / AimPoint (Vector3.zero = unset), Firing (the unit fires
  its weapon itself: actv rate of fire, actv projectile error + trigger error, magazine + reload-1, infinite reserve), Crouching, Melee /
  ThrowGrenade / Jump (pulses, cleared by the unit), Stance ("alert", "flee", "asleep", "flaming"; null = stand), Frozen.
  `NaturalSpeed` (m/s of the move-front animation now), `Grounded`, `Yaw`, `Eye`, `Center`, `Muzzle` (Transform, barrel = +X / right),
  `Weapon`, `WeaponLabel`, `Grenades`, `GrenadeType`, `Body` / `Shield` (fractions), `MaxBody` / `MaxShield` (points), `LastHit(Time)`,
  `Def` (HaloCharacterDef: Flying ...), `FireOnce()`, `Give(weap)`, `Gesture(id)`, `Dive(dir)`, `Evade(right)`, `CustomAnimation(antr, name,
  interpolate, loop, hold)`, `PlayingCustomAnimation`, `StopCustomAnimation()`, `EnterSeat(marker, label)` / `ExitSeat(at, yaw)`, `Teleport`,
  `Kill()` (before the first frame = placed dead: `PlaceDead()`), `KillSilent()`, `Revive()`, `SetName(name)` (HaloObjects).
- AI shots: `HaloUnit.AiShooter = -2` as shooterSlot / SourcePlayer. Units never raise HaloEvents.Fired (Desk would animate the local
  avatar); they raise `HaloBipeds.Fired`. Death raises HaloEvents.Killed (Killer = hit.SourcePlayer, Victim = -1) + HaloBipeds.Died.
- Co-op: `Puppet = true` (Net moves it; hits -> HaloNetGame.PuppetHit), `SetWeapon`, `PlayFire` (visual-only), `PlayMelee`, `PlayThrow`, `Shots`.
- Checkpoint part "characters" registered with HaloCheckpoints on campaign maps (life, vitality, place, weapon, grenades of existing units).

## Damage model
Hit -> nearest hit box (coll node BSP bounds on the model node, layer Hitbox, kinematic Rigidbody on the model holder) -> its material:
shield first when material.ShieldMultiplier > 0 (modifier of the coll's shield material type x multiplier; leak passes), body by the
material type's modifier x body multiplier; head material + "can cause headshots" + no shield = death. Region thresholds shoot regions off
("~damaged" permutation, model rebuilt). Shield: stun (coll stun time) then recharge (coll recharge time). Pings by the biped thresholds.
Death: h-kill for explosions / past the hard death threshold; units without death animations and with a body-destroyed effect pop.
Dead bodies: hit boxes become triggers (shots and players pass), body stays 90 s (max 40).

## Status
- Harness `all a10 a30 a50 b30 b40 c10 c20 c40 d20 d40`: 411 bipds / actvs through every state (seats, move, fire, reload, throw, melee,
  dive, ping, death 200 steps), 0 exceptions, 0 NaN. Compressed animations (D decodes them now): cyborg_cinematic 9/9, pilot 4/6 and every a10 cinematics graph play as custom animations (harness `cine`, `cine2`: 0 failures, 0 NaN).
- Lab `characters` (a10, 23 characters), `characters-a30` (22), `characters-c10` (flood forms, sentinels, monitor, Keyes), `characters-a50`
  (hunters): pictures DevTools/lab/characters*/lineup_{idle,aim,fire,walk,strafe,dying,dead}_rowN.png + close_*.png, looked at: models textured
  with their permutations (marine helmets, crewmen heads) and change colours (crewman shirts, grunt / elite ranks), weapons in the right
  hands (elite plasma rifle, jackal pistol on the left with its arm shield), muzzle flashes / plasma bolts / needles flying, blood decals,
  run / strafe cycles, aim grid, h-kill / s-kill deaths, dropped weapons. a30, a50, c10 passed (sentinels hover); the first a10 run failed only on Cortana (invulnerable, check fixed since).
- Lab `characters-damage` PASSED: pistol bullet on a grunt (30 -> 10 points, h-ping), 3 bullets take an elite minor's shield 1.00 -> 0.40
  with the body untouched (impact material 30 = elite energy shield), shield recharging after the coll's 12 s stun, grunt killed (s-kill
  front gut), HaloBipeds.Died with killer 0, plasma pistol dropped as a pickup.
- Weapon IK, shield glow overlay, distance animation LOD, recorded-control (IHaloRecordedControl) are in but not checked by picture.

## Open
- Shield glow (the biped's modifier shader overlay) not drawn; only the coll's shield effects play.
- Left-hand IK onto the weapon ("left hand marine" etc.) not done (the animations hold the weapons themselves).
- actv weapon damage modifier (AI damage scaling) needs Combat (G); forced shader permutation (elite majors' armour shade) not applied.
- Footsteps use the cyborg's table (HaloFootsteps.Step), not each biped's own footsteps tag.

## Script surface (round 2, HaloUnit.Script.cs)
Invulnerable, DropsItems, SetEmotion(int) (seat "emotions" overlay frame), SetMaximumVitality / SetCurrentVitality (points; MaxBody /
MaxShield include the override), SetPermutation(region, perm) (rebuild), FlashlightOn (light at the "flashlight" marker, numbers ours),
WeaponReadied / ReadyWeapon(), TotalGrenades / SetGrenadeCount. Checkpoint part skips HaloAi.IsActor units (I restores its own).

## Vehicle seats (enter / exit clips)
EnterSeat plays the seat's enter clip (seat anim 7), ExitSeat the exit clip (8) on the seat, then FinishExit stands the unit under the clip's
final pelvis (standing pelvis height from the stand idle) -> airborne -> landing -> normal. `Exiting` while it plays. Lab
`characters-dropship` PASSED (a30 lz_cship, 4 grunts out of the pod, land-hard, then move-right; dropship_exit_0..10 / _after.png).

## Weapon / body sounds (A's audit)
Fire = trigger firing effect; reload = magazine reloading effect; empty = firing effect "empty"; heat + overheated effect (vents, AI plasma
weapons overheat); overcharge (actv special fire mode 1) charging effect + trigger 2; ready effect; weapon attachments via HaloFx.Attach;
body down = HaloImpacts Impact, else the foot tag's Land. Lab characters-a30: fire 233, empty 2, overheat 5, body impacts 10.
NB: a perl substitution emptied HaloUnit.Weapon.cs once; restored from DevTools/labproject (identical copy of the last lab), then re-edited.
