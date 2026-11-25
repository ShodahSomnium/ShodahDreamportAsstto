# Halo Dreamport skeleton -> Assetto Corsa Dreamport (port map)

Research spec for the implementing agents. Topic: which parts of the Halo code at
`D:\Dreamport\Assetta Corsa\Assets\#User\SomniumHalo` are the game-independent core, and how to port them with the rename
Halo -> Ac (namespace `SomniumSpace.Worlds.SomniumAssetto`, asmdef `SomniumAssetto`, folder `Assets/#User/SomniumAssetto`,
log tag `[SomniumAssetto]`).

Labels: **[VERIFIED]** = checked by running code or reading the file / scene byte for byte in this session.
**[BELIEVED]** = inference or recommendation, not tested.

---

## 0. Headline facts

- [VERIFIED] The copy is identical to the original: `diff -rq` of `Assets/#User/SomniumHalo`, `Assets/Halo Scene.unity` and
  `DevTools/build` against `D:\Dreamport\Halo\Halo Dreamport` printed nothing.
- [VERIFIED] The copy compiles as it is with Halo's own build.sh (paths redirected to scratch): runtime 0 errors / 2 warnings,
  runtime_server 0 / 2, editortool 0 / 1, "server_span: only SceneNetworking's Fusion 2.1 callback".
- [VERIFIED] **The core set of 23 files in section 2.1, renamed by sed (section 1) and given 3 tiny stubs (`AcInstall`, `AcSpace.RoomOrigin`,
  `AcNetGame.TryHasRecord`), compiles on its own with the server-like references: 0 errors, 1 warning (CS0618 `SimulationMessagePtr`
  obsolete in SceneNetworking, harmless).** Run through `DevTools/fusionapi/RefDump.dll`, it uses 90 Fusion / Somnium members, **all on
  `known_good.txt`** (nothing unproven). Scratch: `C:/Users/derii/AppData/Local/Temp/claude/acscratch/halo_skeleton/sub1/` (+ `sub1.rsp`).
- [VERIFIED] The vendored NVorbis decoder `Data/Audio/Vorbis/*.cs` (32 files, 4581 lines) renamed the same way compiles on its own
  (0 errors, no Span/stackalloc). Keep it: AC's sounds are FMOD banks (`content/sfx/common.bank` starts with `RIFF`), whose FSB5
  samples are usually Vorbis.
- [VERIFIED] **The scene's uploader still targets the Halo world**: `WorldIdForUpload: 13499`, `DoReplaceExistingWorld: 1`,
  `SelectedIndexForWorldToReplace: 20` (= the world list entry "Id: 13499, Name: Player Capacity", `Scripting.AssemblyName: SomniumHalo`).
  Uploading the copy without sanitising it would **overwrite the Halo Dreamport**. See section 4.3.
- [VERIFIED] There is a second assembly with Halo in its name: `Assets/#User/Community Modules/Scripts/SomniumHalo.CommunityModules.asmdef`
  (name + rootNamespace `SomniumHalo.CommunityModules`, 58 .cs files in that namespace; asmdef guid `0eac857c3f7d3f145947f82658c83ed9`).
  Halo README iteration 10: it was renamed because "assembly names must be unique on the server". **Rename it to
  `SomniumAssetto.CommunityModules`** (asmdef `name`; rootNamespace / namespaces optional) [BELIEVED necessary: same uniqueness rule].
- [VERIFIED] NetworkObject `Flags: 524290` on all 69 scene network objects = `V2 (2) | AllowStateAuthorityOverride (524288)`
  (enum values read from `Assets/Photon/Fusion/Assemblies/Fusion.Runtime.dll` by a dotnet reflection probe:
  None 0, V1 1, V2 2, HasMainNetworkTRSP 256, Ignore 65536, MasterClientObject 131072, DestroyWhenStateAuthorityLeaves 262144,
  AllowStateAuthorityOverride 524288, EnableAreaOfInterest 1048576, EnableExplicitObjectInterest 2097152). The project is on
  Fusion 2.1.3 (defines `FUSION_2_1_OR_NEWER` etc. in ProjectSettings and in the .head files).

---

## 1. The rename (mechanical part)

[VERIFIED: this exact sed produced the compiling subset]
```bash
sed -e 's/SomniumHalo/SomniumAssetto/g' \
    -e 's/HALO_TAGS_BRIDGE/AC_TAGS_BRIDGE/g' \
    -e 's/\bHalo\([A-Z][A-Za-z]*\)/Ac\1/g'  IN.cs > OUT.cs      # file name: Halo*.cs -> Ac*.cs
```
- `SomniumHalo` -> `SomniumAssetto` also renames the log tag (`HaloLog.Tag = "[SomniumHalo] "` -> `"[SomniumAssetto] "`).
- The identifier rule does NOT touch strings with a space after "Halo". These remain and must be edited by hand in the core files
  [VERIFIED by grep of the renamed subset]:

| Renamed file | Line | Text to change |
|---|---|---|
| AcGame.cs | 23 | `[Tooltip("Where Halo is installed (the folder with halo.exe and MAPS).")]` + `_installHint` default (line 24) |
| AcGame.cs | 268, 271, 291, 304, 305, 344 | "Halo Dreamport ...", "Looking for your Halo install...", thread name "Halo boot", the not-found texts, "Halo found: ... multiplayer maps" |
| AcIsolation.cs | 139 | `public const string OwnNetRoot = "Halo Net";` -> the new scene net root name (e.g. `"Ac Net"`) |
| AcPlayer.cs | 330 | `new GameObject("Halo EventSystem (editor)")` |
| AcTags.cs | 282 | log text "has a Halo network record" |
| AcVersion.cs | 8, 12 | Summary, `"Halo Dreamport - iteration "` |
| AcSettings.cs | 27 | folder `"HaloDreamport"` (sed makes it `"AcDreamport"`); choose e.g. `"AssettoDreamport"` |

- Comments keep saying "Halo" after sed; harmless, clean up when touching a file.
- Keep each `.cs.meta` when renaming a file (move `HaloGame.cs.meta` -> `AcGame.cs.meta`) so the scene's `m_Script` guids stay valid
  (see 4.2). If new metas are generated instead (`metas.sh`), the scene's two script guids must be edited.

---

## 2. File-by-file map

Legend: **V** = copy verbatim with the rename. **A** = adapt (rename + the listed edits). **R** = rewrite for AC, keep the pattern.
**S** = skip. Line counts [VERIFIED, `wc -l`].

### 2.1 The compiled-standalone core (23 files, 5561 lines after rename) [VERIFIED compile]

