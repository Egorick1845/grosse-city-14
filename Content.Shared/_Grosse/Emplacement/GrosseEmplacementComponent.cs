using Robust.Shared.GameStates;

namespace Content.Shared._Grosse.Emplacement;

/// <summary>
/// Stationary foldable emplacement: the operator sits, cannot drive it, and aims within a yaw cone.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(GrosseEmplacementSystem))]
public sealed partial class GrosseEmplacementComponent : Component
{
    /// <summary>
    /// Maximum yaw offset from <see cref="DeployedRotation"/> in either direction.
    /// Full cone is twice this value (22.5° → 45°).
    /// </summary>
    [DataField, AutoNetworkedField]
    public Angle MaxYawDeviation = Angle.FromDegrees(22.5);

    /// <summary>
    /// Fraction of incoming damage kept on each side of the split (turret and gunner).
    /// </summary>
    [DataField, AutoNetworkedField]
    public float DamageSplit = 0.5f;

    /// <summary>
    /// World yaw captured when the emplacement is deployed or spawned unfolded.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Angle DeployedRotation;

    /// <summary>
    /// Extra yaw applied to mouse aim and to the deployer's facing.
    /// Leave at zero when the 1-dir sprite barrel already points south (world 0).
    /// </summary>
    [DataField, AutoNetworkedField]
    public Angle VisualRotationOffset;
}

/// <summary>
/// Marks a gunner currently covering behind a <see cref="GrosseEmplacementComponent"/>.
/// Incoming damage is split with the emplacement.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(GrosseEmplacementSystem))]
public sealed partial class GrosseEmplacementCoverComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Emplacement;
}
