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
        public float Conductance = Config.Instance.HEATPIPE_CONDUCTIVITY; // or resistance
    }

    public class HeatPipeManagerFactory : IHeatBehaviorFactory
    {
        public static readonly List<string> HeatPipeSubtypeMask = new List<string> {
            "AirDuct",
            "AirDuct1",
            "AirDuct2",
            "AirDuctCorner",
            "AirDuctLight",
            "AirDuctRamp",
            "AirDuctT",
            "AirDuctX",

            "LargeBlockPipesStraight1",
            "LargeBlockPipesStraight2",
            "LargeBlockPipesEnd",
            "LargeBlockPipesJunction",
            "LargeBlockPipesCornerOuter",
            "LargeBlockPipesCorner",
            "LargeBlockPipesCornerInner"
        };

        public static readonly Dictionary<string, VRageMath.Base6Directions.Direction[]> PipeConnectionMap = new Dictionary<string, Base6Directions.Direction[]>()
        {
            { "AirDuct", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
            { "AirDuct1", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
            { "AirDuct2", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
            { "AirDuctRamp", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
            { "AirDuctLight", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward } },
            { "AirDuctCorner", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Left } },
            { "AirDuctT", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward, Base6Directions.Direction.Left } },
            { "AirDuctX", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward, Base6Directions.Direction.Left, Base6Directions.Direction.Right } },

            { "LargeBlockPipesStraight1", new[] { Base6Directions.Direction.Right, Base6Directions.Direction.Left } },
            { "LargeBlockPipesStraight2", new[] { Base6Directions.Direction.Right, Base6Directions.Direction.Left } },
            { "LargeBlockPipesEnd", new[] { Base6Directions.Direction.Left } },
            { "LargeBlockPipesJunction", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Backward, Base6Directions.Direction.Left, Base6Directions.Direction.Right } },
            { "LargeBlockPipesCornerOuter", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Down } },
            { "LargeBlockPipesCornerInner", new[] { Base6Directions.Direction.Up, Base6Directions.Direction.Right } },
            { "LargeBlockPipesCorner", new[] { Base6Directions.Direction.Forward, Base6Directions.Direction.Right } },
        };

        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager gridManager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            List<HeatPipeManager> gridPipeManagers = new List<HeatPipeManager>();
            MyLog.Default.WriteLine($"[HeatManagement] Start collecting on {grid.DisplayName}...");
            // 1. Get all FatBlocks
            List<IMySlimBlock> slimBlocks = new List<IMySlimBlock>();
            grid.GetBlocks(slimBlocks);

            // 2. Filter to pipe blocks
            var pipeBlocks = slimBlocks
                .Where(s => s.FatBlock != null && HeatPipeSubtypeMask.Contains(s.FatBlock.BlockDefinition.SubtypeName))
                .Select(s => s.FatBlock)
                .ToList();

            // 3. Create HeatPipeNode for each pipe
            var blockToNode = new Dictionary<IMyCubeBlock, HeatPipeNode>();
            foreach (var block in pipeBlocks)
            {
                blockToNode[block] = new HeatPipeNode { Block = block };
            }

            // 4. Attempt to connect each node to its direct neighbors
            foreach (var node in blockToNode.Values)
            {
                // MyLog.Default.WriteLine($"[HeatManagement] Processing {node.Block.DisplayNameText}...");
                var pos = node.Block.Position;

                foreach (var offset in Base6Directions.IntDirections)
                {
                    var neighborPos = pos + offset;
                    var neighborBlock = pipeBlocks.FirstOrDefault(b => b.Position == neighborPos);
                    if (neighborBlock == null)
                        continue;

                    var neighborNode = blockToNode[neighborBlock];

                    // MyLog.Default.WriteLine($"[HeatManagement] Found neighbor: {neighborNode.Block.DisplayNameText}...");

                    if (!ArePipesConnectedByGeometry(node.Block, neighborBlock))
                        continue;

                    // MyLog.Default.WriteLine($"[HeatManagement] Geometry check: pass...");

                    // 5. Try adding this connection to an existing manager
                    bool connected = false;
                    foreach (var manager in gridPipeManagers)
                    {
                        if (manager.TryConnectNodes(node, neighborNode))
                        {
                            connected = true;
                            break;
                        }
                    }

                    // MyLog.Default.WriteLine($"[HeatManagement] Is connected? {connected}...");


                    // 6. If no one accepted, make new manager
                    if (!connected)
                    {
                        var manager = HeatPipeManager.CreateFromSingleNode(node, gridManager);
                        manager.TryConnectNodes(node, neighborNode);
                        gridPipeManagers.Add(manager);
                    }
                }
            }

            // 7. Ensure all nodes are part of a manager (even orphans)
            foreach (var node in blockToNode.Values)
            {
                bool assigned = gridPipeManagers.Any(m => m.ContainsNode(node));
                if (!assigned)
                {
                    var soloManager = HeatPipeManager.CreateFromSingleNode(node, gridManager);
                    soloManager.TryAddNode(node);
                    gridPipeManagers.Add(soloManager);
                }
            }

            // Register behaviors
            foreach (var manager in gridPipeManagers)
            {
                foreach (var node in manager.Nodes)
                {
                    behaviorMap[node.Block] = manager;
                }
            }

            MyLog.Default.WriteLine($"[HeatManagement] Grid {grid.DisplayName} processed. Active managers: {gridPipeManagers.Count}");
        }

        public static bool IsPipeConnectedToBlock(IMyCubeBlock pipe, IMyCubeBlock targetBlock)
        {
            Base6Directions.Direction[] pipeDirs;
            // Only pipe needs to be in the connection map
            if (!PipeConnectionMap.TryGetValue(pipe.BlockDefinition.SubtypeName, out pipeDirs))
                return false;

            var orientationPipe = pipe.Orientation;
            var posPipe = pipe.Position;
            var posTarget = targetBlock.Position;

            foreach (var dir in pipeDirs)
            {
                // Convert pipe's local port to world direction
                var worldDir = orientationPipe.TransformDirection(dir);
                var offset = Base6Directions.GetIntVector(worldDir);
                var expectedNeighborPos = posPipe + offset;

                // Check if target block is in that direction
                if (posTarget == expectedNeighborPos)
                    return true;
            }

            return false;
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


        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager gridManager)
        {
            MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] !!! New block on a grid: {block.BlockDefinition.SubtypeName}");
            var result = new HeatBehaviorAttachResult();
            var gridPipeManagers = gridManager.GetHeatPipeManagers();

            MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Managers on the grid: {gridPipeManagers.Count}");

            if (!HeatPipeSubtypeMask.Contains(block.BlockDefinition.SubtypeName))
                return result;

            MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Type check: passed");

            var grid = block.CubeGrid;
            var pos = block.Position;

            var neighborOffsets = new[] { Vector3I.Forward, Vector3I.Backward, Vector3I.Left, Vector3I.Right, Vector3I.Up, Vector3I.Down };

            var connectedManagers = new List<HeatPipeManager>();
            var neighborNodes = new Dictionary<HeatPipeNode, HeatPipeManager>();

            MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Looking for neighbors");

            // 1. Look for existing pipe connections
            foreach (var offset in neighborOffsets)
            {
                var neighborPos = pos + offset;
                var slim = grid.GetCubeBlock(neighborPos);
                if (slim?.FatBlock == null)
                    continue;

                var neighbor = slim.FatBlock;

                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] >> Found neighbor: {neighbor.DisplayNameText}");

                foreach (var manager in gridPipeManagers)
                {
                    MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] >> Nodes in manager: {manager.Nodes.Count}");
                    HeatPipeNode node = manager.TryGetNode(neighbor);
                    if (node == null)
                        continue;

                    MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] >> Found node");

                    if (HeatPipeManagerFactory.ArePipesConnectedByGeometry(block, neighbor))
                    {
                        MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] >> Connectable, add node");
                        connectedManagers.Add(manager);
                        neighborNodes[node] = manager;
                    }
                }
            }

            MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Found managers: {connectedManagers.Count}, nodes: {neighborNodes.Count}");

            // 2. Create new node
            var newNode = new HeatPipeNode { Block = block };

            // 3. If no connections — create a new manager
            if (connectedManagers.Count == 0)
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] New network created");
                var newManager = new HeatPipeManager(gridManager);
                newManager.TryAddNode(newNode);
                gridPipeManagers.Add(newManager);
                result.Behavior = newManager;
                result.AffectedBlocks = new List<IMyCubeBlock> { block };
                return result;
            }

            // 4. If one manager — just add node to it
            if (connectedManagers.Count == 1)
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Existing network extended");
                var manager = connectedManagers.First();
                foreach (var kvp in neighborNodes)
                {
                    manager.TryConnectNodes(newNode, kvp.Key);
                }
                return result;
            }

            // 5. Multiple managers: merge them
            var mergedManager = new HeatPipeManager(gridManager);
            // Migrate from all old managers
            foreach (var mgr in connectedManagers)
            {
                foreach (var node in mgr.Nodes)
                {
                    mergedManager.TryAddNode(node);

                    foreach (var edge in node.Connections)
                    {
                        // Add each connection only once (A < B)
                        if (edge.A == node && mergedManager.ContainsNode(edge.B))
                            mergedManager.TryConnectNodes(edge.A, edge.B);
                    }
                }
            }

            // Link new node to neighbor nodes
            foreach (var kvp in neighborNodes)
            {
                mergedManager.TryConnectNodes(newNode, kvp.Key);
            }

            result.Behavior = mergedManager;
            result.AffectedBlocks = mergedManager.Nodes.Select(n => n.Block).ToList();
            MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Several managers merged, new manager with {mergedManager.Nodes.Count} created");
            return result;
        }

        public int Priority => 15; // Between batteries and vents
    }

    public class HeatPipeManager : IHeatBehavior, IMultiBlockHeatBehavior
    {

        public static HeatPipeManager CreateFromSingleBlock(IMyCubeBlock block, IGridHeatManager manager)
        {
            return CreateFromSingleNode(new HeatPipeNode { Block = block }, manager);
        }

        public static HeatPipeManager CreateFromSingleNode(HeatPipeNode node, IGridHeatManager manager)
        {
            var m = new HeatPipeManager(manager);
            if (m.TryAddNode(node))
            {
                return m;
            }

            return null;
        }

        private IGridHeatManager _gridManager;
        private List<HeatPipeNode> _nodes = new List<HeatPipeNode>();
        public List<HeatPipeNode> Nodes => _nodes;


        public HeatPipeManager(IGridHeatManager manager)
        {
            _gridManager = manager;
        }

        public float GetHeatChange(float deltaTime) => 0f;

        public HeatPipeNode TryGetNode(IMyCubeBlock block) => _nodes.FirstOrDefault(n => n.Block == block);

        public bool TryAddBlock(IMyCubeBlock block)
        {
            // Must be pipe type
            if (!HeatPipeManagerFactory.HeatPipeSubtypeMask.Contains(block.BlockDefinition.SubtypeName)) // or match your mask
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
                        B = existingNode
                    };
                    newNode.Connections.Add(edge);
                    existingNode.Connections.Add(edge);
                }
            }
        }

        public bool ContainsNode(HeatPipeNode node) => _nodes.Contains(node);

        public bool TryAddNode(HeatPipeNode node)
        {
            if (_nodes.Contains(node))
                return false;

            _nodes.Add(node);
            return true;
        }

        public bool TryConnectNodes(HeatPipeNode a, HeatPipeNode b)
        {
            if (_nodes.Contains(a) && _nodes.Contains(b))
            {
                if (!a.Connections.Any(e => e.B == b || e.A == b))
                {
                    var edge = new HeatPipeEdge { A = a, B = b };
                    a.Connections.Add(edge);
                    b.Connections.Add(edge);
                }
                MyLog.Default.WriteLine($"[HeatManagement] Both nodes here: connected");
                return true;
            }

            if (_nodes.Contains(a))
            {
                MyLog.Default.WriteLine($"[HeatManagement] First node here");
                if (!TryAddNode(b)) return false;
                var edge = new HeatPipeEdge { A = a, B = b };
                a.Connections.Add(edge);
                b.Connections.Add(edge);
                MyLog.Default.WriteLine($"[HeatManagement] First node here: connected");
                return true;
            }

            if (_nodes.Contains(b))
            {
                MyLog.Default.WriteLine($"[HeatManagement] Second node here");
                if (!TryAddNode(a)) return false;
                var edge = new HeatPipeEdge { A = a, B = b };
                a.Connections.Add(edge);
                b.Connections.Add(edge);
                MyLog.Default.WriteLine($"[HeatManagement] Second node here: connected");
                return true;
            }

            MyLog.Default.WriteLine($"[HeatManagement] No node found");

            return false;
        }

        public float GetHeatExchange(IMyCubeBlock network, IMyCubeBlock neighbor, float deltaTime)
        {
            if (_nodes == null || network == null || neighbor == null)
                return 0f;

            if (network.CubeGrid != neighbor.CubeGrid)
                return 0f;

            // Find the node in the network
            var node = _nodes.FirstOrDefault(n => n.Block == network);
            if (node == null)
                return 0f;

            // Ensure the neighbor is physically adjacent (e.g., direct contact)
            var offset = neighbor.Position - network.Position;
            if (offset.RectangularLength() != 1)
                return 0f;

            if (!HeatPipeManagerFactory.IsPipeConnectedToBlock(node.Block, neighbor)) {
                return 0f;
            }

            // Heat transfer from pipe node to neighbor (battery, sink, etc.)
            float tempPipe = HeatSession.Api.Utils.GetHeat(network);
            float tempOther = HeatSession.Api.Utils.GetHeat(neighbor);

            float tempDiff = tempOther - tempPipe;

            float contactArea = HeatSession.Api.Utils.GetLargestFaceArea(network.SlimBlock);

            // Use global pipe-to-block conductance config
            float conductance = Config.Instance.HEATPIPE_CONDUCTIVITY;
            float energyTransferred = tempDiff * contactArea * conductance * deltaTime;

            // Return raw joules of heat
            return -energyTransferred;
        }

        public void SpreadHeat(float deltaTime)
        {
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
                }
            }
        }

        public void Cleanup() => _nodes = null;

        public void ReactOnNewHeat(float heat) { 
            foreach (var node in _nodes) {
                HeatSession.Api.Effects.UpdateBlockHeatLight(node.Block, HeatSession.Api.Utils.GetHeat(node.Block));
            }
        }

        public void AppendNetworkInfo(StringBuilder info)
        {
            if (_nodes == null || _nodes.Count == 0)
            {
                info.AppendLine("  - [Invalid Pipe Network]");
                return;
            }

            int nodeCount = _nodes.Count;
            float totalHeat = 0f;

            foreach (var node in _nodes)
            {
                float heat = HeatSession.Api.Utils.GetHeat(node.Block);

                totalHeat += heat;
            }

            float avgTemp = totalHeat / nodeCount;

            info.AppendLine($"- #{GetHashCode()} Length: {nodeCount}, Avg: {avgTemp:F1} °C");
        }

        public void RemoveBlock(IMyCubeBlock block, IGridHeatManager gridManager, Dictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] !! Block removed {block.DisplayNameText}");
            var node = _nodes.FirstOrDefault(n => n.Block == block);
            if (node == null)
                return;

            MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Connections to remove {node.Connections.Count}");
            // 1. Remove connected edges
            foreach (var edge in node.Connections)
            {
                var other = edge.A == node ? edge.B : edge.A;
                other.Connections.Remove(edge);
            }

            _nodes.Remove(node);
            behaviorMap.Remove(node.Block);

            MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Nodes left {_nodes.Count}");

            // 2. If only one node remains → just keep it
            if (_nodes.Count <= 1)
                return;

            // 3. Recompute sub-networks (connected graphs)
            var subgraphs = DiscoverSubgraphsWithEdges();

            MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Subgraphs discovered {subgraphs.Count}");

            if (subgraphs.Count <= 1)
                return;

            int created = 0;

            foreach (var sub in subgraphs)
            {
                created++;
                var newManager = new HeatPipeManager(gridManager);
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Creating new network with {sub.Nodes.Count} nodes and {sub.Edges.Count} edges");
                foreach (var n in sub.Nodes)
                    newManager.TryAddNode(n);

                foreach (var edge in sub.Edges)
                    newManager.TryConnectNodes(edge.A, edge.B);

                foreach (var n in sub.Nodes)
                    behaviorMap[n.Block] = newManager;
            }

            MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Networks created {created}");

            return;
        }

        private List<HeatPipeSubgraph> DiscoverSubgraphsWithEdges()
        {
            var result = new List<HeatPipeSubgraph>();
            var visited = new HashSet<HeatPipeNode>();

            foreach (var node in _nodes)
            {
                if (visited.Contains(node))
                    continue;

                var subgraph = new HeatPipeSubgraph();
                var stack = new List<HeatPipeNode>();
                stack.Add(node);

                while (stack.Count > 0)
                {
                    var current = stack.Pop();
                    if (!visited.Add(current))
                        continue;

                    subgraph.Nodes.Add(current);

                    foreach (var edge in current.Connections)
                    {
                        if (!subgraph.Edges.Contains(edge))
                            subgraph.Edges.Add(edge);

                        var other = edge.A == current ? edge.B : edge.A;
                        if (!visited.Contains(other))
                            stack.Add(other);
                    }
                }

                result.Add(subgraph);
            }

            return result;
        }

        public void ShowDebugGraph(float deltaTime)
        {
            foreach (var node in _nodes)
            {
                foreach (var edge in node.Connections)
                {
                    if (edge.A != node) continue; // draw once
                    var start = edge.A.Block.GetPosition();
                    var end = edge.B.Block.GetPosition();
                    var color = Color.Lime.ToVector4();
                    MySimpleObjectDraw.DrawLine(
                        start,
                        end,
                        MyStringId.GetOrCompute("GizmoDrawLine"),
                        ref color,
                        .2f,
                        VRageRender.MyBillboard.BlendTypeEnum.AdditiveTop
                    );
                }
            }
        }
    }

    public class HeatPipeSubgraph
    {
        public List<HeatPipeNode> Nodes = new List<HeatPipeNode>();
        public List<HeatPipeEdge> Edges = new List<HeatPipeEdge>();
    }
}