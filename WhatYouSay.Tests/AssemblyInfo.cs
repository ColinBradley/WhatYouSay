// Every test runs against its own private in-memory database, so nothing is shared and
// there is no reason to serialise anything. MethodLevel parallelises individual tests
// rather than only classes, so a large test class does not silently serialise itself.
[assembly: Parallelize(Workers = 0, Scope = ExecutionScope.MethodLevel)]
