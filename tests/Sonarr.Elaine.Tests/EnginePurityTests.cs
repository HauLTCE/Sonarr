using System.Reflection;
using System.Runtime.CompilerServices;
using Sonarr.Elaine.Persona;

namespace Sonarr.Elaine.Tests;

/// <summary>
/// The engine's purity is a design constraint, so it gets a test rather than a code review
/// convention: no Discord, no EF/Npgsql, no Redis, no HTTP, no ambient clock or RNG.
/// </summary>
public class EnginePurityTests
{
    private static readonly string[] Banned =
        ["Discord", "EntityFrameworkCore", "Npgsql", "StackExchange.Redis", "Lavalink", "Serilog"];

    [Fact]
    public void Engine_ReferencesNothingButTheFrameworkAndYamlDotNet()
    {
        Assembly engine = typeof(PersonaGraph).Assembly;
        List<string> referenced = [.. engine.GetReferencedAssemblies().Select(a => a.Name ?? "")];

        Assert.DoesNotContain(referenced, r => Banned.Any(b => r.Contains(b, StringComparison.Ordinal)));
        Assert.Contains("YamlDotNet", referenced);
    }

    [Fact]
    public void Engine_HasNoMutableStaticState()
    {
        // Mutable statics are how ambient time and shared RNG sneak back in, and they would
        // also make hot-reload non-atomic. Everything the engine needs is injected.
        List<string> offenders = [];
        foreach (Type type in typeof(PersonaGraph).Assembly.GetTypes())
        {
            // Skip closure/iterator classes: their static fields are the compiler's lambda
            // caches, which hold no engine state.
            if (type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            {
                continue;
            }

            FieldInfo[] fields = type.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            offenders.AddRange(fields
                .Where(f => !f.IsLiteral && !f.IsInitOnly)
                .Select(f => $"{type.FullName}.{f.Name}"));
        }

        Assert.Empty(offenders);
    }
}
