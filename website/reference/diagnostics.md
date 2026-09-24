# Diagnostics

The generator gives these diagnostics when you compile. Their category is `DependencyModules`.

| ID | Severity | Title |
| --- | --- | --- |
| [DM0001](#dm0001) | Error | DependencyModules generator failed |
| [DM0002](#dm0002) | Warning | Service type cannot be constructed |
| [DM0003](#dm0003) | Error | Dependency module must be partial |
| [DM0004](#dm0004) | Error | Convention match is ambiguous |
| [DM0005](#dm0005) | Warning | Convention matched no types |
| [DM0006](#dm0006) | Warning | Convention matched a type that cannot be constructed |
| [DM0007](#dm0007) | Error | Decorator order is ambiguous |
| [DM0008](#dm0008) | Warning | Service cannot be intercepted |
| [DM0009](#dm0009) | Error | Convention declaration cannot be read |
| [DM0010](#dm0010) | Info | Service is registered by convention |
| [DM0011](#dm0011) | Info | Service is registered conditionally |
| [DM0012](#dm0012) | Warning | Environment condition tests nothing |
| [DM0013](#dm0013) | Warning | Open generic registration cannot be decorated |
| [DM0014](#dm0014) | Warning | Generic type cannot be cross-wired |
| [DM0015](#dm0015) | Warning | Interceptor does not apply to every member |
| [DM0016](#dm0016) | Warning | Assembly-level module attribute needs its namespace imported |
| [DM0017](#dm0017) | Error | Dependency module cannot be nested inside another type |
| [DM0018](#dm0018) | Warning | Module with properties relies on generated equality |
| [DM0019](#dm0019) | Error | Assembly-level module attribute is not composed |
| [DM0020](#dm0020) | Warning | Interception is applied by no module |
| [DM0021](#dm0021) | Warning | [Mock] and [TestExport] name one service on the same method |
| [DM0022](#dm0022) | Warning | Decorator names an implementation while factories are generated |

The table shows the title of each diagnostic. The compiler shows a message with more information, for example the names of the types.

You can change the severity of a diagnostic in an `.editorconfig` file. For example, you can change DM0010 from Info to Warning. You can also suppress a diagnostic with `#pragma warning disable` or with the `NoWarn` MSBuild property.

Some diagnostics have no location in a source file. For these diagnostics, use `NoWarn` or a `.globalconfig` file. `#pragma` and the file sections of `.editorconfig` have no effect on them. These diagnostics have no location:

- DM0001
- DM0008 for a service type without members
- DM0007 and DM0013 for a decorator from `[Decorate]`

Info diagnostics show in the IDE and in a SARIF log. The `dotnet build` output does not show them at the usual verbosity.

## DM0001

An exception occurred when the service part, the interceptor part, or the convention part of the generator wrote code. Some registrations can be missing.

An exception in a different step of the generator gives the compiler warning CS8785 and not DM0001. The generator then writes no code, and `AddModule<T>()` gives the error CS0311.

To correct the problem:

1. Set the `DependencyModules_LogOutputDirectory` MSBuild property to a folder.
2. Build the project again.
3. Write an issue for the problem on the [issues page](https://github.com/ipjohnson/DependencyModules/issues) of the repository.
4. Attach the log files to the issue.

## DM0002

A class with a service attribute is abstract or static. The generator cannot make an instance of it and does not register it.

To correct the problem, put the attribute on a class that is not abstract. You can also register the service with a static factory method.

## DM0003

A class with `[DependencyModule]` is not partial. The generator does not write the other part of the module. If the project has services, the generator writes the registrations as a partial class. Thus the compiler also gives the error CS0260.

To correct the problem, add the `partial` modifier to the class.

## DM0004

Two conventions in one module register the same class as the same service type. The generator does not write these registrations.

To correct the problem, add a filter to one convention. You can also move one convention to a different module.

## DM0005

A convention selects no classes. The message gives a possible cause:

- The service type is a class. A convention selects only by interfaces.
- Only a base class of the classes implements the service type.
- The filters remove all classes.

To correct the problem:

- If the service type is a class, use an interface as the service type. You can also register the classes with attributes.
- If only a base class implements the service type, call `IncludeBaseClasses()`.
- If the filters remove all classes, examine the name, namespace, and assembly filters.

## DM0006

A convention selects a class that has no constructor that the generated code can use. The generator does not register the class.

In the project, the generator gives DM0006 if the class has only `private` or `protected` constructors. In a referenced assembly, it gives DM0006 if the class has no `public` constructor. The service provider uses only `public` constructors. Thus a class with only `internal` constructors gets no DM0006, but the service provider cannot make it, unless the module uses generated factories.

To correct the problem, add a `public` constructor to the class. You can also add a filter that removes the class.

## DM0007

Two decorators in one module have the same service type and the same `Order` value. The sequence of these decorators is not known.

To correct the problem, give the decorators different `Order` values.

## DM0008

The generator cannot intercept a service. It writes no wrapper. Thus, the interceptors do not intercept the members of the service. The message gives the cause:

- The service type has a member that a wrapper cannot send to the service. For the list, refer to [Members that the generator cannot intercept](../guide/interception.md#members-that-the-generator-cannot-intercept). The message gives the name of one such member.
- The class implements no interface.
- The class implements more than one interface, and `[Intercept]` does not set `Service`.
- The class does not implement the `Service` type.
- The service type has no members.

To correct the problem, set `Service`. You can also move the member to an interface that is not intercepted. You can also use a decorator.

## DM0009

The generator cannot read a convention statement. The message gives the cause and the statement. These are the causes:

- The `Conventions` method has no statement body, or a statement is not a chain of calls on the parameter.
- A chain does not start with `RegisterAll`.
- A call is not a convention call.
- An argument is not a value that the compiler knows.
- The convention has no lifetime or more than one lifetime.
- The convention has more than one of `AsSelf()`, `AsSelfWithInterfaces()`, and `AlsoAsSelf()`.
- `RegisterAll()` without a service type has no registration shape or no filter that selects.
- The type that implements `IConventionModule` does not have `[DependencyModule]`. The generator gives this cause only if the project has one or more modules.

To correct the problem, change the statement. For the conditions, refer to [Conventions](../guide/conventions.md).

## DM0010

The generator registered a class from a convention. The message shows the service type and the module. If the convention selected the class from a different interface or a base class, the message also shows that type.

For a class from a referenced assembly, the diagnostic is at the convention statement.

This diagnostic gives information only.

## DM0011

A service has environment conditions. The message shows the conditions. The generator gives DM0011 for each class with a service attribute and conditions, also when the conditions are true. Registrations from conventions do not get DM0011.

This diagnostic gives information only.

## DM0012

An environment condition has no environment name or no key. Thus it does not examine the environment. The generator ignores the condition. The generator ignores arguments that are not string constants. Thus a condition with only such arguments, for example an array, also gives DM0012.

The generator gives DM0012 only for classes with a service attribute. On a `[Decorator]` class or on a class that a convention selects, it ignores the condition and gives no diagnostic. On a convention statement, a condition call without a name or a key gives DM0009.

To correct the problem, give a name or a key. You can also remove the attribute.

## DM0013

The service type of a decorator is a type that the project registers only as an open generic type, for example `IRepository<>`. The service provider cannot decorate an open generic registration. If the project also contains closed registrations of the service type, the generator decorates them and gives no diagnostic.

`[Decorate]` with an open generic service type and a decorator that is not generic also gives DM0013.

To correct the problem, register closed types of the service. For example, declare classes that are not generic, such as `OrderRepository : IRepository<Order>`. The generator then uses a generic decorator for each closed type. For `[Decorate]` with a decorator that is not generic, give a closed service type, for example `typeof(IRepository<Order>)`.

## DM0014

`[CrossWireService]` is on a generic class. The generator cannot cross-wire a generic class and does not register it.

To correct the problem, use `[SingletonService]`, `[ScopedService]`, or `[TransientService]`. To register the class as more than one interface, use one attribute for each interface.

## DM0015

An interceptor does not implement the interface for some members of the service. These members run without the interceptor. The message gives the names of the members.

To correct the problem, implement the missing interface on the interceptor. For example, implement `IAsyncInterceptor` for methods that have the return type `Task`. You can also use the interceptor for a service without such members.

## DM0016

An assembly-level module attribute is in a file that has no `using` directive for the namespace of the module. The code does not compile. A `global using` directive in a different file is also a `using` directive for this check.

The generator examines only attributes without their namespace. It examines them only if the project has a module or a generated `ApplicationModule`.

To correct the problem, add the `using` directive. You can also write the full name, for example `[assembly: Catalog.CatalogModule]`.

## DM0017

A class with `[DependencyModule]` is in a different class. The generator can write the other part of a module only at the namespace level. It does not write the other part of this module.

To correct the problem, move the module to the namespace level.

## DM0018

A module has properties with a `set` accessor, but it does not declare `Equals`. The generated `Equals` method compares only the module type. Thus, two instances with different values are one module. Only the first instance loads.

To correct the problem, declare `Equals` and `GetHashCode` on the module. If each module of this type loads only one time, you can also suppress the warning with `NoWarn` or `.editorconfig`.

## DM0019

An assembly-level module attribute is in a file that the generator does not use for `ApplicationModule`. The generator reads assembly-level module attributes only from `Program.cs`. Thus, it ignores this attribute. The module does not register its services.

The generator gives DM0019 only for an attribute without its namespace. It ignores an attribute with its full name in a different file, and it gives no diagnostic. It examines the files only if the project has a module or a generated `ApplicationModule`.

To correct the problem, move the attribute to `Program.cs`. You can also load the module with `AddModule`.

## DM0020

A class has `[Intercept]`, but no module in the project uses the interception. Thus the interceptors do not run. This can occur when a realm-only module registers the class from a convention, and `[Intercept]` has no realm.

To correct the problem, set `Realm` on `[Intercept]` to the module that registers the class.

## DM0021

A test method has `[TestExport]` for a service type and a `[Mock]` parameter of the same type. The test uses the mock and not the `[TestExport]` registration, with two exceptions. For the exceptions, refer to [Mocks and `[TestExport]`](../guide/testing-mocking.md#mocks-and-testexport). The generator gives DM0021 only if the test project references `DependencyModules.SourceGenerator`.

To correct the problem, if the `[TestExport]` is a default, move it to the test class or to the assembly. You can also remove the attribute that you do not want.

## DM0022

A decorator sets `Implementation`, and the module uses generated factories. If the generator writes factories, a registration does not show its implementation. Thus the decorator changes all registrations of the service type.

The module uses generated factories if `[DependencyModule]` sets `GenerateFactories = true`. If the module does not set `GenerateFactories`, the `DependencyModules_GenerateFactories` MSBuild property sets it.

To correct the problem, remove `Implementation` and decorate all registrations. You can also disable the generated factories for the module. Set `GenerateFactories = false` on the module, or set the MSBuild property to `false`.
