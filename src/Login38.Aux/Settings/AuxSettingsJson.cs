using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Login38.Aux.Settings;

/// <summary>
/// How a character's settings are written to disk.
/// </summary>
/// <remarks>
/// <para>
/// The shape is the one the reference wrote, down to the property names and the way it
/// spelled a cast target. Players have these files, they hold a lot of manual setup, and
/// nothing about them is worth breaking to get tidier names on disk.
/// </para>
/// <para>
/// Written unescaped and indented, because the file is beside the launcher and players do
/// open it. An item name escaped to <c>銀劍</c> is unreadable and unfixable by
/// hand.
/// </para>
/// </remarks>
public static class AuxSettingsJson
{
    /// <summary>The options every read and write goes through.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,

            // A file a player edits should read as the language it is in.
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),

            // Everything about this file is somebody's typing. A field it does not know is
            // from a newer version or a typo, and neither is worth losing the file over.
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        options.Converters.Add(new CastTargetConverter());
        options.Converters.Add(new HelperEntryConverter());

        return options;
    }
}

/// <summary>
/// Reads and writes a cast target the way the reference's own serialiser did.
/// </summary>
/// <remarks>
/// One without a target is a bare string; one with a target is an object of a single
/// property. That is what a Rust enum serialises as, and it is what is in every existing
/// file.
/// </remarks>
internal sealed class CastTargetConverter : JsonConverter<CastTarget>
{
    /// <summary>The name the reference gave the self-cast variant.</summary>
    /// <remarks>
    /// With a trailing underscore, because <c>Self</c> is a keyword in the language it was
    /// written in. Preserved because it is what is on disk.
    /// </remarks>
    private const string SelfName = "Self_";

    public override CastTarget Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return FromName(reader.GetString(), null, null);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"A cast target cannot be a {reader.TokenType}.");
        }

        reader.Read();

        if (reader.TokenType != JsonTokenType.PropertyName)
        {
            throw new JsonException("A cast target object has to name the kind it is.");
        }

        var name = reader.GetString();
        reader.Read();

        string? target = null;
        byte? key = null;

        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                target = reader.GetString();
                break;

            case JsonTokenType.Number:
                key = reader.GetByte();
                break;

            case JsonTokenType.Null:
                break;

            default:
                throw new JsonException($"A cast target's argument cannot be a {reader.TokenType}.");
        }

        reader.Read();

        if (reader.TokenType != JsonTokenType.EndObject)
        {
            throw new JsonException("A cast target holds exactly one kind.");
        }

        return FromName(name, target, key);
    }

    public override void Write(Utf8JsonWriter writer, CastTarget value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var name = ToName(value.Kind);

        switch (value.Kind)
        {
            case CastKind.OnNamedEntity or CastKind.OnNamedItem:
                writer.WriteStartObject();
                writer.WriteString(name, value.Target ?? string.Empty);
                writer.WriteEndObject();
                break;

            // These two are written even when there is no name, because null and absent
            // mean the same thing here and the reference wrote null.
            case CastKind.OnInUseItem or CastKind.OnWieldedItem:
                writer.WriteStartObject();

                if (value.Target is null)
                {
                    writer.WriteNull(name);
                }
                else
                {
                    writer.WriteString(name, value.Target);
                }

                writer.WriteEndObject();
                break;

            case CastKind.Key or CastKind.DelayKey:
                writer.WriteStartObject();
                writer.WriteNumber(name, value.FunctionKey);
                writer.WriteEndObject();
                break;

            default:
                writer.WriteStringValue(name);
                break;
        }
    }

    private static string ToName(CastKind kind) => kind switch
    {
        CastKind.OnSelf => SelfName,
        _ => kind.ToString(),
    };

    private static CastTarget FromName(string? name, string? target, byte? key) => name switch
    {
        "Item" => CastTarget.Item,
        "NoSpec" => CastTarget.Any(CastKind.NoSpec),
        SelfName or "OnSelf" => CastTarget.Any(CastKind.OnSelf),
        "HoverTarget" => CastTarget.Any(CastKind.HoverTarget),
        "SelfItem" or "OnSelfItem" => CastTarget.Any(CastKind.OnSelfItem),
        "DropItem" => CastTarget.Any(CastKind.DropItem),
        "Info" => CastTarget.Any(CastKind.Info),

        "OnNamedEntity" => CastTarget.Named(CastKind.OnNamedEntity, target ?? string.Empty),
        "OnNamedItem" => CastTarget.Named(CastKind.OnNamedItem, target ?? string.Empty),
        "OnInUseItem" => new CastTarget(CastKind.OnInUseItem, target),
        "OnWieldedItem" => new CastTarget(CastKind.OnWieldedItem, target),

        "Key" => CastTarget.FunctionKeyPress(CastKind.Key, key ?? 0),
        "DelayKey" => CastTarget.FunctionKeyPress(CastKind.DelayKey, key ?? 0),

        // A kind from a newer version. Using the item is the one outcome that cannot send
        // the server a packet it did not expect.
        _ => CastTarget.Item,
    };
}

/// <summary>Reads and writes one helper entry in the shape the reference wrote.</summary>
internal sealed class HelperEntryConverter : JsonConverter<HelperEntry>
{
    private const string Id = "id";
    private const string Name = "name";
    private const string Kind = "item_type";
    private const string Cast = "cast_target";

    public override HelperEntry Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"A helper entry cannot be a {reader.TokenType}.");
        }

        var id = HelperEntry.NoState;
        var name = string.Empty;
        var kind = EntryKind.Item;
        var cast = CastTarget.Item;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var property = reader.GetString();
            reader.Read();

            switch (property)
            {
                case Id:
                    id = reader.GetInt32();
                    break;

                case Name:
                    name = reader.GetString() ?? string.Empty;
                    break;

                case Kind:
                    kind = FromLetter(reader.GetString());
                    break;

                case Cast:
                    cast = JsonSerializer.Deserialize<CastTarget>(ref reader, options);
                    break;

                default:
                    reader.Skip();
                    break;
            }
        }

        return new HelperEntry(id, name, kind, cast);
    }

    public override void Write(Utf8JsonWriter writer, HelperEntry value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();
        writer.WriteNumber(Id, value.StateId);
        writer.WriteString(Name, value.Name);
        writer.WriteString(Kind, ToLetter(value.Kind));
        writer.WritePropertyName(Cast);
        JsonSerializer.Serialize(writer, value.Cast, options);
        writer.WriteEndObject();
    }

    private static string ToLetter(EntryKind kind) => kind switch
    {
        EntryKind.Skill => "S",
        EntryKind.Key => "K",
        _ => "I",
    };

    private static EntryKind FromLetter(string? letter) => letter switch
    {
        "S" => EntryKind.Skill,
        "K" => EntryKind.Key,
        _ => EntryKind.Item,
    };
}
