using System.Buffers;
using System.Numerics;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Doors.Components;
using Content.Shared.NodeContainer;
using Content.Shared.NPC;
using Content.Shared.Physics;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Dynamics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Server.NPC.NavMesh;

public sealed partial class NavMeshSystem : SharedNavMeshSystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _maps = default!;

    [Dependency] private EntityQuery<MapGridComponent> _mapGridQuery = default!;
    [Dependency] private EntityQuery<FixturesComponent> _fixturesQuery = default!;

    private static readonly Vector2i[] CardinalNeighbors = [new(0, 1), new(0, -1), new(1, 0), new(-1, 0)];

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GridInitializeEvent>(OnGridInit);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoved);
        SubscribeLocalEvent<CollisionChangeEvent>(OnCollisionChange);
        //SubscribeLocalEvent<GridPathfindingComponent, ComponentShutdown>(OnGridPathShutdown);
        SubscribeLocalEvent<CollisionLayerChangeEvent>(OnCollisionLayerChange);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChange);
    }

    private void OnGridInit(GridInitializeEvent ev)
    {
        var comp = EnsureComp<GridPathfindingComponent>(ev.EntityUid);
        var mapGrid = Comp<MapGridComponent>(ev.EntityUid);

        // Setup large regions via floodfill on grid
        UpdateRegions(ev.EntityUid, mapGrid, comp);
    }

    private void OnGridRemoved(GridRemovalEvent ev){}

    private void OnCollisionChange(ref CollisionChangeEvent ev)
    {
        var xform = Transform(ev.BodyUid);

        if (xform.GridUid == null)
            return;

        // Check with the physics component first to avoid doing lookups where there are no impassable fixtures anyway.
        if ((ev.Body.CollisionLayer & (int)CollisionGroup.Impassable) == 0)
            return;

        if (!IsBarrierEntity(ev.BodyUid))
            return;

        if (!_mapGridQuery.TryComp(xform.GridUid.Value, out var grid))
            return;

        var tile = _maps.CoordinatesToTile(xform.GridUid.Value, grid, xform.Coordinates);
        DirtyTile(xform.GridUid.Value, tile);
    }

    private void OnCollisionLayerChange(ref CollisionLayerChangeEvent ev)
    {
        var uid = ev.Body.Owner;
        var xform = Transform(uid);
        if (xform.GridUid == null)
            return;

        if (!TryComp<DoorComponent>(uid, out _) &&
            (ev.Body.Comp.CollisionLayer & (int)CollisionGroup.Impassable) == 0)
            return;

        if (!_mapGridQuery.TryComp(xform.GridUid.Value, out var grid))
            return;

        var tile = _maps.CoordinatesToTile(xform.GridUid.Value, grid, xform.Coordinates);
        DirtyTile(xform.GridUid.Value, tile);
    }

    private void OnTileChange(ref TileChangedEvent ev)
    {
        foreach (var change in ev.Changes)
        {
            if (!change.EmptyChanged)
                continue;

            DirtyTile(ev.Entity, change.GridIndices);
        }
    }

    private void DirtyTile(EntityUid ent, Vector2i tile)
    {
        if (!TryComp<GridPathfindingComponent>(ent, out var comp))
            return;

        comp.DirtyTiles.Add(tile);
    }

    /// <summary>
    /// Is this entity part of the "wall" around our regions?
    /// </summary>
    /// <param name="entityUid"></param>
    /// <returns></returns>
    private bool IsBarrierEntity(EntityUid entityUid)
    {
        return _fixturesQuery.TryComp(entityUid, out var fixtures) && IsBarrier(fixtures);
    }

    private bool IsBarrier(FixturesComponent fixtures)
    {
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard && (fixture.CollisionLayer & (int)CollisionGroup.Impassable) != 0)
                return true;
        }
        return false;
    }

    private NavMeshRegion? FindRegionForTile(GridPathfindingComponent comp, Vector2i tile)
    {
        foreach (var region in comp.Regions)
        {
            if (region == null)
                continue;

            if (region.ContainsTile(tile))
                return region;
        }
        return null;
    }


    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        // Update any regions with dirty tiles.
        var query = AllEntityQuery<GridPathfindingComponent>();
        while (query.MoveNext(out var ent, out var comp))
        {
            if (comp.DirtyTiles.Count == 0)
                continue;

            if (!_mapGridQuery.TryComp(ent, out var grid))
                continue;

            UpdateRegions(ent, grid, comp);

            // Should probably track the tiles we were able to update this tick and remove them from DirtyTiles.
            // That way if we need to timeslice we can because we have the actual tiles we didn't update ready to go.
            comp.DirtyTiles.Clear();
        }

        //UpdateRegions();
    }

    private void UpdateRegions(EntityUid gridUid, MapGridComponent grid, GridPathfindingComponent comp)
    {
        var barrierTiles = GatherBarrierTiles(gridUid, grid);
        var doorTiles = GatherDoorTiles(gridUid, grid);

        BuildRegion(doorTiles, barrierTiles, gridUid, grid, comp);

        // Connect doors as graph edges
        AddEdgesToGraph(doorTiles, comp);
    }

    private HashSet<Vector2i> GatherBarrierTiles(EntityUid gridUid, MapGridComponent grid)
    {
        var barrierTiles = new HashSet<Vector2i>();
        var allFixturesQuery = AllEntityQuery<FixturesComponent, TransformComponent>();
        while (allFixturesQuery.MoveNext(out var uid, out var fixtures, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;

            if(IsBarrier(fixtures))
                barrierTiles.Add(_maps.CoordinatesToTile(gridUid, grid, xform.Coordinates));
        }
        return barrierTiles;
    }

    private HashSet<Vector2i> GatherDoorTiles(EntityUid gridUid, MapGridComponent grid)
    {
        var doorTiles = new HashSet<Vector2i>();
        var allDoorsQuery = AllEntityQuery<DoorComponent, TransformComponent>();
        while (allDoorsQuery.MoveNext(out var uid, out var door, out var xform))
        {
            if (xform.GridUid != gridUid)
                continue;
            doorTiles.Add(_maps.CoordinatesToTile(gridUid, grid, xform.Coordinates));
        }

        return doorTiles;
    }

    private void BuildRegion(HashSet<Vector2i> doorTiles, HashSet<Vector2i> barrierTiles, EntityUid gridUid, MapGridComponent grid, GridPathfindingComponent comp)
    {
        var visitedTiles = new HashSet<Vector2i>();
        comp.Regions.Clear();
        var regionId = 0;

        // Had to make a decision here about precomputing walkable tiles or not. I think the memory overhead and possible GC
        // time it would add on isn't worth the extra cycles it would save by not checking
        foreach (var tileRef in _maps.GetAllTiles(gridUid, grid, ignoreEmpty: true))
        {
            var tile = tileRef.GridIndices;

            if(barrierTiles.Contains(tile) || doorTiles.Contains(tile))
                continue;

            if (!visitedTiles.Add(tile))
                continue;

            var queue = new Queue<Vector2i>();
            queue.Enqueue(tile);

            var regionTiles = new HashSet<Vector2i>();
            int minX = tile.X, minY = tile.Y, maxX = tile.X, maxY = tile.Y;

            while (queue.TryDequeue(out var currentTile))
            {
                regionTiles.Add(currentTile);
                minX = Math.Min(minX, currentTile.X);
                minY = Math.Min(minY, currentTile.Y);
                maxX = Math.Max(maxX, currentTile.X);
                maxY = Math.Max(maxY, currentTile.Y);

                foreach (var dir in CardinalNeighbors)
                {
                    var next = currentTile + dir;
                    if(!_maps.TryGetTileRef(gridUid, grid, next, out var neighborTile) || neighborTile.Tile.IsEmpty)
                        continue;

                    if (barrierTiles.Contains(next) || doorTiles.Contains(next))
                        continue;

                    if (visitedTiles.Add(next))
                        queue.Enqueue(next);
                }
            }

            comp.Regions.Add(new NavMeshRegion
            {
                Id = regionId,
                Tiles = regionTiles,
                AABB = new Box2(minX, minY, maxX + 1, maxY + 1),
                GridUid = gridUid,
            });

            regionId++;
        }
    }

    private void AddEdgesToGraph(HashSet<Vector2i> doorTiles, GridPathfindingComponent comp)
    {
        // Should probably combine adjacent doors into the same door edge.
        foreach (var doorTile in doorTiles)
        {
            int? regionA = null, regionB = null;
            foreach (var dir in CardinalNeighbors)
            {
                var neighbor = doorTile + dir;
                var neighborRegion = FindRegionForTile(comp, neighbor);
                if (neighborRegion == null)
                    continue;

                if (regionA == null)
                    regionA = neighborRegion.Id;
                else
                    regionB = neighborRegion.Id;
            }

            if (regionA == null || regionB == null)
                continue;

            comp.Regions[regionA.Value].Portals.Add(new RegionPortal(regionB.Value, doorTile));
            comp.Regions[regionB.Value].Portals.Add(new RegionPortal(regionA.Value, doorTile));
        }
    }

}
