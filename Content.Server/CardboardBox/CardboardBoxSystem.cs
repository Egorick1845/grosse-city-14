using Content.Shared.CardboardBox;
using Content.Shared.CardboardBox.Components;
using Content.Shared.Stealth;
using Content.Shared.Storage.Components;

namespace Content.Server.CardboardBox;

public sealed partial class CardboardBoxSystem : SharedCardboardBoxSystem
{
    [Dependency] private SharedStealthSystem _stealth = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<CardboardBoxComponent, StorageAfterOpenEvent>(AfterStorageOpen);
    }

    private void AfterStorageOpen(EntityUid uid, CardboardBoxComponent component, ref StorageAfterOpenEvent args)
    {
        // If this box has a stealth/chameleon effect, disable the stealth effect while the box is open.
        _stealth.SetEnabled(uid, false);
    }
}
