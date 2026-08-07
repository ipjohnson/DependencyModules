using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The generator has to turn arbitrary declared types back into valid C#. These cover the type
/// shapes most likely to be written wrong: nullables, arrays, nested generics, and nested classes.
/// Each case asserts the output still compiles, which is what a mis-rendered type breaks.
/// </summary>
public class TypeShapeTests {

    [Fact]
    public void NullableConstructorParameter_Compiles() {
        Generate(
            """
            public interface IThing;

            [SingletonService]
            public class Thing : IThing {
                public Thing(string? optional) { }
            }
            """);
    }

    [Fact]
    public void NullableValueTypeParameter_Compiles() {
        Generate(
            """
            public interface IThing;

            [SingletonService]
            public class Thing : IThing {
                public Thing(int? count) { }
            }
            """);
    }

    [Fact]
    public void ArrayParameter_Compiles() {
        Generate(
            """
            public interface IDependency;
            public interface IThing;

            [SingletonService]
            public class Dependency : IDependency;

            [SingletonService]
            public class Thing : IThing {
                public Thing(IDependency[] dependencies) { }
            }
            """);
    }

    [Fact]
    public void NestedGenericParameter_Compiles() {
        Generate(
            """
            using System.Collections.Generic;

            public interface IDependency;
            public interface IThing;

            [SingletonService]
            public class Dependency : IDependency;

            [SingletonService]
            public class Thing : IThing {
                public Thing(IEnumerable<IDependency> dependencies) { }
            }
            """);
    }

    [Fact]
    public void DeeplyNestedGenericParameter_Compiles() {
        Generate(
            """
            using System.Collections.Generic;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing {
                public Thing(IDictionary<string, IReadOnlyList<int>> map) { }
            }
            """);
    }

    [Fact]
    public void GenericServiceWithConstraints_Compiles() {
        var generated = Generate(
            """
            public interface IGeneric<T> where T : class;

            [SingletonService]
            public class Generic<T> : IGeneric<T> where T : class;
            """);

        Assert.Contains("IGeneric<>", generated);
    }

    [Fact]
    public void GenericServiceWithSeveralParameters_Compiles() {
        var generated = Generate(
            """
            public interface IPair<TFirst, TSecond>;

            [SingletonService]
            public class Pair<TFirst, TSecond> : IPair<TFirst, TSecond>;
            """);

        Assert.Contains("IPair<,>", generated);
    }

    [Fact]
    public void ClosedGenericService_RegistersTheClosedType() {
        var generated = Generate(
            """
            public interface IGeneric<T>;

            [SingletonService]
            public class StringGeneric : IGeneric<string>;
            """);

        Assert.Contains("string", generated);
    }

    /// <summary>
    /// Regression test: a nested service used to be emitted as TestNamespace.Inner, dropping its
    /// containing type, so the generated registration failed to compile with CS0234.
    /// </summary>
    [Fact]
    public void NestedClassService_IsQualifiedByItsContainingType() {
        var generated = Generate(
            """
            public interface IThing;

            public static class Outer {
                [SingletonService]
                public class Inner : IThing;
            }
            """);

        Assert.Contains("global::TestNamespace.Outer.Inner", generated);
    }

    [Fact]
    public void DeeplyNestedClassService_IsQualifiedByEveryContainingType() {
        var generated = Generate(
            """
            public interface IThing;

            public static class Outer {
                public static class Middle {
                    [SingletonService]
                    public class Inner : IThing;
                }
            }
            """);

        Assert.Contains("global::TestNamespace.Outer.Middle.Inner", generated);
    }

    [Fact]
    public void NestedGenericService_IsQualifiedByItsContainingType() {
        var generated = Generate(
            """
            public interface IGeneric<T>;

            public static class Outer {
                [SingletonService]
                public class Inner<T> : IGeneric<T>;
            }
            """);

        Assert.Contains("global::TestNamespace.Outer.Inner<>", generated);
    }

    [Fact]
    public void ServiceWithAnEnumKey_Compiles() {
        var generated = Generate(
            """
            public interface IThing;

            public enum Flavour { Sweet, Savoury }

            [SingletonService(Key = Flavour.Sweet)]
            public class Thing : IThing;
            """);

        Assert.Contains("AddKeyedSingleton", generated);
    }

    [Fact]
    public void ServiceWithAnIntegerKey_Compiles() {
        var generated = Generate(
            """
            public interface IThing;

            [SingletonService(Key = 42)]
            public class Thing : IThing;
            """);

        Assert.Contains("42", generated);
    }

    [Fact]
    public void ServiceKeyedByAConstant_Compiles() {
        var generated = Generate(
            """
            public interface IThing;

            public static class Keys {
                public const string Primary = "primary";
            }

            [SingletonService(Key = Keys.Primary)]
            public class Thing : IThing;
            """);

        Assert.Contains("AddKeyedSingleton", generated);
    }

    [Fact]
    public void RecordService_Compiles() {
        var generated = Generate(
            """
            public interface IThing;

            [SingletonService]
            public record ThingRecord : IThing;
            """);

        Assert.Contains("ThingRecord", generated);
    }

    [Fact]
    public void ServiceWithSeveralConstructors_Compiles() {
        Generate(
            """
            public interface IDependency;
            public interface IThing;

            [SingletonService]
            public class Dependency : IDependency;

            [SingletonService]
            public class Thing : IThing {
                public Thing() { }
                public Thing(IDependency dependency) { }
            }
            """);
    }

    [Fact]
    public void AbstractBaseAndConcreteService_Compiles() {
        var generated = Generate(
            """
            public interface IThing;

            public abstract class ThingBase : IThing;

            [SingletonService]
            public class Thing : ThingBase;
            """);

        Assert.Contains("Thing", generated);
    }

    [Fact]
    public void ModuleWithNullableProperty_Compiles() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public partial class NullablePropertyModule {
                public string? Optional { get; set; }
            }
            """);

        result.AssertNoErrors();
        Assert.Contains("Optional", result.SourceContaining(".Module.g.cs"));
    }

    [Fact]
    public void ModuleWithStaticProperty_LeavesItOffTheAttribute() {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public partial class StaticPropertyModule {
                public static string Shared { get; set; } = "";
                public string Instance { get; set; } = "";
            }
            """);

        result.AssertNoErrors();
        var generated = result.SourceContaining(".Module.g.cs");

        Assert.Contains("Instance", generated);
        Assert.DoesNotContain("Shared", generated);
    }

    /// <summary>
    /// Compiles the supplied declarations alongside a module and returns the registration file.
    /// </summary>
    private static string Generate(string body) {
        var result = GeneratorTestHarness.Run(
            $$"""
              using DependencyModules.Runtime.Attributes;

              namespace TestNamespace;

              {{body}}

              [DependencyModule]
              public partial class TestModule;
              """);

        result.AssertNoErrors();

        return result.SourceContaining("Dependencies");
    }

}
