using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Map;

namespace Content.Shared.TileMovement;

/// <summary>
/// When attached to an entity with an InputMoverComponent, all mob movement on that entity will
/// be tile-based. Contains info used to facilitate that movement.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class TileMovementComponent : Component
{
    /// <summary>
    /// Whether a tile movement slide is currently in progress.
    /// </summary>
    [AutoNetworkedField]
    public bool SlideActive;

    /// <summary>
    /// This helps determine how long a slide should last. A slide will continue so long
    /// as a movement key (WASD) is being held down, but if it was held down for less than
    /// a certain time period then it will continue for a minimum period.
    /// </summary>
    [AutoNetworkedField]
    public TimeSpan? MovementKeyInitialDownTime;

    /// <summary>
    /// Coordinates from which the current slide first began.
    /// </summary>
    [AutoNetworkedField]
    public EntityCoordinates Origin;

    /// <summary>
    /// Coordinates of the target of the current slide local to the parent grid.
    /// </summary>
    [AutoNetworkedField]
    public Vector2 Destination;
}
