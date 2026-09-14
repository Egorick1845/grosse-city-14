using Robust.Shared.GameStates;

namespace Content.Shared._Grosse.Cars;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GrosseCarRiderComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Car;

    [DataField, AutoNetworkedField]
    public string SlotId = string.Empty;

    [DataField, AutoNetworkedField]
    public bool IsDriver;
}
