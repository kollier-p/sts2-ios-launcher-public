using STS2Mobile.Platform;

namespace STS2Mobile.Platform.Tests;

public sealed class ModRuntimeCapabilitiesTests
{
    [Fact]
    public void DynamicCodeProbeReportsConsistentResults()
    {
        var result = ModRuntimeCapabilities.ProbeDynamicMethod();
        Assert.NotEmpty(result.Detail);
        if (result.DynamicMethodExecuted)
        {
            Assert.True(result.RuntimeReportsDynamicCodeSupported);
            Assert.True(result.RuntimeReportsDynamicCodeCompiled);
        }
        Assert.Equal(
            result.RuntimeReportsDynamicCodeSupported
            && result.RuntimeReportsDynamicCodeCompiled
            && result.DynamicMethodExecuted,
            result.CandidateForDynamicPatching);
    }
}
