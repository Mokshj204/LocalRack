using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalRack.Models;

namespace LocalRack.Services;

[JsonSerializable(typeof(List<Project>))]
[JsonSerializable(typeof(UiState))]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppJsonContext : JsonSerializerContext
{
    // Relaxed escaping keeps shell characters like & and > readable in the hand-editable config file.
    public static AppJsonContext Readable { get; } = new(new JsonSerializerOptions
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    });
}
