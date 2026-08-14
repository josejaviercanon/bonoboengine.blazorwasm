# G9 — Networking


> **Category:** Guide · **Related:** [R1 Library Stack](../reference/R1_library_stack.md) · [R2 Capability Matrix](../reference/R2_capability_matrix.md)

> Deep dive into multiplayer networking for MonoGame 2D games using LiteNetLib, covering client-server architecture, prediction, rollback netcode, deterministic simulation, and Arch ECS integration.

---

## 1. LiteNetLib Setup

**Install:** `dotnet add package LiteNetLib`

LiteNetLib provides reliable/unreliable UDP, connection management, NAT traversal, and serialization helpers. It's the transport layer — you build game networking on top.

### 1.1 Server Setup

```csharp
using LiteNetLib;
using LiteNetLib.Utils;

public class GameServer : INetEventListener
{
    private NetManager _server;
    private readonly Dictionary<int, NetPeer> _peers = new();
    private readonly NetPacketProcessor _packetProcessor = new();
    private int _nextPlayerId;
    private uint _serverTick;

    public void Start(int port = 9050)
    {
        _server = new NetManager(this)
        {
            AutoRecycle = true,
            UpdateTime = 15,                // poll interval in ms
            DisconnectTimeout = 10000,      // 10s before dropping
            EnableStatistics = true,
            NatPunchEnabled = true
        };
        _server.Start(port);
        Console.WriteLine($"Server listening on port {port}");
    }

    public void Update()
    {
        _server.PollEvents();
        _serverTick++;
    }

    public void Stop() => _server.Stop();

    // --- INetEventListener ---
    public void OnConnectionRequest(ConnectionRequest request)
    {
        // Accept with key validation
        if (_peers.Count < 16) // max players
            request.AcceptIfKey("my_game_v1");
        else
            request.Reject();
    }

    public void OnPeerConnected(NetPeer peer)
    {
        int playerId = _nextPlayerId++;
        _peers[playerId] = peer;
        peer.Tag = playerId;

        // Send player their assigned ID
        var writer = new NetDataWriter();
        writer.Put((byte)ServerPacketType.Welcome);
        writer.Put(playerId);
        writer.Put(_serverTick);
        peer.Send(writer, DeliveryMethod.ReliableOrdered);

        Console.WriteLine($"Player {playerId} connected from {peer}");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        int playerId = (int)peer.Tag;
        _peers.Remove(playerId);
        Console.WriteLine($"Player {playerId} disconnected: {disconnectInfo.Reason}");
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader,
        byte channelNumber, DeliveryMethod deliveryMethod)
    {
        var packetType = (ClientPacketType)reader.GetByte();
        int playerId = (int)peer.Tag;

        switch (packetType)
        {
            case ClientPacketType.PlayerInput:
                HandlePlayerInput(playerId, reader);
                break;
            case ClientPacketType.ChatMessage:
                HandleChat(playerId, reader);
                break;
        }
    }

    public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError error)
        => Console.WriteLine($"Network error: {error}");

    public void OnNetworkReceiveUnconnected(IPEndPoint remoteEndPoint,
        NetPacketReader reader, UnconnectedMessageType messageType) { }

    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }

    // --- Broadcast to all peers ---
    public void Broadcast(NetDataWriter writer, DeliveryMethod method)
    {
        foreach (var peer in _peers.Values)
            peer.Send(writer, method);
    }

    public void BroadcastExcept(int excludePlayerId, NetDataWriter writer, DeliveryMethod method)
    {
        foreach (var (id, peer) in _peers)
        {
            if (id != excludePlayerId)
                peer.Send(writer, method);
        }
    }
}
```

### 1.2 Client Setup

```csharp
public class GameClient : INetEventListener
{
    private NetManager _client;
    private NetPeer _serverPeer;
    public int LocalPlayerId { get; private set; } = -1;
    public bool IsConnected => _serverPeer?.ConnectionState == ConnectionState.Connected;
    public uint ServerTick { get; private set; }
    public int Ping => _serverPeer?.Ping ?? 0;

    public event Action OnConnected;
    public event Action<string> OnDisconnected;
    public event Action<NetPacketReader> OnStateReceived;

    public void Connect(string address, int port = 9050)
    {
        _client = new NetManager(this)
        {
            AutoRecycle = true,
            UpdateTime = 15,
            NatPunchEnabled = true
        };
        _client.Start();
        _client.Connect(address, port, "my_game_v1");
    }

    public void Update() => _client?.PollEvents();

    public void Send(NetDataWriter writer, DeliveryMethod method)
    {
        _serverPeer?.Send(writer, method);
    }

    public void Disconnect() => _client?.Stop();

    // --- INetEventListener ---
    public void OnPeerConnected(NetPeer peer)
    {
        _serverPeer = peer;
        Console.WriteLine("Connected to server");
    }

    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
    {
        _serverPeer = null;
        OnDisconnected?.Invoke(info.Reason.ToString());
    }

    public void OnNetworkReceive(NetPeer peer, NetPacketReader reader,
        byte channelNumber, DeliveryMethod deliveryMethod)
    {
        var packetType = (ServerPacketType)reader.GetByte();

        switch (packetType)
        {
            case ServerPacketType.Welcome:
                LocalPlayerId = reader.GetInt();
                ServerTick = reader.GetUInt();
                OnConnected?.Invoke();
                break;
            case ServerPacketType.WorldState:
                ServerTick = reader.GetUInt();
                OnStateReceived?.Invoke(reader);
                break;
        }
    }

    public void OnConnectionRequest(ConnectionRequest request) => request.Reject();
    public void OnNetworkError(IPEndPoint endPoint, System.Net.Sockets.SocketError error) { }
    public void OnNetworkReceiveUnconnected(IPEndPoint ep, NetPacketReader r,
        UnconnectedMessageType t) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
}
```

### 1.3 Packet Type Enums

```csharp
public enum ClientPacketType : byte
{
    PlayerInput     = 1,
    ChatMessage     = 2,
    Ping            = 3,
    LobbyReady      = 4,
    RequestSpawn    = 5
}

public enum ServerPacketType : byte
{
    Welcome         = 1,
    WorldState      = 2,
    PlayerJoined    = 3,
    PlayerLeft      = 4,
    ChatMessage     = 5,
    LobbyUpdate     = 6,
    GameStart       = 7
}
```

### 1.4 Delivery Methods

| Method | Behavior | Use Case |
|---|---|---|
| `ReliableOrdered` | Guaranteed, in-order | Chat, inventory, game events, RPCs |
| `ReliableUnordered` | Guaranteed, any order | Bulk data where order doesn't matter |
| `ReliableSequenced` | Guaranteed, drops old | Ability cooldown state (only latest matters) |
| `Unreliable` | Fire-and-forget | Position updates at high frequency |
| `Sequenced` | Unreliable, drops old | Input snapshots — only latest matters |

**Rule of thumb:** Use `Unreliable` or `Sequenced` for anything sent every frame. Use `ReliableOrdered` for events that must arrive.

---

## 2. Packet Serialization

### 2.1 NetDataWriter / NetDataReader Patterns

LiteNetLib provides `NetDataWriter` and `NetDataReader` for binary serialization. They're fast, zero-allocation, and manual:

```csharp
// --- Writing a packet ---
var writer = new NetDataWriter();
writer.Put((byte)ServerPacketType.WorldState);
writer.Put(_serverTick);
writer.Put((ushort)entityCount);

foreach (var entity in entities)
{
    writer.Put(entity.NetworkId);
    writer.Put(entity.Position.X);
    writer.Put(entity.Position.Y);
    writer.Put(entity.Health);
}

peer.Send(writer, DeliveryMethod.Unreliable);

// --- Reading a packet ---
void HandleWorldState(NetPacketReader reader)
{
    uint tick = reader.GetUInt();
    ushort count = reader.GetUShort();

    for (int i = 0; i < count; i++)
    {
        int networkId = reader.GetInt();
        float x = reader.GetFloat();
        float y = reader.GetFloat();
        int health = reader.GetInt();

        ApplyEntityState(networkId, x, y, health);
    }
}
```

### 2.2 INetSerializable Interface

For structured packet types, implement `INetSerializable`:

```csharp
public struct PlayerInputPacket : INetSerializable
{
    public uint Tick;
    public byte InputFlags;     // bitmask: up/down/left/right/jump/attack
    public float AimAngle;
    public byte SequenceNumber;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Tick);
        writer.Put(InputFlags);
        writer.Put(AimAngle);
        writer.Put(SequenceNumber);
    }

    public void Deserialize(NetDataReader reader)
    {
        Tick = reader.GetUInt();
        InputFlags = reader.GetByte();
        AimAngle = reader.GetFloat();
        SequenceNumber = reader.GetByte();
    }
}

public struct WorldStatePacket : INetSerializable
{
    public uint Tick;
    public EntitySnapshot[] Entities;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Tick);
        writer.Put((ushort)Entities.Length);
        foreach (var e in Entities)
            e.Serialize(writer);
    }

    public void Deserialize(NetDataReader reader)
    {
        Tick = reader.GetUInt();
        ushort count = reader.GetUShort();
        Entities = new EntitySnapshot[count];
        for (int i = 0; i < count; i++)
        {
            Entities[i] = new EntitySnapshot();
            Entities[i].Deserialize(reader);
        }
    }
}

public struct EntitySnapshot : INetSerializable
{
    public int NetworkId;
    public float X, Y;
    public float VelocityX, VelocityY;
    public short Health;
    public byte AnimationState;

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(NetworkId);
        writer.Put(X);
        writer.Put(Y);
        writer.Put(VelocityX);
        writer.Put(VelocityY);
        writer.Put(Health);
        writer.Put(AnimationState);
    }

    public void Deserialize(NetDataReader reader)
    {
        NetworkId = reader.GetInt();
        X = reader.GetFloat();
        Y = reader.GetFloat();
        VelocityX = reader.GetFloat();
        VelocityY = reader.GetFloat();
        Health = reader.GetShort();
        AnimationState = reader.GetByte();
    }
}
```

