using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEditor.Surface.Elements;
using FlaxEditor.Surface.Undo;

namespace FlaxEditor.Surface
{
    public partial class VisjectSurface
    {
        // Reference https://github.com/stefnotch/xnode-graph-formatter/blob/812e08e71c7b9b7eb0810dbdfb0a9a1034da6941/Assets/Examples/MathGraph/Editor/MathGraphEditor.cs

        private class NodeFormattingData
        {
            /// <summary>
            /// Starting from 0 at the main nodes
            /// </summary>
            public int Layer;

            /// <summary>
            /// Position in the layer
            /// </summary>
            public int Offset;

            /// <summary>
            /// How far the subtree needs to be moved additionally
            /// </summary>
            public int SubtreeOffset;
        }

        /// <summary>
        /// Formats a graph where the nodes can be disjointed.
        /// Uses the Sugiyama method
        /// </summary>
        /// <param name="nodes">List of nodes</param>
        protected void FormatGraph(List<SurfaceNode> nodes)
        {
            if (nodes.Count <= 1 || !CanEdit) return;

            var nodesToVisit = new HashSet<SurfaceNode>(nodes);

            // While we haven't formatted every node
            while (nodesToVisit.Count > 0)
            {
                // Run a search in both directions
                var connectedNodes = new List<SurfaceNode>();
                var queue = new Queue<SurfaceNode>();

                var startNode = nodesToVisit.First();
                nodesToVisit.Remove(startNode);
                queue.Enqueue(startNode);

                while (queue.Count > 0)
                {
                    var node = queue.Dequeue();
                    connectedNodes.Add(node);

                    for (int i = 0; i < node.Elements.Count; i++)
                    {
                        if (node.Elements[i] is Box box)
                        {
                            for (int j = 0; j < box.Connections.Count; j++)
                            {
                                if (nodesToVisit.Contains(box.Connections[j].ParentNode))
                                {
                                    nodesToVisit.Remove(box.Connections[j].ParentNode);
                                    queue.Enqueue(box.Connections[j].ParentNode);
                                }
                            }

                        }
                    }
                }

                FormatConnectedGraph(connectedNodes);
            }
        }

        /// <summary>
        /// Formats a graph where all nodes are connected
        /// </summary>
        /// <param name="nodes">List of connected nodes</param>
        protected void FormatConnectedGraph(List<SurfaceNode> nodes)
        {
            if (nodes.Count <= 1 || !CanEdit) return;

            var boundingBox = GetNodesBounds(nodes);

            var nodeData = nodes.ToDictionary(n => n, n => new NodeFormattingData { });

            // Rightmost nodes with none of our nodes to the right of them
            var endNodes = nodes
                    .Where(n => !n.GetBoxes().Any(b => b.IsOutput && b.Connections.Any(c => nodeData.ContainsKey(c.ParentNode))))
                    .OrderBy(n => n.Top) // Keep their relative order
                    .ToList();

            // Longest path layering
            int maxLayer = SetLayers(nodeData, endNodes);

            // Set the vertical offsets
            int maxOffset = SetOffsets(nodeData, endNodes, maxLayer);

            // Get the largest nodes in the Y and X direction
            float[] widths = new float[maxLayer + 1];
            float[] heights = new float[maxOffset + 1];
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodeData.TryGetValue(nodes[i], out var data))
                {
                    if (nodes[i].Width > widths[data.Layer])
                    {
                        widths[data.Layer] = nodes[i].Width;
                    }
                    if (nodes[i].Height > heights[data.Offset])
                    {
                        heights[data.Offset] = nodes[i].Height;
                    }
                }
            }

            // Layout the nodes
            Vector2 minSize = new Vector2(180, 180);