| Halo file | Lines | Purpose | Halo-specific dependencies | Action |
|---|---|---|---|---|
| Core/HaloAccess.cs | 109 | Whitelist (`Whitelist` = "Shodah", "Wildy", "Meloz", "ꨄ Rage Quit ꨄ", "denevraut", "Ninquiet"), `Matches(nick,id[,list])` (trim + OrdinalIgnoreCase), `Verdict {Pending,Allowed,Denied}`, `State`, `Nick`, `UserId`, `Allowed`, `NameTimeout=30`, `Reset()`, private `Decide()` (the one "Access: ..." Player.log line). Pure C#, `static partial` | none | V (keep the list exactly) |
| Core/HaloLog.cs | 20 | `Tag`, `Sink/WarnSink/ErrorSink` (Action<string>), `Info/Warn/Error` | tag text | V |
| Core/HaloShaders.cs | 50 | `Carry(Material[])` (logs "Shaders carried: ..."), `Find(name)` carried -> Shader.Find -> null (logs once), `Carrier(name)` | none | V |
| Core/HaloVersion.cs | 15 | `Iteration`, `Date`, `Summary`, `Label` | values | A (Iteration 1, new Date/Summary, "Assetto Corsa Dreamport - iteration N") |
| Core/Unity/HaloAccessCheck.cs | 74 | `IEnumerator Check()`: empty list -> Allowed; `Application.isEditor` -> Allowed; else waits (fail closed, warning after 30 s) for `SomniumBridge.PlayersContainer.LocalPlayer.Properties.NickName` / `.Id`, then `Decide()` | none | V |
| Game/HaloGame.cs | 370 | Scene root MonoBehaviour: log hold, gate, visitor floor, isolation rules, `StartDreamport`, worker-thread install boot, module hooks, `Guard`, `OnDestroy` order. Details 2.4 | `HaloInstall` (Find/HintProblem/Root/Multiplayer.Count), `HaloSettings.InstallOverride`, `HaloSpace.RoomOrigin`, strings | A |
| Game/HaloSettings.cs | 122 | key=value file `persistentDataPath/<Folder>/settings.txt`; `Text/Num/Int/Flag` + `SetText/SetNum/SetInt/SetFlag`, `Changed` event, debounced `Tick()`/`Save()`, `CacheFolder`; shared keys `install`, `volume`, `haptics`, `lefthanded` | folder name | A (folder name only) |
| Game/Access/HaloGear.cs | 39 | `HasRecord(photonId)`: asks `HaloNetGame.TryHasRecord(id, out known)`; when not known (offline/editor) falls back to "remote is on the whitelist" via `HaloRemotes.All` | `HaloNetGame.TryHasRecord` | V (the net layer must provide `public static bool TryHasRecord(int photonId, out bool known)`) |
| Game/Access/HaloIsolation.cs | 1552 | Hide / silence remote players not "with me" (forceRenderingOff, tag CanvasGroup alpha 0, colliders off, voice volume 0, other sounds mute; exact restore). Long verified comment at the top - keep it | `HaloPlayer.Root`, `HaloPlayer.Head()` (CacheLocal, line ~451), `OwnNetRoot = "Halo Net"`, `OwnNetworkingRoot = "Scene Networking"` | V (+ OwnNetRoot string) |
| Game/Access/HaloIsolationDriver.cs | 44 | `[DefaultExecutionOrder(9990)]` LateUpdate -> `HaloRemotes.Tick()` (3 logged errors max); OnDestroy -> `HaloRemotes.Reset()` | none | V |
| Game/Access/HaloRemotes.cs | 332 | Bridge remote list (`Remote {Id=PhotonId, Nick, RawNick, Root=Body.Root, Player}`), `OnPlayerAdded` subscribe + 1 Hz refresh, `Tick()` = list -> `HaloIsolation.Tick()` -> `HaloTags.Tick()`; `Reset()` = isolation shutdown, tag shutdown, unsubscribe | none | V |
| Game/Access/HaloTags.cs | 662 | Hides Somnium name tags of other Dreamport players while the Dreamport runs (`#define HALO_TAGS_BRIDGE` -> `Features.Info.SetPlayerTagDisableState(true)`, fallback tag CanvasGroup alpha) | `HaloGame.Current`, `HaloAccess.Allowed`, `HaloPlayer`, `HaloGear.HasRecord` | V. The file's header says the Bridge member is "UNPROVEN, FIRST USED HERE" - **it is now on known_good.txt** (lines 134 and 192) [VERIFIED]; keep the define on and update that comment |
| Game/Vr/HaloPlayer.cs | 336 | Local player via Bridge: `XrPresent`, `Head()` (Camera.main / stereo camera, cached per frame), `HasBridgePlayer`, `Root`, `HeadBone`, `LeftHand`, `RightHand` (Body.*), `TryFacingYaw`, `TryFeet`, `Teleport(pos, yawDeg[, done], out how)` (Motion.DoTeleportToPoint; editor fallbacks), `BackKey = K` + `BackKeyPressed()`, `EnsureEditorEventSystem()` | string only | V |
| Game/Vr/HaloXr.cs | 218 | Controllers via UnityEngine.XR InputDevices: struct `HaloHand` (Grip/Trigger with hysteresis GripOn .55/Off .35, TriggerOn .5/Off .3, Valve Index force), `Left/Right`, `Hand(right)`, `Tick()`, `Pulse(right, amp, s)`, `HapticsOn`, lab `Simulated/SimLeft/SimRight` | none | V |
| Game/Vr/HaloTabletGuard.cs | 140 | Switches the client's tablet actions "RightHandTablet"/"LeftHandTablet" (XRTabletFeature found by type name, InputActionReference by reflection) off while held; `Update(bool holding)`, `Release()`; ReleaseAfter 0.8 s | none | V (call `Update(true)` while seated: B/Y are needed in the car) |
| Game/Vr/HaloLaserAim.cs | 131 | Measures grip-pose -> pointing offset from the client's NearFarInteractor; `Has(right)`, `TryAim(right, hand, out aim)` | none | V |
| Game/Vr/HaloBody.cs | 184 | Torso frame from head + hands (Bridge), `HandPose`, `GripToAim = Euler(90,0,0)`, monitor hand placement `DesktopHip` | gun-oriented fields (DesktopAim) | V (compiles); adapt later for hands-on-wheel |
| Game/Vr/HaloFirstPerson.cs | 231 | Monitor camera: forces the client's first-person zoom by queuing scroll notches (Input System by reflection: `UnityEngine.InputSystem.InputSystem, Unity.InputSystem`), holds 2DPlayer actions off (`BlockedActions = PopUpMenu, Rotate, Interact, HideObject, MuteObject`), closes "PopUpMenu.NonXR(Clone)"; `ForceFirstPerson`, `Tick()`, `HoldKeys()`, `Release()` | none | V; extend `BlockedActions` for driving (see 5.3) |
| Game/Desk/HaloAvatarFind.cs | 87 | `ByHead(headPos, out kind)`: own avatar model = humanoid Animator whose head is < 0.6 m from the Bridge head (client roots MainRoot / NonXRMainRoot / MirrorRoot / TeleportRoot / WearablesRoot) | none | V |
| Game/Desk/HaloAvatarCopies.cs | 96 | `Bind(mainAnimator)`, `Copy()`: copies posed bone rotations to the mirror / teleport / shadow copies | none | V |
| Game/Desk/HaloHeadHide.cs | 105 | `HaloHeadHide.Hide(head)` shrinks the own head for the frame, restored at `RenderPipelineManager.endContextRendering`; also `HaloViewScale.Set/Restore/Unhook`; `LabHold` | none | V |
| Game/Net/SceneNetworking.cs | 602 | This project's copy of the Community Modules SceneNetworking (Alpha V3.0, Fusion 2.1 API). See 2.5 | namespace only | V ("do not edit": namespace line only) |
| Game/Shared/HaloLayers.cs | 28 | Layer constants (World 0, Avatar 2, Interactable 16, Environment 23, Hitbox 24, Vehicle 28; ShotMask, GroundMask) | Hitbox/ShotMask are shooter terms | V |

Stubs used for the test (what the rest of the AC code must provide) [VERIFIED as sufficient for compile]:
```csharp
public sealed class AcInstall { public static string HintProblem; public string Root; public List<...> Multiplayer; public static AcInstall Find(string hint); }
public static class AcSpace  { public static readonly Vector3 RoomOrigin = new Vector3(0f, 3000f, 0f); }
public static class AcNetGame { public static bool TryHasRecord(int photonId, out bool known); }
```
(`AcGame.FinishBoot` logs `_install.Multiplayer.Count`; for AC replace it by car / track counts.)

