using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AttendanceSystem.Infrastructure.Persistence.Outbox;

/// <summary>
/// Serialización de eventos de dominio para el outbox. Los objetos de valor (<c>EmployeeId</c>,
/// <c>AttendanceRecordId</c>, ...) tienen constructor privado y las enumeraciones son clases; sin estos
/// convertidores un mensaje podía guardarse pero nunca volver a leerse, así que el reintento en segundo
/// plano no funcionaba. El formato escrito es el mismo que producía la serialización por defecto
/// (<c>{"Value": ...}</c> y <c>{"Id": ..., "Name": ...}</c>), compatible con los mensajes ya guardados.
/// </summary>
public static class DomainEventJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new ValueObjectConverterFactory(), new EnumerationConverterFactory() }
    };

    public static string Serialize(AttendanceSystem.Domain.Primitives.DomainEvent domainEvent) =>
        JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), Options);

    public static object? Deserialize(string content, Type eventType) =>
        JsonSerializer.Deserialize(content, eventType, Options);

    /// <summary>Objeto de valor: propiedad pública <c>Value</c> y fábrica estática <c>From</c> del mismo tipo.</summary>
    private sealed class ValueObjectConverterFactory : JsonConverterFactory
    {
        private static readonly ConcurrentDictionary<Type, (PropertyInfo Value, MethodInfo From)?> Shapes = new();

        public override bool CanConvert(Type typeToConvert) => Shape(typeToConvert) != null;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            var (value, from) = Shape(typeToConvert)!.Value;
            return (JsonConverter)Activator.CreateInstance(typeof(ValueObjectConverter<>).MakeGenericType(typeToConvert), value, from)!;
        }

        private static (PropertyInfo Value, MethodInfo From)? Shape(Type type) => Shapes.GetOrAdd(type, t =>
        {
            if (!t.IsClass || t.Namespace?.StartsWith("AttendanceSystem.Domain.ValueObjects", StringComparison.Ordinal) != true) return null;
            var value = t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
            if (value == null) return null;
            var from = t.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "From" && m.ReturnType == t && m.GetParameters() is { Length: 1 } p && p[0].ParameterType == value.PropertyType);
            return from == null ? null : (value, from);
        });
    }

    private sealed class ValueObjectConverter<T> : JsonConverter<T> where T : class
    {
        private readonly PropertyInfo _value;
        private readonly MethodInfo _from;

        public ValueObjectConverter(PropertyInfo value, MethodInfo from)
        {
            _value = value;
            _from = from;
        }

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;

            object? raw;
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                raw = null;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString();
                    reader.Read();
                    if (string.Equals(name, "Value", StringComparison.OrdinalIgnoreCase))
                        raw = JsonSerializer.Deserialize(ref reader, _value.PropertyType, options);
                    else
                        reader.Skip();
                }
            }
            else
            {
                raw = JsonSerializer.Deserialize(ref reader, _value.PropertyType, options);
            }

            return raw == null ? null : (T)_from.Invoke(null, new[] { raw })!;
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Value");
            JsonSerializer.Serialize(writer, _value.GetValue(value), _value.PropertyType, options);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Enumeración de dominio: hereda de una clase base llamada <c>Enumeration</c> del ensamblado de dominio (hay dos)
    /// con <c>Id</c> entero, y sus valores son campos estáticos públicos del propio tipo.
    /// </summary>
    private sealed class EnumerationConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            if (!typeToConvert.IsClass || typeToConvert.IsAbstract) return false;
            for (var t = typeToConvert.BaseType; t != null; t = t.BaseType)
            {
                // Una de las dos bases vive en el espacio de nombres global: se identifica por ensamblado.
                if (t.Name == "Enumeration" && t.Assembly == typeof(AttendanceSystem.Domain.Primitives.DomainEvent).Assembly)
                    return typeToConvert.GetProperty("Id")?.PropertyType == typeof(int);
            }
            return false;
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(EnumerationConverter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class EnumerationConverter<T> : JsonConverter<T> where T : class
    {
        private static readonly PropertyInfo IdProperty = typeof(T).GetProperty("Id")!;
        private static readonly PropertyInfo? NameProperty = typeof(T).GetProperty("Name");
        private static readonly IReadOnlyList<T> Values = typeof(T)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(T))
            .Select(f => (T)f.GetValue(null)!)
            .ToList();

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null) return null;

            int? id = null;
            if (reader.TokenType == JsonTokenType.Number)
            {
                id = reader.GetInt32();
            }
            else
            {
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString();
                    reader.Read();
                    if (string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase)) id = reader.GetInt32();
                    else reader.Skip();
                }
            }

            if (id == null) throw new JsonException($"La enumeración {typeof(T).Name} no tiene Id.");
            return Values.FirstOrDefault(e => (int)IdProperty.GetValue(e)! == id)
                ?? throw new JsonException($"Valor {id} desconocido para {typeof(T).Name}.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("Id", (int)IdProperty.GetValue(value)!);
            writer.WriteString("Name", NameProperty?.GetValue(value)?.ToString());
            writer.WriteEndObject();
        }
    }
}
