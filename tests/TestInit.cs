using System.Runtime.CompilerServices;
using Brewvio.Services;

namespace Brewvio.Tests;

// Runs once when the test assembly is loaded, before any test executes.
internal static class TestInit
{
    // The tax-rate cache is a process-global static designed for warm Lambda containers.
    // In the test suite (parallel classes sharing the same process) it leaks across tests,
    // so a test reading a 0% rate can poison a parallel test that expects 12%. Disabling the
    // cache makes every GetTaxRateAsync read the test's own transaction-isolated DB value,
    // removing the flakiness without changing production behaviour.
    [ModuleInitializer]
    internal static void Init() => SettingsService.TaxRateCacheEnabled = false;
}
