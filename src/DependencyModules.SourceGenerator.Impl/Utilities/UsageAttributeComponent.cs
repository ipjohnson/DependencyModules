using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Utilities;


public class UsageAttributeComponent : BaseOutputComponent
{
    private readonly string _usage;

    public UsageAttributeComponent(string usage) {
        _usage = usage;
    }

    protected override void WriteComponentOutput(IOutputContext outputContext)
    {
        outputContext.WriteIndent();
        outputContext.WriteLine(_usage);
    }
}
