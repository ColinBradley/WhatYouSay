using Xunit.Sdk;
using Xunit.v3;

// Every test runs against its own private in-memory database, so nothing is shared and
// there is no reason to serialise anything. ParallelMode.All (xUnit v3 4.0+) parallelises
// individual tests rather than only test collections, so a large test class no longer
// silently serialises its own contents.
[assembly: Parallelization(Mode = ParallelMode.All)]
