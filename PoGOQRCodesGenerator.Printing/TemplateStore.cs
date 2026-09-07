using System.Text.Json;

namespace PoGOQRCodesGenerator.Printing;

public sealed class TemplateStore(string directory)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task SaveAsync(CardTemplate template, CancellationToken token = default)
    {
        template.Validate();
        if (!Guid.TryParseExact(template.Id, "N", out _)) throw new ArgumentException("Invalid template ID.");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, template.Id + ".json");
        string temporaryPath = path + ".tmp";
        try
        {
            await using (FileStream stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, template, JsonOptions, token);
            token.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async Task<(List<CardTemplate> Templates, List<string> Errors)> LoadAsync()
    {
        var templates = new List<CardTemplate>();
        var errors = new List<string>();
        if (!Directory.Exists(directory)) return (templates, errors);
        foreach (string path in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                await using FileStream stream = File.OpenRead(path);
                CardTemplate template = await JsonSerializer.DeserializeAsync<CardTemplate>(stream)
                    ?? throw new InvalidDataException("Empty template.");
                template.Validate();
                if (!Guid.TryParseExact(template.Id, "N", out _)) throw new InvalidDataException("Invalid ID.");
                templates.Add(template);
            }
            catch (Exception ex) when (ex is JsonException or IOException or ArgumentException)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return (templates.OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), errors);
    }
}
