# NUnit

`DependencyModules.NUnit` is the NUnit integration. Read [Testing modules](/guide/testing) first —
this page covers only what is specific to NUnit.

```shell
dotnet add package DependencyModules.NUnit
```

```csharp
using DependencyModules.NUnit.Attributes;

public class WeatherTests {
    [ModuleTest]
    [ApplicationModule]
    public void GetForecast(Weather weather) {
        var forecast = weather.GetWeatherForecast().ToArray();

        Assert.That(forecast, Has.Length.EqualTo(5));
    }
}
```

`[ModuleTest]` replaces `[Test]`. `[TestFixture]` on the class is optional — a module test implies a
fixture the same way `[Test]` does.

Everything shared applies unchanged: assembly-level module attributes, `[Mock]`, `[InjectValues]`,
`[TestExport]`, keyed services, and all three [mocking packages](/guide/testing-mocking). Those live
in `DependencyModules.Testing` and name no test framework, so they are the same types either
integration hands you — not copies.

## A container per iteration

Every iteration of a test gets its own container, torn down when that iteration ends. That includes
each `[Repeat]` pass and each `[Retry]` attempt, not just each test case:

```csharp
[ModuleTest]
[ApplicationModule]
[Repeat(3)]
public void EachPassStartsClean(ICallCounter counter) {
    counter.Record();

    Assert.That(counter.Count, Is.EqualTo(1));   // never 2, never 3
}
```

The container's lifetime brackets the whole iteration, so `[SetUp]` and `[TearDown]` both run while
it is alive:

```
container built → [SetUp] → test method → [TearDown] → container disposed
```

That ordering is worth knowing if a `[SetUp]` method needs a service. It cannot take one as a
parameter — NUnit calls it, not this package — but it can read one from `ITestCaseInfo`, or the test
method can do the work instead.

## Data-driven tests

Use `[ModuleTestCase]` rather than NUnit's `[TestCase]`. Row arguments come first, injected ones
after:

```csharp
[ModuleTest]
[ApplicationModule]
[ModuleTestCase("one")]
[ModuleTestCase("two")]
public void MultipleRows(string value, ITemperatureProvider provider) {
    Assert.That(value, Is.Not.Null);        // from [ModuleTestCase]
    Assert.That(provider, Is.Not.Null);     // from the container
}
```

A row may supply fewer arguments than the method takes — that is the point of it — but not more.

::: warning `[TestCase]` will not work here
NUnit's own `[TestCase]` requires a row to supply an argument for *every* parameter, and enforces
that when the test case is built, before this package sees it. A method whose trailing parameters
come from the container fails that check with
`Method requires 2 arguments but TestCaseAttribute only supplied 1`. `[TestCase]` also builds its own
test cases, so combining the two produces one case per row *plus* one more.

`[ModuleTestCase]` is the same idea without the all-or-nothing rule.
:::

Each row is a separate test case, so each gets its own container.

To supply rows from somewhere other than an attribute literal, implement `IModuleTestDataAttribute`:

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class CsvRowsAttribute(string path) : Attribute, IModuleTestDataAttribute {
    public IEnumerable<object?[]> GetRows(MethodInfo method) =>
        File.ReadLines(path).Select(line => line.Split(',').Cast<object?>().ToArray());
}
```

## Differences from the xUnit integration

| | [xUnit](/guide/testing-xunit) | NUnit |
|---|---|---|
| Replaces | `[Fact]` and `[Theory]` | `[Test]` |
| Data rows | `[InlineData]`, `[MemberData]`, any `IDataAttribute` | `[ModuleTestCase]` |
| Class attribute | none needed | `[TestFixture]` optional |
| Fixture instance | one per test | one per fixture, per NUnit's own model |
| Test case metadata | `ITestCaseInfo` exposing `IXunitTestMethod` | `ITestCaseInfo` exposing `TestMethod` |

The fixture row is NUnit's behaviour, not this package's: NUnit constructs the fixture once and
reuses it, so fixture *fields* are shared across tests even though containers are not. Keep per-test
state in the test method, or in a service resolved from the container.

## Skipping, timeouts and categories

NUnit's own attributes work as they always do — `[Ignore]`, `[Explicit]`, `[Category]`,
`[Timeout]`, `[Order]`, `[Parallelizable]`. `[ModuleTest]` only supplies arguments and the container;
it does not replace the rest of NUnit.

## Next

- [Mocking frameworks](/guide/testing-mocking) — `[Mock]` and the three libraries behind it
- [xUnit](/guide/testing-xunit) — the same integration for xUnit
- [Testing registrations](/guide/testing-registrations) — asserting on what a module registered
