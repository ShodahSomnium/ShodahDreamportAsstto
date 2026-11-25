using System;
using System.Collections.Generic;
using UnityEngine;

namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// [N, Net] The players list (part of <see cref="AcNetGame"/>): every other Dreamport player in the instance decoded from their record into a
    /// reused <see cref="AcNetPlayer"/> (no per-frame allocation), names from the Bridge (AcRemotes) once a second, "joined" / "left" lines; and the
    /// master's AI cars as AcNetPlayer entries (<see cref="AiPlayers"/>, IsAi, id -(100 + index)).
    /// </summary>
    public static partial class AcNetGame
    {
        private static readonly AcNetPlayer[] BySlot = new AcNetPlayer[AcNetSlots.PlayerCount];
        private static readonly bool[] JoinLogged = new bool[AcNetSlots.PlayerCount];
        private static readonly List<AcNetPlayer> AiList = new List<AcNetPlayer>(AcNetBlocks.MaxAi);
        private static readonly AcNetPlayer[] AiByIndex = new AcNetPlayer[AcNetBlocks.MaxAi];
        private static float _nextNames;

        /// <summary>The master's AI cars as players (others: read from the AI blocks; the master: its own AI drivers are NOT listed here -
        /// AcAi.Drivers has them). Index order. Do not keep the list.</summary>
        public static IReadOnlyList<AcNetPlayer> AiPlayers { get { return AiList; } }

        private static void RefreshPlayers()
        {
            bool names = Time.unscaledTime >= _nextNames;
            if (names) _nextNames = Time.unscaledTime + 1f;
            // gone: every slot whose remote is no longer listed (or is another player now)
            for (int k = 0; k < BySlot.Length; k++)
            {
                AcNetPlayer p = BySlot[k];
                if (p == null) continue;
                AcNet.Remote r = Net.RemoteAt(k);
                if (r != null && r.Decoded && r.PlayerId == p.PhotonId) continue;
                BySlot[k] = null;
                PlayerList.Remove(p);
                if (JoinLogged[k]) AcLog.Info("Net: " + p.Name + " left");
                JoinLogged[k] = false;
                DropCarOf(p, "the player left");
                RaiseLeft(p);
            }

            IReadOnlyList<AcNet.Remote> listed = Net.Listed;
            for (int i = 0; i < listed.Count; i++)
            {
                AcNet.Remote r = listed[i];
                AcNetPlayer p = BySlot[r.Slot];
                if (p == null)
                {
                    p = new AcNetPlayer { Slot = r.Slot, PhotonId = r.PlayerId, Name = r.Name };
                    BySlot[r.Slot] = p;
                    r.Tag = p;
                    InsertSorted(p);
                    names = true;
                }

                if (names) ResolveName(r, p);
                if (r.Fresh || p.CarId.Length == 0) Fill(p, r);
                p.OnTrack = r.State.OnTrack && p.LayoutHash != 0 && p.LayoutHash == LayoutHash;     // (my layout may change without their record)
                if (!JoinLogged[r.Slot] && (!p.Name.StartsWith("Player ", StringComparison.Ordinal) || Net.Now - r.JoinedAt > 3.0))
                {
                    JoinLogged[r.Slot] = true;
                    AcLog.Info("Net: " + p.Name + " joined (slot " + p.Slot + ", photon id " + p.PhotonId + (p.Vr ? ", VR" : ", monitor") + ")");
                    RaiseJoined(p);
                }
            }

            int master = Net.Online ? Net.MasterId : -1;
            for (int i = 0; i < PlayerList.Count; i++) PlayerList[i].IsMaster = PlayerList[i].PhotonId == master;
        }

        private static void InsertSorted(AcNetPlayer p)
        {
            int at = 0;
            while (at < PlayerList.Count && PlayerList[at].Slot < p.Slot) at++;
            PlayerList.Insert(at, p);
        }

        private static void Fill(AcNetPlayer p, AcNet.Remote r)
        {
            AcNetState st = r.State;
            AcNetCar c = st.Car;
            p.Vr = st.Vr;
            p.Seated = st.Seated;
            p.Driving = st.Driving;
            p.Ready = st.Ready;
            p.Spectating = st.Spectating;
            p.LayoutHash = c.LayoutHash;
            p.OnTrack = st.OnTrack && c.LayoutHash != 0 && c.LayoutHash == LayoutHash;
            p.HasCar = c.Present;
            int rideIn = st.Seated && !st.Driving ? AcNetWire.TargetPlayerId(st.Rider.Target) : -1;
            p.RideInPhotonId = rideIn;
            p.PassengerPhotonId = st.PassengerId;
            p.HasPassengerSeat = c.Present && !st.NoPassengerSeat;
            p.AppliedSeq = st.AppliedSeq;
            p.LastSectorMs[0] = st.Sector0Ms;
            p.LastSectorMs[1] = st.Sector1Ms;
            p.LastSectorMs[2] = st.Sector2Ms;
            FillTiming(p, in c);
            ResolveCar(p, c.CarHash, c.SkinHash);
        }

        private static void FillTiming(AcNetPlayer p, in AcNetCar c)
        {
            p.Laps = c.Laps;
            p.LapMs = c.LapMs;
            p.LastMs = c.LastMs;
            p.BestMs = c.BestMs;
            p.Sector = c.Sector;
            p.Cuts = c.Cuts;
            p.LapValid = c.LapValid;
            p.RaceDistance = c.RaceDistance;
            p.InPitlane = c.InPitlane;
            p.Finished = c.Finished;
            p.FinishedMs = c.FinishMs;
            p.PenaltyMs = c.PenaltyMs;
            p.Position = c.Position;
            p.SpeedKmh = c.Vel.Length * 3.6f;
            p.Gear = c.Gear;
            p.DamageD0 = c.DamageD0;                 // iteration 2 (protocol 3): the raw damage pair (players: ints 42-43, AI: Host 0)
            p.DamageD1 = c.DamageD1;
        }

        /// <summary>Car / skin ids from their hashes through the catalog every client has (strings only change when the hash does).</summary>
        private static void ResolveCar(AcNetPlayer p, int carHash, int skinHash)
        {
            AcContent catalog = Catalog;
            if (p.CarHashSeen == carHash && p.SkinHashSeen == skinHash && ReferenceEquals(p.CatalogSeen, catalog)) return;     // (strings only when a hash changes)
            p.CarHashSeen = carHash;
            p.SkinHashSeen = skinHash;
            p.CatalogSeen = catalog;
            AcCarEntry car = CarByHash(carHash);
            string carId = car != null ? car.Id : carHash == 0 ? "" : "#" + carHash.ToString("x8");
            if (!string.Equals(p.CarId, carId, StringComparison.Ordinal)) p.CarId = carId;
            p.CarInstalled = car != null && car.Owned;
            AcSkinEntry skin = SkinByHash(car, skinHash);
            string skinId = skin != null ? skin.Id : "";
            if (!string.Equals(p.SkinId, skinId, StringComparison.Ordinal)) p.SkinId = skinId;
        }

        private static void ResolveName(AcNet.Remote r, AcNetPlayer p)
        {
            try
            {
                IReadOnlyList<AcRemotes.Remote> all = AcRemotes.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i].Id != r.PlayerId) continue;
                    if (!string.IsNullOrEmpty(all[i].Nick)) r.Name = all[i].Nick;
                    break;
                }
            }
            catch (Exception) { /* the Bridge is not up */ }

            if (LabNames != null)
            {
                string n = LabNames(r.PlayerId);
                if (!string.IsNullOrEmpty(n)) r.Name = n;
            }

            if (!string.Equals(p.Name, r.Name, StringComparison.Ordinal)) p.Name = r.Name ?? "";
        }

        private static void ClearPlayers()
        {
            for (int k = 0; k < BySlot.Length; k++) { BySlot[k] = null; JoinLogged[k] = false; }
            PlayerList.Clear();
            for (int i = 0; i < AiByIndex.Length; i++) AiByIndex[i] = null;
            AiList.Clear();
        }

        private static void RaiseJoined(AcNetPlayer p)
        {
            var h = PlayerJoined;
            if (h == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<AcNetPlayer>)d)(p); }
                catch (Exception e) { AcLog.Error("Net: a PlayerJoined handler failed: " + e); }
            }
        }

        private static void RaiseLeft(AcNetPlayer p)
        {
            var h = PlayerLeft;
            if (h == null) return;
            foreach (Delegate d in h.GetInvocationList())
            {
                try { ((Action<AcNetPlayer>)d)(p); }
                catch (Exception e) { AcLog.Error("Net: a PlayerLeft handler failed: " + e); }
            }
        }

        // ------------------------------------------------------------------ the master's AI cars as players (non-masters)

        /// <summary>No built rig of this client's AI module drives AI index <paramref name="index"/> (no driver for it, or still building).</summary>
        private static bool DriverRigMissing(IReadOnlyList<AcAiDriver> drivers, int index)
        {
            if (drivers == null) return true;
            for (int d = 0; d < drivers.Count; d++)
            {
                AcAiDriver ai = drivers[d];
                if (ai != null && ai.Index == index) return ai.Rig == null || ai.Rig.Root == null;
            }

            return true;
        }

        private static void RefreshAiPlayers()
        {
            bool mine = Net.IsInstanceMaster;
            IReadOnlyList<AcAiDriver> drivers = mine ? AiDriversSafe() : null;
            for (int i = 0; i < AcNetBlocks.MaxAi; i++)
            {
                AcNet.AiCar a = Net.Ai(i);
                // a master keeps drawing an inherited AI car (stand-in + collider) until its own AI module's rig for that index exists: the adopted
                // rigs are built one by one over seconds, and the cars must not vanish (or become drive-through) meanwhile
                // (only cars already drawn as remote cars - inherited; never a new stand-in for my own AI being rebuilt)
                bool listed = a != null && a.Present && (!mine || (AiByIndex[i] != null && DriverRigMissing(drivers, i)));
                AcNetPlayer p = AiByIndex[i];
                if (!listed)
                {
                    if (p != null)
                    {
                        AiByIndex[i] = null;
                        AiList.Remove(p);
                        DropCarOf(p, "the AI car was removed");
                    }

                    continue;
                }

                if (p == null)
                {
                    p = new AcNetPlayer { IsAi = true, AiIndex = i, PhotonId = -100 - i, Slot = -1, HasPassengerSeat = false };
                    AiByIndex[i] = p;
                    a.Tag = p;
                    int at = 0;
                    while (at < AiList.Count && AiList[at].AiIndex < i) at++;
                    AiList.Insert(at, p);
                }

                AcNetCar c = a.Car;
                p.HasCar = true;
                p.Seated = p.Driving = true;
                p.AiLevel = a.Level;
                p.LayoutHash = c.LayoutHash;
                p.OnTrack = c.LayoutHash != 0 && c.LayoutHash == LayoutHash;
                p.IsMaster = false;
                FillTiming(p, in c);
                ResolveCar(p, c.CarHash, c.SkinHash);
                // the name only when what it depends on changed (the text, the names.txt line, the car / skin) - no per-frame skin hashing
                if (!ReferenceEquals(p.AiNameSeen, a.Name) || p.AiNameLineSeen != a.NameLine || p.AiNameCarSeen != c.CarHash || p.AiNameSkinSeen != c.SkinHash)
                {
                    p.AiNameSeen = a.Name;
                    p.AiNameLineSeen = a.NameLine;
                    p.AiNameCarSeen = c.CarHash;
                    p.AiNameSkinSeen = c.SkinHash;
                    string name = ReceivedAiName(a, c.CarHash, c.SkinHash);
                    if (!string.Equals(p.Name, name, StringComparison.Ordinal)) p.Name = name;
                }
            }
        }
    }
}
