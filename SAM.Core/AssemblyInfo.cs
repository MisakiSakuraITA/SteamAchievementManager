using System.Runtime.CompilerServices;

// Lets SAM.Tests exercise internal members (pending-state transitions, storage plumbing)
// directly, rather than reflecting into them or widening them to public just for testing.
[assembly: InternalsVisibleTo("SAM.Tests")]
