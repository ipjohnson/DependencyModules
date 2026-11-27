using DependencyModules.Tests.Infrastructure;

namespace DependencyModules.Specs.GeneratorTests;

/// <summary>
/// The generator has to turn arbitrary declared types back into valid C#. These cover the type
/// shapes most likely to be written wrong: nullables, arrays, nested generics, and nested classes.
/// Each case asserts the output still compiles, which is what a mis-rendered type breaks.
/// </summary>
public partial class TypeShapeTests
{
    [VerifyTest]
    public void NullableConstructorParameter_Compiles()
    {
        Generate(
            """
            public interface IThing;

            [SingletonService]
            public class Thing : IThing {
                public Thing(string? optional) { }
            }
            """
        );
    }

    [VerifyTest]
    public void NullableValueTypeParameter_Compiles()
    {
        Generate(
            """
            public interface IThing;

            [SingletonService]
            public class Thing : IThing {
                public Thing(int? count) { }
            }
            """
        );
    }

    [VerifyTest]
    public void ArrayParameter_Compiles()
    {
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
            """
        );
    }

    [VerifyTest]
    public void NestedGenericParameter_Compiles()
    {
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
            """
        );
    }

    [VerifyTest]
    public void DeeplyNestedGenericParameter_Compiles()
    {
        Generate(
            """
            using System.Collections.Generic;

            public interface IThing;

            [SingletonService]
            public class Thing : IThing {
                public Thing(IDictionary<string, IReadOnlyList<int>> map) { }
            }
            """
        );
    }

    [VerifyTest]
    public void GenericServiceWithConstraints_Compiles()
    {
        var generated = Generate(
            """
            public interface IGeneric<T> where T : class;

            [SingletonService]
            public class Generic<T> : IGeneric<T> where T : class;
            """
        );

        Verify(generated.Contains("IGeneric<>"));
    }

    [VerifyTest]
    public void GenericServiceWithSeveralParameters_Compiles()
    {
        var generated = Generate(
            """
            public interface IPair<TFirst, TSecond>;

            [SingletonService]
            public class Pair<TFirst, TSecond> : IPair<TFirst, TSecond>;
            """
        );

        Verify(generated.Contains("IPair<,>"));
    }

    [VerifyTest]
    public void ClosedGenericService_RegistersTheClosedType()
    {
        var generated = Generate(
            """
            public interface IGeneric<T>;

            [SingletonService]
            public class StringGeneric : IGeneric<string>;
            """
        );

        Verify(generated.Contains("string"));
    }

    /// <summary>
    /// Regression test: a nested service used to be emitted as TestNamespace.Inner, dropping its
    /// containing type, so the generated registration failed to compile with CS0234.
    /// </summary>
    [VerifyTest]
    public void NestedClassService_IsQualifiedByItsContainingType()
    {
        var generated = Generate(
            """
            public interface IThing;

            public static class Outer {
                [SingletonService]
                public class Inner : IThing;
            }
            """
        );

        Verify(generated.Contains("global::TestNamespace.Outer.Inner"));
    }

    [VerifyTest]
    public void DeeplyNestedClassService_IsQualifiedByEveryContainingType()
    {
        var generated = Generate(
            """
            public interface IThing;

            public static class Outer {
                public static class Middle {
                    [SingletonService]
                    public class Inner : IThing;
                }
            }
            """
        );

        Verify(generated.Contains("global::TestNamespace.Outer.Middle.Inner"));
    }

    [VerifyTest]
    public void NestedGenericService_IsQualifiedByItsContainingType()
    {
        var generated = Generate(
            """
            public interface IGeneric<T>;

            public static class Outer {
                [SingletonService]
                public class Inner<T> : IGeneric<T>;
            }
            """
        );

        Verify(generated.Contains("global::TestNamespace.Outer.Inner<>"));
    }

    [VerifyTest]
    public void ServiceWithAnEnumKey_Compiles()
    {
        var generated = Generate(
            """
            public interface IThing;

            public enum Flavour { Sweet, Savoury }

            [SingletonService(Key = Flavour.Sweet)]
            public class Thing : IThing;
            """
        );

        Verify(generated.Contains("AddKeyedSingleton"));
    }

    [VerifyTest]
    public void ServiceWithAnIntegerKey_Compiles()
    {
        var generated = Generate(
            """
            public interface IThing;

            [SingletonService(Key = 42)]
            public class Thing : IThing;
            """
        );

        Verify(generated.Contains("42"));
    }

    [VerifyTest]
    public void ServiceKeyedByAConstant_Compiles()
    {
        var generated = Generate(
            """
            public interface IThing;

            public static class Keys {
                public const string Primary = "primary";
            }

            [SingletonService(Key = Keys.Primary)]
            public class Thing : IThing;
            """
        );

        Verify(generated.Contains("AddKeyedSingleton"));
    }

    [VerifyTest]
    public void RecordService_Compiles()
    {
        var generated = Generate(
            """
            public interface IThing;

            [SingletonService]
            public record ThingRecord : IThing;
            """
        );

        Verify(generated.Contains("ThingRecord"));
    }

    [VerifyTest]
    public void ServiceWithSeveralConstructors_Compiles()
    {
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
            """
        );
    }

    [VerifyTest]
    public void AbstractBaseAndConcreteService_Compiles()
    {
        var generated = Generate(
            """
            public interface IThing;

            public abstract class ThingBase : IThing;

            [SingletonService]
            public class Thing : ThingBase;
            """
        );

        Verify(generated.Contains("Thing"));
    }

    [VerifyTest]
    public void ModuleWithNullableProperty_Compiles()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public partial class NullablePropertyModule {
                public string? Optional { get; set; }
            }
            """
        );

        result.AssertNoErrors();
        Verify(result.SourceContaining(".Module.g.cs").Contains("Optional"));
    }

    [VerifyTest]
    public void ModuleWithStaticProperty_LeavesItOffTheAttribute()
    {
        var result = GeneratorTestHarness.Run(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            [DependencyModule]
            public partial class StaticPropertyModule {
                public static string Shared { get; set; } = "";
                public string Instance { get; set; } = "";
            }
            """
        );

        result.AssertNoErrors();
        var generated = result.SourceContaining(".Module.g.cs");

        Verify(generated.Contains("Instance"));
        Verify(!generated.Contains("Shared"));
    }

    /// <summary>
    /// A nested type's constructor is not the outer type's.
    /// </summary>
    /// <remarks>
    /// GetConstructorInfo used to walk DescendantNodes, which reaches into nested declarations, so a
    /// service containing a nested class with a parameterised constructor was registered against
    /// that constructor's parameters. It now reads the type's own members.
    /// </remarks>
    [VerifyTest]
    public void ANestedTypesConstructorIsNotTheOuterTypes()
    {
        // With factory generation on, the constructor reaches the emitted code as a literal
        // new Outer(...) call. Without it the container picks the constructor at run time and the
        // mistake is invisible, which is why this test sets the property.
        var assembly = GeneratedAssembly.Create(
            """
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            public interface IOuter { }

            [SingletonService]
            public class Outer : IOuter {
                public class Nested {
                    public Nested(string required) { Required = required; }
                    public string Required { get; }
                }
            }

            [DependencyModule]
            public partial class TestModule;
            """,
            buildProperties: new Dictionary<string, string>
            {
                ["DependencyModules_GenerateFactories"] = "true",
            }
        );

        Verify(assembly.ResolveRequired("IOuter").GetType() == assembly.Type("Outer"));
    }

    /// <summary>
    /// Compiles the supplied declarations alongside a module and returns the registration file.
    /// </summary>
    private static string Generate(string body)
    {
        var result = GeneratorTestHarness.Run(
            $$"""
            using DependencyModules.Runtime.Attributes;

            namespace TestNamespace;

            {{body}}

            [DependencyModule]
            public partial class TestModule;
            """
        );

        result.AssertNoErrors();

        return result.SourceContaining("Dependencies");
    }
}
