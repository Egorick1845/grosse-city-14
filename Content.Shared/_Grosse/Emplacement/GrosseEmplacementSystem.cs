using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Foldable;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.MouseRotator;
using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Shared._Grosse.Emplacement;

public sealed partial class GrosseEmplacementSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedVirtualItemSystem _virtual = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private VehicleSystem _vehicle = default!;

    private readonly HashSet<EntityUid> _splitting = [];

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GrosseEmplacementComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<GrosseEmplacementComponent, FoldedEvent>(OnFolded);
        SubscribeLocalEvent<GrosseEmplacementComponent, FoldAttemptEvent>(OnFoldAttempt);
        SubscribeLocalEvent<GrosseEmplacementComponent, VehicleCanRunEvent>(OnCanRun);
        SubscribeLocalEvent<GrosseEmplacementComponent, VehicleOperatorSetEvent>(OnOperatorSet);
        SubscribeLocalEvent<GrosseEmplacementComponent, MouseRotatorRotationEvent>(OnMouseRotation);
        SubscribeLocalEvent<GrosseEmplacementComponent, ShotAttemptedEvent>(OnShotAttempted);
        SubscribeLocalEvent<GrosseEmplacementComponent, BeforeDamageChangedEvent>(OnEmplacementBeforeDamage);
        SubscribeLocalEvent<GrosseEmplacementCoverComponent, BeforeDamageChangedEvent>(OnCoverBeforeDamage);
    }

    private void OnMapInit(Entity<GrosseEmplacementComponent> ent, ref MapInitEvent args)
    {
        CaptureDeployedRotation(ent);
    }

    private void OnFolded(Entity<GrosseEmplacementComponent> ent, ref FoldedEvent args)
    {
        if (args.IsFolded)
            return;

        if (args.User is { } user)
            FaceDeployer(ent, user);

        CaptureDeployedRotation(ent);
    }

    private void OnFoldAttempt(Entity<GrosseEmplacementComponent> ent, ref FoldAttemptEvent args)
    {
        if (_vehicle.HasOperator(ent.Owner))
            args.Cancelled = true;
    }

    private void OnCanRun(Entity<GrosseEmplacementComponent> ent, ref VehicleCanRunEvent args)
    {
        args = args with { CanRun = false };
    }

    private void OnOperatorSet(Entity<GrosseEmplacementComponent> ent, ref VehicleOperatorSetEvent args)
    {
        if (_timing.ApplyingState)
            return;

        if (args.OldOperator is { } oldOperator)
        {
            _virtual.DeleteInHandsMatching(oldOperator, ent.Owner);
            RemComp<GrosseEmplacementCoverComponent>(oldOperator);
        }

        if (args.NewOperator is not { } newOperator)
            return;

        _virtual.TryOccupyHands(ent.Owner, newOperator);

        var cover = EnsureComp<GrosseEmplacementCoverComponent>(newOperator);
        cover.Emplacement = ent.Owner;
        Dirty(newOperator, cover);
    }

    private void OnMouseRotation(Entity<GrosseEmplacementComponent> ent, ref MouseRotatorRotationEvent args)
    {
        var requested = args.Rotation + ent.Comp.VisualRotationOffset;
        args.Rotation = ClampYaw(ent.Comp.DeployedRotation, requested, ent.Comp.MaxYawDeviation);
    }

    private void OnShotAttempted(Entity<GrosseEmplacementComponent> ent, ref ShotAttemptedEvent args)
    {
        if (args.Cancelled)
            return;

        // Folded / carried emplacements are still a Gun in-hand, but they only fire when manned.
        if (TryComp<FoldableComponent>(ent, out var foldable) && foldable.IsFolded)
        {
            args.Cancel();
            return;
        }

        if (!_vehicle.TryGetOperator(ent.Owner, out var operatorEnt) || operatorEnt.Value.Owner != args.User)
        {
            args.Cancel();
            return;
        }

        var origin = _transform.GetMapCoordinates(args.User);
        var to = _transform.ToMapCoordinates(args.Coordinates);
        var distance = (to.Position - origin.Position).Length();
        if (distance < 0.01f)
            distance = 1f;

        var requested = (to.Position - _transform.GetMapCoordinates(ent.Owner).Position).ToWorldAngle()
                        + ent.Comp.VisualRotationOffset;
        var yaw = ClampYaw(ent.Comp.DeployedRotation, requested, ent.Comp.MaxYawDeviation);
        args.Coordinates = _transform.ToCoordinates(new MapCoordinates(origin.Position + yaw.ToWorldVec() * distance, origin.MapId));
    }

    private void OnEmplacementBeforeDamage(Entity<GrosseEmplacementComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive() || _splitting.Contains(ent.Owner))
            return;

        if (!_vehicle.TryGetOperator(ent.Owner, out var operatorEnt))
            return;

        if (args.Origin == operatorEnt.Value.Owner)
            return;

        SplitIncoming(ref args, ent.Owner, operatorEnt.Value.Owner, ent.Comp.DamageSplit);
    }

    private void OnCoverBeforeDamage(Entity<GrosseEmplacementCoverComponent> ent, ref BeforeDamageChangedEvent args)
    {
        if (args.Cancelled || !args.Damage.AnyPositive() || _splitting.Contains(ent.Owner))
            return;

        var emplacement = ent.Comp.Emplacement;
        if (!Exists(emplacement) || !TryComp<GrosseEmplacementComponent>(emplacement, out var emplacementComp))
            return;

        if (args.Origin == emplacement)
            return;

        SplitIncoming(ref args, ent.Owner, emplacement, emplacementComp.DamageSplit);
    }

    private void SplitIncoming(ref BeforeDamageChangedEvent args, EntityUid first, EntityUid second, float split)
    {
        var incoming = new DamageSpecifier(args.Damage);
        foreach (var (type, amount) in args.Damage.DamageDict.ToArray())
        {
            args.Damage.DamageDict[type] = amount * split;
        }

        ApplySplitShare(second, incoming * split, first);
    }

    private void ApplySplitShare(EntityUid target, DamageSpecifier share, EntityUid? origin)
    {
        _splitting.Add(target);
        _damageable.TryChangeDamage(target, share, origin: origin);
        _splitting.Remove(target);
    }

    private void CaptureDeployedRotation(Entity<GrosseEmplacementComponent> ent)
    {
        ent.Comp.DeployedRotation = _transform.GetWorldRotation(ent.Owner);
        Dirty(ent);
    }

    private void FaceDeployer(Entity<GrosseEmplacementComponent> ent, EntityUid user)
    {
        var facing = _transform.GetWorldRotation(user) + ent.Comp.VisualRotationOffset;
        _transform.SetWorldRotation(ent.Owner, facing);
    }

    private static Angle ClampYaw(Angle deployed, Angle requested, Angle maxDeviation)
    {
        var deviation = Angle.ShortestDistance(deployed, requested);
        if (Math.Abs(deviation.Theta) <= maxDeviation.Theta)
            return requested;

        return deployed + new Angle(Math.Sign(deviation.Theta) * maxDeviation.Theta);
    }
}
