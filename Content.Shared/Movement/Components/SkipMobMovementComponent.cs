using Robust.Shared.GameStates;

namespace Content.Shared.Movement.Components;

/// <summary>
/// Marks an entity whose <see cref="InputMoverComponent"/> should not be processed as mob walking.
/// Used by vehicles that consume WASD through movement relay but apply their own physics.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SkipMobMovementComponent : Component;
