namespace SomniumSpace.Worlds.SomniumAssetto
{
    /// <summary>
    /// Physics layers, by the Somnium project's own TagManager indices. Layer names (ProjectSettings/TagManager.asset, same as the
    /// client): 0 Default, 1 TransparentFX, 2 Ignore Raycast, 4 Water, 5 UI, 6 XRInteractable, 7 PhysicalObject, 10 GrababbleObject,
    /// 13 GrabHand, 14 Terrain, 15 Triggers, 16 BuildingItem, 23 Environment, 24 Player, 26 Bullet, 28 Vehicle, 30 IkContact.
    ///
    /// The collision matrix (ProjectSettings/DynamicsManager.asset m_LayerCollisionMatrix, decoded 2026-10-31 by the contracts step;
    /// the string is identical in the client, research/ets_vehicles.md section 2):
    ///   layer 2  (avatar capsule) collides with 0,1,2,3,7,11,12,13,14,16,21,22,23,24,28,29,31
    ///   layer 14 (Terrain)        collides with 0,1,2,3,5,6,7,8..14,16,21,22,23,26,28,29,30,31   (NOT 24)
    ///   layer 28 (Vehicle)        collides with 0,2,3,7,11,14,15,16,23,24,26,28,29,30,31
    ///   layer 23 (Environment)    collides with 0..3,5..23,26,28..31
    /// DREAMPORT_GUIDE section 7: the avatar's CharacterController is on layer 2; desktop clicks reach 0,1,4,5,14,16; the VR pointer
    /// 0,5,6,10,14,16,23 at 5 m; the VR ground check is 0,2,7,14,16,23,28. Bridge SetCollisionDisableState only ORs layer 2 into the avatar
    /// capsule's excludeLayers, so a car the avatar sits in must exclude the avatar's layers itself (<see cref="AvatarExcludes"/>).
    ///
    /// Choices (justified):
    /// - <see cref="Track"/> = 14 "Terrain": the avatar capsule (2) stands on it (2x14 collide) in VR and on a monitor (14 is in the VR
    ///   ground check), cars (28) collide with it (28x14), the VR hands (7) touch it, and it blocks desktop clicks / the VR ray like any
    ///   ground does (wanted: you cannot click through the road). Player (24) does not collide with it - irrelevant (24 is the client's
    ///   own avatar-parts layer).
    /// - Track WALLS (digit+WALL meshes) also go on 14 (the avatar must not walk through a wall either; cars hit them). // ours
    /// - <see cref="Car"/> = 28 "Vehicle": collides with the track (14), other cars (28), walls/props (23) and the avatar (2): a parked car is
    ///   solid to a walking player. It is NOT reached by desktop clicks or the VR ray (28 is in neither list), so door handles and the
    ///   "get in" target must be separate colliders on <see cref="Interactable"/> (16, reached by both pointers). While somebody sits in
    ///   (or rides) the car, every car collider sets <c>excludeLayers |= AvatarExcludes</c> (2|7|24) - else the capsule written into the
    ///   seat each frame pushes the car at 10 m/s depenetration speed (ETS "the truck fell through the world").
    /// - Car bodies also exclude <see cref="GroundMask"/> (wheels hold the car up; the body box never touches the road) - ETS BodyExcludes.
    /// </summary>
    public static class AcLayers
    {
        /// <summary>Default: hub / showroom geometry the player stands on.</summary>
        public const int World = 0;
        /// <summary>The avatar's own capsule (client, "Ignore Raycast").</summary>
        public const int Avatar = 2;
        /// <summary>World-space UI (tablet app, panels).</summary>
        public const int Ui = 5;
        /// <summary>The VR hands' physical boxes (client).</summary>
        public const int PhysicalObject = 7;
        /// <summary>Track collision meshes (road, kerbs, grass, sand, pit lane, walls; each tagged with its AcSurface via
        /// <see cref="AcSurfaceTag"/>). The client's "Terrain" layer: the avatar stands on it, cars collide with it. // ours (choice)</summary>
        public const int Track = 14;
        /// <summary>Clickable / grabbable world objects (door handles, "get in" targets, buttons) - reached by desktop clicks and the VR pointer.</summary>
        public const int Interactable = 16;
        /// <summary>Props and scenery that block players and cars but are not drivable.</summary>
        public const int Environment = 23;
        /// <summary>The client's Player layer (avatar parts).</summary>
        public const int Player = 24;
        /// <summary>Car bodies (local, remote and AI cars). // ours (choice, same as ETS)</summary>
        public const int Car = 28;

        /// <summary>What a car's colliders must pass through while somebody sits in / rides it: the avatar capsule (2), the VR hands (7),
        /// the Player layer (24).</summary>
        public const int AvatarExcludes = (1 << Avatar) | (1 << PhysicalObject) | (1 << Player);
        /// <summary>What a wheel / spawn ray looks for: the track (and the hub floor), never a car.</summary>
        public const int GroundMask = (1 << World) | (1 << Track);
        /// <summary>What a car body box excludes always (the wheels carry it; ETS BodyExcludes).</summary>
        public const int BodyExcludes = GroundMask;
        /// <summary>What a car body box excludes while somebody sits in it (ETS SeatedBodyExcludes).</summary>
        public const int SeatedBodyExcludes = GroundMask | AvatarExcludes;
        /// <summary>What a monitor chase camera keeps clear of (ETS / Halo camera obstacle layers).</summary>
        public const int CameraObstacles = (1 << World) | (1 << Track) | (1 << Interactable) | (1 << Environment);
    }
}
