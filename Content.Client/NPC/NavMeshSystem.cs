using Content.Shared.NPC;

namespace Content.Client.NPC;

public sealed class NavMeshSystem : SharedNavMeshSystem
{
    public Dictionary<NetEntity, List<NavMeshRegionDebug>> Regions = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<NavMeshRegionsMessage>(OnNavMeshRegions);
    }

    private void OnNavMeshRegions(NavMeshRegionsMessage ev)
    {
        Regions[ev.GridUid] = ev.Regions;
    }
}
