# Networking & Multiplayer -- Theory & Concepts

This document covers engine-agnostic multiplayer networking theory for games, including network architectures, state synchronization, lag compensation, rollback netcode, matchmaking, and security. Pseudocode is used throughout. For engine-specific implementations, see the relevant engine module.

---

## Network Architectures

### Client-Server

One authoritative server owns all game state. Clients send inputs; the server validates, simulates, and broadcasts results.

```
// Server loop
while running:
    for client in clients:
        inputs = receive_inputs(client)
        validate(inputs)
        apply_to_simulation(inputs)
    step_simulation(dt)
    snapshot = capture_state()
    for client in clients:
        send_state(client, snapshot)
```

**Advantages:** Anti-cheat (server is authority), consistent state, scales well with dedicated servers.

**Disadvantages:** Server cost, single point of failure, latency to server affects responsiveness.

**Best for:** Competitive games, MMOs, any game where cheating prevention matters.

### Peer-to-Peer (P2P)

Every player runs the full simulation. Players exchange inputs directly.

```
// Each peer
while running:
    local_input = read_local_input()
    broadcast_input(local_input)
    for peer in peers:
        remote_input = receive_input(peer)
        input_buffer[peer].add(remote_input)
    // Only advance when all inputs received for this tick
    if all_inputs_received(current_tick):
        apply_all_inputs(current_tick)
        step_simulation(dt)
```

**Advantages:** No server cost, lower latency between nearby players, simpler infrastructure.

**Disadvantages:** Vulnerable to cheating (every peer has full state), scales poorly (N² connections), one slow peer affects everyone (lockstep).

**Best for:** Fighting games, co-op games, LAN play, small player counts (2-8).

### Relay Server (Hybrid)

A lightweight server forwards packets between peers without running the simulation. Combines P2P's no-simulation-server benefit with client-server's NAT traversal.

```
// Relay server
while running:
    packet = receive_from_any()
    for peer in peers:
        if peer != packet.sender:
            forward(peer, packet)
```

**Used by:** Steam Networking, Epic Online Services, many indie multiplayer games.

---

## The Fundamental Problem: Latency

Light travels ~200km per millisecond in fiber. A round trip across the US takes ~60-80ms minimum. Players expect instant feedback. Every networking technique exists to hide or compensate for this delay.

### Latency Ranges

| Connection | Round Trip | Impact |
|-----------|-----------|--------|
| LAN | <1ms | Imperceptible |
| Same city | 5-15ms | Negligible |
| Same continent | 30-80ms | Noticeable in fast-paced games |
| Cross-continent | 100-200ms | Requires compensation techniques |
| Satellite/mobile | 200-600ms | Severe, limits game design |

---

## State Synchronization

### Full State Sync

Send the entire game state every tick. Simple but bandwidth-heavy.

```
snapshot = {
    entities: [
        { id: 1, x: 100, y: 50, hp: 80, state: "running" },
        { id: 2, x: 300, y: 120, hp: 100, state: "idle" },
        ...
    ],
    tick: 4821,
    timestamp: now()
}
send(snapshot)
```

**When to use:** Small state (card games, board games, <50 entities).

### Delta Compression

Only send what changed since the last acknowledged state.

```
function compute_delta(old_snapshot, new_snapshot):
    delta = {}
    for entity in new_snapshot.entities:
        old = old_snapshot.get(entity.id)
        if old == null:
            delta.created.add(entity)
        elif entity != old:
            changed_fields = diff(old, entity)
            delta.updated.add(entity.id, changed_fields)
    for entity in old_snapshot.entities:
        if not new_snapshot.has(entity.id):
            delta.destroyed.add(entity.id)
    return delta
```

**Bandwidth savings:** Typically 80-95% reduction over full state sync.

### Quantization

Reduce precision to save bandwidth. A 32-bit float position becomes a 16-bit or 10-bit integer within a known range.

```
function quantize(value, min, max, bits):
    range = max - min
    steps = (1 << bits) - 1
    normalized = (value - min) / range
    return round(normalized * steps)

function dequantize(quantized, min, max, bits):
    steps = (1 << bits) - 1
    return min + (quantized / steps) * (max - min)
```

