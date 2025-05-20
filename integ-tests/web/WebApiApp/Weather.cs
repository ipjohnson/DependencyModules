using DependencyModules.Runtime.Attributes;

namespace WebApiApp;

[SingletonService]
public class Weather(
    ISummaryProvider summaryProvider, 
    ITemperatureProvider temperatureProvider) {
    
    public IEnumerable<WeatherForecast> GetWeatherForecast() {
        var forecast = Enumerable.Range(1, 5).Select(index =>
                new WeatherForecast
                (
                    DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
                    temperatureProvider.GetTemperature(),
                    summaryProvider.GetSummary()
                ))
            .ToArray();
        return forecast;
    }
}