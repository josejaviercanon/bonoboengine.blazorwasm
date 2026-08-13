// =============================================================================
// PathComponents.cs — ECS components and systems for pathfinding integration
// Extracted from: G40 — Pathfinding (Section 10)
// Guide: /G/G40_pathfinding.md
// =============================================================================

using System;
using System.Collections.Generic;
using Arch.Core;
using Arch.Core.Extensions;
using Microsoft.Xna.Framework;

namespace U2DToolkit.Examples.Pathfinding
{
    // =========================================================================
    // Components
    // =========================================================================

    /// <summary>
    /// Attach to an entity to request a path. Consumed by <see cref="PathfindingSystem"/>.
    /// </summary>
    public record struct PathRequest(
        Point Start,
        Point Goal,
        bool AllowDiagonal = true
    );

    /// <summary>
    /// Attached by <see cref="PathfindingSystem"/> when a path is found (or failed).
    /// Contains the waypoint list and current progress index.
    /// </summary>
    public record struct PathResult(
        List<Point>? Waypoints,
        int CurrentIndex,
        bool Failed
    );

    /// <summary>
    /// Marks an entity as currently following a path.
    /// Configure speed, waypoint radius, and arrival slowdown radius.
    /// </summary>
    public record struct PathFollowing(
        float Speed,
        float WaypointRadius,
        float ArrivalRadius
    );

    /// <summary>
    /// Tag component for flow-field-based movement instead of individual A* paths.
    /// Entities with this component will query a shared <see cref="FlowField"/>
    /// each frame for their movement direction.
    /// </summary>
    public record struct FlowFieldFollower(
        float Speed
    );

    /// <summary>
    /// World-space position component (Vector2).
    /// Your project likely already has this — adapt as needed.
    /// </summary>
    public record struct Position(Vector2 Value);

    // =========================================================================
    // Systems
    // =========================================================================

    /// <summary>
    /// Processes <see cref="PathRequest"/> components, computes A* paths,
    /// and attaches <see cref="PathResult"/> to entities.
    /// Time-sliced: processes at most <c>maxRequestsPerFrame</c> per update
    /// to avoid frame spikes.
    /// </summary>
    public class PathfindingSystem
    {
        private readonly AStarPathfinder _pathfinder;
        private readonly int _maxPerFrame;

        /// <param name="graph">The grid to pathfind on.</param>
        /// <param name="maxRequestsPerFrame">Cap per frame to prevent spikes (2–3 typical).</param>
        public PathfindingSystem(GridGraph graph, int maxRequestsPerFrame = 3)
        {
            _pathfinder = new AStarPathfinder(graph);
            _maxPerFrame = maxRequestsPerFrame;
        }

        public void Update(World world)
        {
            int processed = 0;

            var query = new QueryDescription().WithAll<PathRequest, Position>();

            world.Query(in query, (Entity entity, ref PathRequest req, ref Position pos) =>
            {
                if (processed >= _maxPerFrame) return;
                processed++;

                var path = _pathfinder.FindPath(req.Start, req.Goal, req.AllowDiagonal);

                if (path != null)
                {
                    // Optionally smooth the path for more natural movement
                    path = _pathfinder.SmoothPath(path);

                    world.Add(entity, new PathResult(
                        Waypoints: path,
                        CurrentIndex: 0,
                        Failed: false
                    ));
                }
                else
                {
                    world.Add(entity, new PathResult(
                        Waypoints: null,
                        CurrentIndex: 0,
                        Failed: true
                    ));
                }

                // Remove the request — it's been handled
                world.Remove<PathRequest>(entity);
            });
        }
    }

    /// <summary>
    /// Moves entities along their A*-computed waypoint paths.
    /// Handles waypoint advancement, arrival slowdown, and cleanup
    /// when the destination is reached.
    /// </summary>
    public class PathMovementSystem
    {
        private readonly float _tileSize;

        /// <param name="tileSize">World-space size of one tile (e.g., 16).</param>
        public PathMovementSystem(float tileSize = 16f)
        {
            _tileSize = tileSize;
        }

        public void Update(World world, float dt)
        {
            var query = new QueryDescription()
                .WithAll<Position, PathResult, PathFollowing>();

            world.Query(in query, (Entity entity,
                ref Position pos, ref PathResult path, ref PathFollowing follow) =>
            {
                if (path.Waypoints == null || path.CurrentIndex >= path.Waypoints.Count)
                {
                    // Path complete or invalid — clean up
                    world.Remove<PathResult>(entity);
                    return;
                }

                var targetCell = path.Waypoints[path.CurrentIndex];
                var targetWorld = new Vector2(
                    targetCell.X * _tileSize + _tileSize * 0.5f,
                    targetCell.Y * _tileSize + _tileSize * 0.5f);

                var offset = targetWorld - pos.Value;
                float dist = offset.Length();

                bool isLast = path.CurrentIndex == path.Waypoints.Count - 1;

                if (dist < follow.WaypointRadius)
                {
                    if (isLast)
                    {
                        // Arrived at destination
                        pos.Value = targetWorld;
                        world.Remove<PathResult>(entity);
                        return;
                    }
                    path.CurrentIndex++;
                    return;
                }

                // Move toward waypoint, slowing down on final approach
                float speed = follow.Speed;
                if (isLast && dist < follow.ArrivalRadius)
                    speed *= (dist / follow.ArrivalRadius);

                var direction = offset / dist;
                pos.Value += direction * speed * dt;
            });
        }
    }

    /// <summary>
    /// Moves entities that use a shared <see cref="FlowField"/> instead of
    /// individual A* paths. Each entity simply queries the flow field for
    /// its movement direction — O(1) per entity per frame.
    /// </summary>
    public class FlowFieldMovementSystem
    {
        private readonly FlowField _flowField;
        private readonly float _tileSize;

        public FlowFieldMovementSystem(FlowField flowField, float tileSize = 16f)
        {
            _flowField = flowField;
            _tileSize = tileSize;
        }

        public void Update(World world, float dt)
        {
            var query = new QueryDescription()
                .WithAll<Position, FlowFieldFollower>();

            world.Query(in query, (ref Position pos, ref FlowFieldFollower follower) =>
            {
                var cell = new Point(
                    (int)(pos.Value.X / _tileSize),
                    (int)(pos.Value.Y / _tileSize));

                var dir = _flowField.GetDirection(cell);
                if (dir == Vector2.Zero) return; // at goal or unreachable

                pos.Value += dir * follower.Speed * dt;
            });
        }
    }
}