**Example:** Position in a 1000×1000 world with 10-bit quantization = ~1 unit precision, 10 bits instead of 32 bits per axis.

### Interest Management

Only send entities relevant to each client. Reduces bandwidth and prevents information cheating (wallhacks).

```
function get_relevant_entities(player, all_entities):
    relevant = []
    for entity in all_entities:
        dist = distance(player.position, entity.position)
        if dist < RELEVANCE_RADIUS:
            relevant.add(entity)
        elif entity.is_global:  // score, timer, announcements
            relevant.add(entity)
    return relevant
```

**Advanced:** Grid-based spatial partitioning for O(1) relevance checks. Pub/sub channels per world region.

---

## Client-Side Prediction

The client simulates locally using its own inputs without waiting for server confirmation. Makes the game feel responsive despite latency.

```
// Client
function on_input(input):
    // Apply immediately (predict)
    predicted_state = simulate(current_state, input)
    apply_locally(predicted_state)

    // Send to server
    send_to_server(input, tick_number)

    // Store for reconciliation
    pending_inputs.add(tick_number, input)
```

**The core idea:** The client is always `RTT / 2` ticks ahead of the server. Local input feels instant. When the server confirms, the client reconciles.

### What to Predict

| Predict | Don't Predict |
|---------|---------------|
| Own movement | Other players' health changes |
| Own weapon firing | Loot drops / pickups |
| Own ability activation | Score changes |
| Jump / dash | Server-authoritative events |
| Door opening (own action) | Other players' actions |

**Rule of thumb:** Predict actions the local player initiates. Don't predict outcomes that depend on server validation.

---

## Server Reconciliation

When the server sends authoritative state, the client must correct any mispredictions.

```
function on_server_state(server_state, server_tick):
    // Discard inputs the server has already processed
    pending_inputs.remove_up_to(server_tick)

    // Start from server's authoritative state
    reconciled = server_state

    // Re-simulate unacknowledged inputs
    for input in pending_inputs:
        reconciled = simulate(reconciled, input)

    // Apply corrected state
    if reconciled != current_predicted_state:
        current_state = reconciled
        // Optionally interpolate to avoid visual snap
        smooth_correction(current_predicted_state, reconciled)
```

### Handling Mispredictions

Small corrections (< threshold): Interpolate smoothly over several frames.

Large corrections (teleport-level): Snap immediately — interpolating would look worse.

```
function smooth_correction(from, to):
    error = to.position - from.position
    if length(error) > SNAP_THRESHOLD:
        position = to.position  // snap
    else:
        correction_offset = error
        // Blend out over CORRECTION_FRAMES
```

---

## Entity Interpolation

Remote entities are rendered between two known server states, creating smooth movement despite discrete updates.

```
// Store received states with timestamps
function on_entity_update(entity_id, state, server_time):
    state_buffer[entity_id].add(server_time, state)

// Render at a time slightly in the past
function render_entity(entity_id, render_time):
    // Render 100ms behind real time (interpolation delay)
    target_time = render_time - INTERPOLATION_DELAY

    buffer = state_buffer[entity_id]
    before, after = buffer.get_surrounding(target_time)

    t = (target_time - before.time) / (after.time - before.time)
    rendered_position = lerp(before.position, after.position, t)
    rendered_rotation = lerp_angle(before.rotation, after.rotation, t)
```

**Interpolation delay:** Typically 2-3 server ticks (e.g., 100ms at 20 tick rate). Higher delay = smoother but more "in the past." Lower delay = more responsive but risks running out of data (extrapolation).

### Extrapolation (Dead Reckoning)

When the next server state hasn't arrived yet, predict where the entity will be using velocity.

```
function extrapolate(last_state, elapsed):
    predicted_pos = last_state.position + last_state.velocity * elapsed
    return predicted_pos
```

**Risk:** Extrapolation guesses wrong when entities change direction. Use sparingly and cap the maximum extrapolation time.

---

## Lag Compensation

### Server-Side Rewind

For hit detection in shooters: when the server processes a shot, it rewinds the world to what the shooting player saw at the time they fired.

