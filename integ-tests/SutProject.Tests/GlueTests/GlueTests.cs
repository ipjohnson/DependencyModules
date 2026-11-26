using DependencyModules.xUnit.Attributes;
using Microsoft.Extensions.DependencyInjection;
using SecondarySutProject;
using SutProject.Glue;
using Xunit;

namespace SutProject.Tests.GlueTests;

public class GlueTests
{
    [ModuleTest]
    [SutModule]
    public void OneModule_GivesItsItems(GlueConsumer consumer, IServiceProvider provider)
    {
        Assert.Equal(new[] { "first" }, consumer.Steps.Select(step => step.Name));
        Assert.Equal(new[] { "first" }, consumer.StepsByName.Keys);
        Assert.Empty(provider.GetServices<IGlueStep>());
    }

    [ModuleTest]
    [SecondarySutModule]
    public void ModulesOfTwoAssemblies_GiveOneCollection(GlueConsumer consumer)
    {
        Assert.Equal(new[] { "second", "first" }, consumer.Steps.Select(step => step.Name));
        Assert.Equal(new[] { "first", "second" }, consumer.StepsByName.Keys);
        Assert.IsType<FirstGlueStep>(consumer.StepsByName["first"]);
    }

    [ModuleTest]
    [SutModule]
    public void AFunc_BuildsAnObjectFromItsArgumentAndTheProvider(
        GlueConsumer consumer,
        ISingletonService singletonService
    )
    {
        var job = consumer.CreateJob(12);

        Assert.Equal(12, job.Id);
        Assert.Same(singletonService, job.SingletonService);
        Assert.NotSame(job, consumer.CreateJob(12));
    }
}
