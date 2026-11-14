using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace TeklaMcpServer.Api.Drawing;

/// <summary>
/// The bolt-chain settings as they are written in the settings file and kept with a context: the
/// names and values of <see cref="BoltChainSettings"/>, enum values by name. A context reads the file
/// once when it is built and keeps the result, so the preview and the batch of one context use the
/// same values whatever happens to the file in between; an edited file takes effect on the next
/// context (refresh=true).
/// </summary>
internal sealed class BoltChainSettingsData
{
    public string? MainPartInternal { get; set; }
    public string? SecondaryPartInternal { get; set; }
    public string? DimensionedProjections { get; set; }
    public double? ClassToleranceDegrees { get; set; }
    public string? GroupMerge { get; set; }
    public double? CombineMinDistance { get; set; }
    public string? PositionBoltsTo { get; set; }
    public string? SecondaryPart { get; set; }

    /// <summary>The settings these values give; a value left out keeps its default.</summary>
    public BoltChainSettings ToSettings(string where)
    {
        var defaults = new BoltChainSettings();
        T Parse<T>(string? value, T fallback, string name) where T : struct, Enum
        {
            if (value == null) return fallback;
            if (Enum.TryParse<T>(value, ignoreCase: false, out var parsed) && Enum.IsDefined(typeof(T), parsed)) return parsed;
            throw new InvalidOperationException(
                $"{where}: {name} \"{value}\" is not one of {string.Join(", ", Enum.GetNames(typeof(T)))}.");
        }
        try
        {
            return new BoltChainSettings(
                ClassToleranceDegrees ?? defaults.ClassToleranceDegrees,
                Parse(MainPartInternal, defaults.MainPartInternal, nameof(MainPartInternal)),
                Parse(SecondaryPartInternal, defaults.SecondaryPartInternal, nameof(SecondaryPartInternal)),
                Parse(DimensionedProjections, defaults.DimensionedProjections, nameof(DimensionedProjections)),
                Parse(GroupMerge, defaults.GroupMerge, nameof(GroupMerge)),
                CombineMinDistance ?? defaults.CombineMinDistance,
                Parse(PositionBoltsTo, defaults.PositionBoltsTo, nameof(PositionBoltsTo)),
                Parse(SecondaryPart, defaults.SecondaryPart, nameof(SecondaryPart)));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new InvalidOperationException($"{where}: {exception.Message}", exception);
        }
    }

    /// <summary>Every value of the settings, as applied: shown in the answer and kept in the saved case.</summary>
    public static BoltChainSettingsData From(BoltChainSettings settings) => new() {
        MainPartInternal = settings.MainPartInternal.ToString(),
        SecondaryPartInternal = settings.SecondaryPartInternal.ToString(),
        DimensionedProjections = settings.DimensionedProjections.ToString(),
        ClassToleranceDegrees = settings.ClassToleranceDegrees,
        GroupMerge = settings.GroupMerge.ToString(),
        CombineMinDistance = settings.CombineMinDistance,
        PositionBoltsTo = settings.PositionBoltsTo.ToString(),
        SecondaryPart = settings.SecondaryPart.ToString()
    };
}

/// <summary>Reads the bolt-chain settings file: %LOCALAPPDATA%\svMCP\settings\bolt-chains.json.</summary>
internal static class BoltChainSettingsFile
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "svMCP", "settings", "bolt-chains.json");

    /// <summary>
    /// The defaults when there is no file. A file that cannot be read, is not valid JSON, names an
    /// unknown setting or holds an unknown value is an error naming the file: never another set of
    /// settings in silence.
    /// </summary>
    public static BoltChainSettings Load(string path)
    {
        if (!File.Exists(path)) return new BoltChainSettings();
        BoltChainSettingsData? data;
        try
        {
            data = JsonSerializer.Deserialize<BoltChainSettingsData>(File.ReadAllText(path), new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow
            });
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException($"Bolt chain settings file {path} cannot be read: {exception.Message}", exception);
        }
        if (data == null) throw new InvalidOperationException($"Bolt chain settings file {path} is empty.");
        return data.ToSettings($"Bolt chain settings file {path}");
    }
}
