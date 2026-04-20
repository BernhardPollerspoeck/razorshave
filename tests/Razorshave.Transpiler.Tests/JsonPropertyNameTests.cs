using Microsoft.CodeAnalysis;

using static Razorshave.Cli.Transpiler.Transpiler;

namespace Razorshave.Transpiler.Tests;

/// <summary>
/// Exercises the <c>[JsonPropertyName]</c>-aware member rewrite in
/// <c>ExpressionEmitter.ResolveMemberName</c>. The attribute lets user code
/// read snake_case JSON (e.g. open-meteo's <c>temperature_2m_max</c>)
/// through PascalCase C# properties without a manual mapping layer.
/// </summary>
public sealed class JsonPropertyNameTests
{
    [Fact]
    public void Nested_class_property_with_JsonPropertyName_emits_the_json_name()
    {
        var source = """
            using System.Text.Json.Serialization;
            using Razorshave.Abstractions;
            namespace Fixtures;
            [Client]
            public sealed class WeatherApi(System.Net.Http.HttpClient http) : ApiClient(http)
            {
                public async System.Threading.Tasks.Task<double> Read()
                {
                    var resp = await Get<Resp>("/x");
                    return resp.Daily.Temperature2mMax[0];
                }

                private sealed class Resp
                {
                    [JsonPropertyName("daily")]
                    public DailyData Daily { get; set; } = new();
                }

                private sealed class DailyData
                {
                    [JsonPropertyName("temperature_2m_max")]
                    public double[] Temperature2mMax { get; set; } = [];
                }
            }
            """;

        // Mirrors how BuildCommand feeds project-bin DLLs into transpile:
        // full reference list = SharedFramework + Razorshave.Abstractions.
        var refs = Razorshave.Cli.Transpiler.MetadataReferenceLoader.SharedFramework()
            .Append((MetadataReference)MetadataReference.CreateFromFile(
                typeof(Razorshave.Abstractions.ClientAttribute).Assembly.Location))
            .ToList();

        var js = TranspileClientClass(source, refs);

        Assert.Contains("temperature_2m_max", js);
        Assert.DoesNotContain("temperature2mMax", js);
    }
}
