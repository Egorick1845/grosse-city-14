using Robust.Shared.Serialization;

namespace Content.Shared._Grosse.Cars;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class GrosseCarSlot
{
    [DataField(required: true)]
    public string Id = string.Empty;

    /// <summary>
    /// Locale id shown on the enter verb.
    /// </summary>
    [DataField(required: true)]
    public LocId Name;

    [DataField(required: true)]
    public string ContainerId = string.Empty;

    [DataField]
    public bool IsDriver;

    [DataField]
    public GrosseCarVisuals? Visuals;

    [DataField]
    public GrosseCarVisualLayers? Layer;
}
