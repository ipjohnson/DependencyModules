using DependencyModules.Runtime.Attributes;
using SutProject.Glue;

namespace SecondarySutProject.Glue;

[IntoList<IGlueStep, int>(5)]
[IntoMap<string, IGlueStep>("second")]
public class SecondGlueStep : IGlueStep
{
    public string Name => "second";
}
