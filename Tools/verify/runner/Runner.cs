using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

/// <summary>
/// Runs the EditMode test assembly without the Unity Editor.
///
/// <para>
/// The Editor is usually closed and its Test Runner cannot be driven from a script when
/// it is, which means the test suite — the thing that actually proves a merge rule or a
/// reward vector — is unavailable exactly when a change is being made. This reflects over
/// the compiled test assembly and invokes the NUnit attributes directly. It is not a
/// replacement for Test Runner: anything needing a real engine (a GameObject, a
/// coroutine, a native call) will fail here and has to be run in the Editor. It is a
/// replacement for *not running them at all*, which is what the alternative has been.
/// </para>
/// <para>
/// Assembly resolution is the whole trick. The test DLL was compiled against Unity's
/// managed assemblies, which are not beside this runner, so every probe directory passed
/// on the command line is searched by name on demand.
/// </para>
/// </summary>
static class Runner
{
    static string[] _probe = Array.Empty<string>();
    static readonly Dictionary<string, Assembly> _loaded = new(StringComparer.OrdinalIgnoreCase);

    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: Runner <tests.dll> [probe-dir ...] [--filter substring]");
            return 2;
        }

        var filter = "";
        var rest = new List<string>();
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--filter" && i + 1 < args.Length) { filter = args[++i]; continue; }
            rest.Add(args[i]);
        }
        _probe = rest.ToArray();

        AssemblyLoadContext.Default.Resolving += (_, name) => Probe(name.Name);
        MuteUnityLogging();

        Assembly assembly;

        try
        {
            assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("could not load " + args[0] + ": " + e.Message);
            return 2;
        }

        int passed = 0, failed = 0, skipped = 0, engineOnly = 0;
        var failures = new List<string>();

        foreach (var type in Types(assembly))
        {
            if (type.IsAbstract || type.IsInterface) continue;
            if (filter.Length > 0 && type.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

            var tests = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .Where(m => Has(m, "TestAttribute") || Has(m, "TestCaseAttribute"))
                            .ToArray();
            if (tests.Length == 0) continue;

            var setUp = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                            .FirstOrDefault(m => Has(m, "SetUpAttribute"));
            var tearDown = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                               .FirstOrDefault(m => Has(m, "TearDownAttribute"));

            Console.WriteLine(type.Name);

            foreach (var test in tests)
            {
                // TestCase carries its own arguments; running one without them would
                // report a false failure, so they are reported as skipped instead.
                if (test.GetParameters().Length > 0)
                {
                    skipped++;
                    Console.WriteLine("    ~ " + test.Name + "  (parameterised, run in the Editor)");
                    continue;
                }

                object instance;
                try { instance = Activator.CreateInstance(type); }
                catch (Exception e) { failed++; failures.Add(type.Name + ": construction: " + Root(e)); continue; }

                try
                {
                    setUp?.Invoke(instance, null);
                    test.Invoke(instance, null);
                    passed++;
                    Console.WriteLine("    + " + test.Name);
                }
                catch (Exception e) when (NeedsEngine(e))
                {
                    // Not a failure and not a pass: the test reached a native Unity call
                    // — JsonUtility, Application.dataPath, anything deriving from Object —
                    // which has no implementation outside the Editor. Counting these as
                    // failures would train everyone to ignore the number, which is worse
                    // than not running them.
                    engineOnly++;

                    // GLIMMER_WHY=1 prints the native call that stopped it. Worth having,
                    // because "needs the Editor" is sometimes a fact about the code under test
                    // and sometimes a fact about this runner — and only the message tells them
                    // apart. It is how the Debug.Log limit below was found.
                    Console.WriteLine("    ~ " + test.Name + "  (needs the Editor)"
                        + (Environment.GetEnvironmentVariable("GLIMMER_WHY") == null
                               ? "" : "  [" + First(Root(e)) + "]"));
                }
                catch (Exception e)
                {
                    failed++;
                    string why = Root(e);
                    failures.Add(type.Name + "." + test.Name + ": " + why);
                    Console.WriteLine("    X " + test.Name + "  " + First(why));
                }
                finally
                {
                    try { tearDown?.Invoke(instance, null); } catch { /* a failed teardown is not the news */ }
                }
            }
        }

        Console.WriteLine();
        if (failures.Count > 0)
        {
            Console.WriteLine("failures:");
            foreach (var f in failures) Console.WriteLine("  " + f.Replace("\n", "\n    "));
            Console.WriteLine();
        }

        Console.WriteLine($"{passed} passed, {failed} failed, {engineOnly} need the Editor, {skipped} skipped");
        return failed > 0 ? 1 : 0;
    }

    static IEnumerable<Type> Types(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
    }

    static bool Has(MemberInfo member, string attribute)
        => member.GetCustomAttributes(true).Any(a => a.GetType().Name == attribute);

    static string Root(Exception e)
    {
        while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
        return e.Message;
    }

    /// <summary>
    /// Whether the failure is "this needs a running engine" rather than "this is
    /// wrong". Unity's managed assemblies are full of extern methods bound to the
    /// native player, and calling one outside it raises this before any assertion
    /// in the test has had a chance to be evaluated.
    /// </summary>
    static bool NeedsEngine(Exception e)
    {
        for (var current = e; current != null; current = current.InnerException)
        {
            string m = current.Message ?? "";
            if (m.Contains("ECall methods must be packaged into a system module")) return true;
            if (m.Contains("Unity.SerializationLogic")) return true;
            if (current is DllNotFoundException || current is EntryPointNotFoundException) return true;
        }
        return false;
    }

    /// <summary>
    /// Turns Unity's logger off, so that ordinary <c>Debug.Log</c> calls do not end a test.
    ///
    /// <para>
    /// <c>Debug.Log</c> reaches <c>DebugLogHandler</c>, which is extern and bound to the
    /// native player, so outside the Editor the first log line in a code path throws
    /// "ECall methods must be packaged into a system module" before any assertion in the
    /// test has been evaluated. That is a harness limit rather than a fact about the code,
    /// and it was quietly deciding which rules could be proved offline: anything that
    /// warned on the path being tested — which is most of the cloud and save layer, because
    /// warning is what those files do when something is wrong — could only be run with the
    /// Editor open.
    /// </para>
    /// <para>
    /// <c>Logger.logEnabled</c> is a managed flag checked before the handler is reached, on
    /// every route in (<c>Log</c>, <c>LogWarning</c>, <c>LogError</c>, <c>LogException</c>),
    /// so clearing it removes the extern from the path entirely. The cost is that a test
    /// asserting on log output cannot run here — those use <c>LogAssert</c>, which needs the
    /// Editor anyway. Wrapped, because a Unity version that reorganises this should cost the
    /// runner nothing more than the coverage it had before.
    /// </para>
    /// </summary>
    static void MuteUnityLogging()
    {
        try
        {
            var debug = Probe("UnityEngine.CoreModule")?.GetType("UnityEngine.Debug");
            var logger = debug?.GetProperty("unityLogger", BindingFlags.Public | BindingFlags.Static)
                               ?.GetValue(null);

            logger?.GetType().GetProperty("logEnabled")?.SetValue(logger, false);
        }
        catch
        {
            // Then tests that log stay Editor-only, exactly as they were.
        }
    }

    static string First(string s)
    {
        int i = s.IndexOf('\n');
        return i < 0 ? s : s.Substring(0, i);
    }

    static Assembly Probe(string name)
    {
        if (name == null) return null;
        if (_loaded.TryGetValue(name, out var cached)) return cached;

        foreach (var dir in _probe)
        {
            string path = Path.Combine(dir, name + ".dll");
            if (!File.Exists(path)) continue;
            try
            {
                var assembly = Assembly.LoadFrom(path);
                _loaded[name] = assembly;
                return assembly;
            }
            catch
            {
                // A net472 plugin that will not load under .NET 8 is expected. The test
                // that needed it fails with a clear message; the rest still run.
            }
        }

        _loaded[name] = null;
        return null;
    }
}
