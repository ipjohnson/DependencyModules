// See https://aka.ms/new-console-template for more information

using ConsoleTestProject;
using DependencyModules.Runtime;
using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;
using SutProject;

[assembly: DependencyModule]

var serviceCollection = new ServiceCollection();

serviceCollection.AddModule<ApplicationModule>();

var container = serviceCollection.BuildServiceProvider();

container.GetRequiredService<TestExport>();

SutModule.Run();
