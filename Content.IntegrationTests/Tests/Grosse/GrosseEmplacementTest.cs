#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Grosse.Emplacement;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Server.Hands.Systems;
using Content.Shared.Foldable;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Components;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Movement.Components;
using Content.Shared.Projectiles;
using Content.Shared.Vehicle.Components;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
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
    hands:
      right:
        location: Right
      left:
        location: Left
    sortedHands:
    - right
    - left
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
            var target = new EntityCoordinates(turret, new Vector2(0f, -10f));
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

    [Test]
    public async Task UnfoldFacesTheDeployer()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var coords = map.GridCoords;
        var entityManager = server.EntMan;
        var foldable = entityManager.System<FoldableSystem>();
        var xform = entityManager.System<SharedTransformSystem>();

        await server.WaitAssertion(() =>
        {
            var turret = entityManager.SpawnEntity("WeaponEmplacementCombineFolded", coords);
            var deployer = entityManager.SpawnEntity(DummyId, coords.Offset(new Vector2(1f, 0f)));
            var facing = Angle.FromDegrees(90);

            xform.SetWorldRotation(deployer, facing);
            Assert.That(entityManager.TryGetComponent(turret, out FoldableComponent? fold));
            Assert.That(fold!.IsFolded, Is.True);
            Assert.That(foldable.TrySetFolded(turret, fold, false, deployer), Is.True, "should unfold");
            Assert.That(xform.GetWorldRotation(turret).EqualsApprox(facing), Is.True, "emplacement should face the deployer");

            // Copy first: calling a method on the field is Execute, which RA0002 forbids from tests.
            var deployed = entityManager.GetComponent<GrosseEmplacementComponent>(turret).DeployedRotation;
            Assert.That(deployed.EqualsApprox(facing), Is.True);
        });
    }

    [Test]
    public async Task ShotIsClampedToYawCone()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var coords = map.GridCoords;
        var entityManager = server.EntMan;
        var buckle = entityManager.System<SharedBuckleSystem>();
        var guns = entityManager.System<SharedGunSystem>();
        var xform = entityManager.System<SharedTransformSystem>();

        EntityUid turret = default;
        EntityUid gunner = default;

        await server.WaitAssertion(() =>
        {
            turret = entityManager.SpawnEntity("WeaponEmplacementCombine", coords);
            gunner = entityManager.SpawnEntity(DummyId, coords);
            Assert.That(buckle.TryBuckle(gunner, gunner, turret), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var gun = entityManager.GetComponent<GunComponent>(turret);
            // East of a south-facing emplacement is well outside ±22.5°.
            var target = new EntityCoordinates(turret, new Vector2(10f, 0f));
            Assert.That(guns.AttemptShoot(gunner, (turret, gun), target), Is.True, "emplacement failed to fire");

            var turretPos = xform.GetWorldPosition(turret);
            var found = false;
            var query = entityManager.EntityQueryEnumerator<ProjectileComponent, PhysicsComponent, TransformComponent>();
            while (query.MoveNext(out _, out _, out var physics, out var projectileXform))
            {
                if ((xform.GetWorldPosition(projectileXform) - turretPos).LengthSquared() > 25f)
                    continue;

                found = true;
                Assert.That(physics.LinearVelocity.Y, Is.LessThan(-1f), "clamped shot should travel south");
                Assert.That(Math.Abs(physics.LinearVelocity.X), Is.LessThan(Math.Abs(physics.LinearVelocity.Y)),
                    "shot aimed east must stay inside the south-facing yaw cone");
            }

            Assert.That(found, Is.True, "emplacement did not spawn a projectile");
        });
    }

    [Test]
    public async Task FoldedEmplacementCannotFireFromHands()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var coords = map.GridCoords;
        var entityManager = server.EntMan;
        var hands = entityManager.System<SharedHandsSystem>();
        var guns = entityManager.System<SharedGunSystem>();

        await server.WaitAssertion(() =>
        {
            var turret = entityManager.SpawnEntity("WeaponEmplacementCombineFolded", coords);
            var carrier = entityManager.SpawnEntity(DummyId, coords);

            var isFolded = entityManager.GetComponent<FoldableComponent>(turret).IsFolded;
            Assert.That(isFolded, Is.True);
            Assert.That(hands.TryPickupAnyHand(carrier, turret), Is.True, "folded emplacement should be portable");
            Assert.That(guns.TryGetGun(carrier, out var held) && held.Owner == turret, Is.True,
                "carrying the folded emplacement still exposes its Gun");

            var ammoBefore = guns.GetAmmoCount(turret);
            Assert.That(ammoBefore, Is.GreaterThan(0), "emplacement spawned with no ammo");

            var gun = entityManager.GetComponent<GunComponent>(turret);
            var target = new EntityCoordinates(turret, new Vector2(0f, -10f));
            Assert.That(guns.AttemptShoot(carrier, (turret, gun), target), Is.False,
                "folded emplacement must not fire from hands");
            Assert.That(guns.GetAmmoCount(turret), Is.EqualTo(ammoBefore), "folded shot consumed ammo");
        });
    }

    [Test]
    public async Task UnfoldedEmplacementCannotFireWithoutGunner()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var coords = map.GridCoords;
        var entityManager = server.EntMan;
        var guns = entityManager.System<SharedGunSystem>();

        await server.WaitAssertion(() =>
        {
            var turret = entityManager.SpawnEntity("WeaponEmplacementCombine", coords);
            var bystander = entityManager.SpawnEntity(DummyId, coords);

            var ammoBefore = guns.GetAmmoCount(turret);
            Assert.That(ammoBefore, Is.GreaterThan(0), "emplacement spawned with no ammo");

            var gun = entityManager.GetComponent<GunComponent>(turret);
            var target = new EntityCoordinates(turret, new Vector2(0f, -10f));
            Assert.That(guns.AttemptShoot(bystander, (turret, gun), target), Is.False,
                "emplacement must only fire when manned");
            Assert.That(guns.GetAmmoCount(turret), Is.EqualTo(ammoBefore), "unmanned shot consumed ammo");
        });
    }

    [Test]
    public async Task GunnerCannotDropOrThrowVirtualItems()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var coords = map.GridCoords;
        var entityManager = server.EntMan;
        var buckle = entityManager.System<SharedBuckleSystem>();
        var hands = entityManager.System<SharedHandsSystem>();
        var throwHands = entityManager.System<HandsSystem>();

        EntityUid turret = default;
        EntityUid gunner = default;

        await server.WaitAssertion(() =>
        {
            turret = entityManager.SpawnEntity("WeaponEmplacementCombine", coords);
            gunner = entityManager.SpawnEntity(DummyId, coords);
            Assert.That(buckle.TryBuckle(gunner, gunner, turret), Is.True);
            Assert.That(hands.GetHandCount(gunner), Is.GreaterThan(0));
            Assert.That(CountBlockingVirtuals(entityManager, gunner, turret), Is.EqualTo(hands.GetHandCount(gunner)),
                "every gunner hand should be occupied by the emplacement");
            Assert.That(hands.CountFreeHands(gunner), Is.EqualTo(0));

            foreach (var held in hands.EnumerateHeld(gunner))
            {
                Assert.That(entityManager.HasComponent<UnremoveableComponent>(held), Is.True);
                var blocking = entityManager.GetComponent<VirtualItemComponent>(held).BlockingEntity;
                Assert.That(blocking, Is.EqualTo(turret));
                Assert.That(hands.TryDrop(gunner, held), Is.False, "gunner must not be able to drop occupancy virtual items");
            }

            Assert.That(CountBlockingVirtuals(entityManager, gunner, turret), Is.EqualTo(hands.GetHandCount(gunner)));
            Assert.That(throwHands.ThrowHeldItem(gunner, coords.Offset(new Vector2(1f, 0f))), Is.False,
                "gunner must not be able to throw occupancy virtual items");
            Assert.That(CountBlockingVirtuals(entityManager, gunner, turret), Is.EqualTo(hands.GetHandCount(gunner)));

            var crowbar = entityManager.SpawnEntity("Crowbar", coords);
            Assert.That(hands.TryPickupAnyHand(gunner, crowbar), Is.False, "occupied hands must not pick up other items");
        });

        await server.WaitRunTicks(15);

        await server.WaitAssertion(() =>
        {
            Assert.That(buckle.TryUnbuckle(gunner, gunner), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(CountBlockingVirtuals(entityManager, gunner, turret), Is.EqualTo(0));
            var crowbar = entityManager.SpawnEntity("Crowbar", coords);
            Assert.That(hands.TryPickupAnyHand(gunner, crowbar), Is.True, "gunner should use hands after leaving the emplacement");
        });
    }

    private static int CountBlockingVirtuals(IEntityManager entityManager, EntityUid user, EntityUid blocking)
    {
        var hands = entityManager.System<SharedHandsSystem>();
        var count = 0;
        foreach (var held in hands.EnumerateHeld(user))
        {
            if (!entityManager.TryGetComponent(held, out VirtualItemComponent? virt))
                continue;

            var blockingEntity = virt.BlockingEntity;
            if (blockingEntity == blocking)
                count++;
        }

        return count;
    }
}