### 2.3 NetPacketProcessor for Typed Packets

`NetPacketProcessor` lets you register handlers by type, avoiding manual switch statements:

```csharp
// Server side
var processor = new NetPacketProcessor();

// Register nested types first
processor.RegisterNestedType<PlayerInputPacket>();

// Subscribe to typed packets
processor.SubscribeReusable<PlayerInputPacket, NetPeer>((packet, peer) =>
{
    int playerId = (int)peer.Tag;
    ProcessInput(playerId, packet);
});

// In OnNetworkReceive
public void OnNetworkReceive(NetPeer peer, NetPacketReader reader,
    byte channelNumber, DeliveryMethod deliveryMethod)
{
    processor.ReadAllPackets(reader, peer);
}

// Client sending
var writer = new NetDataWriter();
processor.Write(writer, new PlayerInputPacket
{
    Tick = _localTick,
    InputFlags = GetInputFlags(),
    AimAngle = _aimAngle,
    SequenceNumber = _seqNum++
});
_client.Send(writer, DeliveryMethod.Sequenced);
```

### 2.4 Input Bitmask Encoding

Pack multiple boolean inputs into a single byte:

```csharp
[Flags]
public enum InputFlags : byte
{
    None    = 0,
    Up      = 1 << 0,
    Down    = 1 << 1,
    Left    = 1 << 2,
    Right   = 1 << 3,
    Jump    = 1 << 4,
    Attack  = 1 << 5,
    Dash    = 1 << 6,
    Interact= 1 << 7
}

// Encoding
InputFlags flags = InputFlags.None;
if (keyboard.IsKeyDown(Keys.W)) flags |= InputFlags.Up;
if (keyboard.IsKeyDown(Keys.S)) flags |= InputFlags.Down;
if (keyboard.IsKeyDown(Keys.A)) flags |= InputFlags.Left;
if (keyboard.IsKeyDown(Keys.D)) flags |= InputFlags.Right;
if (keyboard.IsKeyDown(Keys.Space)) flags |= InputFlags.Jump;

// Decoding
bool isMovingUp = (flags & InputFlags.Up) != 0;
bool isAttacking = (flags & InputFlags.Attack) != 0;
```

---

## 3. Client-Server Authority Model

The server is the single source of truth. Clients send inputs, the server simulates, and broadcasts authoritative state. Clients predict locally so the game feels responsive.

### 3.1 Server Game Loop

```csharp
public class AuthoritativeServer
{
    private readonly GameServer _network;
    private readonly Dictionary<int, PlayerState> _players = new();
    private readonly Dictionary<int, Queue<PlayerInputPacket>> _inputQueues = new();
    private uint _tick;

    private const float TickRate = 1f / 60f;        // 60 ticks/sec simulation
    private const float SendRate = 1f / 20f;         // 20 snapshots/sec to clients
    private float _tickAccumulator;
    private float _sendAccumulator;

    public void Update(float deltaTime)
    {
        _network.Update();

        _tickAccumulator += deltaTime;
        _sendAccumulator += deltaTime;

        // Fixed timestep simulation
        while (_tickAccumulator >= TickRate)
        {
            SimulateTick();
            _tickAccumulator -= TickRate;
            _tick++;
        }

        // Send snapshots at lower rate
        if (_sendAccumulator >= SendRate)
        {
            BroadcastWorldState();
            _sendAccumulator -= SendRate;
        }
    }

    private void SimulateTick()
    {
        // Process one input per player per tick
        foreach (var (playerId, state) in _players)
        {
            PlayerInputPacket input = default;

            if (_inputQueues.TryGetValue(playerId, out var queue) && queue.Count > 0)
                input = queue.Dequeue();
            // else: no input this tick, player stands still

            // Apply input with server authority
            ApplyMovement(ref state, input);
            ValidatePosition(ref state);  // clamp to world bounds, collision, etc.
            _players[playerId] = state;
        }
    }

    private void ApplyMovement(ref PlayerState state, PlayerInputPacket input)
    {
        const float Speed = 200f;
        float dx = 0, dy = 0;

        if ((input.InputFlags & (byte)InputFlags.Left) != 0) dx -= Speed;
        if ((input.InputFlags & (byte)InputFlags.Right) != 0) dx += Speed;
        if ((input.InputFlags & (byte)InputFlags.Up) != 0) dy -= Speed;
        if ((input.InputFlags & (byte)InputFlags.Down) != 0) dy += Speed;

        state.X += dx * TickRate;
        state.Y += dy * TickRate;
        state.LastProcessedInput = input.SequenceNumber;
    }

    private void ValidatePosition(ref PlayerState state)
    {
        // Server-authoritative bounds checking
        state.X = Math.Clamp(state.X, 0, 4096);
        state.Y = Math.Clamp(state.Y, 0, 4096);
    }

    private void BroadcastWorldState()
    {
        var writer = new NetDataWriter();
        writer.Put((byte)ServerPacketType.WorldState);
        writer.Put(_tick);
        writer.Put((ushort)_players.Count);

        foreach (var (id, state) in _players)
        {
            writer.Put(id);
            writer.Put(state.X);
            writer.Put(state.Y);
            writer.Put(state.Health);
            writer.Put(state.LastProcessedInput);
        }

        _network.Broadcast(writer, DeliveryMethod.Unreliable);
    }

    public void HandlePlayerInput(int playerId, NetPacketReader reader)
    {
        var input = new PlayerInputPacket();
        input.Deserialize(reader);

        if (!_inputQueues.ContainsKey(playerId))
            _inputQueues[playerId] = new Queue<PlayerInputPacket>();

        // Buffer inputs (max 10 to prevent memory abuse)
        if (_inputQueues[playerId].Count < 10)
            _inputQueues[playerId].Enqueue(input);
    }
}

public struct PlayerState
{
    public float X, Y;
    public short Health;
    public byte LastProcessedInput;
}
```

### 3.2 Server Authority Validation Patterns

The server should validate everything. Never trust client data:

```csharp
// Speed hack detection
private void ValidateMovement(int playerId, ref PlayerState state, PlayerState previous)
{
    float maxDistPerTick = 250f * TickRate;  // Speed + tolerance margin
    float dist = MathF.Sqrt(
        (state.X - previous.X) * (state.X - previous.X) +
        (state.Y - previous.Y) * (state.Y - previous.Y)
    );

    if (dist > maxDistPerTick * 1.5f)  // 50% tolerance for network jitter
    {
        // Revert to previous valid position
        state.X = previous.X;
        state.Y = previous.Y;
        Console.WriteLine($"Player {playerId} failed speed check: {dist:F1} > {maxDistPerTick:F1}");
    }
}

// Cooldown validation (server tracks cooldowns, not client)
private readonly Dictionary<int, float> _attackCooldowns = new();

private bool CanPlayerAttack(int playerId)
{
    if (_attackCooldowns.TryGetValue(playerId, out float cooldown) && cooldown > 0)
        return false;

    _attackCooldowns[playerId] = 0.5f;  // 500ms cooldown
    return true;
}
```

---

## 4. Client-Side Prediction and Server Reconciliation

### 4.1 The Problem

Without prediction, the local player feels laggy — every action takes a round trip to the server before you see it. With prediction, the client applies inputs immediately but may diverge from the server. Reconciliation corrects the divergence.

### 4.2 Full Prediction System

```csharp
public class ClientPrediction
{
    // Ring buffer of unacknowledged inputs
    private readonly PlayerInputPacket[] _inputHistory;
    private readonly float[] _predictedXHistory;
    private readonly float[] _predictedYHistory;
    private int _inputHead;
    private readonly int _bufferSize;
    private byte _sequenceNumber;

    // Current predicted state
    public float PredictedX { get; private set; }
    public float PredictedY { get; private set; }

    // Last acknowledged state from server
    private byte _lastAcknowledgedInput;

    private const float Speed = 200f;
    private const float TickRate = 1f / 60f;
    private const float ReconciliationThreshold = 0.5f;  // pixels

    public ClientPrediction(int bufferSize = 256)
    {
        _bufferSize = bufferSize;
        _inputHistory = new PlayerInputPacket[bufferSize];
        _predictedXHistory = new float[bufferSize];
        _predictedYHistory = new float[bufferSize];
    }

    /// <summary>
    /// Called each tick: samples input, predicts locally, sends to server.
    /// </summary>
    public PlayerInputPacket RecordAndPredict(InputFlags flags, float aimAngle)
    {
        var input = new PlayerInputPacket
        {
            Tick = 0, // set by caller
            InputFlags = (byte)flags,
            AimAngle = aimAngle,
            SequenceNumber = _sequenceNumber
        };

        // Store input and predicted position
        int idx = _sequenceNumber % _bufferSize;
        _inputHistory[idx] = input;

        // Apply prediction (same logic as server)
        ApplyInput(ref PredictedX, ref PredictedY, input);

        _predictedXHistory[idx] = PredictedX;
        _predictedYHistory[idx] = PredictedY;

        _sequenceNumber++;
        return input;
    }

    /// <summary>
    /// Called when server state arrives. Reconciles prediction with authority.
    /// </summary>
    public void OnServerState(float serverX, float serverY, byte lastProcessedInput)
    {
        _lastAcknowledgedInput = lastProcessedInput;

        // Compare server position with what we predicted at that input
        int ackIdx = lastProcessedInput % _bufferSize;
        float predX = _predictedXHistory[ackIdx];
        float predY = _predictedYHistory[ackIdx];

        float errorX = MathF.Abs(serverX - predX);
        float errorY = MathF.Abs(serverY - predY);

        if (errorX > ReconciliationThreshold || errorY > ReconciliationThreshold)
        {
            // Misprediction — snap to server state and replay unacknowledged inputs
            PredictedX = serverX;
            PredictedY = serverY;

            // Replay all inputs after the acknowledged one
            byte replayFrom = (byte)(lastProcessedInput + 1);
            byte replayTo = _sequenceNumber;

            for (byte seq = replayFrom; seq != replayTo; seq++)
            {
                int idx = seq % _bufferSize;
                ApplyInput(ref PredictedX, ref PredictedY, _inputHistory[idx]);
                _predictedXHistory[idx] = PredictedX;
                _predictedYHistory[idx] = PredictedY;
            }
        }
    }

    private static void ApplyInput(ref float x, ref float y, PlayerInputPacket input)
    {
        float dx = 0, dy = 0;
        var flags = (InputFlags)input.InputFlags;

        if ((flags & InputFlags.Left) != 0) dx -= Speed;
        if ((flags & InputFlags.Right) != 0) dx += Speed;
        if ((flags & InputFlags.Up) != 0) dy -= Speed;
        if ((flags & InputFlags.Down) != 0) dy += Speed;

        x += dx * TickRate;
        y += dy * TickRate;
    }
}
```

