using System.Linq;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Foldable;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.MouseRotator;
using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;
using Robust.Shared.Timing;

namespace Content.Shared._Grosse.Emplacement;

public sealed partial class GrosseEmplacementSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
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

        foreach (var _ in _hands.EnumerateHands(newOperator))
        {
            if (!_virtual.TrySpawnVirtualItemInHand(ent.Owner, newOperator, dropOthers: true, silent: true))
                break;
        }

        var cover = EnsureComp<GrosseEmplacementCoverComponent>(newOperator);
        cover.Emplacement = ent.Owner;
        Dirty(newOperator, cover);
    }

    private void OnMouseRotation(Entity<GrosseEmplacementComponent> ent, ref MouseRotatorRotationEvent args)
    {
        args.Rotation = ClampYaw(ent.Comp.DeployedRotation, args.Rotation, ent.Comp.MaxYawDeviation);
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

    private static Angle ClampYaw(Angle deployed, Angle requested, Angle maxDeviation)
    {
        var deviation = Angle.ShortestDistance(requested, deployed);
        if (Math.Abs(deviation.Theta) <= maxDeviation.Theta)
            return requested;

        return deployed + new Angle(Math.Sign(deviation.Theta) * maxDeviation.Theta);
    }
}
