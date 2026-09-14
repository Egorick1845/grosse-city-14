namespace Content.Shared._Grosse.Cars;

[RegisterComponent]
public sealed partial class GrosseCarRammableComponent : Component
{
    [DataField]
    public float RamMinSpeed = 5f;

    [DataField]
    public float MassOverride = 800f;
}
