---
layout: home

hero:
  name: DependencyModules
  text: Dependency injection, decided at compile time
  tagline: >-
    Declare registration next to the class it belongs to, and a source generator writes the
    IServiceCollection calls during the build. Nothing reflects, nothing scans at startup, and the
    trimmer can follow every registration you declared.
  image:
    src: /hero.svg
    alt: Declarations on the left becoming generated registration code on the right
  actions:
    - theme: brand
      text: Get started
      link: /guide/getting-started
    - theme: alt
      text: Conventions
      link: /guide/conventions
    - theme: alt
      text: View on GitHub
      link: https://github.com/ipjohnson/DependencyModules

features:
  - title: Registrations you can read
    details: >-
      Every registration is emitted into your assembly as plain C#. Set EmitCompilerGeneratedFiles
      and the file under obj/ is the ground truth — no container graph to reason about, no startup
      cost to measure.
    link: /guide/services
    linkText: Registering services

  - title: Conventions without reflection
    details: >-
      Declare what to register once and the generator resolves the matches during the build.
      Assignability, namespaces, attributes and name globs — including types in a referenced package.
    link: /guide/conventions
    linkText: How conventions work

  - title: Trimming and Native AOT safe
    details: >-
      Each match is emitted as a literal typeof(), which the trimmer roots and which carries the
      constructor along with it. The capability that breaks reflection-based scanners is the one that
      works here.
    link: /guide/aot
    linkText: Why it survives trimming

  - title: Mistakes reported at build time
    details: >-
      A convention that matches nothing, a service that cannot be constructed, two conventions
      claiming one service type — each is a DM diagnostic in the IDE rather than an exception at
      startup.
    link: /reference/diagnostics
    linkText: Diagnostics reference

  - title: Decorate and intercept
    details: >-
      Wrap a service with a decorator you write, or with a generated wrapper that routes every member
      through an interceptor. Both compose with conventions and both are ordered globally.
    link: /guide/decorators
    linkText: Decorators and interception

  - title: Built for testing
    details: >-
      The xUnit package builds a provider from the modules a test names and injects the services the
      test asks for, with mocks substituted where you want them.
    link: /guide/testing
    linkText: Testing modules
---

<div class="dm-sample">

## The problem

Every .NET application keeps a list like this, and nothing checks that it is complete:

```csharp
services.AddScoped<IOrderRepository, OrderRepository>();
services.AddSingleton<IEmailSender, SmtpEmailSender>();
// … another two hundred lines
```

Forget a line and you find out at run time, in the environment you deployed to. Reach for a runtime
scanner instead and you trade that for three new problems: you can no longer read what was
registered, the scan runs on every start, and the trimmer cannot see through reflection — so a
published, trimmed build registers nothing at all.

## What it looks like instead

Mark the class, and the registration is written for you during the build.

```csharp
[SingletonService]
public class SmtpEmailSender : IEmailSender { }

[DependencyModule]
public partial class ApplicationModule;
```

```csharp
var services = new ServiceCollection();

services.AddModule<ApplicationModule>();
```

Or declare a rule once, and let it cover everything that fits — including the handler somebody adds
next year.

```csharp
[DependencyModule]
public partial class HandlerModule : IConventionModule {
    void IConventionModule.Conventions(IConventionDefinitions conventions) {
        conventions.RegisterAll(typeof(IRequestHandler<,>)).AsScoped();
        conventions.RegisterAll<IValidator>().InNamespaceOf<OrderMarker>().AsScoped();
    }
}
```

That body never runs. It is read during the build, and what comes out the other side is the same
registration code you would have written by hand:

```csharp
services.AddScoped(typeof(IRequestHandler<CreateOrder, OrderId>), typeof(CreateOrderHandler));
services.AddScoped(typeof(IRequestHandler<RenameOrder, OrderId>), typeof(RenameOrderHandler));
```

</div>

<style>
.dm-sample {
  max-width: 1152px;
  margin: 0 auto;
  padding: 0 24px 64px;
}

.dm-sample h2 {
  border-top: 1px solid var(--vp-c-divider);
  padding-top: 40px;
  margin-top: 8px;
}
</style>
