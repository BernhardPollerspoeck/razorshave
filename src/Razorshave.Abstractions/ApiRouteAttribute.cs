namespace Razorshave.Abstractions;

/// <summary>
/// Declares the base HTTP route for an API-client interface. Combined with
/// HTTP verb attributes (<see cref="GetAttribute"/>, <see cref="PostAttribute"/> etc.)
/// on the interface methods, Razorshave's source generator produces a typed client
/// implementation at build time.
/// </summary>
[AttributeUsage(AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class ApiRouteAttribute(string basePath) : Attribute
{
    public string BasePath { get; } = basePath;
}
