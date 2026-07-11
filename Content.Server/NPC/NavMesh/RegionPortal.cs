using System.Numerics;

namespace Content.Server.NPC.NavMesh;

public readonly struct RegionPortal
{
    public readonly int NeighborRegionId;
    public readonly Vector2i Tile;

    public RegionPortal(int neighborRegionId, Vector2i tile)
    {
        NeighborRegionId = neighborRegionId;
        Tile = tile;
    }
}