### 4.3 Usage in Client Game Loop

```csharp
public class ClientGameLoop
{
    private readonly GameClient _network;
    private readonly ClientPrediction _prediction = new();
    private readonly InterpolationBuffer _interpolation = new();

    // Remote players (interpolated), local player (predicted)
    private readonly Dictionary<int, RemoteEntity> _remoteEntities = new();

    public void FixedUpdate()
    {
        _network.Update();

        // 1. Sample local input
        var flags = SampleInput();

        // 2. Predict locally
        var inputPacket = _prediction.RecordAndPredict(flags, _aimAngle);
        inputPacket.Tick = _localTick;

        // 3. Send input to server
        var writer = new NetDataWriter();
        writer.Put((byte)ClientPacketType.PlayerInput);
        inputPacket.Serialize(writer);
        _network.Send(writer, DeliveryMethod.Sequenced);

        // 4. Update interpolation for remote entities
        foreach (var remote in _remoteEntities.Values)
            remote.Interpolate(Time.FixedDelta);
    }

    public void OnServerStateReceived(NetPacketReader reader)
    {
        uint tick = reader.GetUInt();
        ushort count = reader.GetUShort();

        for (int i = 0; i < count; i++)
        {
            int networkId = reader.GetInt();
            float x = reader.GetFloat();
            float y = reader.GetFloat();
            short health = reader.GetShort();
            byte lastInput = reader.GetByte();

            if (networkId == _network.LocalPlayerId)
            {
                // Reconcile local prediction
                _prediction.OnServerState(x, y, lastInput);
            }
            else
            {
                // Buffer snapshot for interpolation
                if (!_remoteEntities.ContainsKey(networkId))
                    _remoteEntities[networkId] = new RemoteEntity();

                _remoteEntities[networkId].AddSnapshot(tick, x, y);
            }
        }
    }
}
```

---

## 5. Interpolation and Extrapolation

### 5.1 Snapshot Interpolation Buffer

Remote entities render behind real-time to smooth out network jitter:

```csharp
public class InterpolationBuffer
{
    private readonly struct Snapshot
    {
        public readonly uint Tick;
        public readonly float X, Y;
        public readonly double Timestamp;

        public Snapshot(uint tick, float x, float y, double timestamp)
        {
            Tick = tick;
            X = x;
            Y = y;
            Timestamp = timestamp;
        }
    }

    private readonly Snapshot[] _buffer = new Snapshot[32];
    private int _head;
    private int _count;

    // Render 100ms behind server time to absorb jitter
    private const double InterpolationDelay = 0.1;

    public float InterpolatedX { get; private set; }
    public float InterpolatedY { get; private set; }

    public void AddSnapshot(uint tick, float x, float y)
    {
        double now = GetTime();
        _buffer[_head] = new Snapshot(tick, x, y, now);
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;
    }

    public void Update()
    {
        double renderTime = GetTime() - InterpolationDelay;

        // Find the two snapshots that bracket renderTime
        Snapshot? before = null;
        Snapshot? after = null;

        for (int i = 0; i < _count; i++)
        {
            int idx = ((_head - 1 - i) + _buffer.Length) % _buffer.Length;
            ref var snap = ref _buffer[idx];

            if (snap.Timestamp <= renderTime)
            {
                before = snap;

                // Look for the next one after renderTime
                if (i > 0)
                {
                    int nextIdx = (idx + 1) % _buffer.Length;
                    after = _buffer[nextIdx];
                }
                break;
            }
        }

        if (before.HasValue && after.HasValue)
        {
            // Interpolate between the two snapshots
            double range = after.Value.Timestamp - before.Value.Timestamp;
            float t = (range > 0.0001)
                ? (float)((renderTime - before.Value.Timestamp) / range)
                : 0f;
            t = Math.Clamp(t, 0f, 1f);

            InterpolatedX = before.Value.X + (after.Value.X - before.Value.X) * t;
            InterpolatedY = before.Value.Y + (after.Value.Y - before.Value.Y) * t;
        }
        else if (before.HasValue)
        {
            // No future snapshot — extrapolate or hold
            InterpolatedX = before.Value.X;
            InterpolatedY = before.Value.Y;
        }
    }

    private static double GetTime()
        => (double)System.Diagnostics.Stopwatch.GetTimestamp()
           / System.Diagnostics.Stopwatch.Frequency;
}
```

### 5.2 RemoteEntity Wrapper

```csharp
public class RemoteEntity
{
    private readonly InterpolationBuffer _buffer = new();

    public float X => _buffer.InterpolatedX;
    public float Y => _buffer.InterpolatedY;

    public void AddSnapshot(uint tick, float x, float y)
        => _buffer.AddSnapshot(tick, x, y);

    public void Interpolate(float dt)
        => _buffer.Update();
}
```

### 5.3 Extrapolation with Velocity

When snapshots stop arriving (packet loss), extrapolate using the last known velocity:

```csharp
public class ExtrapolatingBuffer
{
    private float _lastX, _lastY;
    private float _velX, _velY;
    private double _lastSnapshotTime;
    private const double MaxExtrapolation = 0.25;  // max 250ms of extrapolation

    public float X { get; private set; }
    public float Y { get; private set; }

    public void AddSnapshot(float x, float y, float vx, float vy)
    {
        _velX = vx;
        _velY = vy;
        _lastX = x;
        _lastY = y;
        _lastSnapshotTime = GetTime();
    }

    public void Update()
    {
        double elapsed = GetTime() - _lastSnapshotTime;

        if (elapsed < MaxExtrapolation)
        {
            // Extrapolate from last known position + velocity
            X = _lastX + _velX * (float)elapsed;
            Y = _lastY + _velY * (float)elapsed;
        }
        else
        {
            // Too long without data — hold position
            X = _lastX + _velX * (float)MaxExtrapolation;
            Y = _lastY + _velY * (float)MaxExtrapolation;
        }
    }

    private static double GetTime()
        => (double)System.Diagnostics.Stopwatch.GetTimestamp()
           / System.Diagnostics.Stopwatch.Frequency;
}
```

---

## 6. Rollback Netcode (Fighting Games)

No off-the-shelf C# library — must implement using GGPO concepts. Rollback requires deterministic simulation, state snapshots, and input prediction.

### 6.1 Core Concepts

1. **Input delay:** Add N frames of delay to local input to give the network time to deliver remote inputs before they're needed.
2. **Prediction:** If remote input hasn't arrived, predict it (usually: repeat last known input).
3. **Rollback:** When the actual remote input arrives and differs from prediction, rewind to that frame, apply correct inputs, and re-simulate forward to the present.
4. **Determinism:** The simulation must produce bit-identical results given the same inputs — see §10 Fixed-Point Math.

### 6.2 Ring Buffer for Game State

```csharp
public class StateRingBuffer<T> where T : struct
{
    private readonly T[] _buffer;
    private readonly int _capacity;

    public StateRingBuffer(int capacity)
    {
        _capacity = capacity;
        _buffer = new T[capacity];
    }

    public ref T this[int frame] => ref _buffer[frame % _capacity];

    public void Save(int frame, in T state) => _buffer[frame % _capacity] = state;
    public T Load(int frame) => _buffer[frame % _capacity];
}
```

### 6.3 Game State Snapshot

For rollback, you need to capture and restore the entire game state:

```csharp
public struct FighterState
{
    public int X, Y;               // fixed-point or integer positions
    public int VelocityX, VelocityY;
    public int Health;
    public int HitstunFrames;
    public int BlockstunFrames;
    public int AnimationFrame;
    public byte CurrentState;       // idle, crouch, jumping, attacking, etc.
    public byte FacingRight;        // 0 or 1
}

public struct GameState
{
    public FighterState Player1;
    public FighterState Player2;
    public int FrameNumber;
    public int RoundTimer;

    // Deep copy for safety (structs are value types so this is trivial)
    public GameState Clone() => this;
}
```

### 6.4 Full Rollback Manager

