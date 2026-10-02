// Portions derived from the Command & Conquer Generals / Zero Hour WorldBuilder sources
// (TheSuperHackers/GeneralsGameCode, AdrianeYves/WorldbuilderZHAdriane, triatomic/worldbuilderQT),
// licensed GPL-3.0 with EA additional terms; see NOTICE-WorldBuilder.md. Modified for GenHub.
namespace GenHub.Core.Models.Tools.WorldBuilder;

/// <summary>
/// One parsed W3D chunk: type, raw payload, and children for containers.
/// Field layouts follow WW3D2/w3d_file.h via the pysage format notes.
/// </summary>
/// <param name="Type">The chunk type id.</param>
/// <param name="Payload">The raw payload bytes (size field with the flag bit masked off).</param>
/// <param name="Children">Sub-chunks in file order; empty for leaves and unknown containers.</param>
public sealed record W3dChunk(uint Type, IList<byte> Payload, IReadOnlyList<W3dChunk> Children);
