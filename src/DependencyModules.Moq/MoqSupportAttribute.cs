using System.Diagnostics.CodeAnalysis;
using DependencyModules.Testing.Attributes.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using MoqLib = Moq;

namespace DependencyModules.Moq;

/// <summary>
/// Supplies a test's mocks with Moq.
/// </summary>
/// <remarks>
/// Applies to a method, a class, or a whole assembly, so a test project can switch mocks on once in
/// an <c>AssemblyInfo</c> rather than per test.
///
/// A test asks for a mock in either of two ways, and both work. A parameter typed
/// <c>Mock&lt;T&gt;</c> hands over the mock itself — the thing you configure and verify on — while
/// the container is separately given its <c>Object</c>, so the service under test is built against
/// that same mock. A parameter marked <c>[Mock]</c> and typed as the service hands over the object
/// instead, and <c>Mock.Get(instance)</c> reaches the mock behind it.
///
/// Mocks are loose, matching Moq's own default: an unconfigured member returns default rather than
/// throwing.
/// </remarks>
/// <example>
/// <code>
/// [ModuleTest]
/// [MoqSupport]
/// public void SendsTheMail(IEmailSender sender, Mock&lt;IAuditLog&gt; log) {
///     log.Verify(x => x.Write(It.IsAny&lt;string&gt;()));
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
public class MoqSupportAttribute : Attribute, IMockSupportAttribute, ITestServiceSetupAttribute {

    /// <summary>
    /// Registers a mock for every <c>Mock&lt;T&gt;</c> the test asked for, alongside the object that
    /// mock produces.
    /// </summary>
    /// <remarks>
    /// Registering both is what lines the two halves up. The test resolves the <c>Mock&lt;T&gt;</c>
    /// and configures it; everything the container builds — the service under test included —
    /// resolves the <c>T</c> and gets that same mock's object. Neither has to know about the other,
    /// and the test wires nothing together itself.
    ///
    /// Runs before any other setup attribute, so a <c>[TestExport]</c> naming a real implementation
    /// of the same service still overrides what is registered here — asking for a mock object is not
    /// the same as declaring the service mocked.
    ///
    /// The value providers behind <c>[Mock]</c> run after this whole pass, so that <c>[Mock]</c>
    /// does beat a <c>[TestExport]</c>. They stand aside for a type named here, which is what lets
    /// <c>[Mock] IFoo</c> and <c>Mock&lt;IFoo&gt;</c> sit on one test and resolve to a matched pair
    /// rather than to two unrelated mocks.
    /// </remarks>
    /// <param name="testMethod">The test the container is being built for.</param>
    /// <param name="serviceCollection">The collection backing the test's container.</param>
    public void SetupServiceCollection(ITestMethodContext testMethod, IServiceCollection serviceCollection) {
        var mocked = new HashSet<Type>();

        foreach (var parameter in testMethod.Method.GetParameters()) {
            // Add returns false for a type already handled: two parameters naming the same
            // Mock<T> are one mock, so configuring either is configuring what the test was given.
            if (!TryGetMockedType(parameter.ParameterType, out var mockedType) ||
                !mocked.Add(mockedType)) {
                continue;
            }

            var mock = CreateMock(mockedType);

            serviceCollection.AddSingleton(parameter.ParameterType, _ => mock);
            serviceCollection.AddSingleton(mockedType, _ => mock.Object);
        }
    }

    /// <summary>
    /// Provides a mocked instance of the specified type, for a parameter marked <c>[Mock]</c>.
    /// </summary>
    /// <remarks>
    /// Constructed through the closed generic because <c>Mock&lt;T&gt;</c> is the only form Moq
    /// offers, and the type is not known until the test asks for it.
    ///
    /// A <c>[Mock] Mock&lt;IFoo&gt;</c> parameter arrives here as the <c>Mock&lt;IFoo&gt;</c>, which
    /// is unwrapped rather than mocked — asking Moq to mock a <c>Mock</c> is never what was meant.
    /// <see cref="SetupServiceCollection"/> owns that parameter and runs afterwards, replacing what
    /// is returned here, so the two spellings converge on the one mock.
    /// </remarks>
    /// <param name="type">The type to mock, or a <c>Mock&lt;T&gt;</c> naming it.</param>
    /// <returns>
    /// The mocked instance — <c>Mock&lt;T&gt;.Object</c> — or a <c>Mock&lt;T&gt;</c> when that is
    /// what was asked for.
    /// </returns>
    public object ProvideMock(Type type) {
        if (TryGetMockedType(type, out var mockedType)) {
            return CreateMock(mockedType);
        }

        return CreateMock(type).Object;
    }

    /// <summary>
    /// True for a type this attribute registers itself: a <c>Mock&lt;T&gt;</c> the test asked for,
    /// or the <c>T</c> behind it.
    /// </summary>
    /// <remarks>
    /// Which is what keeps <c>[Mock] IFoo</c> and <c>Mock&lt;IFoo&gt;</c> on one test resolving to a
    /// matched pair. [Mock] registers after the setup attributes now, so that it beats a
    /// [TestExport] naming the same service — and without this it would also beat the pairing here,
    /// leaving the test configuring one mock while the container handed out another.
    /// </remarks>
    public bool RegistersService(ITestMethodContext testMethod, Type serviceType) {
        foreach (var parameter in testMethod.Method.GetParameters()) {
            if (!TryGetMockedType(parameter.ParameterType, out var mockedType)) {
                continue;
            }

            if (parameter.ParameterType == serviceType || mockedType == serviceType) {
                return true;
            }
        }

        return false;
    }

    private static MoqLib.Mock CreateMock(Type type) =>
        (MoqLib.Mock)Activator.CreateInstance(typeof(MoqLib.Mock<>).MakeGenericType(type))!;

    /// <summary>
    /// Reads <c>IFoo</c> out of a <c>Mock&lt;IFoo&gt;</c>, and reports anything else as not ours.
    /// </summary>
    private static bool TryGetMockedType(Type parameterType, [NotNullWhen(true)] out Type? mockedType) {
        if (parameterType.IsGenericType &&
            parameterType.GetGenericTypeDefinition() == typeof(MoqLib.Mock<>)) {
            mockedType = parameterType.GetGenericArguments()[0];
            return true;
        }

        mockedType = null;
        return false;
    }
}