```csharp
public class RollbackManager
{
    // Configuration
    private const int MaxRollbackFrames = 8;
    private const int InputDelayFrames = 2;
    private const int StateBufferSize = 128;

    // State storage
    private readonly StateRingBuffer<GameState> _stateHistory = new(StateBufferSize);
    private readonly StateRingBuffer<InputPair> _inputHistory = new(StateBufferSize);
    private readonly StateRingBuffer<InputPair> _predictedInputs = new(StateBufferSize);

    // Frame tracking
    private int _localFrame;
    private int _remoteConfirmedFrame;       // last frame we have confirmed remote input
    private int _syncFrame;                  // last frame both inputs are confirmed

    // Input storage
    public struct InputPair
    {
        public byte LocalInput;
        public byte RemoteInput;
        public bool RemoteConfirmed;
    }

    // The deterministic simulation function (provided externally)
    private readonly Func<GameState, byte, byte, GameState> _simulate;

    public GameState CurrentState { get; private set; }

    public RollbackManager(Func<GameState, byte, byte, GameState> simulate)
    {
        _simulate = simulate;
    }

    /// <summary>
    /// Called each frame with local input and any received remote input.
    /// </summary>
    public void AdvanceFrame(byte localInput, byte? receivedRemoteInput, int remoteFrame)
    {
        // Save current state before advancing
        _stateHistory.Save(_localFrame, CurrentState);

        // Store confirmed remote input if we received one
        if (receivedRemoteInput.HasValue)
        {
            for (int f = _remoteConfirmedFrame + 1; f <= remoteFrame; f++)
            {
                ref var stored = ref _inputHistory[f];
                stored.RemoteInput = receivedRemoteInput.Value;
                stored.RemoteConfirmed = true;
            }
            int previousConfirmed = _remoteConfirmedFrame;
            _remoteConfirmedFrame = remoteFrame;

            // Check if we mispredicted — if so, rollback
            bool mispredicted = false;
            for (int f = previousConfirmed + 1; f <= remoteFrame; f++)
            {
                if (_predictedInputs[f].RemoteInput != _inputHistory[f].RemoteInput)
                {
                    mispredicted = true;
                    break;
                }
            }

            if (mispredicted)
                Rollback(previousConfirmed + 1);
        }

        // Predict remote input for current frame (repeat last known)
        byte predictedRemote = PredictRemoteInput();

        // Store inputs for this frame
        ref var frameInput = ref _inputHistory[_localFrame];
        frameInput.LocalInput = localInput;
        if (!frameInput.RemoteConfirmed)
            frameInput.RemoteInput = predictedRemote;

        _predictedInputs[_localFrame] = new InputPair
        {
            LocalInput = localInput,
            RemoteInput = frameInput.RemoteConfirmed ? frameInput.RemoteInput : predictedRemote
        };

        // Simulate one frame
        CurrentState = _simulate(
            CurrentState,
            localInput,
            frameInput.RemoteConfirmed ? frameInput.RemoteInput : predictedRemote
        );

        _localFrame++;
    }

    private void Rollback(int toFrame)
    {
        // Clamp rollback distance
        int rollbackFrames = _localFrame - toFrame;
        if (rollbackFrames > MaxRollbackFrames)
        {
            Console.WriteLine($"WARNING: Rollback of {rollbackFrames} exceeds max {MaxRollbackFrames}");
            return;
        }

        // Restore state at the rollback point
        CurrentState = _stateHistory.Load(toFrame);

        // Re-simulate from rollback point to current frame
        for (int f = toFrame; f < _localFrame; f++)
        {
            _stateHistory.Save(f, CurrentState);

            ref var input = ref _inputHistory[f];
            byte remoteInput = input.RemoteConfirmed
                ? input.RemoteInput
                : PredictRemoteInput();

            CurrentState = _simulate(CurrentState, input.LocalInput, remoteInput);
        }
    }

    private byte PredictRemoteInput()
    {
        // Simple prediction: repeat last confirmed input
        if (_remoteConfirmedFrame >= 0)
            return _inputHistory[_remoteConfirmedFrame].RemoteInput;
        return 0;
    }

    /// <summary>
    /// Get the input to send (delayed by InputDelayFrames).
    /// </summary>
    public byte GetDelayedLocalInput(byte rawInput)
    {
        // In a real implementation, buffer rawInput and return the one
        // from InputDelayFrames ago
        return rawInput;
    }
}
```

### 6.5 Deterministic Simulation Function

```csharp
// This function MUST be deterministic — same inputs = same output, always
public static GameState SimulateFrame(GameState state, byte p1Input, byte p2Input)
{
    state.FrameNumber++;
    state.RoundTimer--;

    // Process Player 1
    ProcessFighter(ref state.Player1, p1Input, ref state.Player2);
    // Process Player 2
    ProcessFighter(ref state.Player2, p2Input, ref state.Player1);

    // Resolve interactions (hits, blocks, clashes)
    ResolveCollisions(ref state);

    return state;
}

private static void ProcessFighter(ref FighterState fighter, byte input, ref FighterState opponent)
{
    // Decrement stun timers
    if (fighter.HitstunFrames > 0) { fighter.HitstunFrames--; return; }
    if (fighter.BlockstunFrames > 0) { fighter.BlockstunFrames--; return; }

    // Movement (integer math for determinism)
    int moveX = 0;
    if ((input & 0x04) != 0) moveX -= 3;  // left
    if ((input & 0x08) != 0) moveX += 3;  // right

    fighter.X += moveX;
    fighter.Y += fighter.VelocityY;

    // Gravity (integer)
    if (fighter.Y < 0)  // above ground
        fighter.VelocityY += 1;  // gravity
    else
    {
        fighter.Y = 0;
        fighter.VelocityY = 0;
    }

    // Jump
    if ((input & 0x01) != 0 && fighter.Y == 0)
        fighter.VelocityY = -12;

    fighter.AnimationFrame++;
}
```

### 6.6 Network Transport for Rollback

```csharp
public class RollbackNetworkTransport
{
    private readonly GameClient _client;
    private readonly RollbackManager _rollback;

    // Send local input with frame number
    public void SendInput(int frame, byte input)
    {
        var writer = new NetDataWriter();
        writer.Put((byte)ClientPacketType.PlayerInput);
        writer.Put(frame);
        writer.Put(input);

        // Send redundant inputs (last N frames) to combat packet loss
        int redundancy = 3;
        for (int i = 1; i <= redundancy && frame - i >= 0; i++)
        {
            writer.Put(frame - i);
            writer.Put(GetHistoricalInput(frame - i));
        }

        _client.Send(writer, DeliveryMethod.Unreliable);
    }

    // Receive remote input
    public void OnInputReceived(NetPacketReader reader)
    {
        int frame = reader.GetInt();
        byte input = reader.GetByte();

        _rollback.AdvanceFrame(GetLocalInput(), input, frame);

        // Process redundant inputs (fill gaps from packet loss)
        while (reader.AvailableBytes >= 5)
        {
            int redundantFrame = reader.GetInt();
            byte redundantInput = reader.GetByte();
            // Store if we haven't received this frame's input yet
            StoreIfMissing(redundantFrame, redundantInput);
        }
    }
}
```

---

## 7. Bandwidth Reduction Techniques

### 7.1 Delta Compression

Only send what changed since the last acknowledged state:

```csharp
public class DeltaCompressor
{
    private readonly Dictionary<int, EntitySnapshot> _lastAckedState = new();

    public void WriteDeltas(NetDataWriter writer, Dictionary<int, EntitySnapshot> current,
        uint ackTick)
    {
        // Gather entities that changed
        var changed = new List<(int Id, EntitySnapshot Snap)>();

        foreach (var (id, snap) in current)
        {
            if (!_lastAckedState.TryGetValue(id, out var prev) || !SnapshotsEqual(prev, snap))
                changed.Add((id, snap));
        }

        writer.Put((ushort)changed.Count);

        foreach (var (id, snap) in changed)
        {
            writer.Put(id);

            // Write a bitmask of which fields changed
            byte mask = 0;
            _lastAckedState.TryGetValue(id, out var prev);

            if (prev.X != snap.X || prev.Y != snap.Y) mask |= 0x01;
            if (prev.VelocityX != snap.VelocityX || prev.VelocityY != snap.VelocityY) mask |= 0x02;
            if (prev.Health != snap.Health) mask |= 0x04;
            if (prev.AnimationState != snap.AnimationState) mask |= 0x08;

            writer.Put(mask);

            if ((mask & 0x01) != 0) { writer.Put(snap.X); writer.Put(snap.Y); }
            if ((mask & 0x02) != 0) { writer.Put(snap.VelocityX); writer.Put(snap.VelocityY); }
            if ((mask & 0x04) != 0) writer.Put(snap.Health);
            if ((mask & 0x08) != 0) writer.Put(snap.AnimationState);

            _lastAckedState[id] = snap;
        }
    }

    private static bool SnapshotsEqual(EntitySnapshot a, EntitySnapshot b)
        => a.X == b.X && a.Y == b.Y && a.VelocityX == b.VelocityX
           && a.VelocityY == b.VelocityY && a.Health == b.Health
           && a.AnimationState == b.AnimationState;
}
```

### 7.2 Quantization

Pack floats into fewer bits when you don't need full precision:

```csharp
public static class Quantization
{
    /// <summary>
    /// Quantize a float position to a ushort (0-65535).
    /// Covers a world range of [0, maxValue].
    /// </summary>
    public static ushort QuantizePosition(float value, float maxValue)
    {
        float normalized = Math.Clamp(value / maxValue, 0f, 1f);
        return (ushort)(normalized * 65535f);
    }

    public static float DequantizePosition(ushort quantized, float maxValue)
    {
        return (quantized / 65535f) * maxValue;
    }

    /// <summary>
    /// Quantize an angle (0 to 2π) into a single byte (256 steps ≈ 1.4° precision).
    /// </summary>
    public static byte QuantizeAngle(float radians)
    {
        float normalized = (radians % MathF.Tau + MathF.Tau) % MathF.Tau / MathF.Tau;
        return (byte)(normalized * 255f);
    }

    public static float DequantizeAngle(byte quantized)
    {
        return (quantized / 255f) * MathF.Tau;
    }

    /// <summary>
    /// Pack two small signed values (-128..127) into one ushort.
    /// Good for velocity components in pixel-art games.
    /// </summary>
    public static ushort PackTwoSBytes(sbyte a, sbyte b)
    {
        return (ushort)(((byte)a << 8) | (byte)b);
    }

    public static (sbyte a, sbyte b) UnpackTwoSBytes(ushort packed)
    {
        return ((sbyte)(packed >> 8), (sbyte)(packed & 0xFF));
    }
}

// Usage: 4096×4096 world, positions quantized from 8 bytes to 4 bytes per entity
var writer = new NetDataWriter();
writer.Put(Quantization.QuantizePosition(entity.X, 4096f));
writer.Put(Quantization.QuantizePosition(entity.Y, 4096f));
// Reader side
float x = Quantization.DequantizePosition(reader.GetUShort(), 4096f);
float y = Quantization.DequantizePosition(reader.GetUShort(), 4096f);
```

### 7.3 Interest Management

Only send data for entities near the player. Critical for large worlds:

```csharp
public class InterestManager
{
    private const float RelevanceRadius = 800f;      // pixels
    private const float RelevanceRadiusSq = RelevanceRadius * RelevanceRadius;

    /// <summary>
    /// Returns the set of entity IDs relevant to the given player position.
    /// </summary>
    public HashSet<int> GetRelevantEntities(float playerX, float playerY,
        Dictionary<int, EntitySnapshot> allEntities)
    {
        var relevant = new HashSet<int>();

        foreach (var (id, entity) in allEntities)
        {
            float dx = entity.X - playerX;
            float dy = entity.Y - playerY;
            float distSq = dx * dx + dy * dy;

            if (distSq <= RelevanceRadiusSq)
                relevant.Add(id);
        }

        return relevant;
    }

    /// <summary>
    /// Build a per-player snapshot containing only relevant entities.
    /// </summary>
    public NetDataWriter BuildPlayerSnapshot(int playerId, float px, float py,
        Dictionary<int, EntitySnapshot> allEntities, uint tick)
    {
        var relevant = GetRelevantEntities(px, py, allEntities);
        var writer = new NetDataWriter();

        writer.Put((byte)ServerPacketType.WorldState);
        writer.Put(tick);
        writer.Put((ushort)relevant.Count);

        foreach (var id in relevant)
        {
            var e = allEntities[id];
            e.Serialize(writer);
        }

        return writer;
    }
}
```

### 7.4 Adaptive Send Rate

Reduce send rate for distant or less important entities:

```csharp
public enum UpdatePriority { High, Medium, Low }

public static UpdatePriority GetPriority(float distanceSq)
{
    if (distanceSq < 200f * 200f) return UpdatePriority.High;   // every tick
    if (distanceSq < 500f * 500f) return UpdatePriority.Medium;  // every 3rd tick
    return UpdatePriority.Low;                                    // every 10th tick
}

// In server broadcast loop
foreach (var (id, entity) in allEntities)
{
    float distSq = DistanceSq(entity, player);
    var priority = GetPriority(distSq);

    bool shouldSend = priority switch
    {
        UpdatePriority.High => true,
        UpdatePriority.Medium => _tick % 3 == 0,
        UpdatePriority.Low => _tick % 10 == 0,
        _ => false
    };

    if (shouldSend)
        WriteEntityToPacket(writer, entity);
}
```

---

## 8. Lobby and Matchmaking

### 8.1 Simple Lobby Server

```csharp
public class LobbyServer
{
    private readonly Dictionary<int, LobbyPlayer> _lobbyPlayers = new();
    private LobbyState _state = LobbyState.Waiting;
    private int _hostPlayerId = -1;

    public struct LobbyPlayer
    {
        public int PlayerId;
        public string Name;
        public bool Ready;
        public NetPeer Peer;
    }

    public enum LobbyState { Waiting, Countdown, InGame }

    public void OnPlayerJoinLobby(int playerId, string name, NetPeer peer)
    {
        _lobbyPlayers[playerId] = new LobbyPlayer
        {
            PlayerId = playerId,
            Name = name,
            Ready = false,
            Peer = peer
        };

        // First player is host
        if (_hostPlayerId == -1)
            _hostPlayerId = playerId;

        BroadcastLobbyState();
    }

    public void OnPlayerReady(int playerId, bool ready)
    {
        if (!_lobbyPlayers.ContainsKey(playerId)) return;

        var player = _lobbyPlayers[playerId];
        player.Ready = ready;
        _lobbyPlayers[playerId] = player;

        BroadcastLobbyState();
        CheckAllReady();
    }

    private void CheckAllReady()
    {
        if (_lobbyPlayers.Count < 2) return;  // need at least 2 players

        bool allReady = _lobbyPlayers.Values.All(p => p.Ready);
        if (allReady && _state == LobbyState.Waiting)
        {
            _state = LobbyState.Countdown;
            StartCountdown();
        }
    }

    private void StartCountdown()
    {
        // 3-second countdown, then start game
        Task.Run(async () =>
        {
            for (int i = 3; i > 0; i--)
            {
                BroadcastCountdown(i);
                await Task.Delay(1000);
            }

            _state = LobbyState.InGame;
            BroadcastGameStart();
        });
    }

    private void BroadcastLobbyState()
    {
        var writer = new NetDataWriter();
        writer.Put((byte)ServerPacketType.LobbyUpdate);
        writer.Put((byte)_lobbyPlayers.Count);

        foreach (var p in _lobbyPlayers.Values)
        {
            writer.Put(p.PlayerId);
            writer.Put(p.Name);
            writer.Put(p.Ready);
            writer.Put(p.PlayerId == _hostPlayerId);
        }

        foreach (var p in _lobbyPlayers.Values)
            p.Peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    private void BroadcastGameStart()
    {
        var writer = new NetDataWriter();
        writer.Put((byte)ServerPacketType.GameStart);

        // Assign spawn positions
        int idx = 0;
        foreach (var p in _lobbyPlayers.Values)
        {
            writer.Put(p.PlayerId);
            writer.Put(100f + idx * 200f);  // spawn X
            writer.Put(300f);                // spawn Y
            idx++;
        }

        foreach (var p in _lobbyPlayers.Values)
            p.Peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }
}
```

### 8.2 Client Lobby Screen

```csharp
public class LobbyScreen
{
    private readonly GameClient _network;
    private readonly List<LobbyPlayerInfo> _players = new();
    private bool _isReady;
    private int _countdown = -1;

    public struct LobbyPlayerInfo
    {
        public int Id;
        public string Name;
        public bool Ready;
        public bool IsHost;
    }

    public void OnLobbyUpdate(NetPacketReader reader)
    {
        _players.Clear();
        byte count = reader.GetByte();

        for (int i = 0; i < count; i++)
        {
            _players.Add(new LobbyPlayerInfo
            {
                Id = reader.GetInt(),
                Name = reader.GetString(),
                Ready = reader.GetBool(),
                IsHost = reader.GetBool()
            });
        }
    }

    public void ToggleReady()
    {
        _isReady = !_isReady;
        var writer = new NetDataWriter();
        writer.Put((byte)ClientPacketType.LobbyReady);
        writer.Put(_isReady);
        _network.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    public void Draw(SpriteBatch sb, SpriteFont font)
    {
        float y = 50;
        sb.DrawString(font, "=== LOBBY ===", new Vector2(100, y), Color.White);
        y += 40;

        foreach (var p in _players)
        {
            string status = p.Ready ? "[READY]" : "[...]";
            string host = p.IsHost ? " (HOST)" : "";
            Color color = p.Ready ? Color.LimeGreen : Color.Gray;
            sb.DrawString(font, $"{p.Name}{host}  {status}", new Vector2(100, y), color);
            y += 30;
        }

        if (_countdown > 0)
        {
            sb.DrawString(font, $"Starting in {_countdown}...",
                new Vector2(100, y + 20), Color.Yellow);
        }
    }
}
```

### 8.3 Simple Matchmaking (Relay Server Pattern)

For public matchmaking without dedicated servers, use a lightweight relay:

```csharp
public class MatchmakingClient
{
    private readonly GameClient _client;

    // Connect to matchmaking server, request a match
    public void FindMatch(string gameMode, int playerRating)
    {
        var writer = new NetDataWriter();
        writer.Put((byte)0x01);  // FindMatch request
        writer.Put(gameMode);
        writer.Put(playerRating);
        _client.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    // Server responds with match details
    public void OnMatchFound(NetPacketReader reader)
    {
        string serverAddress = reader.GetString();
        int serverPort = reader.GetInt();
        string matchToken = reader.GetString();

        // Disconnect from matchmaking, connect to game server
        _client.Disconnect();
        ConnectToGameServer(serverAddress, serverPort, matchToken);
    }
}
```

---

## 9. NAT Traversal

### 9.1 LiteNetLib NAT Punch-Through

LiteNetLib has built-in NAT punch-through support. It requires an intermediary server (the "facilitator") that both peers can reach:

