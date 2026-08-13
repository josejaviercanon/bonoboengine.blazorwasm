# Pathfinding -- Theory & Concepts

This document covers engine-agnostic pathfinding theory for 2D games. All algorithms are presented in pseudocode. For engine-specific implementations, see the relevant engine module.

---

## A* Algorithm

A* finds the shortest path by expanding the most promising node first, using `f(n) = g(n) + h(n)` where `g` is cost-so-far and `h` is a heuristic estimate to the goal.

### Heuristics

| Heuristic | Formula | Best For |
|-----------|---------|----------|
| **Manhattan** | `abs(dx) + abs(dy)` | 4-directional grids (no diagonal) |
| **Euclidean** | `sqrt(dx*dx + dy*dy)` | Any-angle movement, navmesh |
| **Chebyshev** | `max(abs(dx), abs(dy))` | 8-directional grids (uniform diagonal cost) |
| **Octile** | `max(dx,dy) + 0.414 * min(dx,dy)` | 8-directional grids (sqrt(2) diagonal cost) |

**Admissibility:** If `h` never overestimates the true cost, A* guarantees the optimal path. Manhattan is admissible for 4-dir; Octile for 8-dir. Euclidean is always admissible but may expand more nodes on grids.

### A* Pseudocode

```
function a_star(start, goal, graph, heuristic):
    open = priority_queue()
    g_score = {start: 0}
    parent = {}
    closed = set()

    open.enqueue(start, heuristic(start, goal))

    while open is not empty:
        current = open.dequeue()

        if current == goal:
            return reconstruct_path(parent, current)

        if current in closed:
            continue
        closed.add(current)

        for each (neighbor, step_cost) in graph.neighbors(current):
            if neighbor in closed or not graph.is_walkable(neighbor):
                continue

            // Prevent diagonal corner-cutting through walls
            if is_diagonal(current, neighbor):
                if not graph.is_walkable(current.x + dx, current.y) and
                   not graph.is_walkable(current.x, current.y + dy):
                    continue

            terrain_cost = graph.get_cost(neighbor)
            tentative_g = g_score[current] + step_cost * terrain_cost

            if tentative_g < g_score.get(neighbor, infinity):
                g_score[neighbor] = tentative_g
                parent[neighbor] = current
                f = tentative_g + heuristic(neighbor, goal)
                open.enqueue(neighbor, f)

    return null    // no path found
```

### Terrain Weights

Different tiles can have different movement costs (swamp = 3x, road = 0.5x). The grid graph stores a cost multiplier per cell. A* multiplies it by the base step cost. This preserves optimality as long as the heuristic does not overestimate the cheapest possible path.

### Dynamic Obstacles

When entities occupy cells at runtime, update the graph dynamically. Use an occupancy counter per cell rather than a simple walkable flag to handle overlapping obstacles correctly.

---

## Jump Point Search (JPS)

JPS is an optimization for **uniform-cost grids** (all walkable cells cost 1). Instead of expanding every neighbor, it "jumps" along straight lines until it finds a turning point (forced neighbor). This dramatically prunes the open set.

| Scenario | Best Choice |
|----------|-------------|
| Uniform cost grid, large open areas | **JPS** -- huge speedup (5--10x) |
| Weighted terrain | **A*** -- JPS assumes uniform cost |
| Small maps (< 50x50) | **A*** -- JPS overhead not worth it |
| Narrow corridors | **A*** -- JPS advantage shrinks |

**How it works:** The outer search loop is identical to A*. The difference is that neighbor expansion calls a "jump" function that travels in each direction until it hits a wall or finds a forced neighbor (a cell that must be expanded because pruning would miss it).

---

## Flow Fields

When many units need to path to the **same target** (RTS rally point, tower-defense exit), computing individual A* paths is wasteful. A flow field computes a single field that every unit can query.

### Algorithm

1. **Cost field** -- each cell stores its traversal cost
2. **Integration field** -- BFS/Dijkstra from the goal outward; each cell stores total cost to reach the goal
3. **Flow field** -- each cell stores a direction vector pointing toward its lowest-cost neighbor

```
function build_flow_field(graph, goal):
    // Step 1: Dijkstra from goal
    cost = grid of infinity
    cost[goal] = 0
    queue = [goal]

    while queue is not empty:
        current = queue.dequeue()
        for each neighbor of current:
            if not walkable(neighbor): continue
            new_cost = cost[current] + terrain_cost(neighbor)
            if new_cost < cost[neighbor]:
                cost[neighbor] = new_cost
                queue.enqueue(neighbor)

    // Step 2: Build flow vectors
    for each cell (x, y):
        best_cost = cost[x][y]
        best_dir = (0, 0)
        for each neighbor of (x, y):
            if cost[neighbor] < best_cost:
                best_cost = cost[neighbor]
                best_dir = normalize(neighbor - (x, y))
        flow[x][y] = best_dir
```

