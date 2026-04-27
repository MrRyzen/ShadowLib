namespace ShadowLib.RNG.Utilities;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ShadowLib.RNG.Modifiers;

/// <summary>
/// Utility class for loading IEnumerable<WeightModifier<T>> modifiers from a JSON configuration file.
/// </summary>
public static class ModifierLoader
{
    /// <summary>
    /// Loads weight modifiers from a JSON configuration file.
    /// </summary>
    /// <param name="filePath">The path to the JSON configuration file.</param>
    /// <returns>An IEnumerable of WeightModifier<T> objects loaded from the configuration file.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist.</exception>
    /// <exception cref="JsonException">Thrown when the JSON is invalid or cannot be parsed into the expected format.</exception>
    public static IEnumerable<WeightModifier<T>> LoadModifiers<T>(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"The specified file was not found: {filePath}");
        
        var json = File.ReadAllText(filePath);
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };

            var jsonDoc = JsonDocument.Parse(json);
            if (!jsonDoc.RootElement.TryGetProperty("modifiers", out var modifiersElement) || 
                modifiersElement.ValueKind != JsonValueKind.Array)
                throw new JsonException("The JSON configuration must contain a 'modifiers' array.");

            var modifiers = new List<WeightModifier<T>>();
            
            foreach (var element in modifiersElement.EnumerateArray())
            {
                var modifier = DeserializeModifier<T>(element);
                modifiers.Add(modifier);
            }

            return modifiers;
        }
        catch (JsonException ex)
        {
            throw new JsonException($"Error parsing JSON configuration: {ex.Message}", ex);
        }
    }

    private static WeightModifier<T> DeserializeModifier<T>(JsonElement element)
    {
        string id = element.GetProperty("id").GetString() ?? string.Empty;
        string condition = element.GetProperty("condition").GetString() ?? string.Empty;
        
        // Handle target based on type T
        T target = DeserializeTarget<T>(element.GetProperty("targetTag"));
        
        ModifierOperation operation = Enum.Parse<ModifierOperation>(
            element.GetProperty("operation").GetString() ?? "Add", 
            ignoreCase: true
        );
        
        float value = element.GetProperty("value").GetSingle();
        
        int stage = element.TryGetProperty("stage", out var stageElement) 
            ? stageElement.GetInt32() 
            : 999;

        return new WeightModifier<T>(id, condition, target, operation, value, stage);
    }

    private static T DeserializeTarget<T>(JsonElement element)
    {
        // Handle common types
        if (typeof(T) == typeof(string))
            return (T)(object)element.GetString()!;
        
        if (typeof(T) == typeof(int))
            return (T)(object)element.GetInt32();
        
        if (typeof(T) == typeof(Guid))
            return (T)(object)Guid.Parse(element.GetString()!);

        // For other types, try generic deserialization
        return JsonSerializer.Deserialize<T>(element.GetRawText())!;
    }
}