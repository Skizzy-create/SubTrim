using System;
using System.Collections.Generic;

namespace SubTrim
{
    /// <summary>
    /// (c) WildBlueTools WildBlueIndustries.WBIBallastTank. Each FixedUpdate (when splashed) the tank sets
    /// hostPart.buoyancy = baseBuoyancy x (1 - amount/maxAmount) from its ballast resource (IntakeLqd by default) and
    /// moves the amount at fillRate/ventRate u/s while its ventState is Flooding/Venting.
    ///
    /// Drive: SubTrim keeps ventState Closed and sets the ballast resource amount itself, at most fillRate (flooding,
    /// only while the host part is in the water) or ventRate (venting) u/s, so WBI's own buoyancy formula does the physics.
    /// Only tanks of type Ballast are used; ForwardTrim/AftTrim tanks are left to the dive computer's auto-trim.
    /// Tanks in "vessel-wide" mode (no ballast resource on the tank or its parent) are not supported and skipped.
    /// While SubTrim drives, every WBIDiveComputer on the vessel has maintainDepth switched off (restored on release).
    /// </summary>
    public sealed class WBIBallastActuator : BallastActuator
    {
        const int TankTypeBallast = 0;   // BallastTankTypes.Ballast
        const int VentClosed = 0;        // BallastVentStates.Closed

        static bool initialised;
        public static Type TankType { get; private set; }
        public static Type DiveComputerType { get; private set; }
        static Func<object, int> getTankType, getVentState;
        static Action<object, int> setVentState;
        static Func<object, double> getFillRate, getVentRate;
        static Func<object, float> getBaseBuoyancy;
        static Func<object, PartResource> getResource;
        static Func<object, Part> getHostPart;
        static Func<object, bool> getMaintainDepth;
        static Action<object, bool> setMaintainDepth;

        public static bool Init()
        {
            if (initialised) return TankType != null;
            initialised = true;
            Type t = FastAccess.FindType("WildBlueIndustries.WBIBallastTank");
            if (t != null)
            {
                getTankType = FastAccess.Getter<int>(t, "tankType");
                getVentState = FastAccess.Getter<int>(t, "ventState");
                setVentState = FastAccess.Setter<int>(t, "ventState");
                getFillRate = FastAccess.Getter<double>(t, "fillRate");
                getVentRate = FastAccess.Getter<double>(t, "ventRate");
                getBaseBuoyancy = FastAccess.Getter<float>(t, "baseBuoyancy");
                getResource = FastAccess.Getter<PartResource>(t, "resourceBallast");
                getHostPart = FastAccess.Getter<Part>(t, "hostPart");
                if (getResource == null || setVentState == null || getFillRate == null || getVentRate == null)
                    UnityEngine.Debug.LogWarning("[SubTrim] WildBlueTools WBIBallastTank found but its fields differ; not using it");
                else
                {
                    TankType = t;
                    UnityEngine.Debug.Log("[SubTrim] WildBlueTools WBIBallastTank detected");
                }
            }
            Type d = FastAccess.FindType("WildBlueIndustries.WBIDiveComputer");
            if (d != null)
            {
                getMaintainDepth = FastAccess.Getter<bool>(d, "maintainDepth");
                setMaintainDepth = FastAccess.Setter<bool>(d, "maintainDepth");
                if (getMaintainDepth != null && setMaintainDepth != null) DiveComputerType = d;
            }
            return TankType != null;
        }

        /// <summary>True if the tank is a main ballast tank (not a trim tank) and has a local ballast resource.</summary>
        public static bool Usable(PartModule tank)
        {
            if (TankType == null || tank == null) return false;
            if (getTankType != null && getTankType(tank) != TankTypeBallast) return false;
            return getResource(tank) != null;
        }

        /// <summary>The part whose resource and buoyancy the tank drives (the tank or its parent).</summary>
        public static Part HostOf(PartModule tank)
        {
            Part host = getHostPart != null ? getHostPart(tank) : null;
            return host != null ? host : tank.part;
        }

        public static void SuspendDiveComputers(List<PartModule> computers, List<bool> saved)
        {
            saved.Clear();
            for (int i = 0; i < computers.Count; i++)
            {
                PartModule c = computers[i];
                bool was = c != null && getMaintainDepth(c);
                saved.Add(was);
                if (was) setMaintainDepth(c, false);
            }
        }

        public static void RestoreDiveComputers(List<PartModule> computers, List<bool> saved)
        {
            for (int i = 0; i < computers.Count && i < saved.Count; i++)
                if (computers[i] != null && saved[i]) setMaintainDepth(computers[i], true);
            saved.Clear();
        }

        readonly PartModule tank;
        Part host;
        PartResource res;
        double k, baseB, density;

        public WBIBallastActuator(PartModule tank)
        {
            this.tank = tank;
            part = tank.part;
        }

        public override string Kind => "WBI";
        public override int Priority => 2;

        public override bool Refresh(double rhoS, double vesselMass)
        {
            if (!Alive(tank) || !Alive(part)) return false;
            if (res == null)
            {
                res = getResource(tank);
                host = HostOf(tank);
                if (res == null || !Alive(host)) return false;
            }
            if (host.partBuoyancy == null || res.maxAmount <= 0) return false;
            k = host.partBuoyancy.displacement * rhoS;
            baseB = getBaseBuoyancy != null ? getBaseBuoyancy(tank) : 0f;
            if (baseB <= 0 && host.partInfo != null && host.partInfo.partPrefab != null) baseB = host.partInfo.partPrefab.buoyancy;
            density = res.info != null ? res.info.density : 0;
            double full = k * baseB;
            LiftNow = full * (1.0 - res.amount / res.maxAmount) - density * res.amount;
            LiftMin = -density * res.maxAmount;
            LiftMax = full;
            return true;
        }

        public override void Drive(ref DriveContext ctx)
        {
            if (res == null) return;
            if (getVentState(tank) != VentClosed) setVentState(tank, VentClosed);
            double slope = k * baseB / res.maxAmount + density; // lift lost per unit flooded
            if (slope <= 1e-12) return;
            double want = Clamp((k * baseB - Target) / slope, 0, res.maxAmount);
            double up = host.submergedPortion > 0 && !ctx.Emergency ? getFillRate(tank) * ctx.Dt : 0;
            double down = getVentRate(tank) * ctx.RateMultiplier * ctx.Dt;
            res.amount = Approach(res.amount, want, up, down);
        }
    }
}
