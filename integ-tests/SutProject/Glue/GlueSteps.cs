using DependencyModules.Runtime.Attributes;

namespace SutProject.Glue;

public interface IGlueStep
{
    string Name { get; }
}

[IntoList<IGlueStep, int>(10)]
[IntoMap<string, IGlueStep>("first")]
public class FirstGlueStep(ISingletonService singletonService) : IGlueStep
{
    public string Name => "first";

    public ISingletonService SingletonService => singletonService;
}

public class GlueJob(int id, ISingletonService singletonService)
{
    public int Id => id;

    public ISingletonService SingletonService => singletonService;
}

[TransientService]
public class GlueConsumer(
    IReadOnlyList<IGlueStep> steps,
    IReadOnlyDictionary<string, IGlueStep> stepsByName,
    Func<int, GlueJob> createJob
)
{
    public IReadOnlyList<IGlueStep> Steps => steps;

    public IReadOnlyDictionary<string, IGlueStep> StepsByName => stepsByName;

    public GlueJob CreateJob(int id) => createJob(id);
}
