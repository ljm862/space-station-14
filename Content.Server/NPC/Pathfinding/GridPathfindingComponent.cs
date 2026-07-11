using Content.Server.NPC.NavMesh;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
/// Stores the relevant pathfinding data for grids.
/// </summary>
[RegisterComponent, Access(typeof(PathfindingSystem), typeof(NavMeshSystem)), AutoGenerateComponentPause]
public sealed partial class GridPathfindingComponent : Component
{
    [ViewVariables]
    public readonly HashSet<Vector2i> DirtyChunks = new();

    /// <summary>
    /// A list of the NavMeshRegions as part of the pathfinding.
    /// </summary>
    /// <remarks>Uses a List indexed by the ID. Needs a "free id stack" implemented to track spaces</remarks>
    [ViewVariables]
    public readonly List<NavMeshRegion> Regions = new();

    /// <summary>
    /// Stores the set of tiles that were dirty on the previous tick to be updated immediately on the next
    /// </summary>
    [ViewVariables]
    public HashSet<Vector2i> DirtyTiles = new();

    /// <summary>
    /// Next time the graph is allowed to update.
    /// </summary>
    /// Removing this datafield is the lazy fix HOWEVER I want to purge this anyway and do pathfinding at runtime.
    [AutoPausedField]
    public TimeSpan NextUpdate;

    [ViewVariables]
    public readonly Dictionary<Vector2i, GridPathfindingChunk> Chunks = new();

    /// <summary>
    /// Retrieves the chunk where the specified portal is stored on this grid.
    /// </summary>
    [ViewVariables]
    public readonly Dictionary<PathPortal, Vector2i> PortalLookup = new();

    [ViewVariables]
    public readonly List<PathPortal> DirtyPortals = new();
}
