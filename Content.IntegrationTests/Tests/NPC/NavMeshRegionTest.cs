using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.NavMesh;
using Content.Server.NPC.Pathfinding;
using Content.Shared.NPC;
using Content.Shared.Tests;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.NPC;

[TestFixture]
public sealed class NavMeshRegionTest : GameTest
{
    /*
     * Layout:
     *   y4: W W W W W
     *   y3: W . W . W
     *   y2: W . D . W    D = door
     *   y1: W . W . W
     *   y0: W W W W W
     *
     * Border walls + dividing wall at x=2 (y=1, y=3).
     * Door at (2,2) connects left room ↔ right room.
     * Left room tiles: (1,1), (1,2), (1,3) — 3 tiles
     * Right room tiles: (3,1), (3,2), (3,3) — 3 tiles
     * Door tile (2,2) is not in any region.
     */

    private const int MapSize = 5;
    private EntityUid _grid;

    [SetUp]
    public async Task Setup()
    {
        var mapData = await Pair.CreateTestMap();
        _grid = mapData.Grid;
        var map = Server.System<SharedMapSystem>();
        var platingTile = new Tile(1);

        await Server.WaitPost(() =>
        {
            // Fill the grid with floor tiles
            for (var x = 0; x < MapSize; x++)
            {
                for (var y = 0; y < MapSize; y++)
                {
                    map.SetTile(mapData.Grid,
                        new EntityCoordinates(_grid, new Vector2(x + 0.5f, y + 0.5f)),
                        platingTile);
                }
            }

            // Border walls
            for (var x = 0; x < MapSize; x++)
            {
                SpawnWallAt(x, 0);
                SpawnWallAt(x, MapSize - 1);
            }
            for (var y = 1; y < MapSize - 1; y++)
            {
                SpawnWallAt(0, y);
                SpawnWallAt(MapSize - 1, y);
            }

            // Dividing wall at x=2, above and below the door
            SpawnWallAt(2, 1);
            SpawnWallAt(2, 3);

            // Door at (2, 2)
            SpawnDoorAt(2, 2);
        });

        await RunTicksSync(60);
    }

    #region Helpers

    private void SpawnWallAt(int x, int y)
    {
        SEntMan.SpawnAtPosition("WallSolid",
            new EntityCoordinates(_grid, new Vector2(x + 0.5f, y + 0.5f)));
    }

    private EntityUid SpawnDoorAt(int x, int y)
    {
        return SEntMan.SpawnAtPosition("MetalDoor",
            new EntityCoordinates(_grid, new Vector2(x + 0.5f, y + 0.5f)));
    }

    #endregion

    [Test]
    public async Task NavMeshBasics()
    {
        GridPathfindingComponent comp = null;
        await Server.WaitPost(() =>
        {
            comp = SEntMan.GetComponent<GridPathfindingComponent>(_grid);
        });

        Assert.That(comp, Is.Not.Null);
        Assert.That(comp.Regions, Has.Count.EqualTo(2),
            $"Expected 2 regions but got {comp.Regions.Count}");

        var regionTiles = comp.Regions.Select(r => r.Tiles.Count).ToList();
        Assert.That(regionTiles, Has.All.EqualTo(3),
            "Each region should contain exactly 3 floor tiles");

        var doorTile = new Vector2i(2, 2);
        foreach (var region in comp.Regions)
        {
            Assert.That(region.Portals, Has.Count.EqualTo(1),
                $"Region {region.Id} should have exactly 1 portal");

            var portal = region.Portals[0];
            Assert.That(portal.Tile, Is.EqualTo(doorTile));

            var otherRegion = comp.Regions.First(r => r.Id == portal.NeighborRegionId);
            Assert.That(otherRegion.Id, Is.Not.EqualTo(region.Id),
                "Portal must not point to its own region");

            Assert.That(region.Tiles.Contains(doorTile), Is.False,
                $"Region {region.Id} must not contain the door tile");
        }
    }

    [Test]
    public async Task EveryWalkableTileIsInExactlyOneRegion()
    {
        GridPathfindingComponent comp = null;
        await Server.WaitPost(() =>
        {
            comp = SEntMan.GetComponent<GridPathfindingComponent>(_grid);
        });

        // All floor tiles that aren't blocked by walls or doors
        var expectedTiles = new HashSet<Vector2i>
        {
            new(1, 1), new(1, 2), new(1, 3),
            new(3, 1), new(3, 2), new(3, 3),
        };

        var foundTiles = new HashSet<Vector2i>();
        foreach (var region in comp.Regions)
        {
            foreach (var tile in region.Tiles)
            {
                Assert.That(foundTiles.Add(tile), Is.True,
                    $"Tile {tile} belongs to multiple regions (region {region.Id})");
                Assert.That(expectedTiles, Does.Contain(tile),
                    $"Tile {tile} in region {region.Id} is not an expected walkable tile");
            }
        }

        foreach (var expected in expectedTiles)
        {
            Assert.That(foundTiles.Contains(expected), Is.True,
                $"Expected walkable tile {expected} was not found in any region");
        }
    }

    [Test]
    public async Task RegionAABBBoundsAreCorrect()
    {
        GridPathfindingComponent comp = null;
        await Server.WaitPost(() =>
        {
            comp = SEntMan.GetComponent<GridPathfindingComponent>(_grid);
        });

        // Left room: x=[1,1] y=[1,3] -> Box2(1, 1, 2, 4)
        // Right room: x=[3,3] y=[1,3] -> Box2(3, 1, 4, 4)
        var regionA = comp.Regions[0];
        var regionB = comp.Regions[1];

        if (regionA.Tiles.First().X == 1)
        {
            Assert.That(regionA.AABB, Is.EqualTo(new Box2(1, 1, 2, 4)));
            Assert.That(regionB.AABB, Is.EqualTo(new Box2(3, 1, 4, 4)));
        }
        else
        {
            Assert.That(regionA.AABB, Is.EqualTo(new Box2(3, 1, 4, 4)));
            Assert.That(regionB.AABB, Is.EqualTo(new Box2(1, 1, 2, 4)));
        }
    }
}
