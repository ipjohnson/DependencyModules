using DependencyModules.Runtime.Interception;
using Xunit;

namespace DependencyModules.Tests.RuntimeTests;

/// <summary>
/// The runtime interception types exercised through a wrapper written by hand in exactly the shape
/// the generator emits.
/// </summary>
/// <remarks>
/// This is the reference the generator is coded against, and it fails for a reason the generator
/// tests cannot distinguish: it holds no generator, so a failure here is the runtime types not
/// composing rather than the emitted text being wrong. Written first because a shape problem found
/// this way costs minutes, and the same problem found through the generator costs an afternoon.
///
/// The wrapper below is therefore deliberately mechanical. State classes are numbered rather than
/// named after their member, because overloads would collide; argument fields are <c>_arg0</c>
/// onwards, because a parameter named <c>Self</c> or a keyword would collide too.
/// </remarks>
public class InvocationPipelineTests {

    public interface IWork {
        int Double(int value, string label);

        void Record(string entry);

        Task<int> ComputeAsync(int value);

        IAsyncEnumerable<int> Stream(int count);
    }

    [Fact]
    public void SyncMember_ReturnsTheInnerResultThroughBothInterceptors() {
        var fixture = new Fixture();

        var result = fixture.Service.Double(21, "label");

        Assert.Equal(42, result);
        Assert.Equal(["Double(21, label)"], fixture.Implementation.Calls);
    }

    /// <summary>
    /// Interceptors nest: the first declared wraps the second, so it enters first and exits last.
    /// </summary>
    [Fact]
    public void SeveralInterceptors_NestInDeclarationOrder() {
        var fixture = new Fixture();

        fixture.Service.Double(1, "label");

        Assert.Equal([
            "first enter IWork.Double",
            "second enter IWork.Double",
            "second exit IWork.Double",
            "first exit IWork.Double"
        ], fixture.Log);
    }

    [Fact]
    public void VoidMember_RoundTripsThroughNoResult() {
        var fixture = new Fixture();

        fixture.Service.Record("entry");

        Assert.Equal(["Record(entry)"], fixture.Implementation.Calls);
        Assert.Equal("void", default(NoResult).ToString());
    }

    /// <summary>
    /// Writing an argument replaces the value the implementation receives, because the fields written
    /// through the indexer are the fields the last stage passes on.
    /// </summary>
    [Fact]
    public void WritingAnArgument_ReplacesWhatTheImplementationReceives() {
        var fixture = new Fixture();
        fixture.First.BeforeProceed = arguments => arguments[0] = 5;

        var result = fixture.Service.Double(21, "label");

        Assert.Equal(10, result);
        Assert.Equal(["Double(5, label)"], fixture.Implementation.Calls);
    }

    [Fact]
    public void Arguments_ReadByPositionAndName() {
        var fixture = new Fixture();
        var seen = new List<string>();

        fixture.First.BeforeProceed = arguments => {
            for (var i = 0; i < arguments.Count; i++) {
                seen.Add($"{arguments.NameAt(i)}={arguments[i]}");
            }
        };

        fixture.Service.Double(21, "label");

        Assert.Equal(["value=21", "label=label"], seen);
    }

    [Fact]
    public void Caller_CarriesTheInterfaceAndTheMember() {
        var fixture = new Fixture();
        CallerInfo caller = default;

        fixture.First.BeforeCall = context => caller = context;

        fixture.Service.Double(1, "label");

        Assert.Equal(typeof(IWork), caller.ServiceType);
        Assert.Equal("Double", caller.MemberName);
    }

    /// <summary>
    /// The stage index lives on the context rather than the state, so proceeding twice re-enters the
    /// same next stage. A mutable index would walk past it and call the implementation once.
    /// </summary>
    [Fact]
    public void ProceedingTwice_ReEntersTheSameStage() {
        var fixture = new Fixture();
        fixture.First.ProceedCount = 2;

        var result = fixture.Service.Double(21, "label");

        Assert.Equal(42, result);
        Assert.Equal(["Double(21, label)", "Double(21, label)"], fixture.Implementation.Calls);
        Assert.Equal(2, fixture.Log.Count(entry => entry == "second enter IWork.Double"));
    }

