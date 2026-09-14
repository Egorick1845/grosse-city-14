using Content.Server.Decals;
using Content.Shared._Grosse.Cars;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;

namespace Content.Server._Grosse.Cars;

public sealed partial class GrosseCarSkidSystem : EntitySystem
{
    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<GrosseCarComponent, TransformComponent, PhysicsComponent>();
        while (query.MoveNext(out var uid, out var car, out var xform, out _))
        {
            if (!car.IsDrifting)
                continue;

            var worldPos = _transform.GetWorldPosition(uid);
            if ((worldPos - car.LastSkidPosition).Length() < car.DriftDecalSpacing && car.LastSkidPosition != default)
                continue;

            car.LastSkidPosition = worldPos;
            var coords = _transform.GetMoverCoordinates(uid, xform);
            var rotation = _transform.GetWorldRotation(xform);
            var right = (rotation + Angle.FromDegrees(90)).ToWorldVec() * 0.3f;

            TrySkid(car, coords.Offset(right), rotation);
            TrySkid(car, coords.Offset(-right), rotation);
        }
    }

    private void TrySkid(GrosseCarComponent car, EntityCoordinates coords, Angle rotation)
    {
        _decals.TryAddDecal(car.DriftDecal, coords, out _, car.DriftDecalColor, rotation, zIndex: -4, cleanable: true);
    }
}
