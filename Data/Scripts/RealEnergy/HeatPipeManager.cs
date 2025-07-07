using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace TSUT.HeatManagement
{
    public class HeatPipeNode
    {
        public IMyCubeBlock Block;
        public float Heat => HeatSession.Api.Utils.GetHeat(Block);
        public float Capacity => HeatSession.Api.Utils.GetThermalCapacity(Block);
        public List<HeatPipeEdge> Connections = new List<HeatPipeEdge>();
    }

    public class HeatPipeEdge
    {
        public HeatPipeNode A;
        public HeatPipeNode B;
        public float Conductance = 5f; // or resistance
    }

    public class HeatPipeManagerFactory : IHeatBehaviorFactory
    {
        private readonly List<HeatPipeManager> _activeManagers = new List<HeatPipeManager>();

        private static readonly List<string> HeatPipeSubtypeMask = new List<string> {
            "AirDuct",
            "AirDuct2",
            "AirDuctCorner",
            "AirDuctLight",
            "AirDuctRamp",
            "AirDuctT",
            "AirDuctX",
        };

        public static readonly Dictionary<string, VRageMath.Base6Directions.Direction[]> PipeConnectionMap = new Dictionary<string, Base6Directions.Direction[]>()
        {
            { "AirDuct", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
            { "AirDuct2", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Right } },
            { "AirDuctRamp", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Right } },
            { "AirDuctLight", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Right } },
            { "AirDuctCorner", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Left } },
            { "AirDuctT", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward, Base6Directions.Direction.Left } },
            { "AirDuctX", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward, Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
        };

        public void CollectHeatBehaviors(IMyCubeGrid grid, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<IMySlimBlock> slimBlocks = new List<IMySlimBlock>();
            grid.GetBlocks(slimBlocks);

            List<IMyCubeBlock> allBlocks = new List<IMyCubeBlock>();
            foreach (var slim in slimBlocks)
            {
                if (slim.FatBlock != null)
                    allBlocks.Add(slim.FatBlock);
            }

            var pipeBlocks = allBlocks
                .Where(b => HeatPipeSubtypeMask.Contains(b.BlockDefinition.SubtypeName))
                .ToList();

            if (grid.DisplayName == "Base") {
                MyAPIGateway.Utilities.ShowNotification($"Blocks on {grid.DisplayName}: {allBlocks.Count}", 10000, MyFontEnum.White);
                MyAPIGateway.Utilities.ShowNotification($"Pipes on {grid.DisplayName}: {pipeBlocks.Count}", 10000, MyFontEnum.White);
            }

            var pipeSet = new HashSet<IMyCubeBlock>(pipeBlocks);
            var visited = new HashSet<IMyCubeBlock>();

            // Mapping from block to graph node
            var blockToNode = new Dictionary<IMyCubeBlock, HeatPipeNode>();

            foreach (var block in pipeBlocks)
            {
                if (visited.Contains(block) || behaviorMap.ContainsKey(block))
                    continue;

                // Per-cluster data
                var clusterNodes = new List<HeatPipeNode>();
                var queue = new List<IMyCubeBlock>();
                int qIndex = 0;

                queue.Add(block);
                visited.Add(block);

                // Build graph nodes and edges
                while (qIndex < queue.Count)
                {
                    var current = queue[qIndex++];
                    HeatPipeNode currentNode;
                    if (!blockToNode.TryGetValue(current, out currentNode))
                    {
                        currentNode = new HeatPipeNode { Block = current };
                        blockToNode[current] = currentNode;
                        clusterNodes.Add(currentNode);
                    }

                    var neighbors = new List<IMySlimBlock>();
                    current.SlimBlock.GetNeighbours(neighbors);

                    foreach (var neighbor in neighbors)
                    {
                        var fat = neighbor.FatBlock;
                        if (fat == null || !pipeSet.Contains(fat))
                            continue;
                        HeatPipeNode neighborNode;
                        if (!blockToNode.TryGetValue(fat, out neighborNode))
                        {
                            neighborNode = new HeatPipeNode { Block = fat };
                            blockToNode[fat] = neighborNode;
                            clusterNodes.Add(neighborNode);
                        }

                        // Link nodes if not already connected
                        bool alreadyConnected = currentNode.Connections.Exists(e => (e.A == neighborNode || e.B == neighborNode));
                        if (!alreadyConnected && ArePipesConnectedByGeometry(current, fat))
                        {
                            var edge = new HeatPipeEdge
                            {
                                A = currentNode,
                                B = neighborNode,
                                Conductance = 1.0f // you can customize this
                            };
                            currentNode.Connections.Add(edge);
                            neighborNode.Connections.Add(edge);
                        }

                        if (!visited.Contains(fat))
                        {
                            queue.Add(fat);
                            visited.Add(fat);
                        }
                    }
                }

                // Create manager using graph cluster
                var manager = new HeatPipeManager(clusterNodes);

                // Register the manager for all its blocks
                foreach (var node in clusterNodes)
                {
                    behaviorMap[node.Block] = manager;
                }

                _activeManagers.Add(manager);
            }
        }

        public static bool ArePipesConnectedByGeometry(IMyCubeBlock a, IMyCubeBlock b)
        {
            Base6Directions.Direction[] dirsA;
            Base6Directions.Direction[] dirsB;
            if (!PipeConnectionMap.TryGetValue(a.BlockDefinition.SubtypeName, out dirsA) ||
                !PipeConnectionMap.TryGetValue(b.BlockDefinition.SubtypeName, out dirsB))
                return false;

            var orientationA = a.Orientation;
            var orientationB = b.Orientation;

            var posA = a.Position;
            var posB = b.Position;

            foreach (var dirA in dirsA)
            {
                // Convert A's local port to world direction
                var worldDirA = orientationA.TransformDirection(dirA);
                var offset = Base6Directions.GetIntVector(worldDirA);
                var expectedNeighborPos = posA + offset;

                if (posB != expectedNeighborPos)
                    continue;

                // Calculate direction from B to A
                var dirToA = Base6Directions.GetDirection(posA - posB);

                // Check if B has a port facing back at A
                foreach (var dirB in dirsB)
                {
                    var worldDirB = orientationB.TransformDirection(dirB);
                    if (worldDirB == dirToA)
                        return true;
                }
            }

            return false;
        }


        public IHeatBehavior OnBlockAdded(IMyCubeBlock block)
        {
            MyAPIGateway.Utilities.ShowNotification($"Pipes on grid: {_activeManagers.Count}", 10000, MyFontEnum.White);
            // For dynamic addition, just create a single-block manager (chain will be rebuilt on reload)
            if (HeatPipeSubtypeMask.Contains(block.BlockDefinition.SubtypeName))
            {
                foreach (var manager in _activeManagers)
                {
                    if (manager.TryAddBlock(block))
                    {
                        return manager; // Block added to existing graph
                    }
                }

                // No match → create new manager
                var newManager = HeatPipeManager.CreateFromSingleBlock(block);
                _activeManagers.Add(newManager);
                return newManager;
            }
            return null;
        }

        public int Priority => 15; // Between batteries and vents
    }

    public class HeatPipeManager : IHeatBehavior
    {
        public static HeatPipeManager CreateFromSingleBlock(IMyCubeBlock block)
        {
            var node = new HeatPipeNode { Block = block };
            return new HeatPipeManager(new List<HeatPipeNode> { node });
        }

        private List<HeatPipeNode> _nodes;

        public HeatPipeManager(List<HeatPipeNode> nodes)
        {
            _nodes = nodes;
        }

        public float GetHeatChange(float deltaTime) => 0f;

        public bool TryAddBlock(IMyCubeBlock block)
        {
            // Must be pipe type
            if (!block.BlockDefinition.SubtypeName.Contains("AirDuct")) // or match your mask
                return false;

            // Is it connected to any of our known nodes?
            var neighbors = new List<IMySlimBlock>();
            block.SlimBlock.GetNeighbours(neighbors);

            foreach (var neighbor in neighbors)
            {
                var fat = neighbor.FatBlock;
                if (fat == null)
                    continue;

                if (_nodes.Any(n => n.Block == fat))
                {
                    AddNodeAndEdges(block);
                    return true;
                }
            }

            return false; // Not connected
        }

        private void AddNodeAndEdges(IMyCubeBlock block)
        {
            if (_nodes.Any(n => n.Block == block))
                return; // already added

            var newNode = new HeatPipeNode { Block = block };
            _nodes.Add(newNode);

            var neighbors = new List<IMySlimBlock>();
            block.SlimBlock.GetNeighbours(neighbors);

            foreach (var neighbor in neighbors)
            {
                var fat = neighbor.FatBlock;
                if (fat == null)
                    continue;

                var existingNode = _nodes.FirstOrDefault(n => n.Block == fat);
                if (existingNode != null && HeatPipeManagerFactory.ArePipesConnectedByGeometry(block, fat))
                {
                    var edge = new HeatPipeEdge
                    {
                        A = newNode,
                        B = existingNode,
                        Conductance = 1.0f
                    };
                    newNode.Connections.Add(edge);
                    existingNode.Connections.Add(edge);
                }
            }
        }

        public void SpreadHeat(float deltaTime)
        {
            MyAPIGateway.Utilities.ShowNotification($"Nodes foind: {_nodes.Count}", 10000, MyFontEnum.White);
            foreach (var node in _nodes)
            {
                foreach (var edge in node.Connections)
                {
                    // Only process each edge once
                    if (edge.A != node)
                        continue;

                    float tempA = HeatSession.Api.Utils.GetHeat(edge.A.Block);
                    float tempB = HeatSession.Api.Utils.GetHeat(edge.B.Block);

                    float capA = HeatSession.Api.Utils.GetThermalCapacity(edge.A.Block);
                    float capB = HeatSession.Api.Utils.GetThermalCapacity(edge.B.Block);

                    float tempDiff = tempB - tempA;
                    float energyDelta = tempDiff * edge.Conductance * deltaTime;

                    HeatSession.Api.Utils.ApplyHeatChange(edge.A.Block, energyDelta / capA);
                    HeatSession.Api.Utils.ApplyHeatChange(edge.B.Block, -energyDelta / capB);

                    var orange = Color.Orange.ToVector4();
                    MySimpleObjectDraw.DrawLine(
                        edge.A.Block.WorldMatrix.Translation,
                        edge.B.Block.WorldMatrix.Translation,
                        MyStringId.GetOrCompute("GizmoDrawLine"),
                        ref orange,
                        1f
                    );
                    MyAPIGateway.Utilities.ShowNotification("Debug drawing running", 10000, MyFontEnum.White);
                }
            }
        }

        public void Cleanup() => _nodes = null;

        public void ReactOnNewHeat(float heat) { }
    }
}