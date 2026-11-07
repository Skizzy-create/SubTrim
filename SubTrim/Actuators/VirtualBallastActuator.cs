using System.Collections.Generic;

namespace SubTrim
{
    /// <summary>
    /// (e) Fallback "virtual ballast" when the vessel has no real ballast system: scales part.buoyancy of every hull part
    /// (stock buoyancy > 0, with a rigidbody) by one common factor between virtualMinScale and virtualMaxScale of its stock
    /// value. A common factor keeps the centre of buoyancy where it is, so it adds no pitch moment.
    /// Rate limited (virtualScaleRate of stock buoyancy per second) and adding lift costs ecPerTonne EC per tonne
    /// (except in an emergency blow). buoyancyUseCubeNamed is not touched. Stock buoyancy is restored on release.
    /// </summary>
    public sealed class VirtualBallastActuator : BallastActuator
    {
        readonly List<Part> parts = new List<Part>(16);
        readonly List<float> stock = new List<float>(16);
        double capacity; // sum of k_i x stock_i (t at scale 1)
        public double Scale = 1.0;
        bool applied;

        public override string Kind => "Virtual";
        public override int Priority => 4;
        public int PartCount => parts.Count;

        public void Add(Part p)
        {
            parts.Add(p);
            stock.Add(p.partInfo != null && p.partInfo.partPrefab != null ? p.partInfo.partPrefab.buoyancy : p.buoyancy);
        }

        public override bool Refresh(double rhoS, double vesselMass)
        {
            capacity = 0;
            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                if (!Alive(p) || p.partBuoyancy == null) continue;
                capacity += p.partBuoyancy.displacement * rhoS * stock[i];
            }
            if (capacity <= 1e-9) return false;
            SubTrimSettings s = SubTrimSettings.Instance;
            LiftNow = Scale * capacity;
            LiftMin = s.virtualMinScale * capacity;
            LiftMax = s.virtualMaxScale * capacity;
            return true;
        }

        public override void Drive(ref DriveContext ctx)
        {
            SubTrimSettings s = SubTrimSettings.Instance;
            double want = Clamp(Target / capacity, s.virtualMinScale, s.virtualMaxScale);
            double step = s.virtualScaleRate * ctx.RateMultiplier * ctx.Dt;
            if (want > Scale)
            {
                double add = want - Scale;
                if (add > step) add = step;
                if (!ctx.Emergency) add = ctx.Host.PayForLift(add * capacity) / capacity;
                Scale += add;
            }
            else
            {
                Scale = Approach(Scale, want, 0, step);
            }
            Apply();
        }

        public void ApplyNow() => Apply();

        void Apply()
        {
            applied = true;
            float sc = (float)Scale;
            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                if (Alive(p)) p.buoyancy = stock[i] * sc;
            }
        }

        public override void Release()
        {
            if (!applied) return;
            applied = false;
            Scale = 1.0;
            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                if (Alive(p)) p.buoyancy = stock[i];
            }
        }
    }
}