```
// Server
function process_shot(shooter, shot_data):
    // Rewind to shooter's perceived time
    perceived_time = shot_data.timestamp
    rewound_state = history.get_state_at(perceived_time)

    // Perform hit check against historical positions
    hit = raycast(shot_data.origin, shot_data.direction, rewound_state)

    if hit:
        apply_damage(hit.entity, shot_data.damage)
```

**Trade-off:** The shooter sees fair hits (they hit what they aimed at). The victim may feel "shot around corners" because they've already moved in their local simulation.

### History Buffer

The server stores N ticks of world state for rewind queries.

```
class HistoryBuffer:
    max_ticks = SERVER_TICK_RATE * MAX_REWIND_SECONDS  // e.g., 60 * 0.5 = 30 ticks

    function store(tick, state):
        buffer[tick % max_ticks] = state

    function get_at(tick):
        return buffer[tick % max_ticks]

    function get_interpolated(time):
        tick_a = floor(time / tick_duration)
        tick_b = tick_a + 1
        t = frac(time / tick_duration)
        return lerp(buffer[tick_a], buffer[tick_b], t)
```

---

## Rollback Netcode

Used primarily in fighting games and fast-paced action games. Instead of waiting for remote inputs, assume the last known input continues, simulate forward, and correct (rollback) when actual inputs arrive.

### Core Loop

```
function advance_game():
    // Predict remote inputs (repeat last known)
    for remote_player in remote_players:
        predicted_input[remote_player] = last_confirmed_input[remote_player]

    // Simulate with predicted inputs
    save_state(current_tick)
    simulate(local_input, predicted_input, dt)
    current_tick += 1

    // When confirmed inputs arrive
    if received_confirmed_input(remote_tick, remote_input):
        if remote_input != predicted_input_at(remote_tick):
            // Misprediction! Rollback.
            load_state(remote_tick)
            // Re-simulate from remote_tick to current_tick with correct inputs
            for tick in range(remote_tick, current_tick):
                inputs = get_confirmed_or_predicted(tick)
                simulate(inputs, dt)
```

### State Save/Load

Rollback requires saving and restoring complete game state quickly.

```
class RollbackManager:
    state_ring: array[MAX_ROLLBACK_FRAMES]  // circular buffer

    function save_state(tick):
        state_ring[tick % MAX_ROLLBACK_FRAMES] = serialize(game_state)

    function load_state(tick):
        game_state = deserialize(state_ring[tick % MAX_ROLLBACK_FRAMES])
```

**Performance requirement:** Save/load must complete in <1ms. Use flat structs, avoid heap allocations. Copy arrays directly.

### Rollback Budget

| Parameter | Typical Value |
|-----------|--------------|
| Max rollback frames | 7-10 |
| State save frequency | Every tick |
| State size budget | <64KB |
| Save/load time budget | <0.5ms |

### Input Delay

Adding 1-3 frames of deliberate input delay reduces rollback frequency. In a fighting game at 60fps, 2 frames = 33ms — usually imperceptible.

```
actual_input_tick = current_tick + INPUT_DELAY_FRAMES
```

**Trade-off:** More input delay = fewer rollbacks = smoother, but less responsive. Let players choose (casual vs competitive settings).

---

## Tick Rate and Simulation

### Fixed Tick Rate

The server and all clients simulate at the same fixed rate, independent of frame rate.

```
TICK_RATE = 60  // ticks per second
TICK_DURATION = 1.0 / TICK_RATE  // 16.67ms

accumulator = 0
while running:
    accumulator += frame_delta_time
    while accumulator >= TICK_DURATION:
        process_inputs()
        step_simulation(TICK_DURATION)
        accumulator -= TICK_DURATION
        current_tick += 1
    render(accumulator / TICK_DURATION)  // interpolation factor
```

| Game Type | Typical Tick Rate |
|-----------|-------------------|
| FPS (competitive) | 64-128 |
| Fighting games | 60 |
| Action/platformer | 30-60 |
| Strategy/turn-based | 10-20 |
| MMO | 10-30 |

### Deterministic Simulation

If all clients process the same inputs in the same order, they produce identical results. Required for lockstep and rollback.

**Requirements for determinism:**
- Fixed-point math or IEEE 754 with identical rounding
- Same iteration order for all collections
- No random() — use seeded PRNG
- No floating-point optimizations that reorder operations
- Process inputs in canonical order (player ID sort)

