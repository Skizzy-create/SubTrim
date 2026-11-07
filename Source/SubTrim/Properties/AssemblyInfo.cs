using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("SubTrim")]
[assembly: AssemblyDescription("Automatic neutral-buoyancy trim and depth hold for KSP submarines")]
[assembly: AssemblyProduct("SubTrim")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Skizzy-create, MIT License")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

// SubTrim.BDArmory.dll declares KSPAssemblyDependency("SubTrim", 1, 0) so KSP loads this first.
[assembly: KSPAssembly("SubTrim", 1, 0)]
