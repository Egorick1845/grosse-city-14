#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared._Grosse.Cars;
using Content.Shared.Movement.Components;
using Content.Shared.Prototypes;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Grosse;

[TestFixture]
[TestOf(typeof(SharedGrosseCarSystem))]
public sealed class GrosseCarTest : GameTest
{
    private const string DummyId = "GrosseCarTestDummy";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: entity
  id: {DummyId}
  name: {DummyId}
  components:
  - type: Hands
  - type: ComplexInteraction
  - type: InputMover
  - type: Physics
    bodyType: KinematicController
  - type: Body
    prototype: Human
  - type: MobState
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
    public async Task PrototypeConfigIsValid()
    {
        var pair = Pair;
        var server = pair.Server;
        var protoMan = server.ResolveDependency<IPrototypeManager>();
        var factory = server.ResolveDependency<IComponentFactory>();
        var loc = server.ResolveDependency<ILocalizationManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var proto in protoMan.EnumeratePrototypes<EntityPrototype>())
                {
                    if (proto.Abstract || pair.IsTestPrototype(proto))
                        continue;

                    if (!proto.TryGetComponent(out GrosseCarComponent? car, factory))
                        continue;

                    Assert.That(car.Slots, Is.Not.Empty, $"{proto.ID} has no seats");
                    Assert.That(car.Slots.Select(s => s.Id).Distinct().Count(), Is.EqualTo(car.Slots.Count), $"{proto.ID} has duplicate slot ids");
                    Assert.That(car.Slots.Select(s => s.ContainerId).Distinct().Count(), Is.EqualTo(car.Slots.Count), $"{proto.ID} has duplicate container ids");
                    Assert.That(car.Slots.Count(s => s.IsDriver), Is.EqualTo(1), $"{proto.ID} must have exactly one driver slot");
                    Assert.That(car.EngineForce, Is.GreaterThan(0f), $"{proto.ID} engineForce");
                    Assert.That(car.MaxForwardSpeed, Is.GreaterThan(0f), $"{proto.ID} maxForwardSpeed");
                    Assert.That(proto.HasComponent<SkipMobMovementComponent>(factory), $"{proto.ID} missing SkipMobMovement");
                    Assert.That(proto.HasComponent<InputMoverComponent>(factory), $"{proto.ID} missing InputMover");
                    Assert.That(proto.HasComponent<AppearanceComponent>(factory), $"{proto.ID} missing Appearance");
                    Assert.That(proto.TryGetComponent(out PhysicsComponent? physics, factory), $"{proto.ID} missing Physics");
                    Assert.That(physics!.BodyType, Is.EqualTo(BodyType.KinematicController), $"{proto.ID} must be KinematicController");
                    Assert.That(proto.TryGetComponent(out ContainerManagerComponent? containers, factory), $"{proto.ID} missing ContainerContainer");

                    foreach (var slot in car.Slots)
                    {
                        Assert.That(loc.HasString(slot.Name), $"{proto.ID} slot {slot.Id} missing locale {slot.Name}");
                        Assert.That(containers!.Containers.ContainsKey(slot.ContainerId), $"{proto.ID} missing container {slot.ContainerId}");
                        Assert.That(containers.Containers[slot.ContainerId], Is.TypeOf<ContainerSlot>(), $"{proto.ID} {slot.ContainerId} is not a ContainerSlot");
                    }

                    foreach (var action in car.OccupantActions)
                    {
                        Assert.That(protoMan.HasIndex(action), $"{proto.ID} occupant action {action} missing");
                    }
                }
            });
        });
    }

    [Test]
    public async Task CanEnterAndExitKraz()
    {
        var pair = Pair;
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var coords = map.GridCoords;
        var entityManager = server.EntMan;
        var cars = entityManager.System<SharedGrosseCarSystem>();
        var containers = entityManager.System<SharedContainerSystem>();

        await server.WaitAssertion(() =>
        {
            var car = entityManager.SpawnEntity("VehicleKraz17", coords);
            var driver = entityManager.SpawnEntity(DummyId, coords);
            var passenger = entityManager.SpawnEntity(DummyId, coords);
            var passenger2 = entityManager.SpawnEntity(DummyId, coords);
            var extra = entityManager.SpawnEntity(DummyId, coords);

            Assert.That(cars.TryEnterSlot(driver, car, "driver", skipDelay: true), Is.True);
            Assert.That(entityManager.TryGetComponent(driver, out GrosseCarRiderComponent? driverRider));
            Assert.That(driverRider!.IsDriver, Is.True);
            Assert.That(entityManager.HasComponent<RelayInputMoverComponent>(driver), Is.True);
            Assert.That(containers.TryGetContainer(car, "car-driver", out var driverSlot) && driverSlot.Contains(driver));

            Assert.That(cars.TryEnterSlot(passenger, car, "passenger", skipDelay: true), Is.True);
            Assert.That(entityManager.TryGetComponent(passenger, out GrosseCarRiderComponent? passRider));
            Assert.That(passRider!.IsDriver, Is.False);
            Assert.That(entityManager.HasComponent<RelayInputMoverComponent>(passenger), Is.False);

            Assert.That(cars.TryEnterSlot(passenger2, car, "passenger2", skipDelay: true), Is.True);
            Assert.That(cars.TryEnterSlot(extra, car, skipDelay: true), Is.False);

            Assert.That(cars.TryEject((car, entityManager.GetComponent<GrosseCarComponent>(car)), driver, driver, skipDelay: true), Is.True);
            Assert.That(entityManager.HasComponent<GrosseCarRiderComponent>(driver), Is.False);
            Assert.That(entityManager.HasComponent<RelayInputMoverComponent>(driver), Is.False);
            Assert.That(containers.TryGetContainer(car, "car-driver", out var emptyDriver) && emptyDriver.ContainedEntities.Count == 0);
        });
    }
}
