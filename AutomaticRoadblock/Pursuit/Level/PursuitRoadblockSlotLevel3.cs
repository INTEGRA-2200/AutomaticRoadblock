using System.Collections.Generic;
using System.Linq;
using AutomaticRoadblocks.Barriers;
using AutomaticRoadblocks.Instances;
using AutomaticRoadblocks.LightSources;
using AutomaticRoadblocks.Roadblock.Slot;
using AutomaticRoadblocks.Street.Info;
using AutomaticRoadblocks.Utils;
using Rage;
using VehicleType = AutomaticRoadblocks.Vehicles.VehicleType;

namespace AutomaticRoadblocks.Pursuit.Level
{
    public class PursuitRoadblockSlotLevel3 : AbstractPursuitRoadblockSlot
    {
        internal PursuitRoadblockSlotLevel3(Road.Lane lane, BarrierModel mainBarrier, BarrierModel secondaryBarrier, float heading, Vehicle targetVehicle,
            List<LightModel> lightSources, bool shouldAddLights)
            : base(lane, mainBarrier, secondaryBarrier, DetermineVehicleType(), heading, targetVehicle, lightSources, shouldAddLights)
        {
        }

        public override void Spawn()
        {
            base.Spawn();
            CopInstances
                .ToList()
                .ForEach(x => x.AimAt(TargetVehicle, 45000));
        }

        protected override void InitializeCops()
        {
            var isBike = ModelUtils.Vehicles.IsBike(VehicleModel);
            var pedSpawnPosition = CalculatePositionBehindVehicle();
            var totalOccupants = isBike ? 1 : Random.Next(1, 3);

            for (var i = 0; i < totalOccupants; i++)
            {
                Instances.Add(new InstanceSlot(EEntityType.CopPed, GameUtils.GetOnTheGroundPosition(pedSpawnPosition), 0f,
                    (position, heading) =>
                        PedFactory.CreateCopWeaponsForModel(PedFactory.CreateCopForVehicle(VehicleModel, position, heading))));
                pedSpawnPosition += MathHelper.ConvertHeadingToDirection(Heading + 90) * 1.5f;
            }
        }

        protected override void InitializeScenery()
        {
            // no-op
        }

        private static VehicleType DetermineVehicleType()
        {
            return Random.Next(5) switch
            {
                0 => VehicleType.PrisonerTransport,
                1 => VehicleType.StateUnit,
                _ => VehicleType.LocalUnit
            };
        }
    }
}