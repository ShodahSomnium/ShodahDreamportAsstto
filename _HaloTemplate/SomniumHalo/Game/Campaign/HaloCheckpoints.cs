using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumHalo
{
    /// <summary>
    /// [Mission module, K] Halo's checkpoints: game_save takes a snapshot of what matters, a death (or game_revert) puts it back.
    /// K's own parts: the active BSP and game time, every scenario placement's state (built / destroyed / not yet) with where movable ones
    /// (vehicles, lying weapons and equipment) were, the devices (positions, power, groups) and the local player (where, facing, weapons with
    /// rounds, grenades, body and shield). Other modules add theirs with <see cref="Register"/> (Script: globals and threads, AI: encounters,
    /// Characters: units) - capture returns a state object of theirs, restore gets it back after K's parts are in place.
    /// Player.log: "Campaign: checkpoint ..." / "Campaign: revert ..." lines.
    /// </summary>
    public static class HaloCheckpoints
    {
        sealed class Part { public string Key; public Func<object> Capture; public Action<object> Restore; }

        sealed class Item { public int Index; public HaloObjects.State State; public bool Moved; public Vector3 Position; public Quaternion Rotation; }

        sealed class Gun { public HaloTag Tag; public int Loaded, Reserve; public float Charge; }

        sealed class Snapshot
        {
            public string Label = "";
            public int Ticks, Bsp, Number;
            public long CountdownMs; public bool CountdownRunning;
            public float RealTime;
            public readonly List<Item> Objects = new List<Item>();
            public HaloDevices.Snapshot Devices;
            public Vector3 Feet; public float Yaw;
            public readonly Gun[] Guns = new Gun[2];
            public int Held, Frag, Plasma, GrenadeSelected;
            public float Body, Shield;
            public readonly List<KeyValuePair<string, object>> Parts = new List<KeyValuePair<string, object>>();
        }

        static readonly List<Part> Parts = new List<Part>();
        static Snapshot _last;
        static int _number;
        static Coroutine _restoring;

        /// <summary>A checkpoint exists.</summary>
        public static bool HasSave { get { return _last != null; } }
        /// <summary>What took the last checkpoint ("mission start", "game_save", ...), "" when none.</summary>
        public static string Label { get { return _last != null ? _last.Label : ""; } }
        /// <summary>Checkpoints taken this mission (the mission start counts).</summary>
        public static int Taken { get { return _number; } }
        public static int Reverts { get; private set; }
        /// <summary>A revert is being applied (it may wait for a BSP switch).</summary>
        public static bool Restoring { get { return _restoring != null; } }

        /// <summary>A module's part of the checkpoint: <paramref name="capture"/> on every save, <paramref name="restore"/>(its state) on a revert
        /// (after K's objects, devices and player are back). Same key again replaces it.</summary>
        public static void Register(string key, Func<object> capture, Action<object> restore)
        {
            if (string.IsNullOrEmpty(key) || capture == null || restore == null) return;
            Unregister(key);
            Parts.Add(new Part { Key = key, Capture = capture, Restore = restore });
            HaloLog.Info("Campaign: checkpoint part '" + key + "' registered (" + Parts.Count + " parts)");
        }

        public static void Unregister(string key)
        {
            for (int i = Parts.Count - 1; i >= 0; i--) if (Parts[i].Key == key) Parts.RemoveAt(i);
        }

        /// <summary>Mission leave: the snapshot and the registrations go.</summary>
        public static void Clear()
        {
            if (_restoring != null && HaloGame.Current != null) { try { HaloGame.Current.StopCoroutine(_restoring); } catch (Exception) { } }
            _restoring = null;
            _last = null;
            _number = 0;
            Reverts = 0;
            Parts.Clear();
        }

        // ------------------------------------------------------------------ save

        /// <summary>Takes a checkpoint now.</summary>
        internal static void Take(string label)
        {
            var s = new Snapshot { Label = label ?? "", Ticks = HaloCampaign.Ticks, Bsp = HaloCampaign.BspIndex, RealTime = Time.realtimeSinceStartup, Number = _number + 1 };
            HaloCampaign.CountdownState(out s.CountdownMs, out s.CountdownRunning);
            int moved = 0;
            int n = HaloObjects.PlacementCount;
            for (int i = 0; i < n; i++)
            {
                var st = HaloObjects.StateOf(i);
                if (st == HaloObjects.State.NotYet) continue;
                var it = new Item { Index = i, State = st };
                var g = HaloObjects.LiveAt(i);
                if (g != null && Movable(g)) { it.Moved = true; it.Position = g.transform.position; it.Rotation = g.transform.rotation; moved++; }
                s.Objects.Add(it);
            }
            s.Devices = HaloDevices.Capture();
            CapturePlayer(s);
            for (int i = 0; i < Parts.Count; i++)
            {
                object state = null;
                try { state = Parts[i].Capture(); }
                catch (Exception e) { HaloLog.Error("Campaign: checkpoint part '" + Parts[i].Key + "' capture failed: " + e); }
                s.Parts.Add(new KeyValuePair<string, object>(Parts[i].Key, state));
            }
            _last = s;
            _number++;
            HaloLog.Info("Campaign: checkpoint " + _number + " (" + s.Label + ") at " + (s.Ticks / 30f).ToString("0.0") + " s game time, BSP " + s.Bsp + ": " + s.Objects.Count + " placements (" + moved + " movable), "
                         + (s.Devices != null ? s.Devices.Devices.Count : 0) + " devices, player at " + s.Feet.ToString("0.0") + " with " + GunsText(s) + ", " + s.Parts.Count + " module parts");
            if (_number > 1) HaloGame.Guard("checkpoint message", () => HaloEvents.RaiseMessage("Checkpoint... done", 2f));
            HaloCampaign.RaiseCheckpoint();
        }

        /// <summary>Things that move on their own or are carried: vehicles and items (bipeds are the Characters module's part).</summary>
        static bool Movable(GameObject g)
        {
            var mo = g.GetComponent<HaloMissionObject>();
            if (mo == null || mo.Placement == null) return false;
            var k = mo.Placement.Kind;
            return k == HaloMissionData.Kind.Vehicle || k == HaloMissionData.Kind.Weapon || k == HaloMissionData.Kind.Equipment;
        }

        static void CapturePlayer(Snapshot s)
        {
            Vector3 feet;
            s.Feet = HaloPlayer.TryFeet(out feet) ? feet : HaloLocal.Position;
            float yaw; string src;
            Vector3 sp; float sy;
            if (HaloCampaign.RecentSpawn(out sp, out sy)) { s.Feet = sp; s.Yaw = sy; }
            else s.Yaw = HaloPlayer.TryFacingYaw(out yaw, out src) ? yaw : Quaternion.LookRotation(HaloLocal.Forward.sqrMagnitude > 0.01f ? HaloLocal.Forward : Vector3.forward).eulerAngles.y;
            for (int i = 0; i < 2; i++)
            {
                var w = HaloLocal.Weapons[i];
                s.Guns[i] = w != null && w.Tag != null ? new Gun { Tag = w.Tag, Loaded = w.Loaded, Reserve = w.Reserve, Charge = w.Charge } : null;
            }
            var h = HaloCombat.Handling;
            if (h != null)
                for (int i = 0; i < 2; i++)
                {
                    var g = h.Guns[i];
                    if (g != null && g.State != null && s.Guns[i] != null) { s.Guns[i].Loaded = g.State.Loaded; s.Guns[i].Reserve = g.State.Reserve; s.Guns[i].Charge = g.State.Charge; }
                }
            s.Held = HaloLocal.Held;
            s.Frag = HaloLocal.FragGrenades; s.Plasma = HaloLocal.PlasmaGrenades; s.GrenadeSelected = HaloLocal.GrenadeSelected;
            s.Body = HaloVitals.Body; s.Shield = HaloVitals.ShieldPoints;
        }

        static string GunsText(Snapshot s)
        {
            string t = "";
            for (int i = 0; i < 2; i++) if (s.Guns[i] != null) t += (t.Length > 0 ? " + " : "") + System.IO.Path.GetFileName(s.Guns[i].Tag.Name) + " " + s.Guns[i].Loaded + "/" + s.Guns[i].Reserve;
            return (t.Length > 0 ? t : "no weapon") + ", " + s.Frag + "/" + s.Plasma + " grenades";
        }

        // ------------------------------------------------------------------ revert

        /// <summary>Back to the last checkpoint (the mission start when none was taken).</summary>
        internal static void Restore(string why)
        {
            var s = _last;
            if (s == null) { HaloLog.Warn("Campaign: revert (" + why + ") without a checkpoint - nothing to go back to"); Respawn(why); return; }
            Reverts++;
            HaloLog.Info("Campaign: revert " + Reverts + " (" + why + ") to checkpoint " + s.Number + " (" + s.Label + ", " + (s.Ticks / 30f).ToString("0.0") + " s game time, BSP " + s.Bsp + ")");
            HaloGame.Guard("cinematic clear", () => HaloCinematic.Clear());
            HaloGame.Guard("vehicles exit", () => HaloVehicles.ExitSeat());
            if (s.Bsp != HaloCampaign.BspIndex && HaloGame.Current != null)
            {
                HaloCampaign.SwitchBsp(s.Bsp);
                if (_restoring != null) { try { HaloGame.Current.StopCoroutine(_restoring); } catch (Exception) { } }
                _restoring = HaloGame.Current.StartCoroutine(AfterSwitch(s));
                return;
            }
            Apply(s);
        }

        static IEnumerator AfterSwitch(Snapshot s)
        {
            float until = Time.realtimeSinceStartup + 60f;
            while (HaloCampaign.SwitchingBsp && Time.realtimeSinceStartup < until) yield return null;
            _restoring = null;
            if (!HaloCampaign.Active || _last != s) yield break;
            Apply(s);
        }

        static void Apply(Snapshot s)
        {
            // placements: built / destroyed / never built as they were, movable ones where they were
            var saved = new Dictionary<int, Item>();
            for (int i = 0; i < s.Objects.Count; i++) saved[s.Objects[i].Index] = s.Objects[i];
            int n = HaloObjects.PlacementCount, made = 0, gone = 0;
            for (int i = 0; i < n; i++)
            {
                Item it;
                var now = HaloObjects.StateOf(i);
                HaloObjects.State want = saved.TryGetValue(i, out it) ? it.State : HaloObjects.State.NotYet;
                if (want == HaloObjects.State.NotYet && now == HaloObjects.State.Live && HaloObjects.LiveAt(i) != null)
                {
                    // built after the checkpoint (a BSP visited later, or object_create): gone again
                    HaloObjects.SetState(i, HaloObjects.State.NotYet); gone++;
                    continue;
                }
                if (want == HaloObjects.State.Live && HaloObjects.LiveAt(i) != null && InHands(HaloObjects.LiveAt(i)))
                {
                    // picked up after the checkpoint: the player's weapons are put back below; the placement lies there again
                    HaloObjects.SetState(i, HaloObjects.State.NotYet);
                    now = HaloObjects.State.NotYet;
                }
                if (want != now || (want == HaloObjects.State.Live && HaloObjects.LiveAt(i) == null))
                {
                    var g = HaloObjects.SetState(i, want);
                    if (want == HaloObjects.State.Live && g != null) made++;
                    else if (want != HaloObjects.State.Live) gone++;
                }
                if (it != null && it.Moved)
                {
                    var g = HaloObjects.LiveAt(i);
                    if (g != null && !InHands(g))
                    {
                        var rb = g.GetComponent<Rigidbody>();
                        if (rb != null && !rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                        g.transform.SetPositionAndRotation(it.Position, it.Rotation);
                    }
                }
            }
            HaloDevices.Restore(s.Devices);
            Physics.SyncTransforms();
            HaloCampaign.SetTicks(s.Ticks);
            HaloCampaign.SetCountdownState(s.CountdownMs, s.CountdownRunning);
            HaloCampaign.Speed = 1f;
            RestorePlayer(s);
            for (int i = 0; i < s.Parts.Count; i++)
            {
                Part p = null;
                for (int k = 0; k < Parts.Count; k++) if (Parts[k].Key == s.Parts[i].Key) p = Parts[k];
                if (p == null) continue;
                try { p.Restore(s.Parts[i].Value); }
                catch (Exception e) { HaloLog.Error("Campaign: checkpoint part '" + p.Key + "' restore failed: " + e); }
            }
            HaloLog.Info("Campaign: reverted to checkpoint " + s.Number + ": " + made + " objects rebuilt, " + gone + " removed, devices " + (s.Devices != null ? s.Devices.Devices.Count : 0) + ", player at " + s.Feet.ToString("0.0")
                         + " with " + GunsText(s) + ", " + s.Parts.Count + " module parts");
        }

        static bool InHands(GameObject g)
        {
            var gun = g.GetComponent<HaloGun>();
            var h = HaloCombat.Handling;
            return gun != null && h != null && h.IndexOf(gun) >= 0;
        }

        static void RestorePlayer(Snapshot s)
        {
            string how;
            HaloCombat.NoteTeleport();
            bool moved = HaloPlayer.Teleport(s.Feet + Vector3.up * 0.02f, s.Yaw, out how);
            HaloLocal.ResetLife();
            HaloLocal.Position = s.Feet;
            HaloGame.Guard("combat spawned", () => HaloCombat.Spawned());
            var h = HaloCombat.Handling;
            if (h != null)
            {
                h.Unequip();
                for (int i = 0; i < 2; i++) if (s.Guns[i] != null) HaloCampaign.GiveWeapon(s.Guns[i].Tag, s.Guns[i].Loaded, s.Guns[i].Reserve);
                HaloLocal.Held = h.Guns[0] != null ? Mathf.Clamp(s.Held, 0, 1) : -1;
            }
            HaloLocal.FragGrenades = s.Frag; HaloLocal.PlasmaGrenades = s.Plasma; HaloLocal.GrenadeSelected = s.GrenadeSelected;
            if (s.Body > 0f) HaloVitals.Body = s.Body;
            HaloVitals.ShieldPoints = s.Shield;
            HaloLocal.Alive = true;
            HaloCampaign.PlacedAfterRevert(s.Feet, s.Yaw);
            HaloEvents.RaiseLocalSpawned(s.Feet);
            if (!moved) HaloLog.Warn("Campaign: the revert could not move the player yet (" + how + ")");
        }

        /// <summary>No checkpoint at all (cannot happen after the mission start): a fresh life where the player is.</summary>
        static void Respawn(string why)
        {
            HaloLocal.ResetLife();
            HaloGame.Guard("combat spawned", () => HaloCombat.Spawned());
            HaloLocal.Alive = true;
            Vector3 f;
            HaloCampaign.PlacedAfterRevert(HaloPlayer.TryFeet(out f) ? f : HaloLocal.Position, 0f);
        }
    }
}
