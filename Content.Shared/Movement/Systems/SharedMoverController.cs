    using System.Diagnostics.CodeAnalysis;
    using System.Numerics;
    using Content.Shared.Bed.Sleep;
    using Content.Shared.CCVar;
    using Content.Shared.Friction;
    using Content.Shared.Gravity;
    using Content.Shared.Interaction;
    using Content.Shared.Inventory;
    using Content.Shared.Maps;
    using Content.Shared.Mobs.Systems;
    using Content.Shared.Movement.Components;
    using Content.Shared.Movement.Events;
    using Content.Shared.Physics;
    using Content.Shared.Tag;
    using Content.Shared.TileMovement;
    using Robust.Shared.Audio;
    using Robust.Shared.Audio.Systems;
    using Robust.Shared.Configuration;
    using Robust.Shared.Containers;
    using Robust.Shared.Map;
    using Robust.Shared.Map.Components;
    using Robust.Shared.Physics;
    using Robust.Shared.Physics.Components;
    using Robust.Shared.Physics.Controllers;
    using Robust.Shared.Physics.Systems;
    using Robust.Shared.Timing;
    using Robust.Shared.Utility;
    using PullableComponent = Content.Shared.Movement.Pulling.Components.PullableComponent;

    namespace Content.Shared.Movement.Systems;

    /// <summary>
    ///     Handles player and NPC mob movement.
    ///     NPCs are handled server-side only.
    /// </summary>
    public abstract partial class SharedMoverController : VirtualController
    {
        [Dependency] private   readonly IConfigurationManager _configManager = default!;
        [Dependency] protected readonly IGameTiming Timing = default!;
        [Dependency] private   readonly IMapManager _mapManager = default!;
        [Dependency] private   readonly ITileDefinitionManager _tileDefinitionManager = default!;
        [Dependency] private   readonly EntityLookupSystem _lookup = default!;
        [Dependency] private   readonly InventorySystem _inventory = default!;
        [Dependency] private   readonly MobStateSystem _mobState = default!;
        [Dependency] private   readonly SharedAudioSystem _audio = default!;
        [Dependency] private   readonly SharedContainerSystem _container = default!;
        [Dependency] private   readonly SharedMapSystem _mapSystem = default!;
        [Dependency] private   readonly SharedGravitySystem _gravity = default!;
        [Dependency] protected readonly SharedPhysicsSystem Physics = default!;
        [Dependency] private   readonly SharedTransformSystem _transform = default!;
        [Dependency] private   readonly TagSystem _tags = default!;
        [Dependency] private   readonly SharedInteractionSystem _interaction = default!;

        protected EntityQuery<InputMoverComponent> MoverQuery;
        protected EntityQuery<MobMoverComponent> MobMoverQuery;
        protected EntityQuery<MovementRelayTargetComponent> RelayTargetQuery;
        protected EntityQuery<MovementSpeedModifierComponent> ModifierQuery;
        protected EntityQuery<PhysicsComponent> PhysicsQuery;
        protected EntityQuery<RelayInputMoverComponent> RelayQuery;
        protected EntityQuery<PullableComponent> PullableQuery;
        protected EntityQuery<TransformComponent> XformQuery;
        protected EntityQuery<CanMoveInAirComponent> CanMoveInAirQuery;
        protected EntityQuery<NoRotateOnMoveComponent> NoRotateQuery;
        protected EntityQuery<FootstepModifierComponent> FootstepModifierQuery;
        protected EntityQuery<MapGridComponent> MapGridQuery;
        protected EntityQuery<FixturesComponent> FixturesQuery;
        protected EntityQuery<TileMovementComponent> TileMovementQuery;

        /// <summary>
        /// <see cref="CCVars.StopSpeed"/>
        /// </summary>
        private float _stopSpeed;

        private bool _relativeMovement;

        private TimeSpan CurrentTime => PhysicsSystem.EffectiveCurTime ?? Timing.CurTime;

        /// <summary>
        /// Cache the mob movement calculation to re-use elsewhere.
        /// </summary>
        public Dictionary<EntityUid, bool> UsedMobMovement = new();

        public override void Initialize()
        {
            base.Initialize();

            MoverQuery = GetEntityQuery<InputMoverComponent>();
            MobMoverQuery = GetEntityQuery<MobMoverComponent>();
            ModifierQuery = GetEntityQuery<MovementSpeedModifierComponent>();
            RelayTargetQuery = GetEntityQuery<MovementRelayTargetComponent>();
            PhysicsQuery = GetEntityQuery<PhysicsComponent>();
            RelayQuery = GetEntityQuery<RelayInputMoverComponent>();
            PullableQuery = GetEntityQuery<PullableComponent>();
            XformQuery = GetEntityQuery<TransformComponent>();
            NoRotateQuery = GetEntityQuery<NoRotateOnMoveComponent>();
            CanMoveInAirQuery = GetEntityQuery<CanMoveInAirComponent>();
            FootstepModifierQuery = GetEntityQuery<FootstepModifierComponent>();
            MapGridQuery = GetEntityQuery<MapGridComponent>();
            FixturesQuery = GetEntityQuery<FixturesComponent>();
            TileMovementQuery = GetEntityQuery<TileMovement.TileMovementComponent>();

            InitializeInput();
            InitializeRelay();
            Subs.CVar(_configManager, CCVars.RelativeMovement, value => _relativeMovement = value, true);
            Subs.CVar(_configManager, CCVars.StopSpeed, value => _stopSpeed = value, true);
            UpdatesBefore.Add(typeof(TileFrictionController));
        }

        public override void Shutdown()
        {
            base.Shutdown();
            ShutdownInput();
        }

        public override void UpdateAfterSolve(bool prediction, float frameTime)
        {
            base.UpdateAfterSolve(prediction, frameTime);
            UsedMobMovement.Clear();
        }

        /// <summary>
        ///     Movement while considering actionblockers, weightlessness, etc.
        /// </summary>
        protected void HandleMobMovement(
            EntityUid uid,
            InputMoverComponent mover,
            EntityUid physicsUid,
            PhysicsComponent physicsComponent,
            TransformComponent xform,
            float frameTime)
        {
            var canMove = mover.CanMove;
            if (RelayTargetQuery.TryGetComponent(uid, out var relayTarget))
            {
                if (_mobState.IsIncapacitated(relayTarget.Source) ||
                    TryComp<SleepingComponent>(relayTarget.Source, out _) ||
                    !MoverQuery.TryGetComponent(relayTarget.Source, out var relayedMover))
                {
                    canMove = false;
                }
                else
                {
                    mover.RelativeEntity = relayedMover.RelativeEntity;
                    mover.RelativeRotation = relayedMover.RelativeRotation;
                    mover.TargetRelativeRotation = relayedMover.TargetRelativeRotation;
                }
            }

            // Update relative movement
            if (mover.LerpTarget < Timing.CurTime)
            {
                if (TryUpdateRelative(mover, xform))
                {
                    Dirty(uid, mover);
                }
            }

            LerpRotation(uid, mover, frameTime);

            if (!canMove
                || physicsComponent.BodyStatus != BodyStatus.OnGround && !CanMoveInAirQuery.HasComponent(uid)
                || PullableQuery.TryGetComponent(uid, out var pullable) && pullable.BeingPulled)
            {
                UsedMobMovement[uid] = false;
                return;
            }


            UsedMobMovement[uid] = true;
            // Specifically don't use mover.Owner because that may be different to the actual physics body being moved.
            var weightless = _gravity.IsWeightless(physicsUid, physicsComponent, xform);
            var (walkDir, sprintDir) = GetVelocityInput(mover);
            var touching = false;

            // Handle wall-pushes.
            if (weightless)
            {
                if (xform.GridUid != null)
                    touching = true;

                if (!touching)
                {
                    var ev = new CanWeightlessMoveEvent(uid);
                    RaiseLocalEvent(uid, ref ev, true);
                    // No gravity: is our entity touching anything?
                    touching = ev.CanMove;

                    if (!touching && TryComp<MobMoverComponent>(uid, out var mobMover))
                        touching |= IsAroundCollider(PhysicsSystem, xform, mobMover, physicsUid, physicsComponent);
                }
            }

            // Get current tile def for things like speed/friction mods
            ContentTileDefinition? tileDef = null;

            // Don't bother getting the tiledef here if we're weightless or in-air
            // since no tile-based modifiers should be applying in that situation
            if (MapGridQuery.TryComp(xform.GridUid, out var gridComp)
                && _mapSystem.TryGetTileRef(xform.GridUid.Value, gridComp, xform.Coordinates, out var tile)
                && !(weightless || physicsComponent.BodyStatus == BodyStatus.InAir))
            {
                tileDef = (ContentTileDefinition) _tileDefinitionManager[tile.Tile.TypeId];
            }

            if (!weightless && physicsComponent.BodyStatus == BodyStatus.OnGround)
            {
                if (HandleTileMovement(uid, physicsUid, physicsComponent, xform, mover, tileDef, relayTarget))
                    return;
            }

            // Regular movement.
            // Target velocity.
            // This is relative to the map / grid we're on.
            var moveSpeedComponent = ModifierQuery.CompOrNull(uid);

            var walkSpeed = moveSpeedComponent?.CurrentWalkSpeed ?? MovementSpeedModifierComponent.DefaultBaseWalkSpeed;
            var sprintSpeed = moveSpeedComponent?.CurrentSprintSpeed ?? MovementSpeedModifierComponent.DefaultBaseSprintSpeed;

            var total = walkDir * walkSpeed + sprintDir * sprintSpeed;

            var parentRotation = GetParentGridAngle(mover);
            var worldTotal = _relativeMovement ? parentRotation.RotateVec(total) : total;

            DebugTools.Assert(MathHelper.CloseToPercent(total.Length(), worldTotal.Length()));

            var velocity = physicsComponent.LinearVelocity;
            float friction;
            float weightlessModifier;
            float accel;

            if (weightless)
            {
                if (gridComp == null && !MapGridQuery.HasComp(xform.GridUid))
                    friction = moveSpeedComponent?.OffGridFriction ?? MovementSpeedModifierComponent.DefaultOffGridFriction;
                else if (worldTotal != Vector2.Zero && touching)
                    friction = moveSpeedComponent?.WeightlessFriction ?? MovementSpeedModifierComponent.DefaultWeightlessFriction;
                else
                    friction = moveSpeedComponent?.WeightlessFrictionNoInput ?? MovementSpeedModifierComponent.DefaultWeightlessFrictionNoInput;

                weightlessModifier = moveSpeedComponent?.WeightlessModifier ?? MovementSpeedModifierComponent.DefaultWeightlessModifier;
                accel = moveSpeedComponent?.WeightlessAcceleration ?? MovementSpeedModifierComponent.DefaultWeightlessAcceleration;
            }
            else
            {
                if (worldTotal != Vector2.Zero || moveSpeedComponent?.FrictionNoInput == null)
                {
                    friction = tileDef?.MobFriction ?? moveSpeedComponent?.Friction ?? MovementSpeedModifierComponent.DefaultFriction;
                }
                else
                {
                    friction = tileDef?.MobFrictionNoInput ?? moveSpeedComponent.FrictionNoInput ?? MovementSpeedModifierComponent.DefaultFrictionNoInput;
                }

                weightlessModifier = 1f;
                accel = tileDef?.MobAcceleration ?? moveSpeedComponent?.Acceleration ?? MovementSpeedModifierComponent.DefaultAcceleration;
            }

            var minimumFrictionSpeed = moveSpeedComponent?.MinimumFrictionSpeed ?? MovementSpeedModifierComponent.DefaultMinimumFrictionSpeed;
            Friction(minimumFrictionSpeed, frameTime, friction, ref velocity);

            if (worldTotal != Vector2.Zero)
            {
                if (!NoRotateQuery.HasComponent(uid))
                {
                    // TODO apparently this results in a duplicate move event because "This should have its event run during
                    // island solver"??. So maybe SetRotation needs an argument to avoid raising an event?
                    var worldRot = _transform.GetWorldRotation(xform);
                    _transform.SetLocalRotation(xform, xform.LocalRotation + worldTotal.ToWorldAngle() - worldRot);
                }

                if (!weightless && MobMoverQuery.TryGetComponent(uid, out var mobMover) &&
                    TryGetSound(weightless, uid, mover, mobMover, xform, out var sound, tileDef: tileDef))
                {
                    var soundModifier = mover.Sprinting ? 3.5f : 1.5f;

                    var audioParams = sound.Params
                        .WithVolume(sound.Params.Volume + soundModifier)
                        .WithVariation(sound.Params.Variation ?? mobMover.FootstepVariation);

                    // If we're a relay target then predict the sound for all relays.
                    if (relayTarget != null)
                    {
                        _audio.PlayPredicted(sound, uid, relayTarget.Source, audioParams);
                    }
                    else
                    {
                        _audio.PlayPredicted(sound, uid, uid, audioParams);
                    }
                }
            }

            worldTotal *= weightlessModifier;

            if (!weightless || touching)
                Accelerate(ref velocity, in worldTotal, accel, frameTime);

            PhysicsSystem.SetLinearVelocity(physicsUid, velocity, body: physicsComponent);

            // Ensures that players do not spiiiiiiin
            PhysicsSystem.SetAngularVelocity(physicsUid, 0, body: physicsComponent);
        }

        public void LerpRotation(EntityUid uid, InputMoverComponent mover, float frameTime)
        {
            var angleDiff = Angle.ShortestDistance(mover.RelativeRotation, mover.TargetRelativeRotation);

            // if we've just traversed then lerp to our target rotation.
            if (!angleDiff.EqualsApprox(Angle.Zero, 0.001))
            {
                var adjustment = angleDiff * 5f * frameTime;
                var minAdjustment = 0.01 * frameTime;

                if (angleDiff < 0)
                {
                    adjustment = Math.Min(adjustment, -minAdjustment);
                    adjustment = Math.Clamp(adjustment, angleDiff, -angleDiff);
                }
                else
                {
                    adjustment = Math.Max(adjustment, minAdjustment);
                    adjustment = Math.Clamp(adjustment, -angleDiff, angleDiff);
                }

                mover.RelativeRotation += adjustment;
                mover.RelativeRotation.FlipPositive();
                Dirty(uid, mover);
            }
            else if (!angleDiff.Equals(Angle.Zero))
            {
                mover.TargetRelativeRotation.FlipPositive();
                mover.RelativeRotation = mover.TargetRelativeRotation;
                Dirty(uid, mover);
            }
        }

        private void Friction(float minimumFrictionSpeed, float frameTime, float friction, ref Vector2 velocity)
        {
            var speed = velocity.Length();

            if (speed < minimumFrictionSpeed)
                return;

            var drop = 0f;

            var control = MathF.Max(_stopSpeed, speed);
            drop += control * friction * frameTime;

            var newSpeed = MathF.Max(0f, speed - drop);

            if (newSpeed.Equals(speed))
                return;

            newSpeed /= speed;
            velocity *= newSpeed;
        }

        private void Accelerate(ref Vector2 currentVelocity, in Vector2 velocity, float accel, float frameTime)
        {
            var wishDir = velocity != Vector2.Zero ? velocity.Normalized() : Vector2.Zero;
            var wishSpeed = velocity.Length();

            var currentSpeed = Vector2.Dot(currentVelocity, wishDir);
            var addSpeed = wishSpeed - currentSpeed;

            if (addSpeed <= 0f)
                return;

            var accelSpeed = accel * frameTime * wishSpeed;
            accelSpeed = MathF.Min(accelSpeed, addSpeed);

            currentVelocity += wishDir * accelSpeed;
        }

        public bool UseMobMovement(EntityUid uid)
        {
            return UsedMobMovement.TryGetValue(uid, out var used) && used;
        }

        /// <summary>
        ///     Used for weightlessness to determine if we are near a wall.
        /// </summary>
        private bool IsAroundCollider(SharedPhysicsSystem broadPhaseSystem, TransformComponent transform, MobMoverComponent mover, EntityUid physicsUid, PhysicsComponent collider)
        {
            var enlargedAABB = _lookup.GetWorldAABB(physicsUid, transform).Enlarged(mover.GrabRangeVV);

            foreach (var otherCollider in broadPhaseSystem.GetCollidingEntities(transform.MapID, enlargedAABB))
            {
                if (otherCollider == collider)
                    continue; // Don't try to push off of yourself!

                // Only allow pushing off of anchored things that have collision.
                if (otherCollider.BodyType != BodyType.Static ||
                    !otherCollider.CanCollide ||
                    ((collider.CollisionMask & otherCollider.CollisionLayer) == 0 &&
                    (otherCollider.CollisionMask & collider.CollisionLayer) == 0) ||
                    (TryComp(otherCollider.Owner, out PullableComponent? pullable) && pullable.BeingPulled))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        protected abstract bool CanSound();

        private bool TryGetSound(
            bool weightless,
            EntityUid uid,
            InputMoverComponent mover,
            MobMoverComponent mobMover,
            TransformComponent xform,
            [NotNullWhen(true)] out SoundSpecifier? sound,
            ContentTileDefinition? tileDef = null)
        {
            sound = null;

            if (!CanSound() || !_tags.HasTag(uid, "FootstepSound"))
                return false;

            var coordinates = xform.Coordinates;
            var distanceNeeded = mover.Sprinting
                ? mobMover.StepSoundMoveDistanceRunning
                : mobMover.StepSoundMoveDistanceWalking;

            // Handle footsteps.
            if (!weightless)
            {
                // Can happen when teleporting between grids.
                if (!coordinates.TryDistance(EntityManager, mobMover.LastPosition, out var distance) ||
                    distance > distanceNeeded)
                {
                    mobMover.StepSoundDistance = distanceNeeded;
                }
                else
                {
                    mobMover.StepSoundDistance += distance;
                }
            }
            else
            {
                // In space no one can hear you squeak
                return false;
            }

            mobMover.LastPosition = coordinates;

            if (mobMover.StepSoundDistance < distanceNeeded)
                return false;

            mobMover.StepSoundDistance -= distanceNeeded;

            if (FootstepModifierQuery.TryComp(uid, out var moverModifier))
            {
                sound = moverModifier.FootstepSoundCollection;
                return true;
            }

            if (_inventory.TryGetSlotEntity(uid, "shoes", out var shoes) &&
                FootstepModifierQuery.TryComp(shoes, out var modifier))
            {
                sound = modifier.FootstepSoundCollection;
                return true;
            }

            return TryGetFootstepSound(uid, xform, shoes != null, out sound, tileDef: tileDef);
        }

        private bool TryGetFootstepSound(
            EntityUid uid,
            TransformComponent xform,
            bool haveShoes,
            [NotNullWhen(true)] out SoundSpecifier? sound,
            ContentTileDefinition? tileDef = null)
        {
            sound = null;

            // Fallback to the map?
            if (!MapGridQuery.TryComp(xform.GridUid, out var grid))
            {
                if (FootstepModifierQuery.TryComp(xform.MapUid, out var modifier))
                {
                    sound = modifier.FootstepSoundCollection;
                    return true;
                }

                return false;
            }

            var position = grid.LocalToTile(xform.Coordinates);
            var soundEv = new GetFootstepSoundEvent(uid);

            // If the coordinates have a FootstepModifier component
            // i.e. component that emit sound on footsteps emit that sound
            var anchored = grid.GetAnchoredEntitiesEnumerator(position);

            while (anchored.MoveNext(out var maybeFootstep))
            {
                RaiseLocalEvent(maybeFootstep.Value, ref soundEv);

                if (soundEv.Sound != null)
                {
                    sound = soundEv.Sound;
                    return true;
                }

                if (FootstepModifierQuery.TryComp(maybeFootstep, out var footstep))
                {
                    sound = footstep.FootstepSoundCollection;
                    return true;
                }
            }

            // Walking on a tile.
            // Tile def might have been passed in already from previous methods, so use that
            // if we have it
            if (tileDef == null && grid.TryGetTileRef(position, out var tileRef))
            {
                tileDef = (ContentTileDefinition) _tileDefinitionManager[tileRef.Tile.TypeId];
            }

            if (tileDef == null)
                return false;

            sound = haveShoes ? tileDef.FootstepSounds : tileDef.BarestepSounds;
            return sound != null;
        }

        /// <summary>
        /// Runs one tick of tile-based movement given the
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="physicsUid"></param>
        /// <param name="physicsComponent"></param>
        /// <param name="xform"></param>
        /// <param name="mover"></param>
        /// <param name="tileDef"></param>
        /// <param name="relayTarget"></param>
        /// <returns></returns>
        public bool HandleTileMovement(
            EntityUid uid,
            EntityUid physicsUid,
            PhysicsComponent physicsComponent,
            TransformComponent xform,
            InputMoverComponent mover,
            ContentTileDefinition? tileDef,
            MovementRelayTargetComponent? relayTarget)
        {
            if (!TileMovementQuery.TryComp(physicsUid, out var tileMovement))
                return false;

            var immediateDir = DirVecForButtons(mover.HeldMoveButtons);
            var (walkDir, sprintDir) = mover.Sprinting ? (Vector2.Zero, immediateDir) : (immediateDir, Vector2.Zero);
            var moveSpeedComponent = ModifierQuery.CompOrNull(uid);
            var walkSpeed = moveSpeedComponent?.CurrentWalkSpeed ?? MovementSpeedModifierComponent.DefaultBaseWalkSpeed;
            var sprintSpeed = moveSpeedComponent?.CurrentSprintSpeed ?? MovementSpeedModifierComponent.DefaultBaseSprintSpeed;
            var total = walkDir * walkSpeed + sprintDir * sprintSpeed;


            // If we're not moving, do nothing.
            if (total == Vector2.Zero && !tileMovement.SlideActive)
            {
                return true;
            }

            // Set rotation:
            if (!NoRotateQuery.HasComponent(uid))
            {
                // If we're standing still, sync rotation with parent grid.
                if (!tileMovement.SlideActive)
                {
                    var parentRotation = GetParentGridAngle(mover);
                    var worldTotal = _relativeMovement ? parentRotation.RotateVec(total) : total;
                    var worldRot = _transform.GetWorldRotation(xform);
                    _transform.SetLocalRotation(xform, xform.LocalRotation + worldTotal.ToWorldAngle() - worldRot);
                }
                // Otherwise if we're moving, set rotation based how close we are to the destination tile as a LERP in
                // case origin and destination tiles have different rotations.
                else if (TryComp(mover.RelativeEntity, out TransformComponent? rel))
                {
                    var delta = tileMovement.Destination - tileMovement.Origin.Position;
                    var worldRot = _transform.GetWorldRotation(rel).RotateVec(delta).ToWorldAngle();
                    _transform.SetWorldRotation(xform, worldRot);
                }
            }

            // Play step sound.
            if (MobMoverQuery.TryGetComponent(uid, out var mobMover) &&
                TryGetSound(false, uid, mover, mobMover, xform, out var sound, tileDef: tileDef))
            {
                var soundModifier = mover.Sprinting ? 3.5f : 1.5f;
                var audioParams = sound.Params
                    .WithVolume(sound.Params.Volume + soundModifier)
                    .WithVariation(sound.Params.Variation ?? mobMover.FootstepVariation);
                _audio.PlayPredicted(sound, uid, relayTarget?.Source ?? uid, audioParams);
            }

            // Slide logic. If we're in the middle of a slide, check whether it should be ended
            // (and immediately begin a new one if a move button is still being held down). Otherwise,
            // continue slide. If no slide is active, begin a slide.
            if (tileMovement.SlideActive)
            {
                if (CheckForSlideEnd(mover.HeldMoveButtons, xform, tileMovement))
                {
                    EndSlide(tileMovement, uid, mover);
                    if (total != Vector2.Zero)
                    {
                        StartSlide(tileMovement, physicsUid, total, mover);
                    }
                }
                // Otherwise continue the slide.
                else
                {
                    UpdateSlide(tileMovement, physicsUid, physicsUid, mover);
                }
            }
            else
            {
                StartSlide(tileMovement, physicsUid, total, mover);
            }
            Dirty(uid, tileMovement);
            return true;
        }

        private bool CheckForSlideEnd(MoveButtons pressedButtons, TransformComponent transform, TileMovementComponent tileMovement)
        {
            var reachedDestination = transform.LocalPosition.EqualsApprox(tileMovement.Destination, 0.01f);
            var stoppedPressing = pressedButtons == MoveButtons.None && CurrentTime - tileMovement.MovementKeyInitialDownTime >= TimeSpan.FromSeconds(0.14f);
            return reachedDestination || stoppedPressing;
        }

        private void StartSlide(TileMovement.TileMovementComponent tileMovement, EntityUid uid, Vector2 total, InputMoverComponent mover)
        {
            var localPosition = Transform(uid).LocalPosition;
            var dir = Angle.FromWorldVec(total).GetDir();
            var offset = dir.ToIntVec();

            tileMovement.Origin = new EntityCoordinates(uid, localPosition);
            tileMovement.Destination = localPosition + offset;
            tileMovement.MovementKeyInitialDownTime = CurrentTime;
            tileMovement.SlideActive = true;
        }

        private void UpdateSlide(TileMovement.TileMovementComponent tileMovement, EntityUid uid, EntityUid physicsUid, InputMoverComponent mover)
        {
            var targetTransform = Transform(uid);

            if (PhysicsQuery.TryComp(physicsUid, out var physicsComponent))
            {
                var parentRotation = GetParentGridAngle(mover);
                var movementVelocity = (tileMovement.Destination) - (targetTransform.LocalPosition);
                movementVelocity.Normalize();
                movementVelocity *= 8.5f;
                movementVelocity =  parentRotation.RotateVec(movementVelocity);
                PhysicsSystem.SetLinearVelocity(physicsUid, movementVelocity, body: physicsComponent);
                PhysicsSystem.SetAngularVelocity(physicsUid, 0, body: physicsComponent);
            }
        }

        private void EndSlide(TileMovement.TileMovementComponent tileMovement, EntityUid uid, InputMoverComponent mover)
        {
            tileMovement.SlideActive = false;
            tileMovement.MovementKeyInitialDownTime = null;
            var physicsComponent = PhysicsQuery.GetComponent(uid);
            PhysicsSystem.SetLinearVelocity(uid, Vector2.Zero, body: physicsComponent);
            PhysicsSystem.SetAngularVelocity(uid, 0, body: physicsComponent);

            ForceSnapToTile(uid, mover);
        }

        /// <summary>
        /// Instantly snaps an entity to the center of the tile it is currently standing on based on the
        /// given grid. Does not trigger collisions.
        /// </summary>
        /// <param name="uid">UID of entity to be snapped.</param>
        /// <param name="inputMover">InputMoverComponent on the entity to be snapped.</param>
        private void ForceSnapToTile(EntityUid uid, InputMoverComponent inputMover)
        {
            if (TryComp(inputMover.RelativeEntity, out TransformComponent? rel))
            {
                ForceSnapToTile((uid, Transform(uid)), (inputMover.RelativeEntity.Value, rel));
            }
        }

        /// <summary>
        /// Instantly snaps an entity to the center of the tile it is currently standing on based on the
        /// given grid. Does not trigger collisions.
        /// </summary>
        /// <param name="entity">The entity to be snapped.</param>
        /// <param name="grid">The grid whose tiles will be used to calculate snapping.</param>
        /// <returns>The EntityCoordinates at the center of the tile.</returns>
        private EntityCoordinates ForceSnapToTile(Entity<TransformComponent> entity, Entity<TransformComponent> grid)
        {
            var localCoordinates = entity.Comp.Coordinates.WithEntityId(grid.Owner, _transform, EntityManager);
            var tileX = (int)Math.Floor(localCoordinates.Position.X) + 0.5f;
            var tileY = (int)Math.Floor(localCoordinates.Position.Y) + 0.5f;
            var tileCoords = new EntityCoordinates(localCoordinates.EntityId, tileX, tileY);

            if (!localCoordinates.Position.EqualsApprox(tileCoords.Position))
            {
                if (entity.Comp.ParentUid.IsValid())
                {
                    var local2 = tileCoords.WithEntityId(entity.Comp.ParentUid, _transform, EntityManager).Position;
                    _transform.SetLocalPosition(entity.Owner, local2, entity.Comp);
                }
            }

            PhysicsSystem.WakeBody(entity);
            return tileCoords;
        }
    }
