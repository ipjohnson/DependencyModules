using DependencyModules.Runtime.Interfaces;
using DependencyModules.Testing.Attributes.Interfaces;
using VerificationGlue.Tests;

namespace DependencyModules.VerificationGlue.Attributes;

/// <summary>
/// Builds the container of these tests from the module attributes on the assembly, the class and
/// the method, such as <c>[SutModule]</c>.
/// </summary>
/// <remarks>
/// <para>
/// It can go on the assembly, a class or a method. VerificationGlue.Tests uses the narrowest
/// <see cref="ContainerAttribute"/>.
/// </para>
/// <para>
/// <see cref="ContainerAttribute.Scope"/> sets which scope owns the container. With
/// <see cref="ContainerScope.Class"/> or <see cref="ContainerScope.Assembly"/>, each test gets a
/// container of its own, built with its own <c>[Mock]</c> parameters. Only the services that the
/// test marks <c>[Shared]</c>, and the exports that the scope marks
/// <c>[TestExport(Shared = true)]</c>, are the same instances in each test of the scope.
/// </para>
/// </remarks>
public class ModuleTestAttribute : ContainerAttribute<ModuleBootstrap>, IModuleTestAttribute
{
    /// <summary>
    /// Empty. The modules come from the module attributes in the scope.
    /// </summary>
    public Type[] ModuleTypes => [];
}

/// <summary>
/// Builds the container of these tests from <typeparamref name="TModule"/> and the module
/// attributes on the assembly, the class and the method.
/// </summary>
/// <remarks>
/// Use this form for a module that is declared in the test project. The VerificationGlue.Tests
/// generator cannot see the attribute that the DependencyModules generator writes for such a
/// module, so <c>[LocalModule]</c> does not reach the container, but
/// <c>[ModuleTest&lt;LocalModule&gt;]</c> does. See <see cref="ModuleTestAttribute"/> for the
/// scopes.
/// </remarks>
/// <typeparam name="TModule">The module to load first.</typeparam>
public class ModuleTestAttribute<TModule>
    : ContainerAttribute<ModuleBootstrap<TModule>>,
        IModuleTestAttribute
    where TModule : IDependencyModule, new()
{
    /// <summary>
    /// <typeparamref name="TModule"/>.
    /// </summary>
    public Type[] ModuleTypes => [typeof(TModule)];
}
