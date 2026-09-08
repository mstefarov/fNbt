using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("fNbt.Test")]

[assembly: AssemblyTitle("fNbt")]
[assembly: AssemblyDescription("A library for working with NBT files and streams.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("github.com/mstefarov/fNbt")]
[assembly: AssemblyProduct("fNbt")]
[assembly: AssemblyCopyright("2012-2026 Matvei Stefarov")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

[assembly: Guid("9253db1f-f1d4-45aa-a277-4f3ba635d651")]

// AssemblyVersion moves only on major versions, so Framework apps built against any 2.x
// bind to later 2.x without redirects. AssemblyFileVersion tracks every release.
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

// Potentially speed up resource probes

[assembly: NeutralResourcesLanguage("en-US")]
