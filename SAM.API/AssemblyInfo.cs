using System.Runtime.CompilerServices;

// Lets SAM.Tests exercise internal members (NativeStrings' pointer readers among them)
// directly, rather than reflecting into them or widening them to public just for testing.
[assembly: InternalsVisibleTo("SAM.Tests")]