```csharp
// --- NAT Punch Facilitator Server ---
public class NatFacilitator : INatPunchListener, INetEventListener
{
    private NetManager _server;
    private readonly Dictionary<string, IPEndPoint> _waitingPeers = new();

    public void Start(int port = 9051)
    {
        _server = new NetManager(this) { NatPunchEnabled = true };
        _server.NatPunchModule.Init(this);
        _server.Start(port);
    }

    public void Update()
    {
        _server.PollEvents();
        _server.NatPunchModule.PollEvents();
    }

    // INatPunchListener — called when a peer requests NAT introduction
    public void OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
        string token)
    {
        // Token format: "room:ROOM_ID"
        if (_waitingPeers.TryGetValue(token, out var otherEndPoint))
        {
            // Two peers with same token — introduce them
            _server.NatPunchModule.NatIntroduce(
                _waitingPeers[token],   // peer A internal
                remoteEndPoint,          // peer A external
                localEndPoint,           // peer B internal
                remoteEndPoint,          // peer B external
                token
            );
            _waitingPeers.Remove(token);
        }
        else
        {
            // First peer with this token — wait for the other
            _waitingPeers[token] = remoteEndPoint;
        }
    }

    public void OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type,
        string token) { }

    // INetEventListener stubs
    public void OnPeerConnected(NetPeer peer) { }
    public void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) { }
    public void OnNetworkReceive(NetPeer p, NetPacketReader r, byte c, DeliveryMethod d) { }
    public void OnNetworkError(IPEndPoint ep, System.Net.Sockets.SocketError e) { }
    public void OnConnectionRequest(ConnectionRequest request) => request.Reject();
    public void OnNetworkReceiveUnconnected(IPEndPoint ep, NetPacketReader r,
        UnconnectedMessageType t) { }
    public void OnNetworkLatencyUpdate(NetPeer peer, int latency) { }
}
```

```csharp
// --- Client-side NAT Punch ---
public class NatPunchClient : INatPunchListener
{
    private NetManager _netManager;
    private IPEndPoint _peerEndPoint;

    public event Action<IPEndPoint> OnPeerDiscovered;

    public void RequestPunch(string facilitatorAddress, int facilitatorPort, string roomToken)
    {
        _netManager = new NetManager(new DummyListener()) { NatPunchEnabled = true };
        _netManager.NatPunchModule.Init(this);
        _netManager.Start();

        // Send punch request to facilitator
        _netManager.NatPunchModule.SendNatIntroduceRequest(
            facilitatorAddress, facilitatorPort, roomToken);
    }

    public void Update()
    {
        _netManager.PollEvents();
        _netManager.NatPunchModule.PollEvents();
    }

    // Called when NAT punch-through succeeds
    public void OnNatIntroductionSuccess(IPEndPoint targetEndPoint, NatAddressType type,
        string token)
    {
        Console.WriteLine($"NAT punch succeeded! Peer at {targetEndPoint} (type: {type})");
        _peerEndPoint = targetEndPoint;
        OnPeerDiscovered?.Invoke(targetEndPoint);

        // Now connect directly to the peer
        _netManager.Connect(targetEndPoint, "my_game_v1");
    }

    public void OnNatIntroductionRequest(IPEndPoint localEndPoint, IPEndPoint remoteEndPoint,
        string token) { }
}
```

### 9.2 Fallback: Relay Server

NAT punch-through doesn't work for all NAT types (symmetric NAT). Always have a relay fallback:

```csharp
// If NAT punch fails after 5 seconds, fall back to relay
public async Task ConnectToPeer(string roomToken)
{
    var tcs = new TaskCompletionSource<IPEndPoint>();
    _natPunch.OnPeerDiscovered += ep => tcs.TrySetResult(ep);
    _natPunch.RequestPunch(_facilitatorAddress, _facilitatorPort, roomToken);

    var timeout = Task.Delay(5000);
    var completed = await Task.WhenAny(tcs.Task, timeout);

    if (completed == tcs.Task)
    {
        // Direct P2P connection
        ConnectDirect(tcs.Task.Result);
    }
    else
    {
        // Fall back to relay server
        Console.WriteLine("NAT punch failed — using relay");
        ConnectViaRelay(_relayAddress, _relayPort, roomToken);
    }
}
```

---

## 10. Fixed-Point Math for Deterministic Networking

### 10.1 Why Fixed-Point?

IEEE 754 floating-point produces different results across platforms for transcendental functions (sin, cos, sqrt) and even basic operations when FMA optimizations differ. For lockstep networking (RTS, fighting games), every machine must compute bit-identical results. Fixed-point guarantees this.

### 10.2 Fix64 Implementation (Q31.32)

**Install:** `dotnet add package FixedMath.Net` or implement a minimal version:

```csharp
/// <summary>
/// Q16.16 fixed-point number. Range: approximately ±32768 with 1/65536 precision.
/// </summary>
public readonly struct Fix16 : IEquatable<Fix16>, IComparable<Fix16>
{
    public readonly int RawValue;

    private const int FractionalBits = 16;
    private const int One = 1 << FractionalBits;   // 65536

    // Constructors
    private Fix16(int raw) => RawValue = raw;
    public static Fix16 FromRaw(int raw) => new(raw);
    public static Fix16 FromInt(int value) => new(value << FractionalBits);
    public static Fix16 FromFloat(float value) => new((int)(value * One));

    // Conversion
    public float ToFloat() => (float)RawValue / One;
    public int ToInt() => RawValue >> FractionalBits;

    // Constants
    public static readonly Fix16 Zero = new(0);
    public static readonly Fix16 OneVal = new(One);
    public static readonly Fix16 Half = new(One / 2);
    public static readonly Fix16 Pi = FromFloat(3.14159265f);

    // Arithmetic (all deterministic — integer operations only)
    public static Fix16 operator +(Fix16 a, Fix16 b) => new(a.RawValue + b.RawValue);
    public static Fix16 operator -(Fix16 a, Fix16 b) => new(a.RawValue - b.RawValue);
    public static Fix16 operator -(Fix16 a) => new(-a.RawValue);

    public static Fix16 operator *(Fix16 a, Fix16 b)
    {
        // Use long to avoid overflow
        long result = ((long)a.RawValue * b.RawValue) >> FractionalBits;
        return new Fix16((int)result);
    }

    public static Fix16 operator /(Fix16 a, Fix16 b)
    {
        long result = ((long)a.RawValue << FractionalBits) / b.RawValue;
        return new Fix16((int)result);
    }

    // Comparison
    public static bool operator ==(Fix16 a, Fix16 b) => a.RawValue == b.RawValue;
    public static bool operator !=(Fix16 a, Fix16 b) => a.RawValue != b.RawValue;
    public static bool operator <(Fix16 a, Fix16 b) => a.RawValue < b.RawValue;
    public static bool operator >(Fix16 a, Fix16 b) => a.RawValue > b.RawValue;
    public static bool operator <=(Fix16 a, Fix16 b) => a.RawValue <= b.RawValue;
    public static bool operator >=(Fix16 a, Fix16 b) => a.RawValue >= b.RawValue;

    // Deterministic sqrt (Babylonian method)
    public static Fix16 Sqrt(Fix16 value)
    {
        if (value.RawValue <= 0) return Zero;

        long num = (long)value.RawValue << FractionalBits;
        long result = num;

        // Newton's method iterations
        for (int i = 0; i < 16; i++)
        {
            if (result == 0) break;
            result = (result + num / result) >> 1;
        }

        return new Fix16((int)result);
    }

    // Lookup-table sin (256 entries for one quadrant)
    private static readonly int[] SinLut = GenerateSinLut();

    private static int[] GenerateSinLut()
    {
        var lut = new int[256];
        for (int i = 0; i < 256; i++)
            lut[i] = (int)(Math.Sin(i * Math.PI / 512.0) * One);
        return lut;
    }

    public static Fix16 Sin(Fix16 angle)
    {
        // Normalize angle to [0, 4*256) representing [0, 2π)
        int raw = angle.RawValue % (4 * 256 * One / One);  // simplified
        // Full implementation would map angle to LUT index with quadrant handling
        int idx = Math.Abs(raw * 256 / (int)(Pi.RawValue * 2)) % 256;
        return new Fix16(SinLut[idx]);
    }

    public bool Equals(Fix16 other) => RawValue == other.RawValue;
    public int CompareTo(Fix16 other) => RawValue.CompareTo(other.RawValue);
    public override bool Equals(object obj) => obj is Fix16 f && Equals(f);
    public override int GetHashCode() => RawValue;
    public override string ToString() => ToFloat().ToString("F4");
}
```

### 10.3 Fixed-Point Vector

```csharp
public struct FixVec2
{
    public Fix16 X, Y;

    public FixVec2(Fix16 x, Fix16 y) { X = x; Y = y; }

    public static FixVec2 operator +(FixVec2 a, FixVec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static FixVec2 operator -(FixVec2 a, FixVec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static FixVec2 operator *(FixVec2 v, Fix16 s) => new(v.X * s, v.Y * s);

    public Fix16 LengthSquared() => X * X + Y * Y;
    public Fix16 Length() => Fix16.Sqrt(LengthSquared());

    public FixVec2 Normalized()
    {
        var len = Length();
        if (len == Fix16.Zero) return this;
        return new FixVec2(X / len, Y / len);
    }

    public static readonly FixVec2 Zero = new(Fix16.Zero, Fix16.Zero);

    // Convert to MonoGame Vector2 for rendering only
    public Vector2 ToVector2() => new(X.ToFloat(), Y.ToFloat());
}
```

### 10.4 When to Use

