using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Avalonia.Media.Imaging;

namespace Chitin;

public static class ProjectFile
{
    public const int Version = 1;

    private static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        options.Converters.Add(new Vec2Converter());
        options.Converters.Add(new VanishingPointConverter());
        options.Converters.Add(new TraceLineConverter());
        return options;
    }

    public static string Write(World world)
    {
        JsonNode node = JsonSerializer.SerializeToNode(world.Capture(), Options)
            ?? throw new InvalidDataException("The project could not be serialized.");
        node["version"] = Version;

        string? backgroundPng = null;
        if (world.BackgroundImg is { } image)
        {
            byte[] png = World.EncodePng(image);
            if (!World.FitsInProject(png.LongLength))
            {
                throw new InvalidOperationException(
                    "The reference image is over 64 MB as PNG. Remove it (Alt+I) or use a smaller image.");
            }

            backgroundPng = Convert.ToBase64String(png);
        }

        node["backgroundPng"] = backgroundPng;
        return node.ToJsonString(Options);
    }

    // The state without version or image: two equal states give the same text.
    internal static string ToJson(WorldState state)
    {
        return JsonSerializer.Serialize(state, Options);
    }

    public static WorldState Read(string json)
    {
        JsonNode? root = JsonNode.Parse(json);
        if (root is not JsonObject obj)
        {
            throw new InvalidDataException("Project file must contain a JSON object.");
        }

        // A missing version reads as 1; a newer one (2, or 1.5) may use fields this build would drop.
        if (obj["version"] is JsonValue versionValue && versionValue.TryGetValue(out double version) &&
            version > Version)
        {
            throw new InvalidDataException(
                $"Project file version {version.ToString(CultureInfo.InvariantCulture)} is newer than this " +
                $"app can read (version {Version}).");
        }

        WorldState state = obj.Deserialize<WorldState>(Options)
            ?? throw new InvalidDataException("Project file does not contain a document.");
        if (state.Doc == null || state.Grid == null || state.Reference == null ||
            state.VanishingPoints == null || state.TraceLines == null ||
            state.Boxes == null || state.Subdivisions == null || state.Ellipses == null)
        {
            throw new InvalidDataException("Project file is missing a required section.");
        }

        Bitmap? background = null;
        if (obj.TryGetPropertyValue("backgroundPng", out JsonNode? backgroundNode) &&
            backgroundNode is JsonValue value &&
            value.TryGetValue<string>(out string? base64) &&
            !string.IsNullOrEmpty(base64))
        {
            if (base64.Length > World.MaxEncodedImageBytes)
            {
                throw new InvalidDataException("The embedded background is larger than 64 MB encoded.");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                throw new InvalidDataException("The embedded background is not valid base64.");
            }

            if (bytes.LongLength > World.MaxEncodedImageBytes)
            {
                throw new InvalidDataException("The embedded background is larger than 64 MB.");
            }

            try
            {
                background = World.DecodeImage(bytes);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    $"The embedded background could not be loaded: {exception.Message}", exception);
            }
        }

        state.Background = background;
        return state;
    }

    // The reader turns 1e999 into infinity; such a value would also make the next save fail.
    private static double Finite(double value)
    {
        return double.IsFinite(value) ? value : throw new JsonException("A coordinate is out of range.");
    }

    private sealed class Vec2Converter : JsonConverter<Vec2>
    {
        public override Vec2 Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("A point must be a two-number array.");
            }

            reader.Read();
            double x = Finite(reader.GetDouble());
            reader.Read();
            double y = Finite(reader.GetDouble());
            reader.Read();
            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException("A point must be a two-number array.");
            }

            return new Vec2(x, y);
        }

        public override void Write(Utf8JsonWriter writer, Vec2 value,
            JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteEndArray();
        }
    }

    private sealed class VanishingPointConverter : JsonConverter<VanishingPoint>
    {
        public override VanishingPoint Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            int density = root.TryGetProperty("density", out JsonElement element)
                ? element.GetInt32()
                : 0;
            return new VanishingPoint(
                new Vec2(Finite(root.GetProperty("x").GetDouble()), Finite(root.GetProperty("y").GetDouble())),
                density);
        }

        public override void Write(Utf8JsonWriter writer, VanishingPoint value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x", value.Pos.X);
            writer.WriteNumber("y", value.Pos.Y);
            writer.WriteNumber("density", value.Density);
            writer.WriteEndObject();
        }
    }

    private sealed class TraceLineConverter : JsonConverter<TraceLine>
    {
        public override TraceLine Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            using JsonDocument document = JsonDocument.ParseValue(ref reader);
            JsonElement root = document.RootElement;
            return new TraceLine(
                new Vec2(Finite(root.GetProperty("x0").GetDouble()), Finite(root.GetProperty("y0").GetDouble())),
                new Vec2(Finite(root.GetProperty("x1").GetDouble()), Finite(root.GetProperty("y1").GetDouble())));
        }

        public override void Write(Utf8JsonWriter writer, TraceLine value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("x0", value.Start.X);
            writer.WriteNumber("y0", value.Start.Y);
            writer.WriteNumber("x1", value.End.X);
            writer.WriteNumber("y1", value.End.Y);
            writer.WriteEndObject();
        }
    }
}
