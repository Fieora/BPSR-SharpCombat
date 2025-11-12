using System.Text.Json;

namespace BPSR_SharpCombat.Services;

public class SkillNameService
{
    private readonly Dictionary<int, string> _names = new();

    public SkillNameService(IWebHostEnvironment env)
    {
        try
        {
            var path = Path.Combine(env.ContentRootPath, "wwwroot", "data", "skills_en.json");
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                // allow comments by removing // style comments for now
                var cleaned = System.Text.RegularExpressions.Regex.Replace(json, "//.*?$", string.Empty, System.Text.RegularExpressions.RegexOptions.Multiline);
                var doc = JsonDocument.Parse(cleaned);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (int.TryParse(prop.Name, out var id))
                    {
                        var name = prop.Value.GetString();
                        if (!string.IsNullOrEmpty(name)) _names[id] = name;
                    }
                }
            }
        }
        catch
        {
            // ignore parsing errors; service will just be empty
        }
    }

    public string GetShortName(int id)
    {
        return _names.TryGetValue(id, out var v) ? v : $"Skill {id}";
    }
}
