using System.Numerics;
using Content.Shared.CardboardBox.Components;
using Content.Shared.Interaction;
using Content.Shared.Stealth;
using Content.Shared.Stealth.Components;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Vehicle;
using Content.Shared.Vehicle.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Shared.CardboardBox;

public abstract partial class SharedCardboardBoxSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedEntityStorageSystem _storage = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;
    [Dependency] private VehicleSystem _vehicle = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CardboardBoxComponent, ActivateInWorldEvent>(OnInteracted);
        SubscribeLocalEvent<CardboardBoxComponent, StorageBeforeOpenEvent>(BeforeStorageOpen);
        SubscribeLocalEvent<CardboardBoxComponent, StorageAfterCloseEvent>(AfterStorageClosed);
        SubscribeLocalEvent<CardboardBoxComponent, VehicleOperatorSetEvent>(OnOperatorSet);
    }

    private void OnInteracted(Entity<CardboardBoxComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<EntityStorageComponent>(ent, out var box))
            return;

        if (!args.Complex)
        {
            if (box.Open || !box.Contents.Contains(args.User))
                return;
        }

        args.Handled = true;
        _storage.ToggleOpen(args.User, ent, box);
    }

    private void BeforeStorageOpen(Entity<CardboardBoxComponent> ent, ref StorageBeforeOpenEvent args)
    {
        if (ent.Comp.Quiet)
            return;

        if (!_vehicle.TryGetOperator(ent.Owner, out var operatorEnt))
            return;

        if (_timing.CurTime <= ent.Comp.EffectCooldown)
            return;

        if (_net.IsServer)
            RaiseNetworkEvent(new PlayBoxEffectMessage(GetNetEntity(ent), GetNetEntity(operatorEnt.Value.Owner)));

        _audio.PlayPredicted(ent.Comp.EffectSound, ent, null);
        ent.Comp.EffectCooldown = _timing.CurTime + ent.Comp.CooldownDuration;
    }

    private void AfterStorageClosed(Entity<CardboardBoxComponent> ent, ref StorageAfterCloseEvent args)
    {
        if (TryComp(ent, out StealthComponent? stealth))
        {
            _stealth.SetVisibility(ent, stealth.MaxVisibility, stealth);
            _stealth.SetEnabled(ent, true, stealth);
        }
    }

    private void OnOperatorSet(Entity<CardboardBoxComponent> ent, ref VehicleOperatorSetEvent args)
    {
        if (args.NewOperator != null || args.OldOperator == null)
            return;

        _physics.SetLinearVelocity(ent, Vector2.Zero);
    }
}