**Usage:** Each unit looks up `flow[unit_tile_x][unit_tile_y]` to get its movement direction. Cost: O(grid) to build, O(1) per unit per frame to query.

---

## Hierarchical Pathfinding (HPA*)

For large maps, divide the grid into clusters (e.g., 16x16 chunks), precompute inter-cluster edges, then pathfind on the abstract graph first and refine within clusters. This reduces search space dramatically for open-world or RTS maps.

### Steps

1. Divide the map into rectangular clusters
2. Identify border nodes where clusters connect
3. Build an abstract graph of cluster connections with precomputed costs
4. High-level A* on the abstract graph
5. Refine each segment with local A* within each cluster

---

## Navigation Mesh (2D)

Instead of a grid, decompose walkable space into convex polygons. Pathfinding runs on the polygon adjacency graph, then the path is smoothed through polygon edges.

**Best for:** Open areas with irregular geometry where grids waste resolution. Common in top-down games with free-form level design.

---

## Path Smoothing

Raw A* paths on grids are jagged (axis-aligned steps). Smoothing techniques:

- **Line-of-sight smoothing:** Walk the path and remove intermediate waypoints when a straight line to a later waypoint is unobstructed
- **String pulling (funnel algorithm):** For navmesh paths, find the shortest path through the portal edges connecting adjacent polygons
- **Catmull-Rom splines:** Interpolate through waypoints for smooth curves (visual only -- collision still follows the straight segments)

---

## Steering Behaviors

Steering behaviors produce continuous movement using force accumulation. They are combined for emergent motion.

### Core Behaviors

```
function seek(agent, target):
    desired = normalize(target - agent.position) * agent.max_speed
    return truncate(desired - agent.velocity, agent.max_force)

function flee(agent, threat):
    return -seek(agent, threat)

function arrive(agent, target, slow_radius):
    offset = target - agent.position
    dist = length(offset)
    if dist < 1: return -agent.velocity    // brake
    speed = (dist < slow_radius) ? agent.max_speed * (dist / slow_radius) : agent.max_speed
    desired = (offset / dist) * speed
    return truncate(desired - agent.velocity, agent.max_force)

function wander(agent, wander_angle, radius, distance, jitter):
    wander_angle += random(-0.5, 0.5) * jitter
    circle_center = normalize(agent.velocity) * distance
    offset = (cos(wander_angle), sin(wander_angle)) * radius
    return truncate(circle_center + offset, agent.max_force)
```

### Flocking

- **Separation** -- steer away from nearby neighbors
- **Alignment** -- steer toward average heading of neighbors
- **Cohesion** -- steer toward average position of neighbors

Combine with weighted addition: `force = w1*separation + w2*alignment + w3*cohesion`

### Spatial Hashing for Neighbor Queries

Use a spatial hash (grid of cell lists) for efficient neighbor lookups in steering and flocking. Query a radius around each agent to find nearby agents in O(1) amortized time.

---

## Decision Guide

| Scenario | Recommended |
|----------|-------------|
| Single unit, weighted terrain | A* |
| Single unit, uniform grid, large open areas | JPS |
| Many units, one target | Flow Fields |
| Large world, long paths | HPA* |
| Irregular geometry | NavMesh |
| Smooth movement, flocking | Steering Behaviors |

**Combine freely:** An RTS might use flow fields for group movement, A* for individual scouting, and steering behaviors for local obstacle avoidance.

---

---

## Related Engine Guides

- **MonoGame:** [G40 — Pathfinding](../../monogame-arch/guides/G40_pathfinding.md) (A*, flow fields, nav mesh implementation with Arch ECS)
- **MonoGame:** [G4 — AI Systems](../../monogame-arch/guides/G4_ai_systems.md) (steering behaviors, tactical AI, squad movement)
- **Godot:** [G7 — TileMap & Terrain](../../godot-arch/guides/G7_tilemap_and_terrain.md) (AStarGrid2D integration, procedural dungeon generation, chunk-based worlds)

## Related Concept Docs

- [AI Theory](./ai-theory.md) — Decision-making systems that drive pathfinding (FSM, behavior trees, GOAP)
- [Networking Theory](./networking-theory.md) — Networked pathfinding, client-side prediction for movement
- [Tilemap Theory](./tilemap-theory.md) — Grid representations that pathfinding algorithms operate on
