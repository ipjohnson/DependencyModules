using Microsoft.CodeAnalysis;

namespace DependencyModules.SourceGenerator.Impl;

/// <summary>
/// Diagnostics reported by the generator.
///
/// A source generator that stays quiet when something is wrong leaves the developer with a build
/// that succeeds and an application that misbehaves at run time. Everything here exists to move a
/// failure from run time to build time, or at minimum to make it visible.
/// </summary>
public static class DependencyModuleDiagnostics {
    private const string Category = "DependencyModules";

    /// <summary>
    /// Raised when the generator itself throws. Previously the exception was caught and, unless a
    /// log directory happened to be configured, discarded — the build succeeded, no registrations
    /// were produced, and nothing said so.
    /// </summary>
    public static readonly DiagnosticDescriptor GeneratorFailure = new(
        id: "DM0001",
        title: "DependencyModules generator failed",
        messageFormat:
        "The DependencyModules generator failed and registrations may be missing or incomplete: {0}. " +
        "Set DependencyModules_LogOutputDirectory to capture a log, and report the issue at " +
        "https://github.com/ipjohnson/DependencyModules/issues with that log attached.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for a service the container could never construct. Without this the generator emits a
    /// registration that throws when the provider is built, far from the declaration that caused it.
    /// </summary>
    public static readonly DiagnosticDescriptor ServiceCannotBeConstructed = new(
        id: "DM0002",
        title: "Service type cannot be constructed",
        messageFormat:
        "'{0}' is {1} and cannot be instantiated, so it was not registered. " +
        "Apply the service attribute to a concrete class, or register it with a static factory method.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for a module that is not partial. The compiler also reports CS0260 once the generated
    /// half arrives, but that message describes the symptom rather than what to do about it.
    /// </summary>
    public static readonly DiagnosticDescriptor ModuleMustBePartial = new(
        id: "DM0003",
        title: "Dependency module must be partial",
        messageFormat:
        "'{0}' is marked with [DependencyModule] but is not declared partial. " +
        "The generator completes the type with a second partial declaration, so add the partial modifier.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for a module declared inside another type.
    /// </summary>
    /// <remarks>
    /// The generated half is written at namespace level, so a nested declaration produces a second,
    /// detached type of the same name while the nested one never implements
    /// <c>IDependencyModule</c>. <c>AddModule&lt;Outer.Nested&gt;()</c> then fails to compile, but
    /// <c>[assembly: Nested]</c> binds to the detached type's attribute and builds green, registering
    /// nothing.
    /// </remarks>
    public static readonly DiagnosticDescriptor ModuleCannotBeNested = new(
        id: "DM0017",
        title: "Dependency module cannot be nested inside another type",
        messageFormat:
        "'{0}' is marked with [DependencyModule] but is declared inside another type. " +
        "The generator completes a module at namespace level, so this would produce a second, " +
        "unrelated type and register nothing. Move it out to the namespace.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for a module carrying settable properties while relying on the generated
    /// <c>Equals</c>/<c>GetHashCode</c>.
    /// </summary>
    /// <remarks>
    /// Modules de-duplicate by type, which is what stops a module reached twice from registering
    /// everything twice. A module with parameters is a different case: two instances carrying
    /// different values are the same module by that rule, so the first one reached wins and the
    /// other is silently discarded. Supplying an <c>Equals</c> that accounts for the properties
    /// suppresses the generated one and makes the intent explicit either way.
    /// </remarks>
    public static readonly DiagnosticDescriptor ModuleWithPropertiesShouldImplementEquals = new(
        id: "DM0018",
        title: "Module with properties relies on generated equality",
        messageFormat:
        "'{0}' has settable properties but does not declare Equals, so the generated equality " +
        "compares by type alone. Two instances carrying different values count as the same module " +
        "and the first one reached wins. Declare Equals and GetHashCode on '{0}' to say which " +
        "instances are the same.",
        category: Category,
        // Info, not Warning. Dedupe-by-type is correct for the common case — a parameterised module
        // composed once — and this repo's own integration tests carry eleven such modules that are
        // not doing anything wrong. Only a module reached twice with different values is actually
        // bitten, and that is not decidable here. Promote it per project with
        // dotnet_diagnostic.DM0018.severity = warning if you want it enforced.
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised when two conventions in one module register a type as the <i>same</i> service type.
    /// </summary>
    /// <remarks>
    /// Not raised for a type filling more than one role. A class implementing both
    /// <c>INotificationHandler&lt;OrderPlaced&gt;</c> and <c>IRequestPreProcessor&lt;ShipOrder&gt;</c>
    /// is the ordinary shape of a MediatR handler, and the two registrations are independently
    /// predictable from reading the module. It is one service type claimed twice that nobody can
    /// resolve by reading the source, because one lifetime has to win and the declaration does not
    /// say which.
    ///
    /// Equal lifetimes on the same service type are an error too. The outcome is predictable, but
    /// the declaration is redundant, and silently collapsing a duplicate is the failure mode this
    /// codebase avoids.
    /// </remarks>
    public static readonly DiagnosticDescriptor AmbiguousConventionMatch = new(
        id: "DM0004",
        title: "Convention match is ambiguous",
        messageFormat:
        "'{0}' is matched by two conventions in '{1}' that both register it as '{2}'. {3} " +
        "Narrow one of them, or move it to another module.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for a convention that matched nothing. A convention naming a service type no type in
    /// the compilation implements is almost always a mistake, and without this it fails silently —
    /// which is the failure mode this codebase has repeatedly had to hunt down.
    /// </summary>
    public static readonly DiagnosticDescriptor ConventionMatchedNothing = new(
        id: "DM0005",
        title: "Convention matched no types",
        // The advice is a parameter rather than a fixed tail, because the causes need different
        // fixes. Suggesting IncludeBaseClasses() unconditionally told a reader who had already
        // called it — or who had named a class, which can never match whatever they call — to go
        // looking for a typo in their own code.
        messageFormat: "The convention registering '{0}' in '{1}' matched no types. {2}.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for a convention match the container could not construct. The abstract and static
    /// cases are excluded before this point, because an abstract base implementing the convention's
    /// interface is the normal shape; a concrete class with no accessible constructor is not.
    /// </summary>
    public static readonly DiagnosticDescriptor ConventionMatchNotConstructable = new(
        id: "DM0006",
        title: "Convention matched a type that cannot be constructed",
        messageFormat:
        "'{0}' matches the convention registering '{1}' in '{2}', but has no accessible constructor, " +
        "so it was not registered",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised when two decorators of one service share an order. Applying them in an arbitrary order
    /// would nest them in a way nobody declared, and the two nestings behave differently.
    /// </summary>
    public static readonly DiagnosticDescriptor AmbiguousDecoratorOrder = new(
        id: "DM0007",
        title: "Decorator order is ambiguous",
        messageFormat:
        "'{0}' and '{1}' both decorate '{2}' with order {3}, so the order they nest in is undefined. " +
        "Give them distinct Order values.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised when a class marked for interception cannot be wrapped. Interception works through an
    /// interface, and a few member shapes cannot be forwarded; saying so beats emitting a wrapper
    /// that does not compile.
    /// </summary>
    /// <remarks>
    /// The message names the consequence as well as the cause. One member the wrapper cannot override
    /// means <i>no</i> wrapper is generated, so every other member on the interface goes uninterceped
    /// too — and the guide read as though only the offending member did. A reader who fixed the named
    /// member and rebuilt would then meet the next one.
    /// </remarks>
    public static readonly DiagnosticDescriptor CannotIntercept = new(
        id: "DM0008",
        title: "Service cannot be intercepted",
        messageFormat:
        "This service cannot be intercepted, so no wrapper was generated and none of its members are " +
        "intercepted: {0}. Other members may be unsupported for the same reason. Write a decorator " +
        "instead, or move the member to an interface that is not intercepted.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for something in a Conventions method body the generator could not read.
    /// </summary>
    /// <remarks>
    /// That body is configuration rather than code — it is read at compile time and never executed —
    /// so only a closed set of calls can appear in it. An expression outside that set has no
    /// compile-time meaning, and skipping it would drop registrations while the build stayed green.
    /// </remarks>
    public static readonly DiagnosticDescriptor ConventionCannotBeRead = new(
        id: "DM0009",
        title: "Convention declaration cannot be read",
        messageFormat: "This convention declaration could not be read, because {0}: {1}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports, on the class itself, that a convention registered it.
    /// </summary>
    /// <remarks>
    /// A class registered by convention carries no attribute saying so, so nothing at the
    /// declaration explains why it is in the container. This is that explanation, and it names the
    /// interface the match came through when it was not direct — which is what keeps matching
    /// through interface inheritance from looking like luck.
    ///
    /// Informational, so it stays out of the build: measured, a diagnostic at this severity is
    /// invisible at every MSBuild verbosity below detailed. It does appear in the IDE, and in SARIF
    /// when ErrorLog is set. Silence it with dotnet_diagnostic.DM0010.severity = none.
    /// </remarks>
    public static readonly DiagnosticDescriptor ExposedByConvention = new(
        id: "DM0010",
        title: "Service is registered by convention",
        messageFormat: "Exposed as {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>
    /// Reports, on the class itself, that its registration is conditional and on what.
    /// </summary>
    /// <remarks>
    /// An environment condition is evaluated at run time, so the compiler cannot say whether it will
    /// hold and no build-time error is available. The failure that follows is a service that does not
    /// resolve, several layers from the attribute that excluded it. This puts the condition where the
    /// developer is already looking.
    ///
    /// Informational, so it stays out of the build the same way DM0010 does. Silence it with
    /// dotnet_diagnostic.DM0011.severity = none.
    /// </remarks>
    public static readonly DiagnosticDescriptor RegisteredConditionally = new(
        id: "DM0011",
        title: "Service is registered conditionally",
        messageFormat: "Registered only when {0}",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for a condition that names nothing to test.
    /// </summary>
    /// <remarks>
    /// <c>[IfEnvironment()]</c> and <c>[IfEnvironmentValue("")]</c> compile. Written plain they mean
    /// the service never registers anywhere; written as the <c>IfNot</c> form they mean the attribute
    /// does nothing at all. Neither is what anybody intended, and both are invisible until something
    /// fails to resolve in an environment nobody tested.
    /// </remarks>
    public static readonly DiagnosticDescriptor EmptyEnvironmentCondition = new(
        id: "DM0012",
        title: "Environment condition tests nothing",
        messageFormat: "{0} names no {1} to test, so it does not depend on the environment",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for an interceptor that cannot serve some of the members it was applied to.
    /// </summary>
    /// <remarks>
    /// Three interfaces cover the member shapes — <c>IInterceptor</c> for a direct return,
    /// <c>IAsyncInterceptor</c> for a task, <c>IAsyncEnumerableInterceptor</c> for a stream — and the
    /// generator picks per member. An interceptor that implements none of the one a member needs was
    /// simply left out of that member's chain, with nothing said.
    ///
    /// That is the interceptor silently not running. An argument-rewriting interceptor stops
    /// rewriting; read as an authorisation or audit gate, it is a service that quietly is not gated.
    /// The sharpest form is an interceptor implementing only <c>IInterceptor</c> applied to a service
    /// whose members are all async, where it never runs at all and the build is green.
    ///
    /// Reported once per interceptor and member shape rather than once per member, so a wide
    /// interface produces one line rather than forty.
    /// </remarks>
    public static readonly DiagnosticDescriptor InterceptorCannotServeMembers = new(
        id: "DM0015",
        title: "Interceptor does not apply to every member",
        messageFormat:
        "'{0}' does not implement '{1}', so it is not applied to {2} on '{3}': {4}. Those members run " +
        "without it. Implement '{1}' on the interceptor, or apply it to a service that has no such member.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for a decorator whose service is registered as an open generic.
    /// </summary>
    /// <remarks>
    /// Decoration replaces a registration with a factory, and the container refuses a factory for an
    /// open generic service type — "requires registering an open generic implementation type". So
    /// there is nothing to emit, and both shapes of the mistake failed badly until this existed.
    ///
    /// A <i>generic</i> decorator is expanded against the closed constructions a compilation
    /// registers. An open generic registration closes nothing, so the expansion produced no
    /// decorations and the declaration was dropped in silence — a build with a decorator in it that
    /// never runs.
    ///
    /// A <i>non-generic</i> decorator named against an unbound service is worse: it needs no
    /// expansion, so it reached emission carrying <c>IHolder&lt;&gt;</c> and produced
    /// <c>Decorate&lt;IHolder&lt;&gt;&gt;</c> — CS7003 inside generated code, which is the one failure
    /// mode this generator exists to avoid.
    ///
    /// Registering closed constructions is the way through, and the message says so.
    /// </remarks>
    public static readonly DiagnosticDescriptor OpenGenericCannotBeDecorated = new(
        id: "DM0013",
        title: "Open generic registration cannot be decorated",
        messageFormat:
        "'{0}' is registered as an open generic, so '{1}' cannot decorate it. Decoration replaces a " +
        "registration with a factory, and the container does not allow one for an open generic " +
        "service type. Register closed constructions of '{0}' instead — a convention over the open " +
        "generic registers one per implementation, and a generic decorator is then expanded across them.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for <c>[CrossWireService]</c> on a generic type.
    /// </summary>
    /// <remarks>
    /// Cross-wiring means one instance shared across the implementation and every interface it
    /// declares, which is emitted as <c>s =&gt; s.GetRequiredService&lt;T&gt;()</c> per interface — a
    /// factory, and a factory is what an open generic registration cannot have.
    ///
    /// Registering each interface to the same open generic implementation type compiles, and is a
    /// different contract: the container builds one instance per service type, which is the opposite
    /// of what the attribute promises. Silently substituting that would be worse than refusing.
    ///
    /// Until this existed the generated code did not compile at all — the type parameter leaked into
    /// the registration as <c>typeof(ILedger&lt;T&gt;)</c> (CS0246, no <c>T</c> in scope) beside
    /// <c>GetRequiredService&lt;Ledger&lt;&gt;&gt;()</c> (CS7003).
    /// </remarks>
    public static readonly DiagnosticDescriptor CrossWireCannotBeGeneric = new(
        id: "DM0014",
        title: "Generic type cannot be cross-wired",
        messageFormat:
        "'{0}' is generic, so [CrossWireService] cannot register it. Cross-wiring shares one instance " +
        "across every service type, which needs a factory, and the container does not allow one for an " +
        "open generic registration. Use [SingletonService], [ScopedService] or [TransientService] to " +
        "register it, applying one per interface if it needs to answer to more than one.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>
    /// Raised for an assembly-level module attribute whose namespace the file does not import.
    /// </summary>
    /// <remarks>
    /// A module generates its attribute in the module's own namespace, and an assembly-level
    /// attribute has no namespace context to inherit — a <c>using</c> inside a namespace declaration
    /// cannot apply to it, because assembly attributes precede every namespace in the file.
    ///
    /// So <c>[assembly: ApplicationModule]</c> in a file that does not import the module's namespace
    /// fails with <c>CS0246</c> naming <c>ApplicationModuleAttribute</c> — a type the developer never
    /// wrote, generated into a namespace the error does not mention, by a generator whose output they
    /// have probably never looked at. Every part of that error points away from the fix.
    ///
    /// This cannot be decided by asking the compiler, because the attribute does not exist yet while
    /// the generator that writes it is running. It is read from syntax instead: an assembly attribute
    /// whose name matches a module this compilation declares, written unqualified, in a file that
    /// imports neither that namespace nor anything <c>global using</c> supplies.
    /// </remarks>
    public static readonly DiagnosticDescriptor ModuleAttributeNamespaceNotImported = new(
        id: "DM0016",
        title: "Assembly-level module attribute needs its namespace imported",
        messageFormat:
        "'{0}' is declared in '{1}', and an assembly-level attribute has no namespace context, so " +
        "this does not compile. Add 'using {1};' to this file, or write it qualified as " +
        "'[assembly: {1}.{0}]'.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

}
