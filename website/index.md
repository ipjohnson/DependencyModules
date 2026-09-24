---
layout: home

hero:
  name: DependencyModules
  text: Registration code that the generator writes
  tagline: DependencyModules is a source generator for Microsoft.Extensions.DependencyInjection. You put attributes on your classes. The generator writes the registrations when you compile.
  image:
    src: /hero.svg
    alt: DependencyModules
  actions:
    - theme: brand
      text: Get started
      link: /guide/getting-started
    - theme: alt
      text: GitHub
      link: https://github.com/ipjohnson/DependencyModules

features:
  - title: Attributes for services
    details: You put <code>[SingletonService]</code>, <code>[ScopedService]</code>, or <code>[TransientService]</code> on a class. The generator writes one registration call for each service.
  - title: Modules
    details: A module registers the services of a project. A module can use other modules. These modules can also be in packages.
  - title: Conventions
    details: A convention registers all classes that implement an interface. Filters select classes by name, namespace, and attribute.
  - title: Decorators and interception
    details: Decorators and interceptors put code around services. For classes that are not generic, the generated code makes them without reflection.
  - title: Environments
    details: A service can have a registration only in some environments, or only when the environment has a value.
  - title: Tests with modules
    details: xUnit and NUnit tests get services as parameters. Each test gets a new service provider. Mocks replace services.
---

## Example

```csharp
using DependencyModules.Runtime.Attributes;

namespace Shop;

public interface IPriceCalculator
{
    decimal Total(decimal price, int quantity);
}

[SingletonService]
public class PriceCalculator : IPriceCalculator
{
    public decimal Total(decimal price, int quantity) => price * quantity;
}

[DependencyModule]
public partial class ShopModule;
```

```csharp
using DependencyModules.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Shop;

var services = new ServiceCollection();

services.AddModule<ShopModule>();

var calculator = services.BuildServiceProvider().GetRequiredService<IPriceCalculator>();
```

To start, refer to [Getting started](./guide/getting-started.md).
