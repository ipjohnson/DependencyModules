using CSharpAuthor;

namespace DependencyModules.SourceGenerator.Impl.Utilities;

public static class BaseComponentHelper {
    private class NullableEnableComponent : BaseOutputComponent {
        private bool _enable;

        public NullableEnableComponent(bool enable) {
            _enable = enable;
        }

        protected override void WriteComponentOutput(IOutputContext outputContext) {
            var enableString = _enable ? "enable" : "disable";
            outputContext.WriteIndentedLine($"#nullable {enableString}");
        }
    }
    public static void NullableEnable(this BaseOutputComponent baseOutputComponent) {
        baseOutputComponent.AddLeadingTrait(new NullableEnableComponent(true));
        baseOutputComponent.AddTrailingTrait(new NullableEnableComponent(false));
    }
}