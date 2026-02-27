using DependencyModules.xUnit.Attributes;
using Xunit;

namespace SutProject.Tests.StandardTests;

public class RecordServiceTests {
    [ModuleTest]
    [SutModule]
    public void ResolveRecordService(IRecordService recordService) {
        Assert.NotNull(recordService);
        Assert.Equal("RecordService", recordService.GetName());
    }

    [ModuleTest]
    [SutModule]
    public void RecordServiceIsSingleton(IRecordService first, IRecordService second) {
        Assert.Same(first, second);
    }
}

public class RecordModuleTests {
    [ModuleTest]
    [RecordModule]
    public void ResolveServiceFromRecordModule(IRecordModuleService service) {
        Assert.NotNull(service);
        Assert.Equal("FromRecordModule", service.Value);
    }
}