| Networking Model | Use Fixed-Point? | Reason |
|---|---|---|
| Lockstep RTS | **Yes** | All clients simulate — must be deterministic |
| Rollback fighting game | **Yes** | Replaying frames must produce identical results |
| Client-server authority | **No** | Server is truth — clients reconcile, no determinism needed |
| Co-op with host | **No** | Host is authoritative — float + reconciliation is fine |

**FixedMath.Net** provides `Fix64` (Q31.32) with lookup-table trig. For 2D games, the simpler Q16.16 above is often sufficient and faster.

---

## 11. Arch ECS Integration

### 11.1 Network Components

```csharp
// Tag: this entity is networked
public record struct NetworkId(int Value);

// Owned by a specific player
public record struct OwnedBy(int PlayerId);

// Server-authoritative position (received from server)
public record struct ServerPosition(float X, float Y);

// For interpolation of remote entities
public record struct InterpolationState(
    float PrevX, float PrevY,
    float TargetX, float TargetY,
    float T   // interpolation progress 0..1
);

// For prediction of local entities
public record struct PredictedPosition(float X, float Y);

// Network sync metadata
public record struct NetworkDirty(bool IsDirty);

// Last received server tick for this entity
public record struct LastServerTick(uint Tick);
```

### 11.2 NetworkSyncSystem (Server)

Gathers dirty entities and broadcasts state:

```csharp
public class ServerNetworkSyncSystem
{
    private readonly QueryDescription _dirtyQuery = new QueryDescription()
        .WithAll<NetworkId, Position, NetworkDirty>();

    private readonly QueryDescription _allNetworked = new QueryDescription()
        .WithAll<NetworkId, Position>();

    private readonly GameServer _server;
    private uint _tick;
    private float _sendAccumulator;
    private const float SendInterval = 1f / 20f;  // 20 Hz

    public ServerNetworkSyncSystem(GameServer server) => _server = server;

    public void Update(World world, float deltaTime)
    {
        _tick++;
        _sendAccumulator += deltaTime;

        if (_sendAccumulator < SendInterval) return;
        _sendAccumulator -= SendInterval;

        var writer = new NetDataWriter();
        writer.Put((byte)ServerPacketType.WorldState);
        writer.Put(_tick);

        // Count entities first
        int count = 0;
        world.Query(in _allNetworked, (ref NetworkId _) => count++);
        writer.Put((ushort)count);

        // Serialize all networked entities
        world.Query(in _allNetworked, (ref NetworkId netId, ref Position pos) =>
        {
            writer.Put(netId.Value);
            writer.Put(pos.X);
            writer.Put(pos.Y);
        });

        _server.Broadcast(writer, DeliveryMethod.Unreliable);

        // Clear dirty flags
        world.Query(in _dirtyQuery, (ref NetworkDirty dirty) =>
        {
            dirty = new NetworkDirty(false);
        });
    }
}
```

### 11.3 ClientNetworkReceiveSystem

Applies server state to ECS entities:

```csharp
public class ClientNetworkReceiveSystem
{
    private readonly World _world;
    private readonly GameClient _client;
    private readonly Dictionary<int, Entity> _networkIdToEntity = new();

    private readonly QueryDescription _remoteQuery = new QueryDescription()
        .WithAll<NetworkId, InterpolationState>()
        .WithNone<OwnedBy>();

    public ClientNetworkReceiveSystem(World world, GameClient client)
    {
        _world = world;
        _client = client;
        _client.OnStateReceived += OnStateReceived;
    }

    private void OnStateReceived(NetPacketReader reader)
    {
        ushort count = reader.GetUShort();

        for (int i = 0; i < count; i++)
        {
            int networkId = reader.GetInt();
            float x = reader.GetFloat();
            float y = reader.GetFloat();

            if (networkId == _client.LocalPlayerId)
            {
                // Handle via prediction system, not interpolation
                continue;
            }

            if (!_networkIdToEntity.TryGetValue(networkId, out var entity))
            {
                // Spawn new remote entity
                entity = _world.Create(
                    new NetworkId(networkId),
                    new Position(x, y),
                    new InterpolationState(x, y, x, y, 1f),
                    new Sprite(/* remote player texture */)
                );
                _networkIdToEntity[networkId] = entity;
            }
            else
            {
                // Push new target for interpolation
                ref var interp = ref _world.Get<InterpolationState>(entity);
                ref var pos = ref _world.Get<Position>(entity);

                interp = new InterpolationState(
                    PrevX: pos.X, PrevY: pos.Y,
                    TargetX: x, TargetY: y,
                    T: 0f
                );
            }
        }
    }
}
```

### 11.4 InterpolationSystem

Smoothly moves remote entities toward their server target:

```csharp
public class InterpolationSystem
{
    private readonly QueryDescription _query = new QueryDescription()
        .WithAll<Position, InterpolationState>();

    private const float InterpolationSpeed = 10f;  // tune to match send rate

    public void Update(World world, float deltaTime)
    {
        world.Query(in _query, (ref Position pos, ref InterpolationState interp) =>
        {
            interp = interp with { T = MathF.Min(interp.T + deltaTime * InterpolationSpeed, 1f) };

            pos = new Position(
                interp.PrevX + (interp.TargetX - interp.PrevX) * interp.T,
                interp.PrevY + (interp.TargetY - interp.PrevY) * interp.T
            );
        });
    }
}
```

### 11.5 Entity Spawn/Despawn Synchronization

```csharp
public class NetworkEntityManager
{
    private readonly World _world;
    private readonly Dictionary<int, Entity> _entities = new();
    private int _nextNetworkId;

    public NetworkEntityManager(World world) => _world = world;

    // Server: spawn a networked entity and notify clients
    public Entity SpawnNetworked(GameServer server, Position pos, Sprite sprite)
    {
        int netId = _nextNetworkId++;
        var entity = _world.Create(
            new NetworkId(netId),
            pos,
            sprite,
            new NetworkDirty(true)
        );
        _entities[netId] = entity;

        // Notify all clients
        var writer = new NetDataWriter();
        writer.Put((byte)ServerPacketType.PlayerJoined);
        writer.Put(netId);
        writer.Put(pos.X);
        writer.Put(pos.Y);
        server.Broadcast(writer, DeliveryMethod.ReliableOrdered);

        return entity;
    }

    // Server: despawn a networked entity
    public void DespawnNetworked(GameServer server, int networkId)
    {
        if (_entities.TryGetValue(networkId, out var entity))
        {
            _world.Destroy(entity);
            _entities.Remove(networkId);

            var writer = new NetDataWriter();
            writer.Put((byte)ServerPacketType.PlayerLeft);
            writer.Put(networkId);
            server.Broadcast(writer, DeliveryMethod.ReliableOrdered);
        }
    }
}
```

---

## 12. Genre-Specific Networking Patterns

### 12.1 Fighting Game (Peer-to-Peer Rollback)

```
┌──────────┐   UDP (inputs only)   ┌──────────┐
│ Player 1  │◄────────────────────►│ Player 2  │
│ Full sim  │   NAT punch / relay  │ Full sim  │
└──────────┘                       └──────────┘
```

Both players run the full simulation. Only inputs are transmitted. Uses rollback (§6) and fixed-point math (§10) for determinism.

```csharp
public class FightingGameNetcode
{
    private readonly RollbackManager _rollback;
    private readonly NatPunchClient _natPunch;
    private NetPeer _opponent;
    private int _localFrame;

    public void FixedUpdate()
    {
        byte localInput = SampleFightingInput();

        // Add input delay to mask latency
        byte delayedInput = _rollback.GetDelayedLocalInput(localInput);

        // Send input to opponent (with redundancy)
        SendInput(_localFrame, delayedInput);

        // Advance simulation with rollback
        byte? remoteInput = GetReceivedInput(_localFrame);
        _rollback.AdvanceFrame(delayedInput, remoteInput, _localFrame);

        _localFrame++;
    }

    private byte SampleFightingInput()
    {
        byte input = 0;
        // Fighting game uses numpad notation internally
        // Directions + 4 buttons (LP, HP, LK, HK)
        var kb = Keyboard.GetState();
        if (kb.IsKeyDown(Keys.W)) input |= 0x01;  // up
        if (kb.IsKeyDown(Keys.S)) input |= 0x02;  // down
        if (kb.IsKeyDown(Keys.A)) input |= 0x04;  // back
        if (kb.IsKeyDown(Keys.D)) input |= 0x08;  // forward
        if (kb.IsKeyDown(Keys.U)) input |= 0x10;  // LP
        if (kb.IsKeyDown(Keys.I)) input |= 0x20;  // HP
        if (kb.IsKeyDown(Keys.J)) input |= 0x40;  // LK
        if (kb.IsKeyDown(Keys.K)) input |= 0x80;  // HK
        return input;
    }
}
```

### 12.2 Co-op Action Game (Client-Server)

```
┌──────────┐                    ┌──────────┐
│ Client 1  │───── inputs ─────►│  Server   │
│ predict   │◄──── state ──────│ authority │
└──────────┘                    └──────────┘
                                     ▲  │
┌──────────┐                         │  │
│ Client 2  │───── inputs ───────────┘  │
│ predict   │◄──── state ──────────────┘
└──────────┘
```

Uses prediction (§4) for the local player, interpolation (§5) for remote players, server authority (§3) for validation.

```csharp
public class CoopGameServer
{
    private readonly AuthoritativeServer _server;
    private readonly World _world;

    // Server-side systems
    private readonly ServerNetworkSyncSystem _syncSystem;

    public void FixedUpdate(float dt)
    {
        // Process all player inputs
        _server.Update(dt);

        // Run game simulation (physics, AI, spawning, etc.)
        RunPhysics(_world, dt);
        RunAI(_world, dt);
        RunSpawning(_world, dt);

        // Broadcast state to clients
        _syncSystem.Update(_world, dt);
    }
}
```

