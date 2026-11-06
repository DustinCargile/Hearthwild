# Shoulder-camera capsule controller (Unity 6 / NGO client-server)

This is an **MVP starter**, not a production-ready multiplayer combat solution. It provides mouse/gamepad 3D aiming (yaw AND pitch), with the entire attack aligned to the player's aim vector, server-driven movement, simple procedural weapon swings, horizontal/spin/thrust/vertical attack probes, parry/block/dodge/sprint/jump, and injury-system-ready hit reports. Damage intentionally uses **no HP**.

## Required packages

- **Input System** (`com.unity.inputsystem`)
- **Netcode for GameObjects** (`com.unity.netcode.gameobjects`, compatible with the modern `[Rpc]` syntax, e.g. 2.x)
- **Unity Transport** (`com.unity.transport`) / transport selected in NetworkManager

Set **Edit > Project Settings > Player > Active Input Handling** to **Input System Package (New)** or **Both**.

## Fast setup: a network player prefab

1. Make `Player` (empty GameObject with its transform at the character's feet). Add `NetworkObject`, `NetworkTransform`, `CharacterController`, `NetworkCapsuleController`, `CombatTestReceiver` and `WeaponVisualDriver` to its **root**.
2. Set CharacterController **Center (0, 1, 0), Height 2, Radius ~0.35**. You may adjust for your model. Do not also use a root Rigidbody.
3. Create `Player/BodyMesh` as a Unity **3D Capsule** mesh, set local position **(0, 1, 0)**, and **remove its default CapsuleCollider**. The CharacterController on the root handles physical collision. This avoids having the capsule pivot buried in the ground.
4. Create `Player/AimPitchPivot` as an empty GameObject. Put it at local position **(0, 1.36, 0)** for the example height-2 capsule, matching `strikeOriginHeightFraction = 0.68` on the controller (2 × 0.68 = 1.36 above feet). Create a child empty named **ActiveWeapon**, local position approximately **(0.45, 0, 0.45)**. Attach a visible sword/cube mesh as a child of ActiveWeapon. Remove unwanted weapon collision components. The procedural driver rotates the pivot to the aimed pitch, and rotates or translates ActiveWeapon to show each attack.
5. On `WeaponVisualDriver` (on Player root), assign **Height Pivot = AimPitchPivot** (legacy Inspector field name; it now controls pitch, not height) and **Active Weapon = ActiveWeapon**. Leave **Use Procedural Swings** checked.
6. On `NetworkCapsuleController`, assign **Active Weapon**, **Weapon Visuals**, and optionally **Fallback Weapon**. Leave the character Animator blank for the initial cube/capsule test. `CombatTestReceiver` on the player root will show attacks/parries in the Unity console.
7. For a visible local aiming marker, create a small sphere as a child of Player, **remove its SphereCollider**, scale ~0.12, and assign it to the `Local Aim Marker` slot. It will be visible only to the owning client.
8. On the **NetworkTransform**, leave it **server-authoritative** (default in client-server mode). Synchronize world position and rotation (including Y). Do **not** set owner/client authority with this code: only the server calls `CharacterController.Move`.
9. Drag Player to the Project window to create a prefab. Assign it as the **Default Player Prefab** on a scene `NetworkManager` configured with Unity Transport. Remove any testing Player instance from the scene so NGO creates a player for each connection.
10. Add `TopDownCameraFollow` to the scene's **Main Camera**. Use an orthographic camera for a clean top-down perspective (or perspective if desired). The local player's spawn code automatically assigns this camera's Target.
11. Add `NetworkStartGUI` to an empty scene GameObject for Host/Client/Server testing, then enter Play Mode and click **Start Host**. To test two players, connect another Editor instance or client build to the host's Unity Transport address and port.

A network-enabled dedicated-server build can start NetworkManager in server mode through your production bootstrap (the optional GUI also has a Start Dedicated Server button in non-headless runs).

## Testing aimed knee strikes

1. Make an `EnemyDummy` GameObject standing on the floor, add a **CapsuleCollider** and `CombatTestReceiver` to its root. A dummy only needs a `NetworkObject` if your game dynamically spawns/synchronizes it.
2. Move the mouse cursor over the **visible lower body** of the dummy. The camera raycast locates the point under the cursor, then computes an aim vector from `StrikeOrigin` to that point (horizontal yaw + vertical pitch). **There is no attack-height button, mouse-wheel adjustment, or D-pad adjustment.**
3. **Tap left mouse button** for a horizontal swing. Its arc is tilted to the aim pitch, so an aimed -30° attack travels downward through the target while sweeping from side to side. Inspect `[SERVER HIT] ... Knee by Horizontal` in the server console when contact resolves in the knee region.
4. Optional but recommended: add a child `KneeHitbox` at local height **0.5–0.65** above the dummy's feet with a small **SphereCollider (Is Trigger = true)** and `BodyHitZone.region = Knee`. For full anatomy, add Head, Torso, Arms, Legs etc., each with their own colliders and BodyHitZone components. Combat resolution prioritizes overlapping explicitly tagged regions over generic capsule hits.
5. Set `aimLayers` to the layers the cursor may target (enemy/terrain/props); set `targetLayers` to the layers that combat probes should hit; set `obstructionLayers` to a dedicated **Environment/Walls** layer that *does not include characters*. If no environment obstruction layer is set, attacks currently pass through walls.

**Important:** A plain capsule has no actual kneecap. Without explicit hitboxes, the server *infers* knee/leg/torso/head from contact height. The cursor raycast selects the visible collider's surface, **not a hidden bone**: top-down camera occlusion and coarse capsule colliders may make knee targeting imprecise. Accurate anatomical aiming needs a compatible camera, visible limb hitboxes or a body-part selection system, and ultimately animation-driven blade collision.

## Input controls

| Action | Mouse + keyboard | Xbox-style gamepad |
| --- | --- | --- |
| Move | WASD | Left stick |
| Aim direction + pitch | Mouse cursor over 3D world/target | Right stick moves a camera-screen targeting point relative to the player |
| Horizontal / spin | Left click / hold | RB tap / hold |
| Thrust / vertical | Right click / hold | RT tap / hold |
| Parry / block | Left Ctrl tap / hold | LT tap / hold |
| Dodge / sprint | Left Shift tap / hold | B tap / hold |
| Jump | Space | A |

A tap is recognized **on release**; a hold is recognized after `tapHoldThreshold` (default 0.27 seconds). This prevents a tap and hold from both activating, at the cost of some input latency. For competitive-feeling combat later, remap charge moves to separate actions or use alternative attack button semantics.

The InputActionMap and bindings are **created in code per local owning player**: no `.inputactions` asset needs to be made yet. You can replace `CreateInput()` with a generated Input Actions class when you want rebindable controls, UI action maps, local couch co-op, accessibility presets, etc.

## What is authoritative?

- Client sends **move vector, desired yaw, desired pitch, sprint flag** at approximately 20 Hz (unreliable), plus reliable jump/dodge/guard/attack requests.
- Server runs the **CharacterController** movement, gravity, cooldowns and collision queries; sends transforms via **server-authoritative NetworkTransform**.
- Client requests an attack **type, yaw, and pitch only**. Server clamps pitch to the configured range, enforces attack timing, and samples a pitch-tilted weapon pattern using **server positions**. It never trusts a client-reported hit, target ID, position, or damage. Advanced aim-angle abuse prevention still requires validating plausibility against server state.
- Server reports a `CombatStrike` to `ICombatStrikeReceiver.ReceiveStrike` exactly once per target per swing. The sample receiver reports hit region and force, checks block/parry, and causes a brief stagger on a parried player attacker.
- Simple weapon visual animations are broadcast to clients via RPC. They are illustrative animation, **not** precise blade physics or validated clip events.

## Scope and what needs replacing before production

- **Movement prediction/reconciliation**: none. Remote clients will feel input delay. Add tick-based input buffers, owner prediction, interpolation and server correction before shipping.
- **Lag-compensated melee**: none. A moving enemy may be in a different position when the server checks the hit. Add authoritative ticks, hitbox-history rewind, and properly constrained client command timing.
- **Contact accuracy**: sampled sphere overlap volumes approximate the swing. Replace with continuous blade/root motion sweeps keyed to animation events and tracked against server animation state. Large crowds may fill the 64-collider overlap buffer. Optimize allocations in `FindReceiver`.
- **Weapon equipment**: the example uses `fallbackWeapon` or an `EquippedWeapon` component on a child under ActiveWeapon. It assumes the same equipped model is present on all client/server copies. Actual network equipment switching, networked loadouts, persistent weapon IDs and ownership checks are still needed.
- **No stamina, hunger, injuries or armor resolver**: intentional. Wire `CombatStrike` into your existing/future armor penetration, injuries and Conditions manager. Sample parry/block completely negate contact; later differentiate shield coverage, armor zones, partial mitigation, and stagger susceptibility.
- **Animation**: `WeaponVisualDriver` uses procedural socket motion until real clips exist. An optional Animator receives triggers named `AttackHorizontal`, `AttackSpin`, `AttackThrust`, `AttackVertical`, `Parry`, `Dodge`, `Jump` and a bool `Blocking`; create these parameters only if using an Animator Controller. For Animator-driven weapons, disable `Use Procedural Swings` to avoid double rotation.
- **3D aiming**: mouse and gamepad both aim at a camera-raycast world point. Every attack pattern uses that aim direction's pitch. This works in open space and on visible colliders, but cannot precisely target a limb obscured by body geometry. Extend with target-aware selection and per-limb geometry as rigged characters arrive. The pivot must be at the same local height as `StrikeOrigin` (by default 68% of capsule height above feet).
- **No gamepad camera-relative movement**: gamepad stick uses world XZ aligned with a fixed camera heading. Rotate this input by the camera's yaw if you later introduce camera orbiting.
- **No respawn, death, rounds, world streaming or Steam session integration** yet.

## How this feeds into the planned injury model

Replace the TODO in `CombatTestReceiver.ReceiveStrike` with an authoritative pipeline:

`CombatStrike -> armor coverage / hardness / penetration -> region-specific wound -> ongoing Conditions -> decals / effects`

That allows a horizontal knee strike to create a leg injury if the weapon gets through the relevant protection, instead of subtracting generic HP.

## Unity docs

- Netcode RPCs: https://docs.unity.cn/Packages/com.unity.netcode.gameobjects@2.7/manual/advanced-topics/message-system/rpc.html
- Netcode NetworkTransform authority: https://mp-docs.dl.it.unity3d.com/netcode/current/components/networktransform/
- CharacterController.Move: https://docs.unity3d.com/ScriptReference/CharacterController.Move.html
- Input System composite bindings: https://docs.unity.cn/Packages/com.unity.inputsystem@1.6/manual/ActionBindings.html

## Committed sample setup

Open `Assets/Scenes/SampleScene.unity` in Unity **6000.3.0f1**. The existing NetworkManager uses Unity Transport at `127.0.0.1:7777`, with the registered Player prefab and server authority. Enter Play Mode and click **Start Host**. Use WASD to approach EnemyDummy at (0, 0, 2), aim at its lower front surface, then tap left mouse and inspect server hit logs. Its KneeHitbox is a trigger on the enemy layer. The ground uses layer 6; player layer 3 and enemy layer 7 receive combat probes; environment/wall layers 8/9 block attacks. The scene has no preplaced player.

For a second local peer, launch a client build and click Start Client while the host runs. Separate-machine clients require a reachable transport address configured before connecting. Two-peer movement, attacks, parry/block, jump and dodge/sprint still need Play Mode validation.

The requested `moveAction = input.AddAction("Move", InputActionType.Value, expectedControlLayout: "Vector2");` line is deliberately preserved. If Unity reports an API argument error there, resolve that exception separately with the project owner.

## Shoulder camera V3 update

The sample now uses a perspective ThirdPersonShoulderCamera on Main Camera. Mouse and right stick control look; the center reticle targets the world. WASD, left stick and dodge are relative to facing. Escape unlocks the mouse; left click relocks without attacking. Player already includes collider-free sunglasses. Camera collision uses ground/environment/wall layers (6/8/9); aiming also includes player/enemy (3/7). The old TopDownCameraFollow script is retained for compatibility but is not attached in SampleScene.

See `Docs/ShoulderCameraSetup.txt` for full V3 controls and setup. This update supersedes the older top-down aiming and world-axis movement instructions above. Validate host/client look, movement, dodge, attack pitch, cursor recapture and camera obstruction in Unity.
