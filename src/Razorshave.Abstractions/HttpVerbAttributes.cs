namespace Razorshave.Abstractions;

/// <summary>Declares an HTTP GET route segment relative to the containing interface's [ApiRoute].</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class GetAttribute(string path = "") : Attribute
{
    public string Path { get; } = path;
}

/// <summary>Declares an HTTP POST route segment relative to the containing interface's [ApiRoute].</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PostAttribute(string path = "") : Attribute
{
    public string Path { get; } = path;
}

/// <summary>Declares an HTTP PUT route segment relative to the containing interface's [ApiRoute].</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PutAttribute(string path = "") : Attribute
{
    public string Path { get; } = path;
}

/// <summary>Declares an HTTP DELETE route segment relative to the containing interface's [ApiRoute].</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DeleteAttribute(string path = "") : Attribute
{
    public string Path { get; } = path;
}
