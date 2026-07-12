using System.Numerics;
using Content.Shared.NPC;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;

namespace Content.Client.NPC;

public sealed class NavMeshOverlay : Overlay
{
    private readonly IEntityManager _entManager;
    private readonly NavMeshSystem _navMesh;
    private readonly SharedTransformSystem _transformSystem;
    private readonly MapSystem _mapSystem;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public NavMeshOverlay(
        IEntityManager entManager,
        IEyeManager eyeManager,
        NavMeshSystem navMesh,
        SharedTransformSystem transformSystem,
        MapSystem mapSystem)
    {
        _entManager = entManager;
        _navMesh = navMesh;
        _transformSystem = transformSystem;
        _mapSystem = mapSystem;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.DrawingHandle is not DrawingHandleWorld worldHandle)
            return;

        var mapId = args.MapId;
        var xformQuery = _entManager.GetEntityQuery<TransformComponent>();
        var grids = new List<Entity<MapGridComponent>>();

        _mapSystem.FindGridsIntersecting(mapId, args.WorldBounds, ref grids);

        foreach (var grid in grids)
        {
            var netGrid = _entManager.GetNetEntity(grid);

            if (!_navMesh.Regions.TryGetValue(netGrid, out var regions))
                continue;

            if (!xformQuery.TryGetComponent(grid, out var gridXform))
                continue;

            var worldMatrix = _transformSystem.GetWorldMatrix(gridXform);
            worldHandle.SetTransform(worldMatrix);

            foreach (var region in regions)
            {
                DrawRegion(worldHandle, region);
            }
        }

        worldHandle.SetTransform(Matrix3x2.Identity);
    }

    private static void DrawRegion(DrawingHandleWorld worldHandle, NavMeshRegionDebug region)
    {
        var color = Color.FromHsv(new Vector4(360f / (region.Id % 12) / 360f, 0.8f, 0.6f, 1f));
        var fillColor = color.WithAlpha(0.30f);
        var borderColor = color.WithAlpha(0.70f);

        var halfTile = new Vector2(0.40f, 0.40f);
        foreach (var tile in region.Tiles)
        {
            var center = new Vector2(tile.X + 0.5f, tile.Y + 0.5f);
            worldHandle.DrawRect(new Box2(center - halfTile, center + halfTile), fillColor);
        }

        worldHandle.DrawRect(region.AABB, borderColor, false);
    }
}