    [Fact]
    public void NotProceeding_SkipsTheImplementationAndEverythingBelow() {
        var fixture = new Fixture();
        fixture.First.Substitute = 7;

        var result = fixture.Service.Double(21, "label");

        Assert.Equal(7, result);
        Assert.Empty(fixture.Implementation.Calls);
        Assert.DoesNotContain("second enter IWork.Double", fixture.Log);
    }

    [Fact]
    public void AnException_PropagatesThroughThePipeline() {
        var fixture = new Fixture();
        fixture.Implementation.Throw = true;

        Assert.Throws<InvalidOperationException>(() => fixture.Service.Double(1, "label"));

        Assert.Equal(["second exit IWork.Double", "first exit IWork.Double"],
            fixture.Log.Where(entry => entry.Contains("exit")).ToArray());
    }

    /// <summary>
    /// The failure this guards against is measuring the handoff of a task rather than the work: an
    /// interceptor awaits inside its own method body, so what follows the await runs when the call
    /// has finished.
    /// </summary>
    [Fact]
    public async Task AsyncMember_ExitsWhenTheWorkFinishesRatherThanWhenTheTaskIsHandedBack() {
        var fixture = new Fixture();

        var task = fixture.Service.ComputeAsync(21);

        Assert.DoesNotContain(fixture.Log, entry => entry.Contains("exit"));

        var result = await task;

        Assert.Equal(42, result);
        Assert.Equal([
            "first enter IWork.ComputeAsync",
            "second enter IWork.ComputeAsync",
            "second exit IWork.ComputeAsync",
            "first exit IWork.ComputeAsync"
        ], fixture.Log);
    }

    /// <summary>
    /// A stream hands its enumerable back immediately, so an interceptor that treated it as an
    /// ordinary value would observe the construction of the iterator and nothing else.
    /// </summary>
    [Fact]
    public async Task StreamMember_ObservesEachItemAsItIsProduced() {
        var fixture = new Fixture();

        var items = new List<int>();

        await foreach (var item in fixture.Service.Stream(3)) {
            items.Add(item);
        }

        Assert.Equal([0, 1, 2], items);
        Assert.Equal([
            "first enter IWork.Stream",
            "second enter IWork.Stream",
            "second item 0",
            "first item 0",
            "second item 1",
            "first item 1",
            "second item 2",
            "first item 2",
            "second exit IWork.Stream",
            "first exit IWork.Stream"
        ], fixture.Log);
    }

    private sealed class Fixture {

        public Fixture() {
            Log = [];
            Implementation = new WorkImplementation();
            First = new TestInterceptor("first", Log);
            Second = new TestInterceptor("second", Log);
            Service = new Work_Intercepted(Implementation, First, Second);
        }

        public List<string> Log { get; }

        public WorkImplementation Implementation { get; }

        public TestInterceptor First { get; }

        public TestInterceptor Second { get; }

        public IWork Service { get; }
    }

    private sealed class WorkImplementation : IWork {

        public List<string> Calls { get; } = [];

        public bool Throw { get; set; }

        public int Double(int value, string label) {
            Calls.Add($"Double({value}, {label})");

            if (Throw) {
                throw new InvalidOperationException("boom");
            }

            return value * 2;
        }

        public void Record(string entry) {
            Calls.Add($"Record({entry})");
        }

        public async Task<int> ComputeAsync(int value) {
            await Task.Delay(20);

            Calls.Add($"ComputeAsync({value})");

            return value * 2;
        }

        public async IAsyncEnumerable<int> Stream(int count) {
            for (var i = 0; i < count; i++) {
                await Task.Yield();

                yield return i;
            }
        }
    }

