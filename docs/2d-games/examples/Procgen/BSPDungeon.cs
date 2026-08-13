// =============================================================================
// BSPDungeon.cs — Binary Space Partitioning dungeon generator
// Extracted from: G53 — Procedural Generation (Section 3)
// Guide: /G/G53_procedural_generation.md
// =============================================================================

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace U2DToolkit.Examples.Procgen
{
    /// <summary>
    /// A node in the BSP tree. Leaf nodes contain rooms;
    /// internal nodes represent spatial splits.
    /// </summary>
    public sealed class BspNode
    {
        public Rectangle Bounds;
        public BspNode? Left, Right;
        public Rectangle? Room;

        public bool IsLeaf => Left == null && Right == null;
    }

    /// <summary>
    /// Generates dungeon layouts by recursively splitting space via Binary Space
    /// Partitioning. Produces well-distributed rooms connected by L-shaped corridors.
    /// <para>
    /// The algorithm:
    /// 1. Start with the full map as one BSP node.
    /// 2. Recursively split nodes horizontally or vertically until minimum size is reached.
    /// 3. Place a random-sized room inside each leaf node.
    /// 4. Connect sibling rooms with corridors by walking the BSP tree bottom-up.
    /// </para>
    /// </summary>
    public sealed class BspDungeonGenerator
    {
        private readonly SeededRandom _rng;
        private const int MinNodeSize = 8;
        private const int RoomPadding = 2;

        public BspDungeonGenerator(SeededRandom rng) => _rng = rng;

        /// <summary>
        /// Generate a dungeon of the given dimensions.
        /// Returns the tile grid (0 = wall, 1 = floor) and the list of room rectangles.
        /// </summary>
        public (int[,] tiles, List<Rectangle> rooms) Generate(int width, int height)
        {
            var tiles = new int[width, height]; // 0 = wall, 1 = floor
            var root = new BspNode { Bounds = new Rectangle(0, 0, width, height) };
            Split(root);

            var rooms = new List<Rectangle>();
            CreateRooms(root, rooms);
            foreach (var room in rooms)
                CarveRoom(tiles, room);

            ConnectRooms(tiles, root);
            return (tiles, rooms);
        }

        /// <summary>Recursively split a BSP node horizontally or vertically.</summary>
        private void Split(BspNode node)
        {
            if (node.Bounds.Width < MinNodeSize * 2 && node.Bounds.Height < MinNodeSize * 2)
                return;

            // Choose split direction — prefer splitting the longer axis
            bool splitH = _rng.NextBool();
            if (node.Bounds.Width > node.Bounds.Height * 1.25f) splitH = false;
            if (node.Bounds.Height > node.Bounds.Width * 1.25f) splitH = true;

            int max = (splitH ? node.Bounds.Height : node.Bounds.Width) - MinNodeSize;
            if (max < MinNodeSize) return;

            int split = _rng.Next(MinNodeSize, max);

            if (splitH)
            {
                node.Left = new BspNode
                {
                    Bounds = new Rectangle(node.Bounds.X, node.Bounds.Y,
                        node.Bounds.Width, split)
                };
                node.Right = new BspNode
                {
                    Bounds = new Rectangle(node.Bounds.X, node.Bounds.Y + split,
                        node.Bounds.Width, node.Bounds.Height - split)
                };
            }
            else
            {
                node.Left = new BspNode
                {
                    Bounds = new Rectangle(node.Bounds.X, node.Bounds.Y,
                        split, node.Bounds.Height)
                };
                node.Right = new BspNode
                {
                    Bounds = new Rectangle(node.Bounds.X + split, node.Bounds.Y,
                        node.Bounds.Width - split, node.Bounds.Height)
                };
            }

            Split(node.Left);
            Split(node.Right);
        }

        /// <summary>Create a randomly-sized room inside each leaf node.</summary>
        private void CreateRooms(BspNode node, List<Rectangle> rooms)
        {
            if (!node.IsLeaf)
            {
                if (node.Left != null) CreateRooms(node.Left, rooms);
                if (node.Right != null) CreateRooms(node.Right, rooms);
                return;
            }

            int w = _rng.Next(node.Bounds.Width / 2, node.Bounds.Width - RoomPadding);
            int h = _rng.Next(node.Bounds.Height / 2, node.Bounds.Height - RoomPadding);
            int x = node.Bounds.X + _rng.Next(1, node.Bounds.Width - w - 1);
            int y = node.Bounds.Y + _rng.Next(1, node.Bounds.Height - h - 1);

            var room = new Rectangle(x, y, w, h);
            node.Room = room;
            rooms.Add(room);
        }

        /// <summary>Find the center of the nearest room in a BSP subtree.</summary>
        private Point GetRoomCenter(BspNode node)
        {
            if (node.Room.HasValue)
                return node.Room.Value.Center;
            if (node.Left != null) return GetRoomCenter(node.Left);
            if (node.Right != null) return GetRoomCenter(node.Right);
            return node.Bounds.Center;
        }

        /// <summary>Connect sibling rooms by walking the BSP tree bottom-up.</summary>
        private void ConnectRooms(int[,] tiles, BspNode node)
        {
            if (node.IsLeaf) return;
            if (node.Left != null && node.Right != null)
            {
                ConnectRooms(tiles, node.Left);
                ConnectRooms(tiles, node.Right);

                var a = GetRoomCenter(node.Left);
                var b = GetRoomCenter(node.Right);
                CarveCorridor(tiles, a, b);
            }
        }

        /// <summary>Carve an L-shaped corridor between two points.</summary>
        private void CarveCorridor(int[,] tiles, Point a, Point b)
        {
            int x = a.X, y = a.Y;
            while (x != b.X)
            {
                tiles[x, y] = 1;
                x += x < b.X ? 1 : -1;
            }
            while (y != b.Y)
            {
                tiles[x, y] = 1;
                y += y < b.Y ? 1 : -1;
            }
        }

        /// <summary>Carve floor tiles for a room, leaving a 1-cell border around the map edge.</summary>
        private void CarveRoom(int[,] tiles, Rectangle room)
        {
            for (int x = room.X; x < room.X + room.Width; x++)
                for (int y = room.Y; y < room.Y + room.Height; y++)
                    if (x > 0 && x < tiles.GetLength(0) - 1 &&
                        y > 0 && y < tiles.GetLength(1) - 1)
                        tiles[x, y] = 1;
        }
    }
}
