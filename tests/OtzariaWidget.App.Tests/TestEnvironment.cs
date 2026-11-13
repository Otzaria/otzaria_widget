using System.Runtime.CompilerServices;
using OtzariaWidget.App.Provider;

namespace OtzariaWidget.App.Tests;

/// <summary>Keeps unit tests away from the real %LOCALAPPDATA%\OtzariaWidget\provider.log (it is what a human reads when the board misbehaves).</summary>
internal static class TestEnvironment
{
    [ModuleInitializer]
    internal static void Init()
    {
        ProviderLog.DirectoryOverride = Path.Combine(Path.GetTempPath(), "otz-tests-log");
    }
}
