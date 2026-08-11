using DependencyModules.NUnit.Attributes;
using DependencyModules.Runtime.Attributes;
using NUnit.Framework;

namespace SutProject.NUnitTests;

[DependencyModule(OnlyRealm = true)]
public partial class LifetimeModule { }

/// <summary>
/// Counts its own construction and disposal, so "a container per iteration" can be asserted rather
/// than inferred.
/// </summary>
[ScopedService(Realm = typeof(LifetimeModule))]
public class TrackedService : IDisposable {

    private static int _next;

    public static readonly List<int> Constructed = [];

    public static readonly List<int> Disposed = [];

    public TrackedService() {
        Id = Interlocked.Increment(ref _next);

        Constructed.Add(Id);
    }

    public int Id {
        get;
    }

    public void Dispose() => Disposed.Add(Id);
}

/// <summary>
/// The invariant the whole integration exists to hold: one container per test iteration, torn down
/// when that iteration ends.
/// </summary>
/// <remarks>
/// <c>[Repeat]</c> and <c>[Retry]</c> re-run a single test case, so a container built per test
/// <em>case</em> would be shared across every repetition. The fixtures below are ordered by name
/// because the report at the end reads what they recorded; NUnit runs fixtures within an assembly
/// in alphabetical order.
/// </remarks>
public class ARepeatedModuleTests {

    public static readonly List<string> Log = [];

    public static readonly List<int> ServiceIds = [];

    [SetUp]
    public void SetUp() => Log.Add("setup");

    [TearDown]
    public void TearDown() => Log.Add("teardown");

    [ModuleTest(typeof(LifetimeModule))]
    [Repeat(3)]
    public void EachRepetitionGetsItsOwnContainer(TrackedService trackedService) {
        Log.Add($"test:{trackedService.Id}");

        ServiceIds.Add(trackedService.Id);
    }
}

public class BRetriedModuleTests {

    private static int _attempts;

    public static readonly List<int> ServiceIds = [];

    [ModuleTest(typeof(LifetimeModule))]
    [Retry(3)]
    public void EachRetryAttemptGetsItsOwnContainer(TrackedService trackedService) {
        ServiceIds.Add(trackedService.Id);

        _attempts++;

        Assert.That(_attempts, Is.EqualTo(3), "fails the first two attempts on purpose, passes the third");
    }
}

public class CLifetimeReport {

    /// <summary>
    /// The container has to outlive setup and teardown, not sit between them. Wrapping only the test
    /// method would order this setup, open, test, close, teardown — leaving <c>[SetUp]</c> running
    /// before the container exists and <c>[TearDown]</c> after it is gone.
    /// </summary>
    [Test]
    public void SetUpAndTearDownRunInsideTheContainersLifetime() {
        Assert.That(ARepeatedModuleTests.Log, Has.Count.EqualTo(9), "three iterations of setup, test, teardown");

        for (var i = 0; i < 3; i++) {
            Assert.That(ARepeatedModuleTests.Log[i * 3], Is.EqualTo("setup"));
            Assert.That(ARepeatedModuleTests.Log[i * 3 + 1], Does.StartWith("test:"));
            Assert.That(ARepeatedModuleTests.Log[i * 3 + 2], Is.EqualTo("teardown"));
        }
    }

    [Test]
    public void NoServiceInstanceIsSharedBetweenIterations() {
        var repeated = ARepeatedModuleTests.ServiceIds;
        var retried = BRetriedModuleTests.ServiceIds;

        Assert.That(repeated, Has.Count.EqualTo(3));
        Assert.That(retried, Has.Count.EqualTo(3));

        Assert.That(repeated.Concat(retried).Distinct().Count(), Is.EqualTo(6),
            "three repetitions and three retry attempts, six containers, six instances");
    }

    [Test]
    public void EveryIterationsServicesWereDisposedWithItsContainer() {
        var iterationIds = ARepeatedModuleTests.ServiceIds.Concat(BRetriedModuleTests.ServiceIds);

        Assert.That(TrackedService.Disposed, Is.SupersetOf(iterationIds),
            "the container is torn down at the end of the iteration, not left to the fixture");
    }
}