            var undoActions = new List<MoveNodesAction>();
            var topRightPosition = endNodes[0].Location;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodeData.TryGetValue(nodes[i], out var data))
                {
                    Vector2 size = Vector2.Max(minSize, new Vector2(widths[data.Layer], heights[data.Offset]));
                    Vector2 newLocation = (new Vector2(-data.Layer, data.Offset) * size) + topRightPosition;
                    Vector2 locationDelta = newLocation - nodes[i].Location;
                    nodes[i].Location = newLocation;

                    if (Undo != null)
                        undoActions.Add(new MoveNodesAction(Context, new[] { nodes[i].ID }, locationDelta));
                }
            }

            MarkAsEdited(false);
            Undo?.AddAction(new MultiUndoAction(undoActions, "Format nodes"));
        }

        /// <summary>
        /// Assigns a layer to every node
        /// </summary>
        /// <param name="nodeData">The exta node data</param>
        /// <param name="endNodes">The end nodes</param>
        /// <returns>The number of the maximum layer</returns>
        private int SetLayers(Dictionary<SurfaceNode, NodeFormattingData> nodeData, List<SurfaceNode> endNodes)
        {
            int maxLayer = 0;
            var stack = new Stack<SurfaceNode>(endNodes);

            while (stack.Count > 0)
            {
                var node = stack.Pop();
                int layer = nodeData[node].Layer;

                for (int i = 0; i < node.Elements.Count; i++)
                {
                    if (node.Elements[i] is InputBox box && box.HasAnyConnection)
                    {
                        var childNode = box.Connections[0].ParentNode;

                        if (nodeData.TryGetValue(childNode, out var data))
                        {
                            int nodeLayer = Math.Max(data.Layer, layer + 1);
                            data.Layer = nodeLayer;
                            if (nodeLayer > maxLayer)
                            {
                                maxLayer = nodeLayer;
                            }

                            stack.Push(childNode);
                        }
                    }
                }
            }
            return maxLayer;
        }


        /// <summary>
        /// Sets the node offsets
        /// </summary>
        /// <param name="nodeData">The exta node data</param>
        /// <param name="endNodes">The end nodes</param>
        /// <param name="maxLayer">The number of the maximum layer</param>
        /// <returns>The number of the maximum offset</returns>
        private int SetOffsets(Dictionary<SurfaceNode, NodeFormattingData> nodeData, List<SurfaceNode> endNodes, int maxLayer)
        {
            // This piece of code should be explained a bit better, since it does some fairly fancy stuff
            int maxOffset = 0;
            int[] offsets = new int[maxLayer + 1];

            var visitedNodes = new HashSet<SurfaceNode>();

            void SetOffsets(SurfaceNode node, NodeFormattingData straightParentData)
            {
                if (!nodeData.TryGetValue(node, out var data)) return;

                if (data.Layer >= 0 && offsets[data.Layer] > data.Offset)
                {
                    straightParentData.SubtreeOffset = Math.Max(straightParentData.SubtreeOffset, offsets[data.Layer] - data.Offset);
                }

                int childOffset = data.Offset;
                bool straightChild = true;

                for (int i = 0; i < node.Elements.Count; i++)
                {
                    if (node.Elements[i] is InputBox box && box.HasAnyConnection)
                    {
                        var childNode = box.Connections[0].ParentNode;
                        if (!visitedNodes.Contains(childNode) && nodeData.TryGetValue(childNode, out var childData))
                        {
                            visitedNodes.Add(childNode);
                            childData.Offset = childOffset;
                            SetOffsets(childNode, straightChild ? straightParentData : childData);
                            childOffset = childData.Offset + 1;
                            straightChild = false;
                        }
                    }
                }

                if (data.Layer >= 0)
                {
                    data.Offset += straightParentData.SubtreeOffset;
                    if (data.Offset > maxOffset)
                    {
                        maxOffset = data.Offset;
                    }
                    offsets[data.Layer] = data.Offset + 1;
                }
            }

            {
                // An imaginary final node
                var endNodeData = new NodeFormattingData { Layer = -1 };
                int childOffset = 0;
                bool straightChild = true;

                for (int i = 0; i < endNodes.Count; i++)
                {
                    if (nodeData.TryGetValue(endNodes[i], out var childData))
                    {
                        visitedNodes.Add(endNodes[i]);
                        childData.Offset = childOffset;
                        SetOffsets(endNodes[i], straightChild ? endNodeData : childData);
                        childOffset = childData.Offset + 1;
                        straightChild = false;
                    }
                }
            }

            return maxOffset;
        }

    }
}
