using DependencyModules.Runtime.Attributes;

namespace WebApiApp;

public interface IAiSummaryProvider {
    string GetSummary();
}

[SingletonService]
public class AiSummaryProvider : IAiSummaryProvider {

    private static string[] summaries = new[] {
        "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
    };
    
    public string GetSummary() {
        return summaries[Random.Shared.Next(summaries.Length)];
    }
}