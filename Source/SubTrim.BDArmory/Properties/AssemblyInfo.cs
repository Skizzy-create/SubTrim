using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("SubTrim.BDArmory")]
[assembly: AssemblyDescription("SubTrim bridge to the BDArmory Plus Surface AI (Submarine mode)")]
[assembly: AssemblyProduct("SubTrim")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Skizzy-create, MIT License")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

[assembly: KSPAssembly("SubTrim.BDArmory", 1, 0)]
// KSP skips this assembly (and only this one) when either dependency is missing.
// BDArmory Plus declares KSPAssembly("BDArmory", 1, 0).
[assembly: KSPAssemblyDependency("SubTrim", 1, 0)]
[assembly: KSPAssemblyDependency("BDArmory", 1, 0)]
