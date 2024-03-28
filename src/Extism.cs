using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

public abstract class ValueX
{
    [JsonIgnore]
    public abstract string ValueType { get; }
}

public class ValueDateTime : ValueX
{
    [JsonPropertyName("valueDateTime")]
    public required string Value { get; set; }

    [JsonIgnore]
    public override string ValueType => "valueDateTime";
}


public class ValueString : ValueX
{
    [JsonPropertyName("valueString")]
    public required string Value { get; set; }

    [JsonIgnore]
    public override string ValueType => "valueString";
}

public class ValueCode : ValueX
{
    [JsonPropertyName("valueCode")]
    public required string Value { get; set; }

    [JsonIgnore]
    public override string ValueType => "valueCode";
}

public class ValueCoding : ValueX
{
    [JsonPropertyName("valueCoding")]
    public required Coding Value { get; set; }

    [JsonIgnore]
    public override string ValueType => "valueCoding";
}

public class ValueDecimal : ValueX
{
    [JsonPropertyName("valueDecimal")]
    public required string Value { get; set; }

    [JsonIgnore]
    public override string ValueType => "valueDecimal";
}

public class Coding
{
    [JsonPropertyName("system")]
    public string? System { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("display")]
    public string? Display { get; set; }
}

[JsonConverter(typeof(PropertyConverter))]
public class Property
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonIgnore]
    public ValueX? Value { get; set; }
}

public class PropertyConverter : JsonConverter<Property>
{
    public override Property Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var property = new Property();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propertyName = reader.GetString();
                reader.Read();

                switch (propertyName)
                {
                    case "code":
                        property.Code = reader.GetString();
                        break;
                    case "valueString":
                        property.Value = new ValueString { Value = reader.GetString() ?? "" };
                        break;
                    case "valueDateTime":
                        property.Value = new ValueDateTime { Value = reader.GetString() ?? "" };
                        break;
                    case "valueCode":
                        property.Value = new ValueCode { Value = reader.GetString() ?? "" };
                        break;
                    case "valueCoding":
                        property.Value = new ValueCoding { Value = JsonSerializer.Deserialize<Coding>(ref reader, options) ?? new Coding() };
                        break;
                    case "valueDecimal":
                        property.Value = new ValueDecimal { Value = reader.GetString() ?? "" };
                        break;
                }
            }
        }
        return property;
    }

    public override void Write(Utf8JsonWriter writer, Property value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("code", value.Code);

        switch (value?.Value?.ValueType)
        {
            case "valueString":
                writer.WriteString("valueString", ((ValueString)value.Value).Value);
                break;
            case "valueDateTime":
                writer.WriteString("valueDateTime", ((ValueDateTime)value.Value).Value);
                break;
            case "valueCode":
                writer.WriteString("valueCode", ((ValueCode)value.Value).Value);
                break;
            case "valueCoding":
                writer.WritePropertyName("valueCoding");
                JsonSerializer.Serialize(writer, ((ValueCoding)value.Value).Value, options);
                break;
            case "valueDecimal":
                writer.WriteString("valueDecimal", ((ValueDecimal)value.Value).Value);
                break;
        }

        writer.WriteEndObject();
    }
}

public class Concept
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("display")]
    public string? Display { get; set; }


    [JsonPropertyName("properties")]
    public List<Property> Properties { get; set; } = new List<Property>();
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Severity
{
    [JsonPropertyName("error")]
    Error,

    [JsonPropertyName("warning")]
    Warning,

    [JsonPropertyName("information")]
    Information,

    [JsonPropertyName("success")]
    Success
}

[JsonConverter(typeof(ParseDetailConverter))]
public class ParseDetail
{
    [JsonPropertyName("severity")]
    public Severity Severity { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonIgnore]
    public ValueX? Value { get; set; }
}

public class ParseDetailConverter : JsonConverter<ParseDetail>
{
    public override ParseDetail Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var parseDetail = new ParseDetail();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propertyName = reader.GetString();
                reader.Read();

                switch (propertyName)
                {
                    case "severity":
                        parseDetail.Severity = JsonSerializer.Deserialize<Severity>(ref reader, options);
                        break;
                    case "key":
                        parseDetail.Key = reader.GetString();
                        break;
                    case "valueString":
                        parseDetail.Value = new ValueString { Value = reader.GetString() ?? "" };
                        break;
                    case "valueCode":
                        parseDetail.Value = new ValueCode { Value = reader.GetString() ?? "" };
                        break;
                    case "valueCoding":
                        parseDetail.Value = new ValueCoding { Value = JsonSerializer.Deserialize<Coding>(ref reader, options) ?? new Coding() };
                        break;
                    case "valueDecimal":
                        parseDetail.Value = new ValueDecimal { Value = reader.GetString() ?? "" };
                        break;
                }
            }
        }
        return parseDetail;
    }

    public override void Write(Utf8JsonWriter writer, ParseDetail value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("severity", value.Severity.ToString().ToLowerInvariant());
        writer.WriteString("key", value.Key);

        switch (value?.Value?.ValueType)
        {
            case "valueString":
                writer.WriteString("valueString", ((ValueString)value.Value).Value);
                break;
            case "valueCode":
                writer.WriteString("valueCode", ((ValueCode)value.Value).Value);
                break;
            case "valueCoding":
                writer.WritePropertyName("valueCoding");
                JsonSerializer.Serialize(writer, ((ValueCoding)value.Value).Value, options);
                break;
            case "valueDecimal":
                writer.WriteString("valueDecimal", ((ValueDecimal)value.Value).Value);
                break;
        }

        writer.WriteEndObject();
    }
}

public class ParseResponse
{
    [JsonPropertyName("details")]
    public List<ParseDetail>? Details { get; set; }

    [JsonPropertyName("concept")]
    public Concept? Concept { get; set; }
}

// TerminologyDb.cs

public class LookupRequest
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("properties")]
    public List<string>? Properties { get; set; }
}

public class LookupResponse
{
    [JsonPropertyName("concept")]
    public Concept? Concept { get; set; }
}

public class SubsumesRequest
{
    [JsonPropertyName("ancestor")]
    public string? Ancestor { get; set; }

    [JsonPropertyName("descendant")]
    public string? Descendant { get; set; }
}

public class SubsumesResponse
{
    [JsonPropertyName("subsumes")]
    public bool Subsumes { get; set; }
}

// TerminologyEngine.cs

public class ParseRequest
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("properties")]
    public List<string>? Properties { get; set; }
}