    /// <summary>
    /// One interceptor covering all three interfaces, with the behaviour a test needs set on it
    /// rather than expressed as another type, so the wrapper's typed fields stay one type.
    /// </summary>
    private sealed class TestInterceptor(string name, List<string> log)
        : IInterceptor, IAsyncInterceptor, IAsyncEnumerableInterceptor {

        public Action<IArguments>? BeforeProceed { get; set; }

        public Action<CallerInfo>? BeforeCall { get; set; }

        public int ProceedCount { get; set; } = 1;

        public object? Substitute { get; set; }

        public TResult Intercept<TResult>(InvocationContext<TResult> context) {
            log.Add($"{name} enter {context.Caller}");
            BeforeCall?.Invoke(context.Caller);
            BeforeProceed?.Invoke(context.Arguments);

            if (Substitute != null) {
                return (TResult)Substitute;
            }

            try {
                var result = default(TResult)!;

                for (var i = 0; i < ProceedCount; i++) {
                    result = context.Proceed();
                }

                return result;
            } finally {
                log.Add($"{name} exit {context.Caller}");
            }
        }

        public async ValueTask<TResult> InterceptAsync<TResult>(AsyncInvocationContext<TResult> context) {
            log.Add($"{name} enter {context.Caller}");
            BeforeCall?.Invoke(context.Caller);
            BeforeProceed?.Invoke(context.Arguments);

            if (Substitute != null) {
                return (TResult)Substitute;
            }

            try {
                var result = default(TResult)!;

                for (var i = 0; i < ProceedCount; i++) {
                    result = await context.ProceedAsync();
                }

                return result;
            } finally {
                log.Add($"{name} exit {context.Caller}");
            }
        }

        public async IAsyncEnumerable<TItem> InterceptStream<TItem>(StreamInvocationContext<TItem> context) {
            log.Add($"{name} enter {context.Caller}");
            BeforeCall?.Invoke(context.Caller);
            BeforeProceed?.Invoke(context.Arguments);

            await foreach (var item in context.Proceed()) {
                log.Add($"{name} item {item}");

                yield return item;
            }

            log.Add($"{name} exit {context.Caller}");
        }
    }

    /// <summary>
    /// Stands in for generated output. Every construct here is one the generator emits.
    /// </summary>
    private sealed class Work_Intercepted : IWork {
        private readonly IWork _inner;
        private readonly TestInterceptor _i0;
        private readonly TestInterceptor _i1;

        private static readonly CallerInfo Caller0 = new(typeof(IWork), "Double");
        private static readonly CallerInfo Caller1 = new(typeof(IWork), "Record");
        private static readonly CallerInfo Caller2 = new(typeof(IWork), "ComputeAsync");
        private static readonly CallerInfo Caller3 = new(typeof(IWork), "Stream");

        public Work_Intercepted(IWork inner, TestInterceptor i0, TestInterceptor i1) {
            _inner = inner;
            _i0 = i0;
            _i1 = i1;
        }

        public int Double(int value, string label) {
            var state = new State0(this, value, label);

            return state.Invoke(0);
        }

        public void Record(string entry) {
            var state = new State1(this, entry);

            state.Invoke(0);
        }

        public Task<int> ComputeAsync(int value) {
            var state = new State2(this, value);

            return state.Invoke(0).AsTask();
        }

        public IAsyncEnumerable<int> Stream(int count) {
            var state = new State3(this, count);

            return state.Invoke(0);
        }

        private sealed class State0 : InvocationState<int> {
            private readonly Work_Intercepted _self;
            private int _arg0;
            private string _arg1;

            public State0(Work_Intercepted self, int arg0, string arg1) {
                _self = self;
                _arg0 = arg0;
                _arg1 = arg1;
            }

            public override CallerInfo Caller => Caller0;

            public override int Count => 2;

            public override object? this[int index] {
                get =>
                    index switch {
                        0 => _arg0,
                        1 => _arg1,
                        _ => throw new ArgumentOutOfRangeException(nameof(index))
                    };
                set {
                    switch (index) {
                        case 0:
                            _arg0 = (int)value!;
                            break;
                        case 1:
                            _arg1 = (string)value!;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(index));
                    }
                }
            }

