using System.Numerics;
using Content.Shared.Damage.Systems;
using Content.Shared.Friction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Controllers;

namespace Content.Shared._Grosse.Cars;

public sealed partial class SharedGrosseCarController : VirtualController
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedMoverController _mover = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(SharedMoverController));
        UpdatesBefore.Add(typeof(TileFrictionController));
        base.Initialize();
    }

    public override void UpdateBeforeSolve(bool prediction, float frameTime)
    {
        base.UpdateBeforeSolve(prediction, frameTime);

        var query = EntityQueryEnumerator<GrosseCarComponent, PhysicsComponent, TransformComponent, InputMoverComponent>();
        while (query.MoveNext(out var uid, out var car, out var physics, out var xform, out var mover))
        {
            if (prediction && !physics.Predict)
                continue;

            Step(uid, car, physics, xform, mover, frameTime);
        }
    }

    private void Step(
        EntityUid uid,
        GrosseCarComponent car,
        PhysicsComponent physics,
        TransformComponent xform,
        InputMoverComponent mover,
        float frameTime)
    {
        var buttons = SharedMoverController.GetNormalizedMovement(mover.HeldMoveButtons);
        var throttle = (buttons & MoveButtons.Up) != 0;
        var reverseOrBrake = (buttons & MoveButtons.Down) != 0;
        var steerLeft = (buttons & MoveButtons.Left) != 0;
        var steerRight = (buttons & MoveButtons.Right) != 0;

        var heading = TransformSystem.GetWorldRotation(xform) - car.VisualRotationOffset;
        var facing = heading.ToWorldVec();
        if (facing.LengthSquared() < 0.0001f)
            facing = Vector2.UnitX;
        else
            facing = facing.Normalized();

        var velocity = physics.LinearVelocity;
        var speed = Vector2.Dot(velocity, facing);
        var mass = Math.Max(physics.FixturesMass, 1f);
        var wrecked = _damageable.GetTotalDamage(uid).Float() >= car.MaxDriveDamage;

        if (throttle && !wrecked)
            speed = Math.Min(car.MaxForwardSpeed, speed + car.EngineForce / mass * frameTime);
        else if (reverseOrBrake)
        {
            if (speed > 0.15f)
                speed = Math.Max(0f, speed - car.BrakeForce / mass * frameTime);
            else if (!wrecked)
                speed = Math.Max(-car.MaxReverseSpeed, speed - car.EngineForce / mass * frameTime);
        }
        else if (Math.Abs(speed) > 0.001f)
        {
            var friction = car.Friction * frameTime;
            if (car.Handbrake)
                friction += car.HandbrakeForce / mass * frameTime;

            if (Math.Abs(speed) <= friction)
                speed = 0f;
            else
                speed -= Math.Sign(speed) * friction;
        }

        var absSpeed = Math.Abs(speed);
        if (absSpeed >= car.MinSteerSpeed && (steerLeft || steerRight) && steerLeft != steerRight)
        {
            var maxSpeed = Math.Max(car.MaxForwardSpeed, 0.01f);
            var steer = car.SteerRate * (absSpeed / maxSpeed) * frameTime;
            if (steerRight)
                steer = -steer;
            if (speed < 0f)
                steer = -steer;

            heading += steer;
            facing = heading.ToWorldVec().Normalized();
        }

        var desired = facing * speed;
        var grip = car.Handbrake ? car.HandbrakeGrip : car.Grip;
        var lerp = Math.Clamp(grip * frameTime, 0f, 1f);
        velocity = Vector2.Lerp(velocity, desired, lerp);

        var velSpeed = velocity.Length();
        var slip = 0f;
        var drifting = false;
        if (velSpeed >= car.MinDriftSpeed && facing.LengthSquared() > 0.0001f && velSpeed > 0.0001f)
        {
            var cos = Vector2.Dot(facing, velocity / velSpeed);
            slip = MathF.Acos(Math.Clamp(cos, -1f, 1f));
            drifting = slip > car.DriftSlipThreshold;
        }

        if (car.IsDrifting != drifting || Math.Abs(car.DriftSlip - slip) > 0.01f)
        {
            car.IsDrifting = drifting;
            car.DriftSlip = slip;
            Dirty(uid, car);
        }

        PhysicsSystem.SetLinearVelocity(uid, velocity);
        TransformSystem.SetWorldRotation(uid, heading + car.VisualRotationOffset);

        if (velocity.LengthSquared() > 0.0001f)
            PhysicsSystem.WakeBody(uid);

        _mover.UsedMobMovement[uid] = true;
    }
}
