using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace STS2Mobile.Platform;

/// <summary>
/// Diagnostics for an experimental dynamic-code backend. This does NOT turn the
/// bundled Mono runtime into a JIT runtime and does NOT establish that Harmony
/// native detours are supported. Run the executable probe only on explicit user
/// request; normal app startup must never depend on it.
/// </summary>
public static class ModRuntimeCapabilities
{
    public sealed record Snapshot(
        bool RuntimeReportsDynamicCodeSupported,
        bool RuntimeReportsDynamicCodeCompiled,
        bool DynamicMethodExecuted,
        string Detail)
    {
        // Executing the dynamic method is stronger evidence than feature flags
        // in an experimental runtime whose iOS CoreLib may still report AOT.
        // It does not establish that Harmony native detours are supported.
        public bool CandidateForDynamicPatching => DynamicMethodExecuted;
    }

    /// <summary>
    /// Checks whether the current .NET runtime can create and invoke a dynamic
    /// method. This is deliberately stronger than checking for a jailbreak or
    /// the presence of a debugger, but still does not test executable-page
    /// detours, MonoMod, or Harmony.
    /// </summary>
    public static Snapshot ProbeDynamicMethod()
    {
        bool supported = RuntimeFeature.IsDynamicCodeSupported;
        bool compiled = RuntimeFeature.IsDynamicCodeCompiled;
        // Feature flags can remain false in a CoreLib published for iOS even
        // when the experimental native Mono library includes a working JIT.
        // This diagnostic is user-initiated only, so test execution directly.

        try
        {
            var method = new DynamicMethod("sts2_jit_probe", typeof(int), Type.EmptyTypes,
                typeof(ModRuntimeCapabilities).Module, skipVisibility: false);
            var il = method.GetILGenerator();
            il.Emit(OpCodes.Ldc_I4, 42);
            il.Emit(OpCodes.Ret);
            var action = (Func<int>)method.CreateDelegate(typeof(Func<int>));
            if (action() != 42)
                return new Snapshot(supported, compiled, false, "Dynamic method returned the wrong value.");
            return new Snapshot(supported, compiled, true,
                "A dynamic .NET method executed. Mono feature flags: supported=" + supported
                + ", compiled=" + compiled + ". Harmony detours still require separate verification.");
        }
        catch (Exception ex) when (ex is PlatformNotSupportedException or NotSupportedException
                                   or InvalidOperationException or TypeLoadException
                                   or MemberAccessException or System.Security.SecurityException)
        {
            return new Snapshot(supported, compiled, false,
                "Dynamic method execution failed (" + ex.GetType().Name + "): " + ex.Message
                + ". Mono runtime feature flags: supported=" + supported + ", compiled=" + compiled + ".");
        }
    }
}
