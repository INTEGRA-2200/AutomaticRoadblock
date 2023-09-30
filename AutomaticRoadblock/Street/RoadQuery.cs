using System;
using System.Collections.Generic;
using System.Linq;
using AutomaticRoadblocks.Logging;
using AutomaticRoadblocks.Street.Factory;
using AutomaticRoadblocks.Street.Info;
using Rage;
using Rage.Native;

namespace AutomaticRoadblocks.Street
{
    public static class RoadQuery
    {
        private static readonly ILogger Logger = IoC.Instance.GetInstance<ILogger>();

        #region Methods

        /// <summary>
        /// Find the closest vehicle node to the given position.
        /// </summary>
        /// <param name="position">Set the position to use as reference.</param>
        /// <param name="nodeType">Set the road type.</param>
        /// <returns></returns>
        public static VehicleNodeInfo FindClosestNode(Vector3 position, EVehicleNodeType nodeType)
        {
            VehicleNodeInfo closestVehicleNode = null;
            var closestRoadDistance = 99999f;

            foreach (var road in StreetHelper.FindNearbyVehicleNodes(position, nodeType))
            {
                var roadDistanceToPosition = Vector3.Distance2D(road.Position, position);

                if (roadDistanceToPosition > closestRoadDistance)
                    continue;

                closestVehicleNode = road;
                closestRoadDistance = roadDistanceToPosition;
            }

            return closestVehicleNode;
        }

        /// <summary>
        /// Get the closest road near the given position.
        /// </summary>
        /// <param name="position">Set the position to use as reference.</param>
        /// <param name="nodeType">Set the road type.</param>
        /// <returns>Returns the position of the closest road.</returns>
        public static IVehicleNode FindClosestRoad(Vector3 position, EVehicleNodeType nodeType)
        {
            return ToVehicleNode(FindClosestNode(position, nodeType));
        }

        /// <summary>
        /// Find streets while traversing the given distance from the current location.
        /// This collects each discovered node while it traverses the expected distance.
        /// </summary>
        /// <param name="position">The position to start from.</param>
        /// <param name="heading">The heading to traverse the road from.</param>
        /// <param name="distance">The distance to traverse.</param>
        /// <param name="roadType">The road types to follow.</param>
        /// <param name="blacklistedFlags">The flags of a node which should be ignored if present.</param>
        /// <param name="stopAtIntersection">Stop at the first intersection found while travelling along the road.</param>
        /// <returns>Returns the roads found while traversing the distance.</returns>
        /// <remarks>The last element in the collection will always be of type <see cref="EStreetType.Road"/>.</remarks>
        public static ICollection<IVehicleNode> FindRoadsTraversing(Vector3 position, float heading, float distance, EVehicleNodeType roadType,
            ENodeFlag blacklistedFlags, bool stopAtIntersection)
        {
            var nodeInfos = FindVehicleNodesWhileTraversing(position, heading, distance, roadType, blacklistedFlags, stopAtIntersection);
            var startedAt = DateTime.Now.Ticks;
            var roads = nodeInfos
                .Select(ToVehicleNode)
                .ToList();
            var calculationTime = (DateTime.Now.Ticks - startedAt) / TimeSpan.TicksPerMillisecond;
            Logger.Debug($"Converted a total of {nodeInfos.Count} nodes to roads in {calculationTime} millis");
            return roads;
        }

        /// <summary>
        /// Get nearby roads near the given position.
        /// </summary>
        /// <param name="position">Set the position to use as reference.</param>
        /// <param name="nodeType">The allowed node types to return.</param>
        /// <param name="radius">The radius to search for nearby roads.</param>
        /// <returns>Returns the position of the closest road.</returns>
        public static IEnumerable<IVehicleNode> FindNearbyRoads(Vector3 position, EVehicleNodeType nodeType, float radius)
        {
            Assert.NotNull(position, "position cannot be null");
            Assert.NotNull(nodeType, "nodeType cannot be null");
            var nodes = FindNearbyVehicleNodes(position, nodeType, radius);

            return nodes
                .Select(ToVehicleNode)
                .ToList();
        }

        /// <summary>
        /// Verify if the given point is on a road.
        /// </summary>
        /// <param name="position">The point position to check.</param>
        /// <returns>Returns true if the position is on a road, else false.</returns>
        public static bool IsPointOnRoad(Vector3 position)
        {
            return NativeFunction.Natives.IS_POINT_ON_ROAD<bool>(position.X, position.Y, position.Z);
        }

