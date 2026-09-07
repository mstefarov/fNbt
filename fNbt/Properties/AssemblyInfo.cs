using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

[assembly: InternalsVisibleTo("fNbt.Test")]

// General Information about an assembly is controlled through the following 
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.

[assembly: AssemblyTitle("fNbt")]
[assembly: AssemblyDescription("A library for working with NBT files and streams.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("github.com/mstefarov/fNbt")]
[assembly: AssemblyProduct("fNbt")]
[assembly: AssemblyCopyright("2012-2026 Matvei Stefarov")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible 
// to COM components.  If you need to access a type in this assembly from 
// COM, set the ComVisible attribute to true on that type.

[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM

[assembly: Guid("9253db1f-f1d4-45aa-a277-4f3ba635d651")]

// AssemblyVersion moves only on major versions, so Framework apps built against any 2.x
// bind to later 2.x without redirects. AssemblyFileVersion tracks every release.
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.1.0.0")]

// Potentially speed up resource probes

[assembly: NeutralResourcesLanguage("en-US")]
