using DependencyModules.xUnit.Attributes;
using NSubstitute;
using Xunit;

namespace WebApiApp.Tests;

public class WeatherTests {
    [ModuleTest]
    public void GetForecast(Weather weather) {
        var response = weather.GetWeatherForecast().ToArray();
        
        Assert.Equal(5, response.Length);
    }

    [ModuleTest]
    public void GetStaticForecast(
        Weather weather, 
        [Mock] ITemperatureProvider temperatureProvider,
        [Mock] IAiSummaryProvider aiSummaryProvider) {
        temperatureProvider.GetTemperature().Returns(38);
        aiSummaryProvider.GetSummary().Returns("Sunny");
        
        var response = weather.GetWeatherForecast().ToArray();
        Assert.Equal(5, response.Length);

        foreach (var weatherForecast in response) {
            Assert.Equal(38, weatherForecast.TemperatureC);
            Assert.Equal("Sunny", weatherForecast.Summary);
        }
    }
}