// See https://aka.ms/new-console-template for more information

using ConsoleTestProject;
using DependencyModules.Runtime;
using Microsoft.Extensions.DependencyInjection;

var serviceCollection = new ServiceCollection();

serviceCollection.AddModule<ApplicationModule>();

var container = serviceCollection.BuildServiceProvider();

container.GetRequiredService<TestExport>();

SutProject.SutModule.Run();