---

## Transport Protocols

### UDP vs TCP

| | UDP | TCP |
|--|-----|-----|
| Ordering | None (you handle it) | Guaranteed |
| Reliability | None (you handle it) | Guaranteed |
| Latency | Minimal | Head-of-line blocking |
| **Use for** | Gameplay state, inputs | Chat, login, file transfer |

Most real-time games use UDP with custom reliability on top.

### Reliable UDP Patterns

```
// Sender
function send_reliable(packet):
    packet.sequence = next_sequence++
    packet.needs_ack = true
    send(packet)
    unacked[packet.sequence] = { packet, send_time: now() }

// Receiver
function on_receive(packet):
    send_ack(packet.sequence)
    if packet.sequence in already_received:
        return  // duplicate
    already_received.add(packet.sequence)
    process(packet)

// Sender retransmit
function check_retransmits():
    for seq, info in unacked:
        if now() - info.send_time > RTT * 1.5:
            resend(info.packet)
            info.send_time = now()
```

### Channel Multiplexing

Different data streams need different reliability:

| Channel | Reliability | Ordering | Data |
|---------|------------|----------|------|
| Input | Reliable | Ordered | Player inputs |
| State | Unreliable | Sequenced (drop old) | Entity positions |
| Events | Reliable | Unordered | Damage, spawns, deaths |
| Chat | Reliable | Ordered | Text messages |

---

## Connection Management

### Handshake

```
// 1. Client sends connect request with protocol version
Client → Server: CONNECT { version: 2, token: auth_token }

// 2. Server validates and assigns player ID
Server → Client: ACCEPT { player_id: 7, tick_rate: 60, world_seed: 42 }

// 3. Client acknowledges
Client → Server: ACK { player_id: 7 }

// Connection established
```

### Heartbeat and Timeout

```
HEARTBEAT_INTERVAL = 1.0  // seconds
TIMEOUT = 5.0  // seconds without any packet = disconnect

function update():
    if now() - last_send_time > HEARTBEAT_INTERVAL:
        send_heartbeat()
    if now() - last_receive_time > TIMEOUT:
        disconnect("timeout")
```

### Reconnection

Allow players to reconnect after brief disconnections:

1. Server keeps player state for N seconds after disconnect
2. Client reconnects with a session token
3. Server sends full state snapshot to resync
4. Client resumes with prediction/reconciliation

---

## Matchmaking

### Skill-Based Matchmaking (SBMM)

```
// Elo-style rating update
function update_rating(winner, loser, K=32):
    expected_win = 1.0 / (1.0 + 10^((loser.rating - winner.rating) / 400))
    winner.rating += K * (1.0 - expected_win)
    loser.rating += K * (0.0 - (1.0 - expected_win))
```

### Match Quality

```
function match_quality(player_a, player_b):
    rating_diff = abs(player_a.rating - player_b.rating)
    ping_diff = abs(player_a.ping - player_b.ping)
    wait_factor = player_a.wait_time / MAX_WAIT  // relax criteria over time

    score = 1.0
    score -= rating_diff / MAX_RATING_DIFF * 0.5
    score -= ping_diff / MAX_PING_DIFF * 0.3
    score += wait_factor * 0.2  // allow wider matches after waiting
    return clamp(score, 0, 1)
```

### Lobby vs Queue

**Lobby:** Players create/join rooms. Good for casual, community-driven games. Player chooses who to play with.

**Queue:** Players enter a pool; system creates matches. Good for competitive, ranked play. Optimizes for fair matches.

---

## Security

### Never Trust the Client

The client is compromised by default. Every input must be validated server-side.

```
// Server validation
function validate_move(player, input):
    max_speed = player.base_speed * (1 + player.buffs.speed)
    requested_distance = length(input.movement) * dt
    if requested_distance > max_speed * dt * 1.1:  // 10% tolerance for float errors
        // Reject or clamp
        input.movement = normalize(input.movement) * max_speed
    return input
```

### Common Cheats and Countermeasures

