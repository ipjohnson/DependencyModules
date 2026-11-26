using DependencyModules.Runtime.Attributes;
using DependencyModules.VerificationGlue.Attributes;
using Microsoft.Extensions.DependencyInjection;
using SutProject.SubDirectory;
using VerificationGlue.Tests;
using static VerificationGlue.VerifySyntax;

namespace SutProject.VerificationGlueTests;

/// <summary>
/// The module comes from the module attribute on the class.
/// </summary>
[ModuleTest]
[SutModule]
public partial class ModuleResolutionTests
{
    [VerifyTest]
    public void ResolvesAServiceFromTheModule(ISingletonService singletonService)
    {
        Verify(singletonService.GetName() == nameof(SingletonService));
    }

    [VerifyTest]
    public void ResolvesAServiceWithDependencies(IDependencyOne dependencyOne)
    {
        Verify(dependencyOne.SingletonService is SingletonService);
        Verify(dependencyOne.ScopedService is ScopedService);
    }

    [VerifyTest]
    public void ResolvesAKeyedService(
        [FromKeyedServices(Constants.StringValue)] KeyedService keyedService
    )
    {
        Verify(keyedService is not null);
    }

    [VerifyTest]
    public void InjectsTheServiceProvider(IServiceProvider serviceProvider)
    {
        Verify(serviceProvider.GetService<ISingletonService>() is SingletonService);
    }

    [VerifyTest]
    public void ConstructsAnUnregisteredConcreteType(NeedsASingleton needsASingleton)
    {
        Verify(needsASingleton.SingletonService is SingletonService);
    }

    [VerifyTest]
    public void LeavesTheBuiltInValuesToTheRunner(
        CancellationToken cancellationToken,
        TestContext context,
        ITestOutput output
    )
    {
        Verify(!cancellationToken.IsCancellationRequested);
        Verify(context.Uid.Contains(nameof(LeavesTheBuiltInValuesToTheRunner)));
        Verify(ReferenceEquals(output, context.Output));
    }

    [VerifyTest]
    public void LeavesADefaultValueToTheRunner(ISingletonService singletonService, int count = 3)
    {
        Verify(count == 3);
    }

    /// <summary>
    /// A data row fills the first parameters. The container fills the rest.
    /// </summary>
    [VerifyTest]
    [Row("first")]
    [Row("second")]
    public void CombinesARowWithServices(string name, ISingletonService singletonService)
    {
        Verify(name.Length > 0);
        Verify(singletonService is SingletonService);
    }

    public class NeedsASingleton(ISingletonService singletonService)
    {
        public ISingletonService SingletonService { get; } = singletonService;
    }
}

/// <summary>
/// The module comes from the attribute's type argument.
/// </summary>
[ModuleTest<SutModule>]
public partial class GenericModuleTestTests
{
    [VerifyTest]
    public void ResolvesAServiceFromTheModule(ISingletonService singletonService)
    {
        Verify(singletonService is SingletonService);
    }
}

[DependencyModule]
public partial class LocalModule { }

public interface ILocalService
{
    string Name { get; }
}

[SingletonService]
public class LocalService : ILocalService
{
    public string Name => nameof(LocalService);
}

/// <summary>
/// A module declared in the test project, which the generic form reaches.
/// </summary>
[ModuleTest<LocalModule>]
public partial class LocalModuleTests
{
    [VerifyTest]
    public void ResolvesAServiceFromALocalModule(ILocalService localService)
    {
        Verify(localService.Name == nameof(LocalService));
    }
}

/// <summary>
/// The attribute on the method is narrower than the one on the class, so it wins.
/// </summary>
[ModuleTest<LocalModule>]
public partial class MethodLevelModuleTestTests
{
    [VerifyTest]
    [ModuleTest<SutModule>]
    public void UsesTheMethodsContainer(
        ISingletonService singletonService,
        IServiceProvider services
    )
    {
        Verify(singletonService is SingletonService);
        Verify(services.GetService<ILocalService>() is null);
    }
}
