using DependencyModules.Runtime.Attributes;
using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.TestFramework;

[DependencyModule(OnlyRealm = true)]
public partial class LifetimeModule { }

[ScopedService(Realm = typeof(LifetimeModule))]
public class TrackedService : IDisposable {

    private static int _next;

    public static readonly List<int> Disposed = [];

    public TrackedService() {
        Id = Interlocked.Increment(ref _next);
    }

    public int Id {
        get;
    }

    public void Dispose() {
        lock (Disposed) {
            Disposed.Add(Id);
        }
    }
}

/// <summary>
/// The container is torn down when its test has run, not when the run ends.
/// </summary>
/// <remarks>
/// Until 2026-09-05 <c>ModuleTestCase</c> handed the provider to the case's <c>DisposalTracker</c>,
/// and xUnit disposes a test case only after every case in the assembly has run. Every container
/// a run built, and every singleton in it, lived until the run ended; a probe that handed three
/// providers to an assembly fixture found all three alive at its disposal. The NUnit integration
/// has always released the container in a <c>finally</c> around the test, and
/// <c>IterationLifetimeTests</c> in the NUnit project holds it to that.
/// <para>
/// Two tests in one class, which xUnit runs one after the other in an order it does not promise:
/// whichever runs second sees the first's service, and asserts that its container has already
/// been disposed. Per case rather than per row: the rows of a data-driven test share the case and
/// are released together when the last row has run.
/// </para>
/// </remarks>
public class ContainerLifetimeTests {

    private static readonly object Sync = new();

    private static readonly List<int> Seen = [];

    [ModuleTest(typeof(LifetimeModule))]
    public void TheContainerOfATestThatHasRunIsDisposed(TrackedService service) => AssertEarlierDisposed(service);

    [ModuleTest(typeof(LifetimeModule))]
    public void WhicheverOfTheTwoRanFirst(TrackedService service) => AssertEarlierDisposed(service);

    private static void AssertEarlierDisposed(TrackedService current) {
        lock (Sync) {
            foreach (var earlier in Seen) {
                Assert.Contains(earlier, TrackedService.Disposed);
            }

            Assert.DoesNotContain(current.Id, TrackedService.Disposed);

            Seen.Add(current.Id);
        }
    }
}
