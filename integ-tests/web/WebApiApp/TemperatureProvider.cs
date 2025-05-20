using DependencyModules.Runtime.Attributes;

namespace WebApiApp;

public interface ITemperatureProvider {
    int GetTemperature();
} 

[SingletonService]
public class TemperatureProvider : ITemperatureProvider {

    public int GetTemperature() {
        return Random.Shared.Next(-20, 55);
    }
}