        /// <summary>
        /// Verify if the given position is a dirt/offroad location based on the slow road flag.
        /// </summary>
        /// <param name="position">The position to check.</param>
        /// <returns>Returns true when the position is a dirt/offroad, else false.</returns>
        public static bool IsSlowRoad(Vector3 position)
        {
            var nodeId = GetClosestNodeId(position);
            return IsSlowRoad(nodeId);
        }

        /// <summary>
        /// Convert the given node info into actual road information.
        /// </summary>
        /// <param name="nodeInfo">The vehicle node info to convert.</param>
        /// <returns>Returns the discovered road info for the given node.</returns>
        public static IVehicleNode ToVehicleNode(VehicleNodeInfo nodeInfo)
        {
            return nodeInfo.Flags.HasFlag(ENodeFlag.IsJunction)
                ? IntersectionFactory.Create(nodeInfo)
                : RoadFactory.Create(nodeInfo);
        }

        #endregion

        #region Functions

        private static VehicleNodeInfo FindVehicleNodeWithHeading(Vector3 position, float heading, EVehicleNodeType nodeType,
            ENodeFlag blacklistedFlags = ENodeFlag.None)
        {
            Logger.Trace($"Searching for vehicle nodes at {position} matching heading {heading} and type {nodeType}");
            var nodes = StreetHelper.FindNearbyVehicleNodes(position, nodeType).ToList();
            var closestNodeDistance = 9999f;
            var closestNode = (VehicleNodeInfo)null;

            // filter out any nodes which match one or more blacklisted conditions
            var filteredNodes = nodes
                .Where(x => (x.Flags & blacklistedFlags) == 0)
                .ToList();

            foreach (var node in filteredNodes)
            {
                var distance = position.DistanceTo(node.Position);

                if (distance > closestNodeDistance)
                    continue;

                closestNodeDistance = distance;
                closestNode = node;
            }

            if (closestNode != null)
            {
                // verify if we're at an intersection
                // if so, follow the current heading when the intersection heading difference is too large
                if (closestNode.Flags.HasFlag(ENodeFlag.IsJunction) && Math.Abs(closestNode.Heading - heading) >= 65f)
                {
                    closestNode = new VehicleNodeInfo(closestNode.Position, heading)
                    {
                        LanesInSameDirection = closestNode.LanesInSameDirection,
                        LanesInOppositeDirection = closestNode.LanesInOppositeDirection,
                        Density = closestNode.Density,
                        Flags = closestNode.Flags
                    };
                }
                // verify if the closest node heading is opposite of the wanted heading
                // if so, reverse the node information
                else if (Math.Abs(closestNode.Heading - heading) > 115f)
                {
                    Logger.Debug($"Reversing the closest matching node ({closestNode}) as it's heading is the opposite of the wanted heading ({heading})");
                    closestNode = new VehicleNodeInfo(closestNode.Position, MathHelper.NormalizeHeading(closestNode.Heading + 180f))
                    {
                        LanesInSameDirection = closestNode.LanesInOppositeDirection,
                        LanesInOppositeDirection = closestNode.LanesInSameDirection,
                        Density = closestNode.Density,
                        Flags = closestNode.Flags
                    };
                }
            }

            Logger.Debug(closestNode != null
                ? $"Using node {closestNode} as closest matching"
                : $"No matching node found for position: {position} with heading {heading}");

            return closestNode;
        }

