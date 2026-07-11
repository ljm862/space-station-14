using System.Numerics;
using Content.Shared.NPC;

namespace Content.Server.NPC.NavMesh;

public sealed class NavMeshRegion
{
    public int Id;

    public EntityUid GridUid;

    /// <summary>
    /// Used to check if a region could have the tile. Saves us iterating and hashing the same tile O(n) times.
    /// Instead we do the bounds check O(n) and the hashset check a handful of times at most.
    /// </summary>
    public Box2 AABB;

    public HashSet<Vector2i> Tiles = new();

    public List<RegionPortal> Portals = new();

    public PathfindingData Data;

    /// <summary>
    /// Checks if the tile is within the region's AABB bounds. If so then checks for presence in the
    /// tiles set.
    /// </summary>
    /// <param name="tile"></param>
    /// <returns></returns>
    public bool ContainsTile(Vector2i tile)
    {
        return AABB.Contains(new Vector2(tile.X, tile.Y)) && Tiles.Contains(tile);
    }
}
