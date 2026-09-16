#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Grosse.Emplacement;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Movement.Components;
using Content.Shared.Vehicle.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Grosse;

[TestFixture]
[TestOf(typeof(GrosseEmplacementSystem))]
public sealed class GrosseEmplacementTest : GameTest
{
    private const string DummyId = "GrosseEmplacementTestDummy";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {DummyId}
  name: {DummyId}
  components:
  - type: Buckle
  - type: Hands
  - type: ComplexInteraction
  - type: InputMover
  - type: Physics
    bodyType: KinematicController
  - type: Body
    prototype: Human
  - type: MobState
  - type: StandingState
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: Fixtures
    fixtures:
      fix1:
        shape:
          !type:PhysShapeCircle
          radius: 0.35
        density: 80
        mask:
        - MobMask
        layer:
        - MobLayer
";

    [Test]
    public async Task ShotDoesNotHitTurretOrGunnerAndGunnerSitsBehind()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var coords = map.GridCoords;
        var entityManager = server.EntMan;
        var buckle = entityManager.System<SharedBuckleSystem>();
        var guns = entityManager.System<SharedGunSystem>();
        var damageable = entityManager.System<DamageableSystem>();
        var xform = entityManager.System<SharedTransformSystem>();

        EntityUid turret = default;
        EntityUid gunner = default;

        await server.WaitAssertion(() =>
        {
            turret = entityManager.SpawnEntity("WeaponEmplacementCombine", coords);
            gunner = entityManager.SpawnEntity(DummyId, coords);

            Assert.That(buckle.TryBuckle(gunner, gunner, turret), Is.True, "gunner should buckle to the emplacement");
            Assert.That(entityManager.TryGetComponent(turret, out VehicleComponent? vehicle));
            Assert.That(vehicle!.Operator, Is.EqualTo(gunner));
            Assert.That(entityManager.HasComponent<GrosseEmplacementCoverComponent>(gunner), Is.True);
            Assert.That(entityManager.GetComponent<InputMoverComponent>(turret).CanMove, Is.False, "emplacement must stay stationary");
            Assert.That(guns.TryGetGun(gunner, out var gun) && gun.Owner == turret, Is.True, "TryGetGun should return the emplacement");
        });

        await server.WaitRunTicks(5);

        var ammoBefore = 0;
        await server.WaitAssertion(() =>
        {
            ammoBefore = guns.GetAmmoCount(turret);
            Assert.That(ammoBefore, Is.GreaterThan(0), "emplacement spawned with no ammo");

            var gun = entityManager.GetComponent<GunComponent>(turret);
            var target = new EntityCoordinates(turret, new Vector2(10f, 0f));
            Assert.That(guns.AttemptShoot(gunner, (turret, gun), target), Is.True, "emplacement failed to fire");
        });

        await server.WaitRunTicks(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(guns.GetAmmoCount(turret), Is.LessThan(ammoBefore), "shot did not consume ammo");
            Assert.That(damageable.GetTotalDamage(turret), Is.EqualTo(FixedPoint2.Zero), "shot hit the emplacement");
            Assert.That(damageable.GetTotalDamage(gunner), Is.EqualTo(FixedPoint2.Zero), "shot hit the gunner");

            var turretPos = xform.GetWorldPosition(turret);
            var gunnerPos = xform.GetWorldPosition(gunner);
            var delta = gunnerPos - turretPos;
            var local = (-xform.GetWorldRotation(turret)).RotateVec(delta);

            Assert.That(delta.Length(), Is.GreaterThan(0.3f), "gunner is inside the emplacement sprite");
            Assert.That(local.Y, Is.GreaterThan(0.3f), "gunner should sit behind the emplacement along buckleOffset +Y");
        });
    }

    [Test]
    public async Task IncomingDamageIsSplitEvenly()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var coords = map.GridCoords;
        var entityManager = server.EntMan;
        var buckle = entityManager.System<SharedBuckleSystem>();
        var damageable = entityManager.System<DamageableSystem>();

        EntityUid turret = default;
        EntityUid gunner = default;

        await server.WaitAssertion(() =>
        {
            turret = entityManager.SpawnEntity("WeaponEmplacementCombine", coords);
            gunner = entityManager.SpawnEntity(DummyId, coords);
            Assert.That(buckle.TryBuckle(gunner, gunner, turret), Is.True);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var hitTurret = new DamageSpecifier { DamageDict = { ["Blunt"] = 20 } };
            Assert.That(damageable.TryChangeDamage(turret, hitTurret), Is.True);

            // Dummy has no armor, so it receives an exact half of the incoming hit.
            Assert.That(damageable.GetTotalDamage(gunner), Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(damageable.GetTotalDamage(turret), Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(damageable.GetTotalDamage(turret), Is.LessThan(FixedPoint2.New(20)));
        });

        await server.WaitAssertion(() =>
        {
            var hitGunner = new DamageSpecifier { DamageDict = { ["Blunt"] = 20 } };
            Assert.That(damageable.TryChangeDamage(gunner, hitGunner), Is.True);

            Assert.That(damageable.GetTotalDamage(gunner), Is.EqualTo(FixedPoint2.New(20)));
            Assert.That(damageable.GetTotalDamage(turret), Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(damageable.GetTotalDamage(turret), Is.LessThan(FixedPoint2.New(20)));
        });
    }
}