        private static List<VehicleNodeInfo> FindVehicleNodesWhileTraversing(Vector3 position, float heading, float expectedDistance, EVehicleNodeType nodeType,
            ENodeFlag blacklistedFlags, bool stopAtIntersection)
        {
            var startedAt = DateTime.Now.Ticks;
            var nextNodeCalculationStartedAt = DateTime.Now.Ticks;
            var distanceTraversed = 0f;
            var distanceToMove = 5f;
            var findNodeAttempt = 0;
            var nodeInfos = new List<VehicleNodeInfo>();
            var lastFoundNodeInfo = new VehicleNodeInfo(position, heading);

            // keep traversing the road while the expected distance isn't reached
            // or we've ended at a junction (never end at a junction as it actually never can be used)
            while (distanceTraversed < expectedDistance || lastFoundNodeInfo.Flags.HasFlag(ENodeFlag.IsJunction))
            {
                if (findNodeAttempt == 5)
                {
                    Logger.Warn($"Failed to traverse road, unable to find next node after {lastFoundNodeInfo} (tried {findNodeAttempt} times)");
                    break;
                }

                // never start at an intersection as it causes wrong directions being taken
                var nodeTypeForThisIteration = IsFirstNodeDetectionCycle(nodeType, nodeInfos, findNodeAttempt)
                    ? EVehicleNodeType.AllRoadNoJunctions
                    : nodeType;
                var findNodeAt = lastFoundNodeInfo.Position + MathHelper.ConvertHeadingToDirection(lastFoundNodeInfo.Heading) * distanceToMove;
                var nodeTowardsHeading = FindVehicleNodeWithHeading(findNodeAt, lastFoundNodeInfo.Heading, nodeTypeForThisIteration, blacklistedFlags);

                if (nodeTowardsHeading == null
                    || nodeTowardsHeading.Position.Equals(lastFoundNodeInfo.Position)
                    || nodeInfos.Any(x => x.Position.Equals(nodeTowardsHeading.Position)))
                {
                    distanceToMove *= 1.5f;
                    findNodeAttempt++;
                }
                else
                {
                    var additionalDistanceTraversed = lastFoundNodeInfo.Position.DistanceTo(nodeTowardsHeading.Position);
                    var nextNodeCalculationDuration = (DateTime.Now.Ticks - nextNodeCalculationStartedAt) / TimeSpan.TicksPerMillisecond;
                    nodeInfos.Add(nodeTowardsHeading);
                    distanceToMove = 5f;
                    findNodeAttempt = 0;
                    distanceTraversed += additionalDistanceTraversed;
                    lastFoundNodeInfo = nodeTowardsHeading;
                    Logger.Trace(
                        $"Traversed an additional {additionalDistanceTraversed} distance in {nextNodeCalculationDuration} millis, new total traversed distance = {distanceTraversed}, total nodes = {nodeInfos.Count}");
                    nextNodeCalculationStartedAt = DateTime.Now.Ticks;

                    // verify if we need to stop at the first intersection or not
                    if (stopAtIntersection && nodeTowardsHeading.IsJunctionNode)
                    {
                        Logger.Trace($"Junction has been reached for {position} heading {heading}");
                        break;
                    }
                }
            }

            var calculationTime = (DateTime.Now.Ticks - startedAt) / TimeSpan.TicksPerMillisecond;
            Logger.Debug(
                $"Traversed a total of {position.DistanceTo(lastFoundNodeInfo.Position)} distance with expectation {expectedDistance} within {calculationTime} millis\n" +
                $"origin: {position}, destination: {lastFoundNodeInfo.Position}");
            return nodeInfos;
        }

        private static IEnumerable<VehicleNodeInfo> FindNearbyVehicleNodes(Vector3 position, EVehicleNodeType nodeType, float radius,
            int rotationInterval = 20)
        {
            const int radInterval = 2;
            var nodes = new List<VehicleNodeInfo>();
            var rad = 1;

            while (rad <= radius)
            {
                for (var rot = 0; rot < 360; rot += rotationInterval)
                {
                    var x = (float)(position.X + rad * Math.Sin(rot));
                    var y = (float)(position.Y + rad * Math.Cos(rot));

                    var node = StreetHelper.FindVehicleNode(new Vector3(x, y, position.Z + 5f), nodeType);

                    if (nodes.Contains(node))
                        continue;

                    Logger.Trace($"Discovered new vehicle node, {node}");
                    nodes.Add(node);
                }

                if (radius % radInterval != 0 && rad == (int)radius - (int)radius % radInterval)
                {
                    rad += (int)radius % radInterval;
                }
                else
                {
                    rad += radInterval;
                }
            }

            return nodes;
        }

        private static bool IsFirstNodeDetectionCycle(EVehicleNodeType nodeType, IReadOnlyCollection<VehicleNodeInfo> nodeInfos, int findNodeAttempt)
        {
            // if the first attempt failed to find a node, continue at an intersection
            return nodeInfos.Count == 0 && findNodeAttempt == 0 && nodeType is EVehicleNodeType.AllNodes or EVehicleNodeType.MainRoadsWithJunctions;
        }

        private static int GetClosestNodeId(Vector3 position)
        {
            return NativeFunction.CallByName<int>("GET_NTH_CLOSEST_VEHICLE_NODE_ID", position.X, position.Y, position.Z, 1, 1, 1077936128, 0f);
        }

        /// <summary>
        /// Verify if the given node is is offroad.
        /// </summary>
        /// <param name="nodeId">The node id to check.</param>
        /// <returns>Returns true when the node is an alley, dirt road or carpark.</returns>
        private static bool IsSlowRoad(int nodeId)
        {
            // PATHFIND::_GET_IS_SLOW_ROAD_FLAG
            return NativeFunction.CallByHash<bool>(0x4F5070AA58F69279, nodeId);
        }

        #endregion
    }
}