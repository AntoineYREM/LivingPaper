using System.Buffers.Binary;
using LivingPaper.Application;
using LivingPaper.Domain;

namespace LivingPaper.Persistence;

// The passages of a book in the file of an export or of an example (docs/specs/rag.md, section 8): the model that made the
// vectors and their size once, then each passage with its scenes and its vector, written as base64 of 32-bit floats
// (little-endian), about three times smaller than a list of numbers. Everything read from a file is untrusted: ToSet()
// validates it.
internal sealed class PassageIndexFile
{
    public string? Model { get; set; }

    public int? VectorSize { get; set; }

    public List<PassageFile?>? Passages { get; set; }

    public static PassageIndexFile From(PassageSet set, bool withVectors) => new()
    {
        Model = withVectors ? set.EmbeddingModel : null,
        VectorSize = withVectors ? set.Vectors[0].Length : null,
        Passages =
        [
            .. set.Passages.Select((passage, index) => (PassageFile?)new PassageFile
            {
                Number = passage.Number,
                Text = passage.Text,
                Scenes = [.. passage.Scenes.Select(scene => (SceneFile?)new SceneFile { Summary = scene.Summary, Present = [.. scene.Present] })],
                Vector = withVectors ? Encode(set.Vectors[index]) : null,
            }),
        ],
    };

    // Null, with the name of the field at fault (never what is written in it), when something is not acceptable.
    public PassageSet? ToSet(out string field)
    {
        field = "passageIndex.passages";
        if (Passages is null || Passages.Count == 0 || Passages.Count > PassageLimits.Passages)
        {
            return null;
        }

        if (Model is not null && (string.IsNullOrWhiteSpace(Model) || Model.Length > LlmProviderLimits.Model || Model.Any(char.IsControl)))
        {
            field = "passageIndex.model";
            return null;
        }

        // The size goes with the model: both or neither.
        if (Model is null ? VectorSize is not null : VectorSize is not (> 0 and <= PassageLimits.VectorSize))
        {
            // Vectors with no model are told at the first of them.
            if (Model is not null || !Passages.Any(passage => passage?.Vector is not null))
            {
                field = "passageIndex.vectorSize";
                return null;
            }
        }

        var passages = new List<StoredPassage>(Passages.Count);
        var vectors = new List<float[]>(Model is null ? 0 : Passages.Count);
        for (var index = 0; index < Passages.Count; index++)
        {
            field = $"passageIndex.passages[{index}]";
            if (Passages[index] is not { } passage)
            {
                return null;
            }

            if (passage.Number != index + 1)
            {
                field += ".number";
                return null;
            }

            if (string.IsNullOrWhiteSpace(passage.Text) || passage.Text.Length > PassageCutter.MaxLength)
            {
                field += ".text";
                return null;
            }

            if (ReadScenes(passage.Scenes) is not { } scenes)
            {
                field += ".scenes";
                return null;
            }

            if (Model is null)
            {
                if (passage.Vector is not null)
                {
                    field += ".vector";
                    return null;
                }
            }
            else if (Decode(passage.Vector, VectorSize!.Value) is { } vector)
            {
                vectors.Add(vector);
            }
            else
            {
                field += ".vector";
                return null;
            }

            passages.Add(new StoredPassage(passage.Number, passage.Text, scenes));
        }

        field = string.Empty;
        return new PassageSet(passages, Model, vectors);
    }

    private static List<PassageScene>? ReadScenes(List<SceneFile?>? scenes)
    {
        if (scenes is null || scenes.Count > PassageLimits.ScenesPerPassage)
        {
            return null;
        }

        var read = new List<PassageScene>(scenes.Count);
        foreach (var scene in scenes)
        {
            if (scene is null || string.IsNullOrWhiteSpace(scene.Summary) || scene.Summary.Length > PassageLimits.SceneLetters
                || scene.Present is null || scene.Present.Count > PassageLimits.NamesPerScene
                || scene.Present.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > CharacterLimits.Name))
            {
                return null;
            }

            read.Add(new PassageScene(scene.Summary, [.. scene.Present.Select(name => name!)]));
        }

        return read;
    }

    private static string Encode(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        for (var index = 0; index < vector.Length; index++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * sizeof(float)), vector[index]);
        }

        return Convert.ToBase64String(bytes);
    }

    // Null for anything but exactly "size" finite numbers that are not all zeros (such a vector has no direction).
    private static float[]? Decode(string? text, int size)
    {
        var bytes = new byte[size * sizeof(float)];
        if (text is null || text.Length > (bytes.Length + 2) / 3 * 4
            || !Convert.TryFromBase64String(text, bytes, out var written) || written != bytes.Length)
        {
            return null;
        }

        var vector = new float[size];
        for (var index = 0; index < size; index++)
        {
            vector[index] = BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(index * sizeof(float)));
        }

        return Array.TrueForAll(vector, float.IsFinite) && !Array.TrueForAll(vector, number => number == 0) ? vector : null;
    }
}

internal sealed class PassageFile
{
    public int Number { get; set; }

    public string? Text { get; set; }

    public List<SceneFile?>? Scenes { get; set; }

    public string? Vector { get; set; }
}

internal sealed class SceneFile
{
    public string? Summary { get; set; }

    public List<string?>? Present { get; set; }
}
