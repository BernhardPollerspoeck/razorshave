namespace Razorshave.Abstractions;

/// <summary>
/// Marks a type as a Razorshave client-side service that may be registered in the
/// Razorshave container and injected into components. Types without this attribute
/// cannot be resolved at runtime in the transpiled output.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ClientAttribute : Attribute
{
}
