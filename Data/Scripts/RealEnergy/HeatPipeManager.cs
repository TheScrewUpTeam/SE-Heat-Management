using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        public float Conductance = Config.Instance.HEATPIPE_CONDUCTIVITY * 100; // or resistance mult by 30 for faster heat expansion
    }

    public class HeatPipeManagerFactory : IHeatBehaviorFactory
    {
        public static readonly Dictionary<string, Base6Directions.Direction[]> PipeConnectionMap = new Dictionary<string, Base6Directions.Direction[]>()
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

            // Small grid blocks
            { "SmallNeonTubesStraight1", new[] { Base6Directions.Direction.Right, Base6Directions.Direction.Left } },
            { "SmallNeonTubesStraight2", new[] { Base6Directions.Direction.Right, Base6Directions.Direction.Left } },
            { "SmallNeonTubesCorner", new[] { Base6Directions.Direction.Right, Base6Directions.Direction.Forward } },
            { "SmallNeonTubesBendUp", new[] { Base6Directions.Direction.Right, Base6Directions.Direction.Up } },
            { "SmallNeonTubesBendDown", new[] { Base6Directions.Direction.Left, Base6Directions.Direction.Down } },
            { "SmallNeonTubesStraightDown", new[] { Base6Directions.Direction.Right, Base6Directions.Direction.Down } },
            { "SmallNeonTubesStraightEnd1", new[] { Base6Directions.Direction.Right, Base6Directions.Direction.Left } },
            { "SmallNeonTubesU", new[] { Base6Directions.Direction.Right } },
            { "SmallNeonTubesT", new[] { Base6Directions.Direction.Right, Base6Directions.Direction.Left, Base6Directions.Direction.Backward } },
            { "SmallNeonTubesCircle", new[] { Base6Directions.Direction.Right, Base6Directions.Direction.Left, Base6Directions.Direction.Backward, Base6Directions.Direction.Forward } },
        };

        private static readonly Dictionary<MyDefinitionId, bool> _pipeCandidateCache = new Dictionary<MyDefinitionId, bool>();

        public void CollectHeatBehaviors(IMyCubeGrid grid, IGridHeatManager gridManager, IDictionary<IMyCubeBlock, IHeatBehavior> behaviorMap)
        {
            if (grid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement] Start collecting on {grid.DisplayName}...");
            }

            List<IMySlimBlock> slimBlocks = new List<IMySlimBlock>();
            grid.GetBlocks(slimBlocks);

            var pipeBlocks = slimBlocks
                .Where(s => s.FatBlock != null && IsPipeCandidate(s.FatBlock))
                .Select(s => s.FatBlock)
                .ToList();

            foreach (var pipe in pipeBlocks)
            {
                if (behaviorMap.ContainsKey(pipe))
                    continue;

                var result = OnBlockAdded(pipe, gridManager);

                if (result.Behavior != null)
                {
                    foreach (var block in result.AffectedBlocks)
                    {
                        behaviorMap[block] = result.Behavior;
                    }
                }
            }

            if (grid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement] Grid {grid.DisplayName} processed. Networks created: {behaviorMap.Values.OfType<HeatPipeManager>().Distinct().Count()}");
            }
        }


        public static Base6Directions.Direction TransformDirection(MatrixI orientation, Base6Directions.Direction localDirection)
        {
            switch (localDirection)
            {
                case Base6Directions.Direction.Forward: return orientation.Forward;
                case Base6Directions.Direction.Backward: return Base6Directions.GetOppositeDirection(orientation.Forward);
                case Base6Directions.Direction.Up: return orientation.Up;
                case Base6Directions.Direction.Down: return Base6Directions.GetOppositeDirection(orientation.Up);
                case Base6Directions.Direction.Left: return Base6Directions.GetOppositeDirection(orientation.Right);
                case Base6Directions.Direction.Right: return orientation.Right;
                default: return Base6Directions.Direction.Forward;
            }
        }

        public static bool IsPipeConnectedToBlock(IMyCubeBlock pipe, IMyCubeBlock targetBlock)
        {
            var pipeDirs = GetPipeDirections(pipe);
            // Only pipe needs to be in the connection map
            if (pipeDirs.Length < 1)
                return false;

            var pipePos = pipe.Position;
            var pipeMatrix = new MatrixI(pipe.Orientation);

            // Collect all positions occupied by the target block
            var occupiedPositions = new HashSet<Vector3I>();
            var min = targetBlock.SlimBlock.Min;
            var max = targetBlock.SlimBlock.Max;

            for (int x = min.X; x <= max.X; x++)
            {
                for (int y = min.Y; y <= max.Y; y++)
                {
                    for (int z = min.Z; z <= max.Z; z++)
                    {
                        occupiedPositions.Add(new Vector3I(x, y, z));
                    }
                }
            }

            foreach (var dir in pipeDirs)
            {
                // Convert pipe local port to world direction
                var worldDir = TransformDirection(pipeMatrix, dir);
                var offset = Base6Directions.GetIntVector(worldDir);
                var neighborPos = pipePos + offset;

                if (occupiedPositions.Contains(neighborPos))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool ArePipesConnectedByGeometry(IMyCubeBlock a, IMyCubeBlock b)
        {
            var dirsA = GetPipeDirections(a);
            var dirsB = GetPipeDirections(b);

            if (dirsA == null || dirsA.Length == 0 || dirsB == null || dirsB.Length == 0)
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

        private static Base6Directions.Direction FindClosestDirection(Vector3 localPos)
        {
            // Normalize by largest axis
            Vector3 abs = Vector3.Abs(localPos);

            if (abs.X > abs.Y && abs.X > abs.Z)
                return localPos.X > 0 ? Base6Directions.Direction.Right : Base6Directions.Direction.Left;

            if (abs.Y > abs.X && abs.Y > abs.Z)
                return localPos.Y > 0 ? Base6Directions.Direction.Up : Base6Directions.Direction.Down;

            return localPos.Z < 0 ? Base6Directions.Direction.Forward : Base6Directions.Direction.Backward;
        }

        private static Base6Directions.Direction[] GetPipeDirections(IMyCubeBlock block)
        {

            Base6Directions.Direction[] dirs;
            // 1. Try dictionary (heat-only pipes, etc.)
            if (PipeConnectionMap.TryGetValue(block.BlockDefinition.SubtypeName, out dirs))
                return dirs;

            // 2. Try conveyor dummies
            var dummies = new Dictionary<string, IMyModelDummy>();
            block.Model.GetDummies(dummies);

            var result = new List<Base6Directions.Direction>();

            foreach (var kv in dummies)
            {
                var name = kv.Key.ToLower();
                if (!name.Contains("conveyor"))
                {
                    continue;
                }

                var localPos = kv.Value.Matrix.Translation;

                var gridDir = FindClosestDirection(localPos);

                result.Add(gridDir);
            }

            return result.ToArray();
        }

        internal static bool IsPipeCandidate(IMyCubeBlock block)
        {
            var defId = block.BlockDefinition;

            bool cached;

            if (_pipeCandidateCache.TryGetValue(defId, out cached))
                return cached;

            bool result = false;

            // 1. Check hardcoded subtype mask
            if (PipeConnectionMap.ContainsKey(block.BlockDefinition.SubtypeName))
            {
                result = true;
            }
            else if (!block.BlockDefinition.SubtypeName.ToLowerInvariant().Contains("conveyor"))
            {
                result = false;
            }
            else
            {
                // 2. Check for conveyor dummies
                var dummies = new Dictionary<string, IMyModelDummy>();
                block.Model.GetDummies(dummies);

                foreach (var kv in dummies)
                {
                    var name = kv.Key.ToLower();
                    if (name.Contains("conveyor"))
                    {
                        result = true;
                        break;
                    }
                }
            }

            _pipeCandidateCache[defId] = result; // cache it
            return result;
        }

        public List<IMyCubeBlock> GetConnectedBlocks(IMyCubeBlock root)
        {
            if (root.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Find connected from: {root.Position}");
            }
            var result = new List<IMyCubeBlock>();

            var grid = root.CubeGrid;
            var pos = root.Position;
            var dirs = GetPipeDirections(root);
            var orientation = root.Orientation;

            if (root.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Directions to look: {dirs.Length}");
            }

            foreach (var dir in dirs)
            {
                // Rotate local direction into grid space
                var gridDir = orientation.TransformDirection(dir);
                var neighborPos = pos + Base6Directions.GetIntVector(gridDir);

                var slim = grid.GetCubeBlock(neighborPos);
                if (root.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                {
                    MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Checking direction: {dir}, grid: {gridDir}, pos: {neighborPos}, found: {slim?.FatBlock != null}");
                }
                if (slim?.FatBlock == null)
                    continue;

                var neighbor = slim.FatBlock;

                // Confirm that both have ports facing each other
                if (ArePipesConnectedByGeometry(root, neighbor))
                {
                    result.Add(neighbor);
                }
                else
                {
                    if (root.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                    {
                        MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Result: {root.DisplayNameText} -!> {neighbor.DisplayNameText}");
                    }
                }

            }

            return result;
        }

        public HeatBehaviorAttachResult OnBlockAdded(IMyCubeBlock block, IGridHeatManager gridManager)
        {
            if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] !!! New block on a grid: {block.BlockDefinition.SubtypeName}");
            }
            var result = new HeatBehaviorAttachResult();
            var gridPipeManagers = gridManager.GetHeatPipeManagers();

            if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Managers on the grid: {gridPipeManagers.Count}");
            }

            if (!IsPipeCandidate(block))
                return result;

            if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Type check: passed");
            }

            var grid = block.CubeGrid;
            var pos = block.Position;

            var neighborOffsets = new[] { Vector3I.Forward, Vector3I.Backward, Vector3I.Left, Vector3I.Right, Vector3I.Up, Vector3I.Down };

            var connectedManagers = new List<HeatPipeManager>();
            var neighborNodes = new Dictionary<HeatPipeNode, HeatPipeManager>();

            if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Looking for neighbors");
            }

            var connectedNeighbors = GetConnectedBlocks(block);

            if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Found {connectedNeighbors.Count}");
            }

            // 1. Check connected neighbors for existing networks
            foreach (var cneighbor in connectedNeighbors)
            {
                if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                {
                    MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] >> Found neighbor: {cneighbor.DisplayNameText}");
                }

                foreach (var manager in gridPipeManagers)
                {
                    if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                    {
                        MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] >> Nodes in manager: {manager.Nodes.Count}");
                    }
                    HeatPipeNode node = manager.TryGetNode(cneighbor);
                    if (node == null)
                        continue;

                    if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                    {
                        MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] >> Found node");
                    }

                    if (ArePipesConnectedByGeometry(block, cneighbor))
                    {
                        if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                        {
                            MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] >> Connectable, add node");
                        }
                        connectedManagers.Add(manager);
                        neighborNodes[node] = manager;
                    }
                }
            }


            // // 1. Look for existing pipe connections
            // foreach (var offset in neighborOffsets)
            // {
            //     var neighborPos = pos + offset;
            //     var slim = grid.GetCubeBlock(neighborPos);
            //     if (slim?.FatBlock == null)
            //         continue;

            //     var neighbor = slim.FatBlock;

            //     if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            //     {
            //         MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] >> Found neighbor: {neighbor.DisplayNameText}");
            //     }

            //     foreach (var manager in gridPipeManagers)
            //     {
            //         if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            //         {
            //             MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] >> Nodes in manager: {manager.Nodes.Count}");
            //         }
            //         HeatPipeNode node = manager.TryGetNode(neighbor);
            //         if (node == null)
            //             continue;

            //         if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            //         {
            //             MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] >> Found node");
            //         }

            //         if (ArePipesConnectedByGeometry(block, neighbor))
            //         {
            //             if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            //             {
            //                 MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] >> Connectable, add node");
            //             }
            //             connectedManagers.Add(manager);
            //             neighborNodes[node] = manager;
            //         }
            //     }
            // }

            if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Found managers: {connectedManagers.Count}, nodes: {neighborNodes.Count}");
            }

            // 2. Create new node
            var newNode = new HeatPipeNode { Block = block };

            // 3. If no connections — create a new manager
            if (connectedManagers.Count == 0)
            {
                if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                {
                    MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] New network created");
                }
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
                if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                {
                    MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Existing network extended");
                }
                var manager = connectedManagers.First();
                foreach (var kvp in neighborNodes)
                {
                    manager.TryConnectNodes(newNode, kvp.Key);
                }
                result.Behavior = manager;
                result.AffectedBlocks = new List<IMyCubeBlock> { block };
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
            if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Several managers merged, new manager with {mergedManager.Nodes.Count} created");
            }
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
            if (!HeatPipeManagerFactory.IsPipeCandidate(block))
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
                if (a.Block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                {
                    MyLog.Default.WriteLine($"[HeatManagement] Both nodes here: connected");
                }
                return true;
            }

            if (_nodes.Contains(a))
            {
                if (a.Block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                {
                    MyLog.Default.WriteLine($"[HeatManagement] First node here");
                }
                if (!TryAddNode(b)) return false;
                var edge = new HeatPipeEdge { A = a, B = b };
                a.Connections.Add(edge);
                b.Connections.Add(edge);
                if (a.Block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                {
                    MyLog.Default.WriteLine($"[HeatManagement] First node here: connected");
                }
                return true;
            }

            if (_nodes.Contains(b))
            {
                if (b.Block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                {
                    MyLog.Default.WriteLine($"[HeatManagement] Second node here");
                }
                if (!TryAddNode(a)) return false;
                var edge = new HeatPipeEdge { A = a, B = b };
                a.Connections.Add(edge);
                b.Connections.Add(edge);
                if (b.Block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                {
                    MyLog.Default.WriteLine($"[HeatManagement] Second node here: connected");
                }
                return true;
            }

            if (a.Block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement] No node found");
            }

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

            if (!HeatPipeManagerFactory.IsPipeConnectedToBlock(node.Block, neighbor))
            {
                return 0f;
            }

            // Heat transfer from pipe node to neighbor (battery, sink, etc.)
            float tempPipe = HeatSession.Api.Utils.GetHeat(network);
            float tempOther = HeatSession.Api.Utils.GetHeat(neighbor);

            float tempDiff = tempOther - tempPipe;

            float contactArea = HeatSession.Api.Utils.GetLargestFaceArea(neighbor.SlimBlock);

            // Use global pipe-to-block conductance config
            float conductance = Config.Instance.HEATPIPE_CONDUCTIVITY;
            float energyTransferred = tempDiff * contactArea * conductance * deltaTime;

            // Return raw joules of heat
            return energyTransferred;
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
                    float limit;

                    // Limited to factual energy in the block
                    if (energyDelta > 0)
                    {
                        limit = tempDiff * capB / 2;
                        energyDelta = Math.Min(energyDelta, limit);
                    }
                    else
                    {
                        limit = tempDiff * capA / 2;
                        energyDelta = Math.Max(energyDelta, limit);
                    }

                    HeatSession.Api.Utils.ApplyHeatChange(edge.A.Block, energyDelta / capA);
                    HeatSession.Api.Utils.ApplyHeatChange(edge.B.Block, -energyDelta / capB);
                }
            }
        }

        public void Cleanup() => _nodes = null;

        public void ReactOnNewHeat(float heat)
        {
            foreach (var node in _nodes)
            {
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
            if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] !! Block removed {block.DisplayNameText}");
            }
            var node = _nodes.FirstOrDefault(n => n.Block == block);
            if (node == null)
                return;

            if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Connections to remove {node.Connections.Count}");
            }
            // 1. Remove connected edges
            foreach (var edge in node.Connections)
            {
                var other = edge.A == node ? edge.B : edge.A;
                other.Connections.Remove(edge);
            }

            _nodes.Remove(node);
            behaviorMap.Remove(node.Block);

            if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Nodes left {_nodes.Count}");
            }

            // 2. If only one node remains → just keep it
            if (_nodes.Count <= 1)
                return;

            // 3. Recompute sub-networks (connected graphs)
            var subgraphs = DiscoverSubgraphsWithEdges();

            if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Subgraphs discovered {subgraphs.Count}");
            }

            if (subgraphs.Count <= 1)
                return;

            int created = 0;

            foreach (var sub in subgraphs)
            {
                created++;
                var newManager = new HeatPipeManager(gridManager);
                if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
                {
                    MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Creating new network with {sub.Nodes.Count} nodes and {sub.Edges.Count} edges");
                }
                foreach (var n in sub.Nodes)
                    newManager.TryAddNode(n);

                foreach (var edge in sub.Edges)
                    newManager.TryConnectNodes(edge.A, edge.B);

                foreach (var n in sub.Nodes)
                    behaviorMap[n.Block] = newManager;
            }

            if (block.CubeGrid.CustomName.Contains(Config.HeatDebugString))
            {
                MyLog.Default.WriteLine($"[HeatManagement,OnBlockAdded] Networks created {created}");
            }

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

        public int GetNetworkSize()
        {
            return _nodes?.Count ?? 0;
        }

        public int GetNetworkHash()
        {
            return GetHashCode();
        }

        public float GetAverageTemperature()
        {
            if (_nodes == null || _nodes.Count == 0)
                return 0f;

            float totalTemp = 0f;
            foreach (var node in _nodes)
            {
                totalTemp += HeatSession.Api.Utils.GetHeat(node.Block);
            }
            return totalTemp / _nodes.Count;
        }
    }

    public class HeatPipeSubgraph
    {
        public List<HeatPipeNode> Nodes = new List<HeatPipeNode>();
        public List<HeatPipeEdge> Edges = new List<HeatPipeEdge>();
    }
}