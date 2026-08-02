namespace Lightbox.Raster.Tests;

/// <summary>
/// The engine's registries are process-wide, so tests that point them at
/// something must not run beside tests that point them somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PaletteRegistry"/>, <see cref="ClipRegionRegistry"/> and
/// <see cref="SymbolRegistry"/> are static by design: the engine resolves a
/// swatch, a clip or a symbol by id at render time, and the registry is what
/// makes that possible without threading a document through every call. The
/// cost is that <c>Clear()</c> in one test class empties the registry a class
/// running in parallel had just filled.
/// </para>
/// <para>
/// xUnit parallelises by collection, so naming one here is what serialises
/// them. Every class that calls Register, Reset or Clear on a registry — or
/// that reads <c>SymbolRasterizer.CachedFrames</c>, which the same Clear
/// empties — belongs in it.
/// </para>
/// <para>
/// This is the same product smell as the brush store next door and it is
/// written down for the same reason: the isolation is a workaround for global
/// state, not a property of the tests.
/// </para>
/// </remarks>
[CollectionDefinition("Registries", DisableParallelization = true)]
public class RegistryCollection;