| Cheat | Prevention |
|-------|-----------|
| Speed hack | Server validates movement distance per tick |
| Teleport | Server rejects impossible position changes |
| Wallhack | Interest management — don't send hidden entities |
| Aimbot | Statistical analysis of aim patterns |
| Damage hack | Server calculates all damage |
| Packet manipulation | Server validates all state transitions |

### Rate Limiting

```
function process_client_input(client, input):
    if client.inputs_this_second > MAX_INPUTS_PER_SECOND:
        warn(client, "rate limited")
        return
    client.inputs_this_second += 1
    // process normally
```

---

## Bandwidth Optimization

### Packet Budgets

| Game Type | Per-Client Budget | Typical Packet Size |
|-----------|-------------------|---------------------|
| FPS (64 tick) | 64-128 KB/s | 1-2 KB/packet |
| Fighting (P2P) | 8-16 KB/s | 128-256 bytes/packet |
| MMO | 16-32 KB/s | Variable |
| Turn-based | <1 KB/s | Variable |

### Bit Packing

Pack multiple values into minimal bits instead of using full bytes.

```
function write_player_state(writer, state):
    writer.write_bits(state.x, 16)         // 0-65535
    writer.write_bits(state.y, 16)         // 0-65535
    writer.write_bits(state.angle, 8)      // 0-255 (1.4° precision)
    writer.write_bits(state.health, 7)     // 0-127
    writer.write_bits(state.weapon_id, 4)  // 0-15
    writer.write_bit(state.crouching)      // 0-1
    // Total: 52 bits = 6.5 bytes vs 24+ bytes with naive serialization
```

### Priority Accumulator

When bandwidth is tight, send the most important updates first.

```
function prioritize_entities(viewer, entities):
    for entity in entities:
        priority = 1.0
        priority *= distance_factor(viewer, entity)   // closer = higher
        priority *= relevance_factor(entity)           // players > projectiles > decorations
        priority *= staleness_factor(entity)           // longer since last sent = higher
        entity.accumulated_priority += priority

    // Sort by accumulated priority, send top N
    sorted = sort_descending(entities, by: accumulated_priority)
    for entity in sorted[:BUDGET]:
        send_update(entity)
        entity.accumulated_priority = 0  // reset after sending
```

---

## Clock Synchronization

Clients and server must agree on time for interpolation and lag compensation.

```
// NTP-style clock sync
function sync_clock():
    t1 = local_time()
    send_ping(t1)

function on_pong(t1, t2, t3):
    t4 = local_time()
    rtt = (t4 - t1) - (t3 - t2)
    one_way_delay = rtt / 2
    clock_offset = ((t2 - t1) + (t3 - t4)) / 2

    // Smooth the offset over multiple samples
    offsets.add(clock_offset)
    estimated_offset = median(offsets)  // median resists outliers
```

**Perform sync:** On connect, then periodically (every 10-30 seconds). Use the median of recent samples.

---

## Architecture Patterns Summary

| Pattern | Latency Feel | Cheat Resistance | Complexity | Best For |
|---------|-------------|-------------------|-----------|---------|
| Lockstep P2P | High (waits for all) | Low | Low | Turn-based, RTS (small) |
| Lockstep + Input Delay | Medium | Low | Low | Fighting games (legacy) |
| Rollback P2P | Low | Low | High | Fighting games (modern) |
| Client-Server Authoritative | Medium | High | Medium | FPS, MMO, competitive |
| Client Prediction + Reconciliation | Low | High | High | FPS, action games |
| Relay Server | Medium | Low | Low | Co-op, casual |

---

## Choosing Your Architecture

```
Start here:
├── Competitive / anti-cheat important?
│   ├── Yes → Client-Server Authoritative
│   │   ├── Fast-paced? → Add client prediction + reconciliation
│   │   └── Slower-paced? → Server state sync is enough
│   └── No → Peer-to-Peer
│       ├── Needs frame-perfect sync? → Rollback netcode
│       ├── Turn-based / slow? → Lockstep
│       └── Co-op / casual? → Relay server
└── Player count?
    ├── 2-8 → P2P viable
    ├── 8-64 → Client-server preferred
    └── 64+ → Client-server required (with interest management)
```

---

*Implementation examples are available in engine-specific modules.*
