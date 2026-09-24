using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using DependencyModules.Runtime.Interfaces;
using DependencyModules.Tests.Infrastructure;
using Xunit;

namespace DependencyModules.Tests.GeneratorTests;

/// <summary>
/// The attributes that the generator puts on the code it writes. A module is a partial class, so
/// an attribute on a generated part of the class applies to the members the developer writes too.
/// </summary>
public class GeneratedMemberAttributeTests
{
    private const BindingFlags Declared =
        BindingFlags.DeclaredOnly
        | BindingFlags.Public
        | BindingFlags.NonPublic
        | BindingFlags.Instance
        | BindingFlags.Static;

    private const string Source = """
        using DependencyModules.Runtime.Attributes;
        using DependencyModules.Runtime.Interception;
        using DependencyModules.Runtime.Interfaces;
        using Microsoft.Extensions.DependencyInjection;

        namespace TestNamespace;

        public interface IClock;

        [SingletonService]
        public class Clock : IClock;

        public interface IGreeter
        {
            string Hello();
        }

        public class PassInterceptor : IInterceptor
        {
            public TResult Intercept<TResult>(InvocationContext<TResult> context) => context.Proceed();
        }

        [SingletonService]
        [Intercept(typeof(PassInterceptor))]
        public class Greeter : IGreeter
        {
            public string Hello() => "hello";
        }

        [DependencyModule]
        public partial class TestModule : IServiceCollectionConfiguration
        {
            public void ConfigureServices(IServiceCollection services) { }
        }
        """;

    /// <summary>
    /// Coverage tools skip a whole type that has <c>[ExcludeFromCodeCoverage]</c>, so the attribute
    /// on the module class hid <c>ConfigureServices</c>.
    /// </summary>
    [Fact]
    public void TheMembersTheDeveloperWrites_AreMeasured()
    {
        var module = GeneratedAssembly.Create(Source).Type("TestModule");

        Assert.False(Excluded(module));
        Assert.False(Excluded(module.GetMethod("ConfigureServices")!));
    }

    [Fact]
    public void TheGeneratedMembersOfTheModule_AreExcluded()
    {
        var assembly = GeneratedAssembly.Create(Source);
        var module = assembly.Type("TestModule");

        var generated = module
            .GetMethods(Declared)
            .Where(method => method.Name != "ConfigureServices")
            .Cast<MemberInfo>()
            .Append(module.TypeInitializer!)
            .Append(assembly.Type("TestModuleAttribute").GetMethod("GetModule")!)
            .ToArray();

        Assert.Contains(generated, member => member.Name.EndsWith("InternalGetModules"));
        Assert.All(
            generated,
            member => Assert.True(Excluded(member), $"{member.Name} is measured")
        );
    }

    [Fact]
    public void TheInterceptorWrapper_IsExcluded()
    {
        Assert.True(Excluded(GeneratedAssembly.Create(Source).Type("Greeter_Intercepted")));
    }

    [Fact]
    public void WithExcludeGeneratedCodeFromCoverageFalse_NothingIsExcluded()
    {
        var assembly = GeneratedAssembly.Create(
            Source,
            buildProperties: new Dictionary<string, string>
            {
                ["ExcludeGeneratedCodeFromCoverage"] = "false",
            }
        );

        foreach (var name in new[] { "TestModule", "TestModuleAttribute", "Greeter_Intercepted" })
        {
            var type = assembly.Type(name);

            Assert.False(Excluded(type), $"{name} is excluded");
            Assert.All(
                type.GetMembers(Declared),
                member => Assert.False(Excluded(member), $"{name}.{member.Name} is excluded")
            );
        }
    }

    /// <summary>
    /// IntelliSense hides a member of a referenced assembly only for
    /// <c>[EditorBrowsable(EditorBrowsableState.Never)]</c>. <c>[Browsable(false)]</c> is for the
    /// Properties window of a designer.
    /// </summary>
    [Fact]
    public void TheInternalMembersOfIDependencyModule_AreHiddenFromIntelliSense()
    {
        var members = typeof(IDependencyModule)
            .GetMethods()
            .Where(method => method.Name.StartsWith("Internal"))
            .ToArray();

        Assert.Equal(5, members.Length);
        Assert.All(members, member => Assert.True(Hidden(member), $"{member.Name} is shown"));
    }

    [Fact]
    public void TheGeneratedInternalMembers_AreHiddenFromIntelliSense()
    {
        var members = GeneratedAssembly
            .Create(Source)
            .Type("TestModule")
            .GetMethods(Declared)
            .Where(method => method.Name.Contains(".Internal"))
            .ToArray();

        Assert.NotEmpty(members);
        Assert.All(members, member => Assert.True(Hidden(member), $"{member.Name} is shown"));
    }

    private static bool Excluded(MemberInfo member) =>
        member.IsDefined(typeof(ExcludeFromCodeCoverageAttribute), false);

    private static bool Hidden(MemberInfo member) =>
        member.GetCustomAttribute<EditorBrowsableAttribute>()?.State == EditorBrowsableState.Never
        && !member.IsDefined(typeof(BrowsableAttribute), false);
}
