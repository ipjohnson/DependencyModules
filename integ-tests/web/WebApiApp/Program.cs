using DependencyModules.Runtime;
using WebApiApp;

var builder = WebApplication.CreateBuilder(args);

// The point of this project: the generated module registers the services, and the endpoint below
// resolves one out of the container the same way any ASP.NET Core app would.
builder.Services.AddModule<ApplicationModule>();

var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/weatherforecast", (Weather weather) => weather.GetWeatherForecast())
    .WithName("GetWeatherForecast");

app.Run();