            public override string NameAt(int index) =>
                index switch {
                    0 => "value",
                    1 => "label",
                    _ => throw new ArgumentOutOfRangeException(nameof(index))
                };

            public override int Invoke(int stage) {
                switch (stage) {
                    case 0:
                        return _self._i0.Intercept(new InvocationContext<int>(this, 0));
                    case 1:
                        return _self._i1.Intercept(new InvocationContext<int>(this, 1));
                    default:
                        return _self._inner.Double(_arg0, _arg1);
                }
            }
        }

        private sealed class State1 : InvocationState<NoResult> {
            private readonly Work_Intercepted _self;
            private string _arg0;

            public State1(Work_Intercepted self, string arg0) {
                _self = self;
                _arg0 = arg0;
            }

            public override CallerInfo Caller => Caller1;

            public override int Count => 1;

            public override object? this[int index] {
                get =>
                    index switch {
                        0 => _arg0,
                        _ => throw new ArgumentOutOfRangeException(nameof(index))
                    };
                set {
                    switch (index) {
                        case 0:
                            _arg0 = (string)value!;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(index));
                    }
                }
            }

            public override string NameAt(int index) =>
                index switch {
                    0 => "entry",
                    _ => throw new ArgumentOutOfRangeException(nameof(index))
                };

            public override NoResult Invoke(int stage) {
                switch (stage) {
                    case 0:
                        return _self._i0.Intercept(new InvocationContext<NoResult>(this, 0));
                    case 1:
                        return _self._i1.Intercept(new InvocationContext<NoResult>(this, 1));
                    default:
                        _self._inner.Record(_arg0);

                        return default;
                }
            }
        }

        private sealed class State2 : AsyncInvocationState<int> {
            private readonly Work_Intercepted _self;
            private int _arg0;

            public State2(Work_Intercepted self, int arg0) {
                _self = self;
                _arg0 = arg0;
            }

            public override CallerInfo Caller => Caller2;

            public override int Count => 1;

            public override object? this[int index] {
                get =>
                    index switch {
                        0 => _arg0,
                        _ => throw new ArgumentOutOfRangeException(nameof(index))
                    };
                set {
                    switch (index) {
                        case 0:
                            _arg0 = (int)value!;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(index));
                    }
                }
            }

            public override string NameAt(int index) =>
                index switch {
                    0 => "value",
                    _ => throw new ArgumentOutOfRangeException(nameof(index))
                };

            public override ValueTask<int> Invoke(int stage) {
                switch (stage) {
                    case 0:
                        return _self._i0.InterceptAsync(new AsyncInvocationContext<int>(this, 0));
                    case 1:
                        return _self._i1.InterceptAsync(new AsyncInvocationContext<int>(this, 1));
                    default:
                        return new ValueTask<int>(_self._inner.ComputeAsync(_arg0));
                }
            }
        }

        private sealed class State3 : StreamInvocationState<int> {
            private readonly Work_Intercepted _self;
            private int _arg0;

            public State3(Work_Intercepted self, int arg0) {
                _self = self;
                _arg0 = arg0;
            }

            public override CallerInfo Caller => Caller3;

            public override int Count => 1;

            public override object? this[int index] {
                get =>
                    index switch {
                        0 => _arg0,
                        _ => throw new ArgumentOutOfRangeException(nameof(index))
                    };
                set {
                    switch (index) {
                        case 0:
                            _arg0 = (int)value!;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(index));
                    }
                }
            }

            public override string NameAt(int index) =>
                index switch {
                    0 => "count",
                    _ => throw new ArgumentOutOfRangeException(nameof(index))
                };

            public override IAsyncEnumerable<int> Invoke(int stage) {
                switch (stage) {
                    case 0:
                        return _self._i0.InterceptStream(new StreamInvocationContext<int>(this, 0));
                    case 1:
                        return _self._i1.InterceptStream(new StreamInvocationContext<int>(this, 1));
                    default:
                        return _self._inner.Stream(_arg0);
                }
            }
        }
    }
}