### 2.2 Other generic or semi-generic pieces (not in the compile test)

| Halo file | Lines | Purpose | Halo dependencies | Action |
|---|---|---|---|---|
| Core/HaloSpace.cs | 53 | Halo -> Unity coordinates (x,y,z)->(x,z,y)*3.048, quaternion map, `RoomOrigin (0,3000,0)`, `MapOrigin` | `HVec3` (Data/Map), Halo units | R: keep `RoomOrigin` (scene spawn is at y 3000.9, spawn floor y 2999.5) and `MapOrigin`; AC's kn5 conversion belongs to the format spec |
| Game/HaloGame.Front.cs | 14 | `FrontStart => HaloFront.Start(this)`, `FrontBootDone`, `FrontTick`, `FrontStop` | HaloFront | A (call AcFront) |
| Game/HaloGame.Net.cs | 25 | `NetStart => HaloNetGame.Start(this); AddOnce<HaloNetDriver>()`, `NetTick`, `NetStop` | HaloNetGame | A |
| Game/HaloGame.Match.cs / .Campaign.cs | 9 / 10 | Match / campaign hooks | Halo modules | R as `RaceTick`/`RaceStop` (Campaign: S) |
| Game/Shared/HaloSession.cs | 164 | `Phase {Hub,Loading,Playing,Ending}`, events PhaseChanged / MapOpened / WorldBuilt / Unloading raised per handler in try/catch, `UtcNow`, `HaloRules` | HaloInstall.MapEntry, HaloMap, HaloScenario, HaloRules (game types, weapons) | R: same shape with track/layout/cars/race rules |
| Game/Shared/HaloLocal*.cs | 64+42+26+11+37 | Local player state, partial per module | weapons, Halo tags | R (racing fields; keep the "partial per owner" rule) |
| Game/Shared/HaloDamage.cs | 76 | hits, kills, teams, HaloEvents | shooter | S |
| Game/Net/HaloFusionTransport.cs | 317 | `IHaloSlotTransport` over Fusion: finds `NetworkBridgeData` objects BY NAME (prefixes) once a second (then 5 s while incomplete), checks `IntCount >= 64`; `Read` (IntArray.Get), `Write` (sets only changed ints, needs HasStateAuthority), `RequestAuthority` (Fusion 2.1: logs once when RequestStateAuthority returns false), `Present` = Runner.ActivePlayers PlayerIds (1 s / on join-leave), `IsMaster` = SceneNetworking.IsMasterClient, `Describe()` (SessionInfo by reflection, FNV fingerprint) | record map: player 0..49, world 50..53, coop 54.. (HaloNetWire/HaloNetWorld/HaloNetCoop prefixes) | A: keep everything; replace the prefix table / record ranges by AC's blocks |
| Game/Net/Core/HaloNetTransport.cs | 255 | `IHaloSlotTransport` interface (Online, LocalId, SlotCount, WorldCount, CoopCount, IsMaster, Present, Update, IsValid, HasAuthority, Read, Write, RequestAuthority, Describe, Dispose) + `HaloLoopbackWorld`/`HaloLoopbackTransport` (in-memory Fusion rules: RequestDelay 0.12 s, SendEvery 1/16 s, OrphanDelay 0.4 s) for harness/lab | record counts constants | A (interface V; drop CoopCount if no coop blocks) |
| Game/Net/Core/HaloSlotClaim.cs | 305 | `HaloSlotRules` (Contains, FreeFor, Listed, Counts, ToFree) + `HaloSlotClaim` state machine Idle -> Asking -> Confirming -> Owned (Slots 50, AuthorityWait 3 s, ConfirmAfter 1 s, AskEvery 1 s, LostPatience 1.5 s, RoundPause 5 s; first try slot = localId % count) | `HaloNetWire.{Used, AtMagic, Magic, OwnerOf, GenerationOf, StatusOf}`, `HaloNetStatus.Playing` | V once AcNetWire keeps the same header |
| Game/Net/Core/HaloNetWire.cs | 783 | The 64-int player record: header + Halo payload + packers | Halo payload (weapons, hits, grenades, CTF) | R: keep header ints 0-4 and the packers (2.5.3); new racing payload |
| Game/Net/Core/HaloNet.cs | 894 | Session over the transport: read all records each frame, claim, housekeeping (write orphaned slots free), remotes, master, world, write own (WriteEvery 0.1 s, MinWriteGap 0.07 s), `HasRecord(playerId)`, `Stop()` writes own slot free, `StatsLine`, capped `Log` (300 lines) | hits / pickups / drops / objectives / teams / coop | A: keep TickCore skeleton (lines 211-330: `_t.Update`, read loop, `StepClaim`, `Housekeeping` every 0.25 s, `RefreshRemotes`, `StepMaster`, `WriteOwn`), `HasRecord`, `Stop`; drop the rest |
| Game/Net/Core/HaloNet.Master.cs | 520 | Instance master rule + game command + lobby (2.5.4) | HaloNetState fields, team code | A (master rule verbatim) |
| Game/Net/Core/HaloNetTimeline.cs | 184 | Remote pose interpolation: writer-stamped samples, `Delay 0.16 s`, `SnapDistance 6 m`, `CatchUpRate 0.3`, 12 samples; already carries `VehiclePos/VehicleVel/VehicleRot/VehicleFlags` (bit 1 = respawn, no interpolation) | HaloV3/HaloQ (in HaloNetWire.cs) | A: the car pose path is ready-made |
| Game/Net/Core/HaloNetStream.cs | 185 | Byte blob streamed round-robin through N records (224 bytes per record, FNV checksum, blob id), for host state | HaloNetCoop/HaloNet constants | V-ish (only if a hand-over blob is needed) |
| Game/Net/Core/HaloNetWorld.cs | 349 | Master-written world blocks (header 7 ints: magic, seq, stamp, writer, game hash/number/start; payload 4 x 57 = 228 ints) | Halo payload (objectives, pickups, vehicles) | R: same header, race-control payload |
| Game/Net/Core/HaloNetCoop.cs | 521 | Campaign actor / mission blocks | campaign | S |
| Game/Net/HaloNetHostState.cs | 180 | Hand-over part registry: `Register(key, order, capture, adopt)`, `Every = 15 s`, `Promoted` event, `Pack/Unpack` | `HaloNetGame.Net.HostStream` | A if race state must survive more than the world blocks |
| Game/Net/HaloNetGame*.cs | 345 + parts | Net public face: `Start/Tick/Stop`, `TryHasRecord`, `Up/Online/Claimed/IsMaster/MasterKnown/MasterName`, `Players`, `HaloNetDriver` (LateUpdate order 1000, in HaloNetGame.cs line 338) | Halo gameplay | R (pattern), HaloNetDriver V |
| Game/Front/HaloTabletLink.cs | 1303 | Borrows the client's tablet (types TabletControl / TabsContainer / HomeTab / TabletStatusBarView found by type name), adds one home button + app canvas; `Instance`, `AppTitle = "HALO"`, `Owned = "Halo.Tablet."`, `Frame`, `AppCanvas`, `IsLinked`, `AppOpen`, `AppShown` / `LinkChanged`; skips objects named `"Halo "...` (line 636) | `HaloMenuPanel.Width/Height`, `HaloUi.UiLayer`, `HaloTabletApp`, `HaloPlayer`, `HaloGame` | A: the user wants the menus in the tablet - this is THE piece to port (AppTitle "ASSETTO CORSA", Owned "Ac.Tablet.", skip prefix = scene object prefix) |
| Game/Front/HaloTabletApp.cs | 170 | The app (page in tablet frame; closes on match start; `Shutdown` from FrontStop) | Halo pages | R |
| Game/Front/HaloSettingsUi.cs | 114 | Settings row registry `Toggle/Stepper/Choice` (keys in HaloSettings) | none of note | A |
| Game/Front/HaloUi.cs, HaloMenuPanel.cs | 405, 353 | World-space UI kit (GraphicRaycaster + TrackedDeviceGraphicRaycaster recipe), one menu page used by hub and tablet | Halo ui.map art, fonts | R (keep the raycaster recipe and "one page, two hosts" pattern) |
| Game/Front/HaloLoading.cs | 435 | Loading screen: monitor overlay / VR 3.4 m screen 2.5 m ahead, fade from black into the map over ~1 s, back to hub; follows Match progress | Halo ui.map art and strings | R: AC art exists per track: `content/tracks/<t>/ui/preview.png`, `outline.png`, `ui_track.json` [VERIFIED present for drift, imola]; cars `content/cars/<c>/ui/badge.png`, `ui_car.json` |
| Game/Desk/HaloDesk.cs, HaloDeskBody.cs | 300, 511 | Monitor play conductor; the camera tricks: `beginCameraRendering` sets `nearClipPlane = FirstPersonNear (0.08)` and divides FOV for zoom, restored at `endCameraRendering` (HaloDeskBody lines 88-112), V key first/third person (`desk.view`), LateUpdate order 9996 | cyborg puppet, weapons | R (copy the camera hook code; drop the puppet) |
| Game/Desk/HaloDeskPlayer, HaloPuppet, HaloPuppetPoser, HaloCyborg, HaloWeaponAnim | 289/547/626/208/264 | Halo cyborg animation puppet | Halo anims | S (use ETS seat / steering code for seated avatars) |
| Game/Vehicles/HaloVehicleSeat.cs (reference only) | 436 | Proven seat recipe: `SetCollisionDisableState(true)`, `SetFlyModeDisableState(true)`, `SetTeleportationDisableState(true)`, `SetMovementSpeed(0,0,0)`, `SetGravity(0,0)`, `SetJump(0,0)`, `SetFallingAnimationDisabled(true)`, `SetVRIKLegMovementDisabled(true)`, `Poses.SetPose(PoseType.SittingStraight)`; undo with 1,1,1 / `PoseType.None` (lines 87-93, 160-172, 347) | Halo vehicle | reference for the AC seat |
| Game/Vehicles/HaloVehicleCamera.cs (reference) | 165 | Monitor chase camera: disables the client's CinemachineBrain by type name, writes the camera late, obstacle layers 0/14/16/23 | Halo trak | reference |
| Data/Audio/Vorbis/* | 32 files, 4581 | NVorbis 0.10.5 (MIT) in nested class | none | V [VERIFIED compile] |
| Data/** (rest) | | Halo map / tag readers | Halo | S |
| Resources/*.mat, Shaders/** | | Halo shaders + carrier materials (HaloEnv, HaloModel, HaloTransparent, HaloGlass, HaloSprite, Fx/HaloFx) | Halo | S (AC World module makes its own; carriers list emptied, 4.4) |
| README.md | | Iterations table + Player.log lines | Halo | R (new README, Iterations starts at 1) |

### 2.3 Editor

| File | Lines | Action |
|---|---|---|
| Editor/SomniumHalo.Editor.asmdef | - | A: section 3 |
| Editor/HaloLab.cs | 172 | A: section 7 |
| Editor/HaloSceneResave.cs | 25 | A: `ScenePath`, log tag, method path `SomniumSpace.Worlds.SomniumAssetto.Editor.AcSceneResave.Run` |
| Editor/LabStubs/* (6 .cs + `SomniumHalo.LabStubs.asmdef`) | 62 | V with rename if the tablet lab is ported (stand-ins for TabletControl, TabsContainer, HomeTab, TabletStatusBarView, SwitchTabButton, XRInteractableButton; asmdef: no references, `autoReferenced: false`, `defineConstraints: ["UNITY_EDITOR"]`) |
| Editor/HaloLab<Module>.cs (16 files) | | S |

### 2.4 HaloGame.cs in detail (the gate) [VERIFIED by reading]

- Serialized: `Material[] _shaderCarriers`, `string _installHint` (default `C:\Program Files (x86)\Microsoft Games\Halo`). Statics: `Current`.
- Public: `Install`, `Rescan()` (re-boot after the player set a folder), `InstallHint`, `ShaderCarriers`, `Status`, `StatusIsError`,
  `event Action<string,bool> StatusChanged`, `SetStatus(text, error)`, `AddOnce<T>()`, `static Guard(string what, Action a)`.
- Partial hooks: `FrontStart/FrontBootDone/FrontTick/FrontStop`, `NetStart/NetTick/NetStop`, `MatchTick/MatchStop`, `CampaignTick/CampaignStop`.
  For AC: keep Front + Net, replace Match/Campaign by `RaceTick/RaceStop` (or keep the name Match).
- `Awake` (line 88): `_heldLog.Clear()`; `HaloLog.Sink/WarnSink/ErrorSink` = HoldLog(level) (max 500 lines); `StartCoroutine(Gate())`.
- `Gate()` (133): steps `HaloAccess.Check()`; on the first wait: `StartIsolation(NobodyRule("waiting for the whitelist check"))` +
  `ShowPlainFloor()`; denied -> `StartIsolation(NobodyRule("visitor (not on the whitelist)"))`, floor, `ReleaseLog(false)` (sinks null, held
  lines dropped), stop; allowed -> `ReleaseLog(true)` (sinks = Debug.Log/LogWarning/LogError, flush), `HidePlainFloor()`, `StartDreamport()`.
- `ShowPlainFloor` (167): `CreatePrimitive(Plane)` "Floor", child of root, at `HaloSpace.RoomOrigin`, scale (6,1,6) = 60 m, URP Lit (fallback
  Unlit) grey 0.42, smoothness 0.1.
- `NobodyRule`: `IsWithMe = id => false`, `WorldRoot = transform`. `WhitelistRule`: `IsWithMe = HaloGear.HasRecord`, `ShowDelay 0`,
  `DecideInterval 0`, `WorldRoot = transform`. `StartIsolation(rule)`: `HaloIsolation.Start(rule)`, `AddOnce<HaloIsolationDriver>()`.
- `StartDreamport` (264): `_started`, `Current = this`, version log, `HaloShaders.Carry(_shaderCarriers)`, `StartIsolation(WhitelistRule())`,
  `SetStatus("Looking for ...")`, `Guard(FrontStart)`, `Guard(NetStart)`, `StartBoot()`.
- `StartBoot`: hint = `HaloSettings.InstallOverride` else `_installHint`; background Thread "Halo boot" -> `HaloInstall.Find(hint)`, sets
  `_bootHintProblem`, `_bootError` / `_bootInstall`, `_bootMs`, `_bootDone` (volatile).
- `Update`: returns until `_started`; once `_bootDone && !_bootHandled` -> `FinishBoot()` (status line, `Guard(FrontBootDone)`); then
  `Guard(NetTick)`, `Guard(FrontTick)`, `Guard(MatchTick)`, `Guard(CampaignTick)` - net first.
- `OnDestroy`: Guard Campaign/Match/Net/Front stop; then **in its own try** `HaloRemotes.Reset()`; `HaloAccess.Reset()`; `Current = null`.
- AC edits: `_installHint` default (Steam: `C:\Program Files (x86)\Steam\steamapps\common\assettocorsa`; this machine
  `E:\SteamLibrary\steamapps\common\assettocorsa`), texts, `AcInstall`. Optional improvement [BELIEVED]: `Update` builds 4 lambdas
  capturing `this` per frame (`() => NetTick()` ...), i.e. 4 delegate allocations a frame against the "no allocation per frame" rule -
  cache them as `Action` fields in Awake.
- Note [VERIFIED by reading SceneNetworking]: SceneNetworking logs `"[World] [SceneNetworking] ..."` with Debug.Log from its own Awake,
  i.e. also on a visitor's Player.log (not tagged with the Dreamport). Halo shipped like this; do not edit SceneNetworking.

### 2.5 Networking core in detail

#### 2.5.1 SceneNetworking (this world's copy) [VERIFIED by reading]
- `public class SceneNetworking : MonoBehaviour, INetworkRunnerCallbacks`, header "Alpha V3.0 [2026, 05, 01]".
- Serialized: `_autoTransferObjectsAuthority = true`, `_editorConnectPhotonNetwork = false`, `_networkPrefabs`, `_networkPrefabsGuid` (both empty in the scene).
- Statics: events `OnNetworkRunnerCreated`, `OnLocalPlayerJoined`, `OnBecomeWorldMaster`, `OnOtherPlayerJoined(PlayerRef)`,
  `OnOtherPlayerLeft(PlayerRef)`, `OnReliableMessageReceived(ReliableKey, byte[])`; `Instance`, `NetworkRunnerRef`, `IsNetworkReady`,
  `IsConnected` (ETS fix: `!= null && IsRunning`), `IsDebugMode`, `IsMasterClient` (= `NetworkRunnerRef.IsSharedModeMasterClient`).
- `NETWORK_OBJECT_DEFAULT_FLAGS` = `AllowStateAuthorityOverride` under `FUSION_2_1_OR_NEWER` (else V1 | it).
- Awake: finds all NetworkObjects (incl. inactive) sorted by SortKey; polls `NetworkRunner.Instances[0].IsRunning` every 50 ms; setup adds
  callbacks, registers prefabs, catches up a local join that happened before the callbacks (ETS addition).
- Local join: `RegisterSceneObjects(SceneRef.FromPath(scene.path), objects)`, `IsNetworkReady = true`, SlowLoop every 1 s: as master
  `ReassignNullObjectsAuthority()` (RequestStateAuthority on objects whose `StateAuthority.IsNone`) + master change detection.
  OnPlayerLeft as master -> reassign. **This is how orphaned slots come back to the master.**
- `OnReliableDataReceived(..., ReadOnlySpan<byte> data)` under FUSION_2_1 (the only allowed Span; build.sh checks).

#### 2.5.2 NetworkBridgeData64 read / write API [VERIFIED]
- `Assets/#User/Community Modules/Plugins/SomniumSpace/Networking/NetworkBridgeData64.cs`: `[Networked, Capacity(64)] NetworkArray<int>`,
  `IntArray`, `IntCount => 64` ("256 bytes"). Base `SomniumSpace.Network.Bridge.NetworkBridgeData` (a NetworkBehaviour).
- Use exactly HaloFusionTransport's calls: `data.Object.IsValid`, `data.Object.HasStateAuthority`, `data.IntArray.Get(i)/Set(i,v)/Length`,
  `data.IntCount`, `data.Object.RequestStateAuthority()` (bool in 2.1), `Runner.LocalPlayer.PlayerId`, `Runner.ActivePlayers`.
- Write only changed ints (Fusion sends changed ints only). Writes ~10 Hz (`WriteEvery 0.1`, never closer than `MinWriteGap 0.07`;
  Fusion delivers ~16 Hz).

#### 2.5.3 Record header (keep for AC) [VERIFIED from HaloNetWire.cs lines 185-300]
```
int 0  magic ('HLO1' = 0x484C4F31; 0 = never written)   -> pick a new one, e.g. 'ACP1'
int 1  seq (+1 per write)        int 2  stamp: writer clock ms
int 3  owner PlayerId+1 (bits 0-15) | claim generation (16-23) | protocol (24-27)
int 4  flags: status (bits 0-1: Free 0 / Present 1 / Playing 2) | ... | instance-master bit (bit 7)
```
Helpers to keep (pure C#, no Unity): `OwnerOf`, `GenerationOf`, `StatusOf`, `WriteFree(into, stampMs)` (keeps generation),
`WriteClaim(into, localId, generation, stampMs)`, `Unit8`, `Mm/FromMm`, `PackCm2/UnpackCm2` (3 x 21-bit signed cm in two ints, +-10.48 km),
`SetCm/GetCm`, `PackOffset`, `PackQuat/UnpackQuat` (one int), `PackDir`, `PackVel`, `Yaw16`, `Set` (returns changed), `Clamp`,
`NameHash` (FNV-1a lower-case), structs `HaloV3`, `HaloQ` (Lerp, Nlerp, AngleDeg). `MaxSlots 50`, `Ints 64`, `NoSlot 63`.
[BELIEVED] PackCm2's +-10.48 km range covers AC tracks (Nordschleife is ~20 km long but its extent is smaller; check the track bounds
in the format spec).

#### 2.5.4 Instance master + hand-over [VERIFIED by reading HaloNet.Master.cs lines 1-190]
- Upload server Fusion has **no `NetworkRunner.GetMasterClient`**. Rule: every client writes its own `IsSharedModeMasterClient` as flag bit 7;
  master = 1) me if my runner says so and I hold a record; 2) else owner of the first listed record with the bit; 3) else the Dreamport
  player with the LOWEST PlayerId (Fusion ids grow with join order). Offline: me.
- `IsInstanceMaster`, `MasterPending` (online, master not known yet: grey out master controls), `MasterId`.
- `StartGame(zoneHash, startUtc)`: master writes map hash + running number (above any seen, so it grows across hand-over) + UTC start into its
  own record; everyone reads the master's record (`Game {On, ZoneHash, Seq, Start, MasterId}`) and joins once; late joiners find it.
- `TakeOverAsMaster()`: a new master keeps commanding the running game and keeps the lobby it read (lobby phase 3 = running stays running).
- Lobby: `LobbyOpen/Close/Start(seconds)/Cancel/Step`, `PublishRules(a,b,c)`, `LobbyCountdownSeconds = 5`.
- Shared clock: `HaloNet.UtcClock = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0` (race start lights can use UTC seconds).
- Host state hand-over (campaign): HaloNetHostState + HaloNetStream through "Halo Net Host 0..3" (every 15 s + checkpoints).
  [BELIEVED] For racing, a race-control block written by the master (grid, lap count, start UTC, finish order) is enough; any client can
  take it over because everything a late joiner needs is in that block plus each player's own record (laps, lap times are owner-written).

---

## 3. asmdef files [VERIFIED contents]

`Assets/#User/SomniumHalo/SomniumHalo.asmdef` (meta guid `73c53c1f2acefb650a3b65d882ecda8d`) -> `SomniumAssetto.asmdef`:
```json
{
    "name": "SomniumAssetto",
    "rootNamespace": "SomniumSpace.Worlds.SomniumAssetto",
    "references": [
        "Unity.TextMeshPro",
        "UnityEngine.UI",
        "Unity.XR.Interaction.Toolkit",
        "SomniumSpace.Network",
        "Fusion.Unity",
        "Unity.RenderPipelines.Universal.Runtime",
        "Unity.RenderPipelines.Core.Runtime"
    ],
    "includePlatforms": [], "excludePlatforms": [], "allowUnsafeCode": false, "overrideReferences": false,
    "precompiledReferences": [], "autoReferenced": true, "defineConstraints": [], "versionDefines": [], "noEngineReferences": false
}
```
(The Bridge DLL `Assets/Plugins/SomniumSpace/Bridge/SomniumSpace.Bridge.dll` is auto-referenced. Never add the CommunityModules assembly.)

`Editor/SomniumHalo.Editor.asmdef` (guid `ba9dbcd894548a2efe4722398e650129`) -> name `SomniumAssetto.Editor`, rootNamespace
`SomniumSpace.Worlds.SomniumAssetto.Editor`, references `["SomniumAssetto", "Unity.TextMeshPro", "UnityEngine.UI",
"Unity.XR.Interaction.Toolkit", "SomniumSpace.Network", "Fusion.Unity", "Unity.RenderPipelines.Universal.Runtime",
"Unity.RenderPipelines.Core.Runtime"]`, `includePlatforms: ["Editor"]`, rest as above.

`Editor/LabStubs/SomniumHalo.LabStubs.asmdef` -> `SomniumAssetto.LabStubs`, rootNamespace `SomniumSpace.Worlds.SomniumAssetto.LabStubs`,
references `[]`, `autoReferenced: false`, `defineConstraints: ["UNITY_EDITOR"]` (runtime-kind so AddComponent works in the lab).

`Assets/#User/Community Modules/Scripts/SomniumHalo.CommunityModules.asmdef` -> `SomniumAssetto.CommunityModules` (6 GUID references,
keep them). `NamespaceHelper.asset` there points at this asmdef by guid (`ASMDEFAsset` fileID 5897886265953266890, guid 0eac857c...).

---

## 4. The scene `Assets/Halo Scene.unity` [VERIFIED by parsing the YAML]

6072 lines, 549 610 bytes. Rename to e.g. `Assets/Assetto Scene.unity` (+ .meta). `EditorBuildSettings.m_Scenes` is empty (nothing
references the scene guid). RenderSettings: `m_Fog: 1`, `m_FogMode: 1` (Linear, 150-1000) - keep fog ON + Linear (guide 7).

### 4.1 Objects (SceneRoots order: 100001, 300001, 400001, 93833483, 1205748436, 4400000002)

| Object | GO fileID | Components |
|---|---|---|
| Uploader | 100000 (Transform 100001) | WorldSDKConfig MonoBehaviour &100002 (script fileID -553830836, guid 5a46fca9cfe00e84b83457af8d644413), WorldConfig &100003 (fileID 252206447, guid ca5d94d337ec5644a94dfc0f52bb6771: SDK 3.1.9, Unity 6000.3.5f2, `SpawnPointPos {0, 3000.9, 0}`, `RespawnHeightY -1000`, Subcategory 1) |
| - Directional Light (child of Uploader) | 200000 | Light (type 1, soft shadows) + URP additional light data; local pos (0,3,0) |
| Halo Game | 300000 (Transform 300001, at origin) | HaloGame &300002 (script guid `5019c37a5ca87a44930bcba7891261dd` = `Game/HaloGame.cs.meta`): `_shaderCarriers` (6), `_installHint` |
| Spawn Floor (fallback collider) | 400000 | BoxCollider 24 x 1 x 24 at (0, 2999.5, 0) |
| PostProcessing | 93833481 | Volume (global) -> `SomniumSpaceTemplates/SimpleWorldTemplate/Light/PostProcessing Profile.asset` |
| Somnium Rules | 1205748433 | two Somnium rule components (guid 6af85501506cb7f4db5388060eadf518): `_disableGlide: 1`, `_disableFlyMode: 1` |
| Halo Net | 4400000001 (Transform 4400000002) | parent only; children = Scene Networking + 69 blocks |
| - Scene Networking | 4400000100 / T 4400000101 / MB 4400000102 | SceneNetworking (script guid `9af48822bf30f5d4fbbd3f9e6f4df9d9` = `Game/Net/SceneNetworking.cs.meta`) |
| - Halo Net Player 0..49 | 4400000200 + 10k | Transform +1, NetworkObject +2, NetworkBridgeData64 +3 |
| - Halo Net World 0..3 | 4400000800 + 10b | same layout |
| - Halo Net Actor 0..8 | 4400000900 + 10a | same layout (campaign) |
| - Halo Net Mission 0..1 | 4400001000 + 10m | same layout (campaign) |
| - Halo Net Host 0..3 | 4400001100 + 10h | same layout (hand-over stream) |

One block, as YAML (slot 1) [VERIFIED]:
```yaml
--- !u!1 &4400000210          # GameObject: m_Component 4400000211, 4400000212, 4400000213; m_Layer 0; m_Name: Halo Net Player 1; m_IsActive 1
--- !u!4 &4400000211          # Transform: identity, m_Father: {fileID: 4400000002}
--- !u!114 &4400000212        # NetworkObject
  m_Script: {fileID: -1552182283, guid: e725a070cec140c4caffb81624c8c787, type: 3}   # Photon/Fusion/Assemblies/Fusion.Runtime.dll
  SortKey: 396116010
  SendPriority: 1
  EnableInterpolation: 1
  Flags: 524290
  NestedObjects: []
  NetworkedBehaviours:
  - {fileID: 4400000213}
  ForceRemoteRenderTimeframe: 0
--- !u!114 &4400000213        # NetworkBridgeData64
  m_Script: {fileID: 11500000, guid: 0b8147e129a49fd49ba7bdc9dc75daaa, type: 3}     # Community Modules/.../NetworkBridgeData64.cs
```
SortKeys (unique uint32 per object; Player 0 = 1206490484, Player 1 = 396116010, Player 2 = 3880708832, ..., World 0 = 1304574431,
Actor 0 = 1790764283, Mission 0 = 2276954135, Host 0 = 1823458932). [VERIFIED] They are NOT a linear function of the fileID across
the scene (steps of -810374474 hold only for runs of a few objects). Fusion's editor bakes SortKey and NetworkedBehaviours when the
scene is saved (ETS `Editor/EtsNetSceneSetup.cs` notes this; Halo's `Editor/HaloSceneResave.cs` re-saves in batch mode). So: when
adding blocks by YAML, give new unique fileIDs and unique SortKeys, then run the resave method once.

[BELIEVED] AC blocks: keep "Player 0..49" + "World 0..3" (race control); delete Actor/Mission/Host (and their 15 entries in the
"Halo Net" Transform `m_Children`) unless the hand-over stream is wanted. Rename every `m_Name` "Halo Net ..." -> the new prefix and
make the code prefixes match (`AcNetWire.ObjectPrefix`, `AcNetWorld.ObjectPrefix`, `AcIsolation.OwnNetRoot`, the TabletLink skip prefix).
A `NetworkBridgeEvents` block (RPC messages, proven, see 6) would need a new object (NetworkObject + NetworkBridgeEvents); ETS has
"Ets Net Events".

### 4.2 Script references to keep valid
- HaloGame component: `m_Script guid 5019c37a5ca87a44930bcba7891261dd` -> keep this guid on `AcGame.cs.meta` (move the meta), or edit the scene.
- SceneNetworking: `guid 9af48822bf30f5d4fbbd3f9e6f4df9d9` -> same.
- The class name must equal the file name (`AcGame` in `AcGame.cs`); the serialized field names `_shaderCarriers`, `_installHint` should
  stay (or the scene values are lost).

### 4.3 Uploader fields to sanitise (`--- !u!114 &100002`, lines 171-210 + 1047-1048) [VERIFIED values]
| Field (line) | Current | Set to |
|---|---|---|
| `AssetName` (171) | `Player Capacity` | Assetto Corsa Dreamport name |
| `AssetDescription` (172-173) | "This is a simulation of 1000 players per instance, for testing only." | own text |
| `PreviewTexture` (175) | `{fileID: 2800000, guid: b8f3f6f987b8ee24fafcef540830cc60}` (#User/Preview Pictures/PreviewPicture_2026-10-19_10-23-15.jpg) | `{fileID: 0}` |
| `Preview360Texture` (184) | `{fileID: 8900000, guid: e90daba7d1d92f8499ad9ffcd84e9d4d}` (Preview360_2026-10-19_10-23-18.jpg) | `{fileID: 0}` |
| `Preview360ImageData` (1047) | 377 329-char JPEG hex blob | empty |
| `LastBuildSizeBytes` (176) | 2517569 | 0 (cosmetic) |
| `WorldIdForUpload` (189) | **13499** | **0** |
| `DoReplaceExistingWorld` (190) | **1** | **0** |
| `SelectedIndexForWorldToReplace` (209) | 20 (= world 13499) | 0 [BELIEVED: avoids pre-selecting Halo if replace is ever ticked] |
| `ScriptingAssembly` (194) | `SomniumHalo` | `SomniumAssetto` |
| `CheckedScriptingAssembly` (1048) | `SomniumHalo` | `SomniumAssetto` |
| `AsmdefReference` (197-198) | `{fileID: 5897886265953266890, guid: 73c53c1f2acefb650a3b65d882ecda8d, type: 3}` | same fileID, the SomniumAssetto.asmdef guid (keep 73c5... if the meta is moved, or a fresh guid [BELIEVED cleaner]) |
| `UploadedWorlds` (210-1046) | cached account world list (incl. 13499 "Player Capacity", AssemblyName SomniumHalo) | harmless cache, leave |

### 4.4 `_shaderCarriers` on the root (6 entries) [VERIFIED]
`d9e314d4...` Resources/HaloEnv.mat, `90e07cfa...` Resources/HaloModel.mat, `25c68d4b...` Resources/HaloTransparent.mat,
`ea249cb9...` Resources/HaloGlass.mat, `df210229...` Shaders/Fx/HaloFx.mat, `dc51a8e7...` Resources/HaloSprite.mat.
All Halo shaders -> empty the list (`_shaderCarriers: []`) at the port; the AC World module adds its own carriers (only materials
referenced by the scene reach the upload; `Shader.Find` finds only client shaders).

---

## 5. Racing-relevant notes on the core

5.1 [VERIFIED] Isolation rule for AC is unchanged: "with me" = holds a written AC player slot (`AcGear.HasRecord` -> `AcNetGame.TryHasRecord`).
A visitor never claims one. This requires the net layer to start for whitelisted players only (it does: NetStart from StartDreamport).

5.2 [VERIFIED] Seat lock Bridge calls are proven (2.2, HaloVehicleSeat). Car rule (guide 7): a vehicle the avatar rides must exclude
layers 2|7|24 from its colliders; VR seats turn by heading only.

5.3 [VERIFIED from `E:\Somnium Space\Main Build\Assets\_SomniumSpace\Inputs\2D\2D Input Actions.inputactions`] Map `2DPlayer` actions:
Look, Exit, Move, Rotate, Tablet, Microphone, First Camera Action, Second Camera Action, Zoom, Jump, Interact, Camera Preset, PopUpMenu,
Inspector, MuteObject, HideObject, Sprint, Gamepad FreeView Enable, Gamepad Mouse Enable/Move/Click Button/Click, Gamepad Zoom,
Gamepad Scroll, Gamepad Move, Gamepad Look. AcFirstPerson's `BlockedActions` can add "Move", "Jump", "Sprint", "Gamepad Move" while
driving on a monitor (WASD/arrows would otherwise also drive the avatar) [BELIEVED; Bridge SetMovementSpeed(0) already freezes walking].

5.4 [VERIFIED] HaloNetTimeline already interpolates a driven vehicle (`InVehicle`, `VehiclePos/Vel/Rot`, respawn flag) - the base for
remote cars at ~10 Hz drawn 0.16 s behind.

---

## 6. Proven Fusion / Bridge API a racing game will need [VERIFIED against DevTools/fusionapi/known_good.txt, 297 lines]

Fusion:
- State authority: `NetworkObject::RequestStateAuthority`, `NetworkObject::get_HasStateAuthority`, `NetworkObject::get_StateAuthority`
  (PlayerRef of the owner), `NetworkObject::get_IsValid`, `NetworkObject::Flags`, `NetworkObject::SortKey`, `NetworkBehaviour::get_HasStateAuthority`,
  `SimulationBehaviour::get_Object`.
- Ints: `NetworkArray<T>::Get/Set/get_Length`; `SomniumSpace.Network.Bridge.NetworkBridgeData::get_IntArray / get_IntCount`.
- Players: `NetworkRunner::get_LocalPlayer`, `get_ActivePlayers`, `get_IsRunning`, `get_Instances`, `get_IsSharedModeMasterClient`,
  `AddCallbacks`, `RegisterSceneObjects`, `StartGame`; `PlayerRef::get_PlayerId / get_IsNone / get_IsRealPlayer / get_None / op_Equality /
  op_Inequality`; `SceneRef::FromPath / get_IsValid`.
- Messages: `NetworkBridgeEvents::RPC_SendMessageToAll / ToPlayer / ToController / ToProxies` + `add_/remove_OnMessageTo*`,
  `add_OnSpawned`, `add_OnStateAuthorityChanged`; `NetworkBridge::RPC_SendMessageToAll`, `get_SyncByteArray` (need scene objects).
- Physics add-on type `Fusion.Addons.Physics.NetworkRigidbody3D` (type only).
- NOT proven (do not use without a test upload): `NetworkRunner.GetMasterClient`, `NetworkObject.ReleaseStateAuthority`,
  `NetworkRunner.SimulationTime`/tick members, `SessionInfo` members (Halo reads them by reflection only, for a log line).
  Master detection = the record-bit rule (2.5.4). PlayerId == Bridge `Properties.PhotonId`.

Somnium Bridge (all proven): `SomniumBridge.get_PlayersContainer / get_Environment / get_LocalStorage`; `ISomniumPlayersContainer.LocalPlayer /
AllPlayers / OnPlayerAdded (add/remove)`; `ISomniumPlayer.Properties / References / Features`; `Properties.Id / NickName / PhotonId / IsLocal`;
`References.Body` -> `Root / Head / LeftHand / RightHand` (+ `Neck` in the new-format list); `Features.Motion.DoTeleportToPoint /
SetMovementSpeed / SetGravity / SetJump / SetCollisionDisableState / SetFlyModeDisableState / SetTeleportationDisableState`;
`Features.Poses.SetPose(PoseType)`; `Features.Avatar.SetVRIKLegMovementDisabled`; `Features.Animation.SetFallingAnimationDisabled`;
`Features.Info.SetPlayerTagDisableState`; `ISomniumEnvironment.SetSkybox / SetSunSource / UpdateEnvironment`;
`ISomniumWorldsLocalStorage.ReadText / WriteText / TextExists` (e.g. personal best laps); `SomniumTriggerActionArgs` (.ctor, IsLocal, IsPlayer).

---

## 7. Lab runner `Editor/HaloLab.cs` API [VERIFIED by reading]

- `[InitializeOnLoad] HaloLabBoot`: active only with `-halolab <scenario>`; opens `"Assets/Halo Scene.unity"`, enters play mode, adds
  GameObject "Halo Lab" with `HaloLabRunner`. `Arg(name, fallback)` reads command-line args.
- `HaloLabRunner : MonoBehaviour`: `static Register(string name, Func<HaloLabRunner, IEnumerator> run)`; props `Cam` (lab camera, near
  0.05, far 5000, fov 70, disabled), `Out` (`-halout`, default `D:\Dreamport\Halo\Halo Dreamport\DevTools\lab`), `Scenario`, `Failed`,
  `Game` (= HaloGame.Current); `static Log(s)` ("LAB: "), `Fail(why)`, `Check(ok, what)`, `Wait(cond, timeout, what)`, `Frames(n)`,
  `Aim(eye, lookAt, fov)`, `Shot(name, w=960, h=540)` (PNG + average colour); `Guarded` steps nested IEnumerators, timeout
  `-halotimeout` (default 900 s), exits Unity with 0/1.
- `Main()` waits up to 120 s for `HaloGame.Current.Install != null` ("boot"), then runs the scenario.
- AC edits: scene path, arg names (e.g. `-aclab`, `-acout`, `-actimeout`; lab.sh must pass the same), default Out path, `AcGame`,
  "Ac Lab" object name. Scenario files `Editor/AcLab<Module>.cs` with the same Register pattern.

---

## 8. DevTools scripts with hard-coded Halo paths [VERIFIED by grep]

| File | Lines with "Halo" | What to change |
|---|---|---|
| build/build.sh | 2, 5, 6, 24, 29 | `B="D:/Dreamport/Assetta Corsa/DevTools/build"`, `SRC=".../Assets/#User/SomniumAssetto"`, the two `sed 's\|.*SomniumHalo/\|\|'` |
| build/runtime.head, runtime_server.head | 24, 29-37 (10 each) | `-r:"D:/Dreamport/Halo/Halo Dreamport/..."` -> `D:/Dreamport/Assetta Corsa/...`: `Assets/Plugins/SomniumSpace/Bridge/SomniumSpace.Bridge.dll`, `Assets/Photon/Fusion/Assemblies/Fusion.{Runtime,Common,Sockets,Log,Realtime}.dll`, `Library/ScriptAssemblies/{Fusion.Unity,SomniumSpace.Network,Photon.Realtime}.dll`, `Assets/Photon/PhotonLibs/netstandard2.0/release/PhotonClient.dll` (all exist in the copy). UI/TMP/XRI/URP come from the ETS project's Library (lines 22-23, 27, 53-54) - may stay or point to this project's Library (present). The server head = runtime head minus `UNITY_EDITOR` define and minus UnityEditor.CoreModule |
| build/editortool.head | 23, 28-36 | same paths |
| fusionapi/check.sh | 4 | `D=".../Assetta Corsa/DevTools/fusionapi"` (writes current.txt / known_good.sorted there) |
| metas.sh | 4 | default ROOT -> `.../Assets/#User/SomniumAssetto` (comment says "Metroid": stale) |
| lab.sh | 2, 4, 8, 11, 25, 26, 32 | `D=`, mainopen path, project paths (`P=`), `-halolab/-halout` arg names, OUTW, `grep "SomniumHalo\]"` |
| labcopy.sh | 5, 7, 9, 12 | `D=`, `.copied` path, the scene file name `Halo Scene.unity` |
| mainopen.ps1 | 1, 2 | `-match 'Halo Dreamport'` -> `'Assetta Corsa'` (else it reports the Halo editor as "main open") |
| harness/HaloHarness.csproj | 12, 13 | rename to AcHarness.csproj, `DefineConstants AC_HARNESS` (HALO_HARNESS is used by no code), `Compile Include` the new Data/** + Core/AcLog.cs |
| harness/Probe*.cs | all | Halo probes: delete; keep `Program.cs` (dispatch), `Png.cs` (PNG writer) |
| AGENTS.md | whole | rewrite for AC |

Stale Halo material in DevTools [VERIFIED sizes]: `labproject/` 2.9 GB (a full Halo project copy with Library and `.copied`, so labcopy.sh
would only mirror `Assets/#User` into a stale project: delete it or remove `.copied`), `lab/` 662 MB of Halo pictures, `agents/` 605 MB
(Halo module notes A..X, REQUESTS.md), `reference/` (Halo tag layouts, invader-defs, riat), `logs/`, `work/`, `lab_all_summary.txt`,
`build/outM`, `build/outM3`. Deleting is the user's / lead's call.

---

## 9. Suggested port order [BELIEVED]

1. Rename CommunityModules asmdef; create `Assets/#User/SomniumAssetto` with the asmdefs (section 3).
2. Copy the 23 core files with the sed (section 1) + string edits; add `Data/Audio/Vorbis`; write `AcSpace` (RoomOrigin), `AcInstall`
   (Steam paths, `content/cars`, `content/tracks`), `AcNetGame.TryHasRecord`.
3. Scene: rename file + objects, keep the two script guids, sanitise the uploader (4.3), empty `_shaderCarriers`, trim blocks, resave.
4. Net: AcNetWire (header as 2.5.3 + car payload), AcSlotClaim (V), AcFusionTransport (A), AcNet (trimmed), master rule.
5. Front: AcTabletLink + app page; settings rows; loading screen with the track's `ui/preview.png`.
6. DevTools path edits; compile x3 + API guard before every upload.

---

## Critic addendum (completeness critic, 2026-10-31)

1. **Correction (section 0 / 2.2 Data/Audio/Vorbis):** AC banks are **FSB5 PCM16 (mode 2), not Vorbis** - verified on 86/86 car banks and
   common.bank by `ac_physics_sound_rules.md` 2.3 and by `ets_vehicles.md` 10.2. The vendored NVorbis is dead weight for AC; leave it out
   (fewer files for the upload server) unless a mod car with a Vorbis bank turns up (then decode codec 15 like ETS).
2. **`AcInstall.Find` - the stub in 2.1 needs a real finder; port ETS's `Ets/EtsInstall.cs`** (libraryfolders.vdf walk, lines 128-300;
   README l.3611) or Skyrim's `Data/SkyInstall.cs` (also Proton drives). AC facts [VERIFIED on this PC]: Steam app id **244210**, install dir
   name **`assettocorsa`**, manifest `<library>/steamapps/appmanifest_244210.acf` (`"installdir" "assettocorsa"`). This PC:
   `C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf` lists libraries `C:\Program Files (x86)\Steam`, `E:\SteamLibrary` (whose
   `apps` block contains `"244210"`) and `D:\SteamLibrary`. Validate a candidate root by `content/cars`, `content/tracks`,
   `content/sfx/GUIDs.txt` and `system/data/surfaces.ini` existing. Keep the tablet's install-folder override (`AcSettings.InstallOverride`).
   `AcGame.FinishBoot` should log "found N cars (M DLC stubs), K track layouts" instead of `Multiplayer.Count`.
3. Track world placement: AC tracks span up to ~2 km from their origin (monza AI grid min (-562,-1628) max (1397,1238)); with the hub at
   RoomOrigin y=3000 keep the track at its own origin (or a MapOrigin offset) - PackCm2's +-10.48 km is ample [VERIFIED numbers from the
   track spec; placement = design].
