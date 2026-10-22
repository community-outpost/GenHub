namespace GenHub.Core.Models.Security;

/// <summary>
/// A machine-bound secret used as key material for encryption at rest.
/// </summary>
/// <param name="Secret">The secret text.</param>
/// <param name="FromPrimarySource">True when the secret came from the platform machine ID rather than the fallback.</param>
public readonly record struct MachineSecret(string Secret, bool FromPrimarySource);
