using System;
using System.Collections.Generic;
using SubTrim;

// 1-D vertical model of a submarine under SubTrim + the BDArmory Submarine AI.
//
//  * Ballast: the controller asks for a net buoyancy fraction u (of weight); the actuator moves the real ballast toward
//    it at a limited rate (fraction of weight per second), exactly like the allocator + rate-limited actuators.
//  * True net buoyancy = ballast + disturbance (hydrodynamic lift, model error), unknown to the controller.
//  * Vertical dynamics: dv/dt = g*n_true*(1/(1+addedMass)) - v/tau - c*v|v| (water drag); free surface at alt 0.
//  * The AI pitches toward its target exactly like BDModuleSurfaceAI 1.13 (MaxSlopeAngle 10 deg) and the forward speed adds
//    speed*sin(pitch) of vertical speed (pitch reached with a first-order lag).
// Checks: reaches the target, overshoot, settling, steady-state error, |net| stays within MaxTrim (+surface reserve).
static class Program
{
    sealed class Scenario
    {
        public string Name;
        public double Speed = 10;          // forward speed (m/s), 0 = no AI pitch help
        public double Tau = 20;            // vertical drag time constant (s)
        public double Quad = 0.02;         // quadratic drag (1/m)
        public double Rate = 0.005;        // ballast rate, fraction of weight per second
        public double Disturbance = 0;     // unknown lift offset (fraction of weight)
        public double StartAlt = 0;
        public double StartNet = 0.15;     // surfaced reserve
        public (double t, double target)[] Targets = { (0, -50) };
        public double Duration = 600;
    }

    const double G = 9.81;
    const double AddedMass = 0.3;

    static int Main()
    {
        var cfg = new DepthControllerConfig();
        var list = new List<Scenario>
        {
            new Scenario { Name = "Dive 0 -> -50 m, AI at 10 m/s (SSBN-like)" },
            new Scenario { Name = "Same, +0.8% hydrodynamic lift", Disturbance = 0.008 },
            new Scenario { Name = "Same, -0.8% (bow-heavy / model error)", Disturbance = -0.008 },
            new Scenario { Name = "No AI (manual), buoyancy only", Speed = 0 },
            new Scenario { Name = "Slow pumps 0.2%/s, low drag (big boat)", Rate = 0.002, Tau = 60 },
            new Scenario { Name = "Fast pumps 2%/s, high drag (small boat)", Rate = 0.02, Tau = 8 },
            new Scenario { Name = "AI -50 -> -10 (missile) -> -50", Targets = new[] { (0.0, -50.0), (300.0, -10.0), (600.0, -50.0) }, Duration = 900 },
            new Scenario { Name = "Deep hold -150 m, AI 15 m/s", Speed = 15, Targets = new[] { (0.0, -150.0) }, Duration = 900 },
            new Scenario { Name = "Start submerged at -80 neutral, hold -50", StartAlt = -80, StartNet = 0 },
        };

        bool allOk = true;
        Console.WriteLine($"Gains: depthGain {cfg.DepthGain}, Kp {cfg.Kp}, Ki {cfg.Ki}, maxTrim {cfg.MaxTrim}, band {cfg.CaptureBand} m");
        Console.WriteLine();
        foreach (Scenario sc in list)
            allOk &= Run(sc, cfg);
        Console.WriteLine(allOk ? "ALL CHECKS PASSED" : "SOME CHECKS FAILED");
        return allOk ? 0 : 1;
    }

