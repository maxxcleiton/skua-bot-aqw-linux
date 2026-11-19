using System.Text.Json;
using Skua.Core.Models;

namespace Skua.Core.Services;

public static class ScriptFavoritesManager
{
    private static readonly string _filePath = Path.Combine(ClientFileSources.SkuaDIR, "favorite_scripts.json");
    private static readonly HashSet<string> _favorites = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _lock = new();
    private static bool _loaded;

    public static void Load()
    {
        lock (_lock)
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (File.Exists(_filePath))
                {
                    string json = File.ReadAllText(_filePath);
                    var items = JsonSerializer.Deserialize<List<string>>(json);
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            if (!string.IsNullOrWhiteSpace(item))
                                _favorites.Add(Normalize(item));
                        }
                    }
                }
            }
            catch { }
        }
    }

    public static bool IsFavorite(string? scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
            return false;

        lock (_lock)
        {
            Load();
            return _favorites.Contains(Normalize(scriptPath));
        }
    }

    public static void SetFavorite(string? scriptPath, bool isFav)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
            return;

        lock (_lock)
        {
            Load();
            string norm = Normalize(scriptPath);
            if (isFav)
                _favorites.Add(norm);
            else
                _favorites.Remove(norm);
            Save();
        }
    }

    public static bool ToggleFavorite(string? scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
            return false;

        lock (_lock)
        {
            Load();
            string norm = Normalize(scriptPath);
            bool isFav;
            if (_favorites.Contains(norm))
            {
                _favorites.Remove(norm);
                isFav = false;
            }
            else
            {
                _favorites.Add(norm);
                isFav = true;
            }
            Save();
            return isFav;
        }
    }

    public static HashSet<string> GetAllFavorites()
    {
        lock (_lock)
        {
            Load();
            return new HashSet<string>(_favorites, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(ClientFileSources.SkuaDIR);
            string json = JsonSerializer.Serialize(_favorites.OrderBy(x => x).ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
        catch { }
    }

    private static string Normalize(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}
