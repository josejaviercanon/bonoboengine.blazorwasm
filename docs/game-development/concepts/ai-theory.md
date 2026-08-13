# AI Systems -- Theory & Concepts

This document covers engine-agnostic game AI theory, including FSMs, behavior trees, GOAP, utility AI, and perception systems. Pseudocode is used throughout. For engine-specific implementations, see the relevant engine module.

---

## Finite State Machines (FSM)

The workhorse of game AI. Simple, debuggable, and perfect for entities with clearly defined behavioral modes.

### Structure

- **States** -- each with OnEnter, OnUpdate, OnExit callbacks
- **Transitions** -- rules that trigger state changes
- **Current state** -- only one state is active at a time

```
function fsm_update(entity, dt):
    next_state = evaluate_transitions(entity, current_state)
    if next_state != current_state:
        current_state.on_exit()
        current_state = next_state
        current_state.on_enter()
    current_state.on_update(dt)
```

### Hierarchical FSM (HFSM)

Nest state machines -- a "Combat" super-state contains sub-states like Engage, Retreat, UseAbility. The parent FSM delegates to the sub-machine when in the Combat state.

### When to Use

- Simple enemies with 2--5 distinct behaviors
- Boss phase management
- Menu/UI state management

---

## Behavior Trees

More expressive than FSMs for complex, multi-step decision-making. Nodes return **Success**, **Failure**, or **Running**.

### Node Types

| Node | Behavior |
|------|----------|
| **Sequence** | Runs children left-to-right; fails on first failure |
| **Selector** | Runs children left-to-right; succeeds on first success |
| **Parallel** | Runs all children simultaneously; configurable success/fail policy |
| **Decorator** | Wraps one child (Inverter, Repeater, UntilFail, Cooldown) |
| **Leaf** | Executes an action or checks a condition |

### Pseudocode

```
function sequence_tick(children, context):
    for child in children (starting from running_index):
        status = child.tick(context)
        if status == RUNNING: return RUNNING
        if status == FAILURE: return FAILURE
    return SUCCESS

function selector_tick(children, context):
    for child in children (starting from running_index):
        status = child.tick(context)
        if status == RUNNING: return RUNNING
        if status == SUCCESS: return SUCCESS
    return FAILURE
```

### Blackboard Pattern

A shared data store for the behavior tree. Nodes read and write to the blackboard rather than coupling directly to each other. Keys are strings, values are typed data.

### When to Use

- Complex multi-step enemy behaviors
- NPCs with varied reactions to different situations
- When FSM transition explosion becomes unmanageable

---

## GOAP (Goal-Oriented Action Planning)

Agents declare goals and available actions; a planner finds the cheapest action sequence to achieve a goal. Ideal for emergent AI.

### Data Model

- **World state** -- set of boolean key-value pairs (e.g., "isArmed": true)
- **Action** -- has a cost, preconditions (world state required), and effects (world state changes)
- **Goal** -- desired world state with a priority

### Planning Algorithm

GOAP uses A* on the space of world states:

```
function plan(current_state, goal, actions):
    open = priority_queue()
    open.enqueue((current_state, [], 0), 0)

    while open is not empty:
        (state, plan, cost) = open.dequeue()

        if goal_met(state, goal.desired_state):
            return plan

        for action in actions:
            if not preconditions_met(state, action.preconditions):
                continue
            new_state = apply_effects(state, action.effects)
            new_cost = cost + action.cost
            heuristic = count_unmet_conditions(new_state, goal.desired_state)
            open.enqueue((new_state, plan + [action], new_cost), new_cost + heuristic)

    return null    // no plan found
```

### Example

```
Actions: GoToArmory (cost 3), GetWeapon (cost 2, needs: atArmory),
         AttackIntruder (cost 1, needs: isArmed, canSeeIntruder)
Goal: intruderDown = true
Result: GoToArmory -> GetWeapon -> AttackIntruder
```

### When to Use

- Guards, NPCs with multiple ways to achieve goals
- Emergent behavior where actions can be combined in unexpected ways
- When you want AI that adapts to changing world state

---

## Utility AI

Score every possible action with response curves; pick the highest. Handles nuance that behavior trees and FSMs struggle with.

### Response Curves

```
Linear(x):     clamp(m * x + b, 0, 1)
Quadratic(x):  clamp(x^exp, 0, 1)
Logistic(x):   1 / (1 + exp(-steepness * (x - midpoint)))
Step(x):       1 if x >= threshold else 0
```

### Scoring

Each action has multiple **considerations** (functions that return 0--1 scores). The final score uses compensated multiplicative scoring:

```
function score_action(action, entity):
    score = 1.0
    for consideration in action.considerations:
        score *= consideration(entity)
    // Compensation: raise to 1/n to normalize
    return pow(score, 1.0 / len(action.considerations))
```

Select the action with the highest score.

### When to Use

- Many possible actions with continuously varying context (hunger, fear, health, ammo)
- Sims-style needs systems
- Dynamic squad tactics
- When BT condition explosion becomes unmanageable

---

## Perception Systems

### Vision Cone (Field of View)

```
function in_vision_cone(origin, facing, vision_range, half_angle, target):
    to_target = target - origin
    dist = length(to_target)
    if dist > vision_range or dist < 0.01: return false
    dot_product = dot(normalize(facing), to_target / dist)
    return dot_product >= cos(half_angle)
```

### Hearing

```
function can_hear(listener_pos, sensitivity, sound_origin, sound_radius):
    effective_radius = sound_radius * sensitivity
    return distance_squared(listener_pos, sound_origin) <= effective_radius^2
```

### Line-of-Sight

Cast a ray through the tile grid (Bresenham or DDA) to check if walls block vision between two points.

---

## Influence Maps

Spatial scoring grids for strategic AI decisions. Each cell stores a floating-point influence value.

- **Stamp** influence at entity positions (enemy threat, resource value)
- **Propagate** each frame with diffusion and decay for smooth spatial gradients
- **Query** to find safest retreat path, best attack position, or resource-rich areas

---

## Boss Pattern Design

Bosses typically use a phase-based FSM:

- **Phases** triggered by HP thresholds (e.g., >66% = phase 1, >33% = phase 2, else phase 3)
- Each phase has a **pattern sequence** of attacks
- Later phases have shorter cooldowns, faster projectiles, and more aggressive patterns
- Phase transitions trigger special animations or effects

---

## Architecture Decision Guide

| Scenario | Recommended |
|----------|-------------|
| Simple enemy with 2--4 states | FSM |
| Complex multi-step behavior | Behavior Tree |
| Many actions, continuous context | Utility AI |
| Emergent/planning AI (guards, NPCs) | GOAP |
| Smooth movement/flocking | Steering Behaviors |
| Strategic macro-AI | Influence Maps |
| Boss encounters | Phase FSM + Attack Patterns |

**Combine freely:** A guard might use GOAP for high-level planning, a behavior tree to execute each action step, steering behaviors for movement, and A* for pathfinding.

---

*Implementation examples are available in engine-specific modules.*