    static bool Run(Scenario sc, DepthControllerConfig cfg)
    {
        var ctl = new DepthController();
        double dt = 0.02, tick = 0.1, acc = 0;
        double alt = sc.StartAlt, v = 0, ballast = sc.StartNet, cmd = ballast, pitch = 0;
        bool satUp = false, satDown = false;
        double maxNetHold = 0, overshoot = 0, lastTarget = double.NaN, settleTime = double.NaN, segStart = 0;
        int ti = 0;
        double target = sc.Targets[0].target;
        bool ok = true;
        var notes = new List<string>();

        for (double t = 0; t < sc.Duration; t += dt)
        {
            if (ti + 1 < sc.Targets.Length && t >= sc.Targets[ti + 1].t)
            {
                Finish(sc, ref ok, notes, lastTarget, alt, overshoot, settleTime, segStart);
                ti++;
                target = sc.Targets[ti].target;
                overshoot = 0;
                settleTime = double.NaN;
                segStart = t;
            }
            lastTarget = target;

            // Controller at 10 Hz.
            acc += dt;
            if (acc >= tick - 1e-9)
            {
                double u = ctl.Step(cfg, alt, target, v, acc, satUp, satDown);
                cmd = u;
                acc = 0;
                if (ctl.InBand) maxNetHold = Math.Max(maxNetHold, Math.Abs(ballast));
            }

            // Rate-limited ballast (range +-30% of weight).
            double step = sc.Rate * dt;
            if (cmd > ballast) ballast = Math.Min(cmd, ballast + step);
            else ballast = Math.Max(cmd, ballast - step);
            satUp = ballast >= 0.3; satDown = ballast <= -0.3;

            // AI pitch (BDModuleSurfaceAI 1.13 formula), lagged.
            double pitchWanted = 0;
            if (sc.Speed > 0)
            {
                double a = Math.Min(alt, -0.01);
                pitchWanted = a > target ? -10 * (1 - a / target) : 10 * (1 - target / a);
                pitchWanted = Math.Max(-10, Math.Min(10, pitchWanted));
            }
            pitch += (pitchWanted - pitch) * Math.Min(1, dt / 3.0);

            double nTrue = ballast + sc.Disturbance;
            // Partly surfaced: the hull emerges over the top 3 m and loses up to 20% of weight in buoyancy (floats).
            if (alt > -3) nTrue -= (alt + 3) / 3 * 0.2;
            double accel = G * nTrue / (1 + AddedMass) - v / sc.Tau - sc.Quad * v * Math.Abs(v);
            v += accel * dt;
            double vTotal = v + sc.Speed * Math.Sin(pitch * Math.PI / 180);
            alt += vTotal * dt;
            if (alt > 0) { alt = 0; if (v > 0) v = 0; }

            double err = alt - target;
            // Overshoot: beyond the target in the direction of travel.
            double from = ti == 0 ? sc.StartAlt : sc.Targets[ti - 1].target;
            double over = from > target ? target - alt : alt - target; // >0 means past target
            if (over > overshoot) overshoot = over;
            if (Math.Abs(err) <= 3 && double.IsNaN(settleTime)) settleTime = t - segStart;
            if (Math.Abs(err) > 3) settleTime = double.NaN;
        }
        Finish(sc, ref ok, notes, lastTarget, alt, overshoot, settleTime, segStart);

        if (maxNetHold > cfg.MaxTrim + 0.0011) { ok = false; notes.Add($"net {maxNetHold:P1} exceeded maxTrim while holding"); }
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {sc.Name}");
        foreach (string n in notes) Console.WriteLine("        " + n);
        Console.WriteLine($"        integral {ctl.Integral:P2}, ballast {ballast:P2}, max |net| in band {maxNetHold:P2}");
        return ok;
    }

    static void Finish(Scenario sc, ref bool ok, List<string> notes, double target, double alt, double overshoot, double settle, double segStart)
    {
        double err = alt - target;
        string s = $"target {target,6:F0} m: final {alt,7:F1} m (err {(Math.Abs(err) < 0.05 ? 0 : err):F1}), overshoot {Math.Max(0, overshoot):F1} m, settled(+-3 m) {(double.IsNaN(settle) ? "never" : settle.ToString("F0") + " s")}";
        notes.Add(s);
        if (Math.Abs(err) > 3) ok = false;
        if (overshoot > 12) ok = false;
    }
}
