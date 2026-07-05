using System.Linq;
using System.Numerics;
using System.Threading;
using Content.IntegrationTests.Fixtures;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Physics;
using Content.Shared.Tests;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.NPC;

[TestFixture]
public sealed class PathfindingTest : GameTest
{
    /*
     * Reference initial layout:
     *   y4: W W W W W
     *   y3: W . . . W
     *   y2: W . . . W
     *   y1: W S . E W
     *   y0: W W W W W
     *    x  0 1 2 3 4
     *
     * Setup creates only floor tiles + border walls.
     * Each test builds the interior walls and doors it needs.
     */

    private const int MapSize = 5;
    private EntityUid _grid = default!;
    private EntityCoordinates _startCoords;
    private EntityCoordinates _endCoords;

    private PathfindingSystem _pathfinding;


    [SetUp]
    public async Task Setup()
    {
        var mapData = await Pair.CreateTestMap();
        _grid = mapData.Grid;
        var platingTile = new Tile(1);
        var map = Server.System<SharedMapSystem>();
        _pathfinding = Server.System<PathfindingSystem>();

        await Server.WaitPost(() =>
        {
            for (var x = 0; x < MapSize; x++)
            {
                for (var y = 0; y < MapSize; y++)
                {
                    map.SetTile(mapData.Grid,
                        new EntityCoordinates(_grid, new Vector2(x + 0.5f, y + 0.5f)),
                        platingTile);
                }
            }

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

            var start = SEntMan.SpawnAtPosition("IntegrationTestMarker",
                new EntityCoordinates(_grid, new Vector2(1.5f, 1.5f)));
            var end = SEntMan.SpawnAtPosition("IntegrationTestMarker",
                new EntityCoordinates(_grid, new Vector2(3.5f, 1.5f)));

            SEntMan.GetComponent<TestMarkerComponent>(start).Id = "start";
            SEntMan.GetComponent<TestMarkerComponent>(end).Id = "end";

            _startCoords = new EntityCoordinates(_grid, new Vector2(1.5f, 1.5f));
            _endCoords = new EntityCoordinates(_grid, new Vector2(3.5f, 1.5f));
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
    public async Task ObstacleAStarPath()
    {
        await Server.WaitPost(() =>
        {
            SpawnWallAt(2, 1);
        });

        await RunTicksSync(15);

        var pathTask = _pathfinding.GetPath(
            _startCoords,
            _endCoords,
            0.5f,
            (int)CollisionGroup.MobLayer,
            (int)CollisionGroup.MobMask,
            CancellationToken.None);

        for (var i = 0; i < 300; i++)
        {
            if (pathTask.IsCompleted)
                break;
            await RunTicksSync(1);
        }

        Assert.That(pathTask.IsCompletedSuccessfully, "Path request did not complete within timeout");
#pragma warning disable RA0004
        var result = pathTask.Result;
#pragma warning restore RA0004
        Assert.That(result.Result, Is.EqualTo(PathResult.Path),
            $"Expected path but got {result.Result} with {result.Path?.Count ?? 0} nodes");

        Assert.That(result.Path, Is.Not.Null.And.Not.Empty, "Path should contain nodes");
        Assert.That(result.Path, Has.Count.GreaterThan(1), "Path should have more than one node");

        var obstacleTile = new Box2(new Vector2(2f, 1f), new Vector2(3f, 2f));
        foreach (var node in result.Path)
        {
            Assert.That(obstacleTile.Contains(node.Box.Center, false), Is.False,
                $"Path node at {node.Box.Center} is inside obstacle tile");
        }
    }

    [Test]
    public async Task NoPathIntoWall()
    {
        var wallCoords = new EntityCoordinates(_grid, new Vector2(2.5f, 0.5f));

        var pathTask = _pathfinding.GetPath(
            _startCoords,
            wallCoords,
            0.5f,
            (int)CollisionGroup.MobLayer,
            (int)CollisionGroup.MobMask,
            CancellationToken.None);

        for (var i = 0; i < 300; i++)
        {
            if (pathTask.IsCompleted)
                break;
            await RunTicksSync(1);
        }

        Assert.That(pathTask.IsCompletedSuccessfully, "Path request did not complete within timeout");
#pragma warning disable RA0004
        Assert.That(pathTask.Result.Result, Is.EqualTo(PathResult.NoPath));
#pragma warning restore RA0004
    }

    [Test]
    public async Task DoorClosedBlockedPath()
    {
        await Server.WaitPost(() =>
        {
            SpawnDoorAt(2, 1);
            SpawnWallAt(2, 2);
            SpawnWallAt(2, 3);
        });

        await RunTicksSync(15);

        var pathTask = _pathfinding.GetPath(
            _startCoords,
            _endCoords,
            0.5f,
            (int)CollisionGroup.MobLayer,
            (int)CollisionGroup.MobMask,
            CancellationToken.None);

        for (var i = 0; i < 300; i++)
        {
            if (pathTask.IsCompleted)
                break;
            await RunTicksSync(1);
        }

        Assert.That(pathTask.IsCompletedSuccessfully, "Path request did not complete within timeout");
#pragma warning disable RA0004
        Assert.That(pathTask.Result.Result, Is.EqualTo(PathResult.NoPath));
#pragma warning restore RA0004
    }

    [Test]
    public async Task DoorClosedInteractPath()
    {
        await Server.WaitPost(() =>
        {
            SpawnDoorAt(2, 1);
            SpawnWallAt(2, 2);
            SpawnWallAt(2, 3);
        });

        await RunTicksSync(15);

        var pathTask = _pathfinding.GetPath(
            _startCoords,
            _endCoords,
            0.5f,
            (int)CollisionGroup.MobLayer,
            (int)CollisionGroup.MobMask,
            CancellationToken.None,
            PathFlags.Interact);

        for (var i = 0; i < 300; i++)
        {
            if (pathTask.IsCompleted)
                break;
            await RunTicksSync(1);
        }

        Assert.That(pathTask.IsCompletedSuccessfully, "Path request did not complete within timeout");
#pragma warning disable RA0004
        var result = pathTask.Result;
#pragma warning restore RA0004
        Assert.That(result.Result, Is.EqualTo(PathResult.Path),
            $"Expected path but got {result.Result}");

        Assert.That(result.Path, Is.Not.Null.And.Not.Empty, "Path should contain nodes");

        var doorTile = new Box2(new Vector2(2f, 1f), new Vector2(3f, 2f));
        Assert.That(result.Path.Any(node => doorTile.Contains(node.Box.Center, false)), Is.True,
            "Path should go through the unlocked door tile");
    }
}
