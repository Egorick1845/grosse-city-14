using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Decals;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Grosse.Cars;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GrosseCarComponent : Component
{
    [DataField]
    public List<GrosseCarSlot> Slots = new();

    [DataField]
    public TimeSpan EntryDelay = TimeSpan.FromSeconds(0.8);

    [DataField]
    public TimeSpan ExitDelay = TimeSpan.FromSeconds(1.5);

    [DataField]
    public List<EntProtoId> OccupantActions = new();

    [DataField]
    public float OccupantDamageMultiplier = 0.35f;

    [DataField]
    public float MaxDriveDamage = 200f;

    [DataField]
    public float EngineForce = 4000f;

    [DataField]
    public float BrakeForce = 6000f;

    [DataField]
    public float Friction = 1.5f;

    [DataField]
    public float MaxForwardSpeed = 8f;

    [DataField]
    public float MaxReverseSpeed = 3f;

    [DataField]
    public float SteerRate = 2.2f;

    [DataField]
    public float MinSteerSpeed = 0.6f;

    [DataField]
    public float Grip = 4f;

    [DataField]
    public float HandbrakeGrip = 0.6f;

    [DataField]
    public float HandbrakeForce = 5000f;

    /// <summary>
    /// South-facing RSI locked with overrideDirection: add this to heading so the nose matches entity forward.
    /// </summary>
    [DataField]
    public Angle VisualRotationOffset = Angle.FromDegrees(90);

    [DataField]
    public float MinImpactSpeed = 4f;

    [DataField]
    public DamageSpecifier HitDamage = new();

    [DataField]
    public DamageSpecifier SelfDamage = new();

    [DataField]
    public DamageSpecifier WallDamage = new();

    [DataField]
    public TimeSpan ImpactCooldown = TimeSpan.FromSeconds(0.5);

    [DataField]
    public TimeSpan KnockdownTime = TimeSpan.FromSeconds(1.5);

    [DataField]
    public float ImpactRestitution = 0.3f;

    [DataField]
    public float PushMultiplier = 1f;

    [DataField]
    public float MinDriftSpeed = 4f;

    [DataField]
    public float DriftSlipThreshold = 0.35f;

    [DataField]
    public ProtoId<DecalPrototype> DriftDecal = "GrosseCarSkid";

    [DataField]
    public float DriftDecalSpacing = 0.35f;

    [DataField]
    public Color DriftDecalColor = Color.Black;

    [DataField]
    public SoundSpecifier? DriftSound;

    [DataField]
    public float EngineSoundSpeed = 0.4f;

    [DataField]
    public SoundSpecifier? ImpactSound = new SoundCollectionSpecifier("MetalThud");

    [AutoNetworkedField]
    public bool Handbrake;

    [AutoNetworkedField]
    public bool IsDrifting;

    [AutoNetworkedField]
    public float DriftSlip;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField]
    public TimeSpan LastImpact;

    public Vector2 LastSkidPosition;
    public EntityUid? EngineSoundEntity;
    public EntityUid? DriftSoundEntity;
}
