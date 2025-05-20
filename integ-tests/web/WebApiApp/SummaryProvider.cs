using DependencyModules.Runtime.Attributes;

namespace WebApiApp;

public interface ISummaryProvider {
    string GetSummary();
}

[SingletonService]
public class SummaryProvider(IAiSummaryProvider aiSummaryProvider) : ISummaryProvider {
    
    public string GetSummary() {
        return aiSummaryProvider.GetSummary();
    }
}