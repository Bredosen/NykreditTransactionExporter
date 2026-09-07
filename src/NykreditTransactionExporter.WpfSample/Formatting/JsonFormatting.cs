using System.Text.Json;

namespace NykreditTransactionExporter.WpfSample.Formatting;

internal static class JsonFormatting
{
    #region Static Fields
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true
    };
    #endregion

    #region Format JSON element
    public static string Format(JsonElement element)
    {
        return JsonSerializer.Serialize(element, IndentedOptions);
    }
    #endregion
}
