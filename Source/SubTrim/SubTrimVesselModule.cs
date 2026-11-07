using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SubTrim
{
    public enum TrimState
    {
        Off,
        Standby,
        NoBallast,
        Holding,
        Surfacing,
        Emergency
    }

    /// <summary>
    /// One per loaded vessel. Decides whether the vessel is a submarine SubTrim should manage, picks the depth target
    /// (AI command, manual target or "hold where you are"), runs the depth controller at tickInterval (10 Hz by default)
    /// from FixedUpdate, and spreads the requested change in net buoyancy over the vessel's ballast actuators.
    ///
    /// Cost: actuators/panels/AI binding are rebuilt only on structural vessel changes (O(parts)); each tick is one
    /// O(parts) pass for mass and buoyancy plus O(actuators), with no allocations.
    /// </summary>
    public class SubTrimVesselModule : VesselModule
    {
        public const string ModeAuto = "Auto";
        public const string ModeOn = "On";
        public const string ModeOff = "Off";

        const string ReasonAI = "AI";
        const string ReasonManual = "manual";
        const string ReasonHold = "hold";
        const string ReasonUser = "emergency blow";
        const string ReasonLowEc = "low EC";
        const string ReasonFlood = "flooding";
        const string ReasonNotSub = "not a submarine";
        const string ReasonDry = "not in water";
        const string ReasonDisabled = "disabled";
        const string ReasonNoBallast = "no ballast found";

        /// <summary>Virtual ballast scale, persisted so a dived boat does not pop up after a reload.</summary>
        [KSPField(isPersistant = true)] public float virtualScale = 1f;
        /// <summary>Player-requested emergency blow, persisted.</summary>
        [KSPField(isPersistant = true)] public bool emergencyBlow = false;

        readonly List<ModuleSubTrim> panels = new List<ModuleSubTrim>(2);
        ModuleSubTrim master;
        readonly List<IDepthCommandSource> sources = new List<IDepthCommandSource>(2);
        IDepthCommandSource source;
        readonly List<BallastActuator> actuators = new List<BallastActuator>(8);
        readonly List<PartModule> diveComputers = new List<PartModule>(1);
        readonly List<bool> diveComputerSaved = new List<bool>(1);
        BuoyancyAdjusterActuator adjuster;
        VirtualBallastActuator virtualBallast;
        VesselModule hullBreach;
        readonly DepthController controller = new DepthController();

        bool dirty = true;
        bool controlling;
        bool lowEc;
        bool floodEmergency;
        bool sourceWasActive;
        bool saturatedUp, saturatedDown;
        bool cfgSubmarine;
        float accumulator;
        float recheckTimer;
        int partCountAtBind = -1;
        double holdAltitude = 1;
        double crushDepth = double.PositiveInfinity;
        double ecMax;
        HullBreachLink.State hb;

        // Telemetry (read by ModuleSubTrim for the PAW).
        public TrimState State { get; private set; } = TrimState.Standby;
        public string Reason { get; private set; } = ReasonDry;
        public double TargetAltitude { get; private set; }
        public double NetFraction { get; private set; }
        public double CommandFraction { get; private set; }
        public double CrushDepth => crushDepth;
        public bool LowEc => lowEc;
        public bool FloodEmergency => floodEmergency;

        public override Activation GetActivation() => Activation.FlightScene | Activation.LoadedVessels;

        protected override void OnStart()
        {
            GameEvents.onVesselWasModified.Add(OnVesselModified);
            GameEvents.onVesselPartCountChanged.Add(OnVesselModified);
        }

        void OnDestroy()
        {
            GameEvents.onVesselWasModified.Remove(OnVesselModified);
            GameEvents.onVesselPartCountChanged.Remove(OnVesselModified);
            if (controlling) StopControl();
        }

        void OnVesselModified(Vessel v)
        {
            if (v == vessel) dirty = true;
        }

        public override void OnGoOnRails()
        {
            accumulator = 0f;
        }

        public override void OnUnloadVessel()
        {
            if (controlling) StopControl(true);
            dirty = true;
        }

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || !vessel.loaded || vessel.packed) return;
            SubTrimSettings s = SubTrimSettings.Instance;
            accumulator += TimeWarp.fixedDeltaTime;
            if (accumulator < s.tickInterval) return;
            float dt = accumulator;
            accumulator = 0f;

            if (dirty || vessel.parts.Count != partCountAtBind) Rebind();
            recheckTimer -= dt;
            if (recheckTimer <= 0f)
            {
                recheckTimer = 5f;
                // BuoyancyAdjuster can be switched on/off at any time from its window.
                if (BuoyancyAdjusterActuator.EnabledFor(vessel) != (adjuster != null)) Rebind();
            }

            try
            {
                Tick(s, dt);
            }
            catch (Exception e)
            {
                Debug.LogError("[SubTrim] " + vessel.vesselName + ": " + e);
                dirty = true;
            }
        }

        void Tick(SubTrimSettings s, double dt)
        {
            CelestialBody body = vessel.mainBody;
            string mode = master != null ? master.mode : ModeAuto;
            bool haveHB = hullBreach != null && HullBreachLink.Read(hullBreach, out hb);
            bool isSub = cfgSubmarine || (source != null && source.Present && source.IsSubmarine) || (haveHB && hb.IsSubmarine);
            bool inWater = body != null && body.ocean && (vessel.Splashed || vessel.altitude < 0.0);

            if (!s.enabled || mode == ModeOff || (mode != ModeOn && !isSub) || !inWater)
            {
                if (controlling) StopControl();
                State = (!s.enabled || mode == ModeOff) ? TrimState.Off : TrimState.Standby;
                Reason = !s.enabled || mode == ModeOff ? ReasonDisabled : (!inWater ? ReasonDry : ReasonNotSub);
                return;
            }
            if (actuators.Count == 0)
            {
                if (controlling) StopControl();
                State = TrimState.NoBallast;
                Reason = ReasonNoBallast;
                return;
            }
            if (!controlling) StartControl();

            // --- Measure: mass and fully-submerged buoyant capacity, the same way PartBuoyancy computes the force. ---
            double rhoS = body.oceanDensity * PhysicsGlobals.BuoyancyScalar;
            double mass = 0, capacity = 0;
            List<Part> parts = vessel.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                if (p == null || p.rb == null) continue;
                mass += p.rb.mass;
                PartBuoyancy pb = p.partBuoyancy;
                if (pb != null) capacity += pb.displacement * rhoS * p.buoyancy;
            }
            if (mass <= 0) return;

            for (int i = 0; i < actuators.Count; i++)
            {
                if (!actuators[i].Refresh(rhoS, mass))
                {
                    dirty = true;
                    return;
                }
            }
            double net = adjuster != null ? (adjuster.Level - 1.0) * mass : capacity - mass;

            // --- Target. ---
            bool srcActive = source != null && source.Present && source.Active;
            if (sourceWasActive && !srcActive) CaptureHold(s);
            sourceWasActive = srcActive;

            double target;
            string reason;
            if (master != null && master.manualDepth)
            {
                target = master.targetDepth <= 0.5f ? 1.0 : -master.targetDepth;
                reason = ReasonManual;
            }
            else if (srcActive)
            {
                target = source.CommandedAltitude();
                reason = ReasonAI;
            }
            else
            {
                target = holdAltitude;
                reason = ReasonHold;
            }

            // --- Emergencies: player blow, low EC, HullBreach flooding. ---
            vessel.GetConnectedResourceTotals(PartResourceLibrary.ElectricityHashcode, out double ecAmount, out ecMax);
            if (ecMax > 0)
            {
                double f = ecAmount / ecMax;
                if (lowEc) { if (f > s.resumeEcFraction) lowEc = false; }
                else if (f < s.lowEcFraction) lowEc = true;
            }
            else lowEc = false;

            if (haveHB)
            {
                double limit = s.hullBreachWaterFraction * mass;
                bool critical = s.hullBreachSurfaceOnCritical && hb.Critical > 0;
                if (!floodEmergency) floodEmergency = critical || hb.Water > limit;
                else if (!critical && hb.Water < 0.5 * limit) floodEmergency = false;
            }
            else floodEmergency = false;

            bool emergency = emergencyBlow || lowEc || floodEmergency;
            if (emergencyBlow) reason = ReasonUser;
            else if (floodEmergency) reason = ReasonFlood;
            else if (lowEc) reason = ReasonLowEc;

            bool surface = emergency || target > s.surfaceAltitude;
            if (!surface)
            {
                // Never hold below a safe fraction of the shallowest crush depth, or too close to the sea floor.
                if (!double.IsInfinity(crushDepth)) target = Math.Max(target, -crushDepth * s.crushSafetyFactor);
                double floor = vessel.terrainAltitude;
                if (floor < 0) target = Math.Max(target, floor + s.keelClearance);
                surface = target > s.surfaceAltitude;
            }

            // --- Control law. ---
            double netTarget;
            double rateMult = 1.0;
            if (emergency)
            {
                netTarget = double.PositiveInfinity;
                rateMult = s.emergencyRateMultiplier;
                controller.Integral = 0;
                State = TrimState.Emergency;
            }
            else if (surface)
            {
                netTarget = s.surfaceReserve * mass;
                State = TrimState.Surfacing;
            }
            else
            {
                double u = controller.Step(s.controller, vessel.altitude, target, vessel.verticalSpeed, dt, saturatedUp, saturatedDown);
                netTarget = u * mass;
                State = TrimState.Holding;
            }

            // --- Allocate the change in net lift to actuators in priority order. ---
            double remaining = emergency ? double.PositiveInfinity : netTarget - net;
            bool wantUp = remaining > 0;
            for (int i = 0; i < actuators.Count; i++)
            {
                BallastActuator a = actuators[i];
                double t = a.LiftNow + remaining;
                if (t > a.LiftMax) t = a.LiftMax;
                if (t < a.LiftMin) t = a.LiftMin;
                if (!double.IsInfinity(remaining)) remaining -= t - a.LiftNow;
                a.Target = t;
            }
            double slack = 1e-3 * mass;
            saturatedUp = wantUp && remaining > slack;
            saturatedDown = !wantUp && remaining < -slack;

            DriveContext ctx;
            ctx.Dt = dt;
            ctx.RateMultiplier = rateMult;
            ctx.Emergency = emergency;
            ctx.TargetAltitude = surface ? 0.0 : target;
            ctx.VesselMass = mass;
            ctx.Host = this;
            for (int i = 0; i < actuators.Count; i++) actuators[i].Drive(ref ctx);

            if (virtualBallast != null) virtualScale = (float)virtualBallast.Scale;
            TargetAltitude = surface ? 0.0 : target;
            NetFraction = net / mass;
            CommandFraction = emergency ? double.NaN : netTarget / mass;
            Reason = reason;
        }

        /// <summary>Charge EC for adding <paramref name="tonnes"/> of lift. Returns the lift actually paid for.</summary>
        public double PayForLift(double tonnes)
        {
            double cost = tonnes * SubTrimSettings.Instance.ecPerTonne;
            if (cost <= 0 || ecMax <= 0) return tonnes; // vessel without EC storage: free
            double got = vessel.rootPart.RequestResource(PartResourceLibrary.ElectricityHashcode, cost, ResourceFlowMode.ALL_VESSEL);
            return got >= cost ? tonnes : tonnes * (got / cost);
        }

        void CaptureHold(SubTrimSettings s)
        {
            holdAltitude = vessel.altitude < s.surfaceAltitude ? vessel.altitude : 1.0;
        }

        void StartControl()
        {
            controlling = true;
            controller.Reset();
            CaptureHold(SubTrimSettings.Instance);
            for (int i = 0; i < actuators.Count; i++) actuators[i].TakeControl();
            if (diveComputers.Count > 0) WBIBallastActuator.SuspendDiveComputers(diveComputers, diveComputerSaved);
            if (virtualBallast != null)
            {
                SubTrimSettings s = SubTrimSettings.Instance;
                virtualBallast.Scale = Mathf.Clamp(virtualScale, s.virtualMinScale, s.virtualMaxScale);
                virtualBallast.ApplyNow(); // so this tick's buoyancy measurement already sees it
            }
            if (SubTrimSettings.Instance.debug) Debug.Log("[SubTrim] " + vessel.vesselName + ": control started (" + actuators.Count + " actuators)");
        }

        void StopControl(bool keepVirtualScale = false)
        {
            controlling = false;
            for (int i = 0; i < actuators.Count; i++) actuators[i].Release();
            if (diveComputers.Count > 0) WBIBallastActuator.RestoreDiveComputers(diveComputers, diveComputerSaved);
            if (!keepVirtualScale) virtualScale = 1f;
            State = TrimState.Standby;
            if (SubTrimSettings.Instance.debug) Debug.Log("[SubTrim] " + vessel.vesselName + ": control stopped");
        }

        /// <summary>Rebuild panels, AI binding and actuators from the vessel's parts. Only on structural changes.</summary>
        void Rebind()
        {
            bool wasControlling = controlling;
            if (controlling) StopControl(true);
            dirty = false;
            partCountAtBind = vessel.parts.Count;
            SubTrimSettings s = SubTrimSettings.Instance;

            panels.Clear();
            actuators.Clear();
            diveComputers.Clear();
            adjuster = null;
            virtualBallast = null;
            cfgSubmarine = false;
            bool allowVirtual = s.virtualBallast;
            crushDepth = double.PositiveInfinity;

            KPBallastActuator.Init();
            WBIBallastActuator.Init();
            Type kpType = KPBallastActuator.ModuleType;
            Type tankType = WBIBallastActuator.TankType;
            Type diveType = WBIBallastActuator.DiveComputerType;

            CelestialBody body = vessel.mainBody;
            double p0 = body != null && body.atmosphere ? body.GetPressure(0) : 0.0;
            double rhoG = body != null ? body.oceanDensity * body.GeeASL * PhysicsGlobals.GravitationalAcceleration : 0.0;

            var excluded = new HashSet<Part>();
            var wbiHosts = new HashSet<Part>();
            List<Part> parts = vessel.parts;
            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                if (p == null) continue;
                if (rhoG > 0 && p.maxPressure > p0)
                {
                    double d = (p.maxPressure - p0) / rhoG;
                    if (d < crushDepth) crushDepth = d;
                }
                PartModuleList modules = p.Modules;
                for (int j = 0; j < modules.Count; j++)
                {
                    PartModule m = modules[j];
                    if (m is ModuleSubTrim panel)
                    {
                        panels.Add(panel);
                        if (panel.isSubmarine) cfgSubmarine = true;
                        if (!panel.allowVirtualBallast) allowVirtual = false;
                        continue;
                    }
                    Type t = m.GetType();
                    if (kpType != null && t == kpType)
                    {
                        actuators.Add(new KPBallastActuator(m, s.kpNativeDepthHold));
                        excluded.Add(p);
                    }
                    else if (tankType != null && t == tankType)
                    {
                        Part host = WBIBallastActuator.HostOf(m);
                        excluded.Add(host);
                        if (WBIBallastActuator.Usable(m))
                        {
                            actuators.Add(new WBIBallastActuator(m));
                            wbiHosts.Add(host);
                        }
                    }
                    else if (diveType != null && t == diveType)
                    {
                        diveComputers.Add(m);
                    }
                }
            }

            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                if (p == null || wbiHosts.Contains(p)) continue;
                for (int r = 0; r < s.ballastResources.Count; r++)
                {
                    PartResource res = p.Resources.Get(s.ballastResources[r]);
                    if (res != null && res.maxAmount > 0) actuators.Add(new ResourceBallastActuator(p, res));
                }
            }

            if (BuoyancyAdjusterActuator.EnabledFor(vessel))
            {
                // BuoyancyAdjuster replaces stock buoyancy entirely; part.buoyancy changes would have no effect.
                adjuster = new BuoyancyAdjusterActuator(vessel);
                actuators.Clear();
                actuators.Add(adjuster);
            }
            else if (actuators.Count == 0 && allowVirtual)
            {
                var vb = new VirtualBallastActuator();
                for (int i = 0; i < parts.Count; i++)
                {
                    Part p = parts[i];
                    if (p == null || excluded.Contains(p) || p.partBuoyancy == null) continue;
                    if (p.physicalSignificance != Part.PhysicalSignificance.FULL) continue;
                    float stock = p.partInfo != null && p.partInfo.partPrefab != null ? p.partInfo.partPrefab.buoyancy : p.buoyancy;
                    if (stock > 0f) vb.Add(p);
                }
                if (vb.PartCount > 0)
                {
                    virtualBallast = vb;
                    actuators.Add(vb);
                }
            }
            actuators.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            if (sources.Count == 0)
            {
                IList<IDepthCommandSourceFactory> factories = SubTrimRegistry.Factories;
                for (int i = 0; i < factories.Count; i++)
                {
                    IDepthCommandSource src = factories[i].Create();
                    if (src != null) sources.Add(src);
                }
            }
            source = null;
            for (int i = 0; i < sources.Count; i++)
            {
                IDepthCommandSource src = sources[i];
                bool bound;
                try { bound = src.Bind(vessel); }
                catch (Exception e)
                {
                    Debug.LogWarning("[SubTrim] Depth source " + src.Name + " failed to bind: " + e.Message);
                    bound = false;
                }
                if (bound && (source == null || (!source.IsSubmarine && src.IsSubmarine))) source = src;
            }
            sourceWasActive = source != null && source.Present && source.Active;

            hullBreach = HullBreachLink.Find(vessel);

            if (master == null || !panels.Contains(master))
            {
                master = null;
                for (int i = 0; i < panels.Count && master == null; i++) if (panels[i].isSubmarine) master = panels[i];
                if (master == null && panels.Count > 0) master = panels[0];
            }
            if (master != null) SyncPanels(master);

            if (s.debug || cfgSubmarine || (source != null && source.IsSubmarine))
            {
                var sb = new StringBuilder();
                sb.Append("[SubTrim] ").Append(vessel.vesselName).Append(": ");
                AppendActuatorSummary(sb);
                sb.Append(", AI ").Append(source != null ? source.Name + (source.IsSubmarine ? " (Submarine)" : " (not Submarine)") : "none");
                sb.Append(", cfg sub ").Append(cfgSubmarine);
                if (!double.IsInfinity(crushDepth)) sb.Append(", crush ").Append(crushDepth.ToString("F0")).Append(" m");
                if (hullBreach != null) sb.Append(", HullBreach");
                if (diveComputers.Count > 0) sb.Append(", WBI dive computers ").Append(diveComputers.Count);
                Debug.Log(sb.ToString());
            }

            if (wasControlling && actuators.Count > 0) StartControl();
        }

        void SyncPanels(ModuleSubTrim from)
        {
            for (int i = 0; i < panels.Count; i++)
            {
                ModuleSubTrim p = panels[i];
                if (p == from) continue;
                p.mode = from.mode;
                p.manualDepth = from.manualDepth;
                p.targetDepth = from.targetDepth;
            }
        }

        // ---------------------------------------------------------------- panel interface

        /// <summary>A panel's setting changed: it becomes the master and the other panels follow it.</summary>
        public void PanelChanged(ModuleSubTrim panel)
        {
            if (!panels.Contains(panel)) dirty = true;
            master = panel;
            SyncPanels(panel);
            if (vessel != null) CaptureHold(SubTrimSettings.Instance);
        }

        public void ToggleEmergency()
        {
            emergencyBlow = !emergencyBlow;
            if (!emergencyBlow && vessel != null) CaptureHold(SubTrimSettings.Instance);
        }

        public void AppendActuatorSummary(StringBuilder sb)
        {
            if (actuators.Count == 0)
            {
                sb.Append("no ballast");
                return;
            }
            int kp = 0, wbi = 0, res = 0;
            for (int i = 0; i < actuators.Count; i++)
            {
                BallastActuator a = actuators[i];
                if (a is KPBallastActuator) kp++;
                else if (a is WBIBallastActuator) wbi++;
                else if (a is ResourceBallastActuator) res++;
            }
            bool first = true;
            if (adjuster != null) { sb.Append("BuoyancyAdjuster"); first = false; }
            if (kp > 0) { sb.Append(first ? "" : " + ").Append("KP Naval x").Append(kp); first = false; }
            if (wbi > 0) { sb.Append(first ? "" : " + ").Append("WBI tank x").Append(wbi); first = false; }
            if (res > 0) { sb.Append(first ? "" : " + ").Append("ballast tank x").Append(res); first = false; }
            if (virtualBallast != null) sb.Append(first ? "" : " + ").Append("virtual (").Append(virtualBallast.PartCount).Append(" parts)");
        }

        /// <summary>Fill the panel's status strings. Called by the panel only while its PAW is open (allocates).</summary>
        public void FillStatus(ModuleSubTrim panel)
        {
            var sb = new StringBuilder(64);
            switch (State)
            {
                case TrimState.Holding:
                    sb.Append("Holding ").Append(TargetAltitude.ToString("F0")).Append(" m (").Append(Reason).Append(')');
                    break;
                case TrimState.Surfacing:
                    sb.Append("Surfaced (").Append(Reason).Append(')');
                    break;
                case TrimState.Emergency:
                    sb.Append("BLOWING BALLAST (").Append(Reason).Append(')');
                    break;
                case TrimState.NoBallast:
                    sb.Append("Idle: no ballast found");
                    break;
                default:
                    sb.Append(State == TrimState.Off ? "Off" : "Standby").Append(" (").Append(Reason).Append(')');
                    break;
            }
            panel.status = sb.ToString();

            sb.Length = 0;
            if (State == TrimState.Holding || State == TrimState.Surfacing || State == TrimState.Emergency)
            {
                sb.Append("net ").Append((NetFraction * 100).ToString("+0.0;-0.0")).Append("%");
                if (!double.IsNaN(CommandFraction)) sb.Append(" (want ").Append((CommandFraction * 100).ToString("+0.0;-0.0")).Append("%)");
                sb.Append(", alt ").Append(vessel.altitude.ToString("F0")).Append(" m, ").Append(vessel.verticalSpeed.ToString("+0.0;-0.0")).Append(" m/s");
            }
            else sb.Append("-");
            panel.trim = sb.ToString();

            sb.Length = 0;
            AppendActuatorSummary(sb);
            double flooded = 0, span = 0;
            for (int i = 0; i < actuators.Count; i++)
            {
                double w = actuators[i].LiftMax - actuators[i].LiftMin;
                flooded += actuators[i].FloodedFraction * w;
                span += w;
            }
            if (span > 0 && controlling) sb.Append(", ").Append((flooded / span * 100).ToString("F0")).Append("% flooded");
            if (!double.IsInfinity(crushDepth)) sb.Append(", crush ").Append(crushDepth.ToString("F0")).Append(" m");
            panel.ballast = sb.ToString();
        }
    }
}
