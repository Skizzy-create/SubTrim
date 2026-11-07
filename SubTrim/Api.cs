using System.Collections.Generic;

namespace SubTrim
{
    /// <summary>
    /// Something that commands a submarine's depth, e.g. an AI. One instance is bound to one vessel.
    /// Optional plugins (SubTrim.BDArmory.dll) register a factory at startup, so the core plugin
    /// never references their assemblies.
    /// </summary>
    public interface IDepthCommandSource
    {
        /// <summary>Short name for the PAW, e.g. "BDA".</summary>
        string Name { get; }

        /// <summary>Find the controller on this vessel. Called on structural changes only. Returns true if found.</summary>
        bool Bind(Vessel vessel);

        /// <summary>The bound controller still exists (Unity object alive, same vessel).</summary>
        bool Present { get; }

        /// <summary>The controller is configured as a submarine (e.g. BDA SurfaceAI "Submarine" mode).</summary>
        bool IsSubmarine { get; }

        /// <summary>The controller is currently flying the vessel.</summary>
        bool Active { get; }

        /// <summary>The controller's configured cruise/combat altitude (negative below sea level).</summary>
        float CruiseAltitude { get; }

        /// <summary>The altitude the controller is steering to right now (negative below sea level).
        /// Must not allocate: it is called at the control rate.</summary>
        float CommandedAltitude();
    }

    public interface IDepthCommandSourceFactory
    {
        IDepthCommandSource Create();
    }

    public static class SubTrimRegistry
    {
        static readonly List<IDepthCommandSourceFactory> factories = new List<IDepthCommandSourceFactory>(2);

        public static IList<IDepthCommandSourceFactory> Factories => factories;

        public static void Register(IDepthCommandSourceFactory factory)
        {
            if (factory == null || factories.Contains(factory)) return;
            factories.Add(factory);
            UnityEngine.Debug.Log("[SubTrim] Registered depth command source " + factory.GetType().FullName);
        }
    }
}