### 12.3 RTS (Deterministic Lockstep)

```
┌──────────┐   inputs + hash    ┌──────────┐
│ Player 1  │◄─────────────────►│ Player 2  │
│ Full sim  │   every N frames  │ Full sim  │
└──────────┘                    └──────────┘
```

All players simulate the same game — only inputs are sent. A hash of the game state is sent periodically to detect desyncs.

```csharp
public class LockstepManager
{
    private readonly Dictionary<int, byte[]> _inputsForTurn = new();
    private int _currentTurn;
    private int _playerCount;
    private const int TurnLengthFrames = 4;  // execute inputs every 4 frames
    private int _frameInTurn;

    // All players must submit input before the turn advances
    public bool TurnReady => _inputsForTurn.Count >= _playerCount;

    public void SubmitLocalInput(byte[] input)
    {
        _inputsForTurn[_localPlayerId] = input;
        BroadcastInput(_localPlayerId, input, _currentTurn);
    }

    public void OnRemoteInput(int playerId, byte[] input, int turn)
    {
        if (turn == _currentTurn)
            _inputsForTurn[playerId] = input;
    }

    public void Update(Func<Dictionary<int, byte[]>, GameState> simulate)
    {
        _frameInTurn++;

        if (_frameInTurn >= TurnLengthFrames)
        {
            if (TurnReady)
            {
                // All inputs received — execute the turn
                var state = simulate(_inputsForTurn);
                _inputsForTurn.Clear();
                _currentTurn++;
                _frameInTurn = 0;

                // Periodic desync check
                if (_currentTurn % 30 == 0)
                    BroadcastStateHash(ComputeStateHash(state));
            }
            else
            {
                // Waiting for inputs — game pauses
                _frameInTurn = TurnLengthFrames;  // hold
            }
        }
    }

    private uint ComputeStateHash(GameState state)
    {
        // Simple hash of all entity positions for desync detection
        uint hash = 2166136261;
        hash ^= (uint)state.Player1.X; hash *= 16777619;
        hash ^= (uint)state.Player1.Y; hash *= 16777619;
        hash ^= (uint)state.Player2.X; hash *= 16777619;
        hash ^= (uint)state.Player2.Y; hash *= 16777619;
        return hash;
    }
}
```

### 12.4 Turn-Based Game (Request/Response)

The simplest networking model — no prediction, no interpolation, no rollback:

```csharp
public class TurnBasedNetwork
{
    // Client sends action when it's their turn
    public void SendAction(GameClient client, TurnAction action)
    {
        var writer = new NetDataWriter();
        writer.Put((byte)ClientPacketType.PlayerInput);
        writer.Put((byte)action.Type);     // Move, Attack, UseItem, EndTurn
        writer.Put(action.TargetX);
        writer.Put(action.TargetY);
        writer.Put(action.ItemId);
        client.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    // Server validates and broadcasts result to all players
    public void OnActionReceived(int playerId, TurnAction action)
    {
        if (playerId != _currentTurnPlayer)
            return;  // not your turn

        if (!ValidateAction(playerId, action))
            return;  // invalid action

        // Apply action server-side
        var result = ExecuteAction(playerId, action);

        // Broadcast result to all clients
        BroadcastActionResult(playerId, action, result);

        // Advance turn
        _currentTurnPlayer = GetNextPlayer(_currentTurnPlayer);
        BroadcastTurnChange(_currentTurnPlayer);
    }
}
```

---

## 13. Complete Integration Example

Tying it all together — a minimal client-server co-op game loop with Arch ECS:

```csharp
public class NetworkedGame : Game
{
    // Networking
    private GameClient _client;
    private ClientPrediction _prediction;

    // ECS
    private World _world;

    // Systems
    private ClientNetworkReceiveSystem _receiveSystem;
    private InterpolationSystem _interpSystem;

    // Local player entity
    private Entity _localPlayer;

    protected override void Initialize()
    {
        _world = World.Create();
        _client = new GameClient();
        _prediction = new ClientPrediction();
        _interpSystem = new InterpolationSystem();

        _client.OnConnected += OnConnected;
        _client.OnStateReceived += OnServerState;
        _client.Connect("127.0.0.1", 9050);

        base.Initialize();
    }

    private void OnConnected()
    {
        // Create local player entity (predicted, not interpolated)
        _localPlayer = _world.Create(
            new NetworkId(_client.LocalPlayerId),
            new Position(0, 0),
            new PredictedPosition(0, 0),
            new OwnedBy(_client.LocalPlayerId),
            new Sprite(/* player texture */)
        );

        _receiveSystem = new ClientNetworkReceiveSystem(_world, _client);
    }

    private void OnServerState(NetPacketReader reader)
    {
        // Handled by ClientNetworkReceiveSystem + prediction reconciliation
        ushort count = reader.GetUShort();
        for (int i = 0; i < count; i++)
        {
            int netId = reader.GetInt();
            float x = reader.GetFloat();
            float y = reader.GetFloat();
            short health = reader.GetShort();
            byte lastInput = reader.GetByte();

            if (netId == _client.LocalPlayerId)
            {
                _prediction.OnServerState(x, y, lastInput);
                // Update local entity with predicted position
                ref var pos = ref _world.Get<Position>(_localPlayer);
                pos = new Position(_prediction.PredictedX, _prediction.PredictedY);
            }
        }
    }

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _client.Update();

        // Sample input and predict locally
        var flags = SampleInput();
        var inputPacket = _prediction.RecordAndPredict(flags, 0f);
        inputPacket.Tick = (uint)gameTime.TotalGameTime.TotalMilliseconds;

        // Send input to server
        var writer = new NetDataWriter();
        writer.Put((byte)ClientPacketType.PlayerInput);
        inputPacket.Serialize(writer);
        _client.Send(writer, DeliveryMethod.Sequenced);

        // Update predicted local position
        if (_world.IsAlive(_localPlayer))
        {
            ref var pos = ref _world.Get<Position>(_localPlayer);
            pos = new Position(_prediction.PredictedX, _prediction.PredictedY);
        }

        // Interpolate remote entities
        _interpSystem.Update(_world, dt);

        base.Update(gameTime);
    }

    private static InputFlags SampleInput()
    {
        var kb = Keyboard.GetState();
        var flags = InputFlags.None;
        if (kb.IsKeyDown(Keys.W)) flags |= InputFlags.Up;
        if (kb.IsKeyDown(Keys.S)) flags |= InputFlags.Down;
        if (kb.IsKeyDown(Keys.A)) flags |= InputFlags.Left;
        if (kb.IsKeyDown(Keys.D)) flags |= InputFlags.Right;
        return flags;
    }
}
```

---

## Quick Reference Tables

### Delivery Method Decision Matrix

| Data Type | Method | Rate | Why |
|---|---|---|---|
| Player position/input | `Sequenced` | Every tick | Only latest matters, drop stale |
| World state snapshot | `Unreliable` | 20 Hz | Stale snapshots are useless |
| Chat message | `ReliableOrdered` | On event | Must arrive, must be in order |
| Spawn/despawn event | `ReliableOrdered` | On event | Must arrive |
| Hit/damage event | `ReliableOrdered` | On event | Must arrive |
| Ability cooldown state | `ReliableSequenced` | On change | Only latest matters, must arrive |
| Health bar update | `ReliableSequenced` | On change | Only latest matters, must arrive |

### Bandwidth Budget (Per Entity Per Second)

| Technique | Bytes/Entity/Update | At 20 Hz |
|---|---|---|
| Full snapshot (2 floats + health) | 12 | 240 B/s |
| Quantized position (2 × ushort) | 4 | 80 B/s |
| Delta compressed (avg) | ~3 | 60 B/s |
| Quantized + delta (avg) | ~2 | 40 B/s |

For 50 entities at 20 Hz: Full = 12 KB/s, Optimized = 2 KB/s. That's an 83% reduction.

### Genre Networking Summary

| Genre | Model | Transport | Prediction | Determinism | Key Challenge |
|---|---|---|---|---|---|
| Fighting game | Rollback P2P | LiteNetLib UDP | Input prediction | **Required** (fixed-point) | Rollback frame budget |
| Co-op action | Client-server | LiteNetLib UDP | Client-side prediction | Not required | Reconciliation smoothness |
| RTS | Lockstep P2P | LiteNetLib UDP | None (wait for inputs) | **Required** (fixed-point) | Simulation determinism |
| Turn-based | Request/response | TCP or Reliable UDP | None | Not required | Simplest model |
| MMO-style | Client-server | LiteNetLib UDP | Client-side prediction | Not required | Interest management, scale |
| Battle royale | Client-server | LiteNetLib UDP | Client-side prediction | Not required | Entity count, area of interest |

### Performance Checklist

- [ ] Use `Sequenced` / `Unreliable` for high-frequency data (positions, inputs)
- [ ] Use `ReliableOrdered` only for events that must arrive
- [ ] Send snapshots at 20 Hz, not 60 Hz — interpolation covers the gap
- [ ] Quantize positions if your world fits in ushort range
- [ ] Delta compress snapshots — skip unchanged entities
- [ ] Implement interest management for >50 networked entities
- [ ] Buffer 2-3 snapshots for interpolation (100ms behind)
- [ ] Cap input queue depth server-side to prevent memory abuse
- [ ] Validate all client inputs server-side (speed, cooldowns, bounds)
- [ ] Include redundant inputs in rollback packets to survive packet loss
- [ ] Profile bandwidth with `NetManager.Statistics` — target <50 KB/s per player
