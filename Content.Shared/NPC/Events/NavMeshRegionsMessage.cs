using Robust.Shared.Serialization;

namespace Content.Shared.NPC;

[Serializable, NetSerializable]
public sealed class NavMeshRegionsMessage : EntityEventArgs
{
    public NetEntity GridUid;
    public List<NavMeshRegionDebug> Regions = new();
}

[Serializable, NetSerializable]
public readonly struct NavMeshRegionDebug
{
    public readonly int Id;
    public readonly Box2 AABB;
    public readonly List<Vector2i> Tiles;

    public NavMeshRegionDebug(int id, Box2 aabb, List<Vector2i> tiles)
    {
        Id = id;
        AABB = aabb;
        Tiles = tiles;
    }
}
