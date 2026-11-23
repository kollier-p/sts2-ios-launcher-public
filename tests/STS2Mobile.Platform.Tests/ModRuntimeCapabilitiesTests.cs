using STS2Mobile.Platform;

namespace STS2Mobile.Platform.Tests;

public sealed class ModRuntimeCapabilitiesTests
{
    [Fact]
    public void DynamicCodeProbeReportsConsistentResults()
    {
        var result = ModRuntimeCapabilities.ProbeDynamicMethod();
        Assert.NotEmpty(result.Detail);
        Assert.Equal(result.DynamicMethodExecuted, result.CandidateForDynamicPatching);
    }
}
