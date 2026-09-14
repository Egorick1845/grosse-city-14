using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Effects;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Stunnable;
using Robust.Shared.Audio;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;

namespace Content.Shared._Grosse.Cars;

public sealed partial class SharedGrosseCarSystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedColorFlashEffectSystem _color = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedStunSystem _stun = default!;

    private void InitializeCollision()
    {
        SubscribeLocalEvent<GrosseCarComponent, StartCollideEvent>(OnStartCollide);
    }

    private void OnStartCollide(Entity<GrosseCarComponent> ent, ref StartCollideEvent args)
    {
        if (_timing.ApplyingState)
            return;

        if (!args.OurFixture.Hard || !args.OtherFixture.Hard)
            return;

        if (args.OtherEntity == ent.Owner)
            return;

        if (HasComp<MapGridComponent>(args.OtherEntity))
            return;

        if (TryComp<GrosseCarRiderComponent>(args.OtherEntity, out var rider) && rider.Car == ent.Owner)
            return;

        var now = _timing.CurTime;
        if (now - ent.Comp.LastImpact < ent.Comp.ImpactCooldown)
            return;

        var relVel = args.OurBody.LinearVelocity - args.OtherBody.LinearVelocity;
        var relSpeed = relVel.Length();
        if (relSpeed < 0.15f)
            return;

        var dir = relVel / relSpeed;
        var mCar = Math.Max(args.OurBody.FixturesMass, 1f);

        if (TryComp<GrosseCarRammableComponent>(args.OtherEntity, out var rammable))
        {
            HandleRammable(ent, args.OtherEntity, dir, relSpeed, mCar, rammable);
            return;
        }

        if (HasComp<MobStateComponent>(args.OtherEntity))
        {
            HandleMob(ent, args.OtherEntity, args.OtherBody, dir, relSpeed, mCar);
            return;
        }

        if ((args.OtherFixture.CollisionLayer & (int) CollisionGroup.Impassable) != 0)
            HandleWall(ent, dir, relSpeed, mCar);
    }

    private void HandleMob(
        Entity<GrosseCarComponent> ent,
        EntityUid other,
        PhysicsComponent otherBody,
        Vector2 dir,
        float relSpeed,
        float mCar)
    {
        var mOther = Math.Max(otherBody.FixturesMass, 1f);
        SlowCar(ent, dir, relSpeed, mCar, mOther);

        if (_net.IsServer)
        {
            var push = dir * (mCar / (mCar + mOther) * relSpeed * ent.Comp.PushMultiplier);
            _physics.SetLinearVelocity(other, otherBody.LinearVelocity + push);

            if (relSpeed >= ent.Comp.MinImpactSpeed)
            {
                var scale = relSpeed / ent.Comp.MinImpactSpeed;
                ApplyCarDamage(ent, ent.Comp.SelfDamage * scale);
                _damageable.TryChangeDamage(other, ent.Comp.HitDamage * scale);

                if (relSpeed >= ent.Comp.MinImpactSpeed * 1.5f)
                    _stun.TryKnockdown(other, ent.Comp.KnockdownTime, force: true);
            }
        }

        PlayImpact(ent);
    }

    private void HandleWall(Entity<GrosseCarComponent> ent, Vector2 dir, float relSpeed, float mCar)
    {
        SlowCar(ent, dir, relSpeed, mCar, float.PositiveInfinity);

        if (_net.IsServer && relSpeed >= ent.Comp.MinImpactSpeed)
        {
            var scale = relSpeed / ent.Comp.MinImpactSpeed;
            ApplyCarDamage(ent, ent.Comp.WallDamage * scale);
        }

        PlayImpact(ent);
    }

    private void HandleRammable(
        Entity<GrosseCarComponent> ent,
        EntityUid other,
        Vector2 dir,
        float relSpeed,
        float mCar,
        GrosseCarRammableComponent rammable)
    {
        var mOther = Math.Max(rammable.MassOverride, 1f);
        SlowCar(ent, dir, relSpeed, mCar, mOther);

        if (_net.IsServer && relSpeed >= rammable.RamMinSpeed)
        {
            var scale = relSpeed / Math.Max(rammable.RamMinSpeed, 0.01f);
            _damageable.TryChangeDamage(other, ent.Comp.HitDamage * scale);
            ApplyCarDamage(ent, ent.Comp.SelfDamage * 0.5f * scale);
        }

        PlayImpact(ent);
    }

    private void SlowCar(Entity<GrosseCarComponent> ent, Vector2 dir, float relSpeed, float mCar, float mOther)
    {
        if (!TryComp<PhysicsComponent>(ent.Owner, out var physics))
            return;

        float reduce;
        if (float.IsPositiveInfinity(mOther))
            reduce = 1f;
        else
            reduce = mOther / (mCar + mOther);

        var lost = dir * (relSpeed * reduce * (1f - ent.Comp.ImpactRestitution));
        _physics.SetLinearVelocity(ent.Owner, physics.LinearVelocity - lost);
        _physics.WakeBody(ent.Owner);
        ent.Comp.LastImpact = _timing.CurTime;
        Dirty(ent.Owner, ent.Comp);
    }

    private void ApplyCarDamage(Entity<GrosseCarComponent> ent, DamageSpecifier damage)
    {
        _damageable.TryChangeDamage(ent.Owner, damage);

        if (ent.Comp.OccupantDamageMultiplier <= 0f)
            return;

        var occupantDamage = damage * ent.Comp.OccupantDamageMultiplier;
        foreach (var slot in ent.Comp.Slots)
        {
            if (!_container.TryGetContainer(ent.Owner, slot.ContainerId, out var container))
                continue;

            foreach (var occupant in container.ContainedEntities)
            {
                _damageable.TryChangeDamage(occupant, occupantDamage);
            }
        }
    }

    private void PlayImpact(Entity<GrosseCarComponent> ent)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        if (ent.Comp.ImpactSound != null)
        {
            _audio.PlayPredicted(ent.Comp.ImpactSound, ent.Owner, null,
                AudioParams.Default.WithVariation(0.125f).WithVolume(-0.125f));
        }

        _color.RaiseEffect(Color.Red, new List<EntityUid> { ent.Owner }, Filter.Pvs(ent.Owner, entityManager: EntityManager));
    }
}
