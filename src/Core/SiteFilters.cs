using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pulso;

[JsonConverter(typeof(SiteFilterModeConverter))]
public enum SiteFilterMode { Off, Focus, Always }

internal readonly record struct SiteFilterPolicy(long Porn, long Gambling)
{
    public int ActiveCount => (Porn != 0 ? 1 : 0) + (Gambling != 0 ? 1 : 0);

    public bool IsActiveAt(DateTimeOffset now)
    {
        long end = Math.Max(Porn, Gambling);
        return Porn == -1 || Gambling == -1 || end > 0 && end > now.ToUnixTimeMilliseconds();
    }

    public static SiteFilterPolicy At(FocusData data, DateTimeOffset? focusEnd)
    {
        long Deadline(SiteFilterMode mode) => mode switch
        {
            SiteFilterMode.Always => -1,
            SiteFilterMode.Focus => focusEnd?.ToUnixTimeMilliseconds() ?? 0,
            _ => 0
        };
        return new(Deadline(data.PornFilter), Deadline(data.GamblingFilter));
    }
}

internal sealed class SiteFilterModeConverter : JsonConverter<SiteFilterMode>
{
    public override SiteFilterMode Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString() switch { "focus" => SiteFilterMode.Focus, "always" => SiteFilterMode.Always, _ => SiteFilterMode.Off };
        if (!reader.TrySkip()) throw new JsonException();
        return SiteFilterMode.Off;
    }

    public override void Write(Utf8JsonWriter writer, SiteFilterMode value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch { SiteFilterMode.Focus => "focus", SiteFilterMode.Always => "always", _ => "off" });
}
