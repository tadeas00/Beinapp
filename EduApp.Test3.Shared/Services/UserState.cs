using System;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Dapper;

public class UserState
{
    private readonly IConfiguration _config;

    public UserState(IConfiguration config)
    {
        _config = config;
    }

    public bool IsLoggedIn { get; private set; } = false;
    
    // Jednoduchá kontrola admina (můžeš později napojit na DB roli)
    public bool IsAdmin => Email == "admin@test.pro";
    
    public string UserName { get; private set; } = "";
    public string Email { get; private set; } = "";

    public int Xp { get; private set; } = 0;
    public int Level { get; private set; } = 1;
    public int Streak { get; private set; } = 0;

    public int XpToNextLevel => GetXpForLevel(Level + 1);
    public int XpForCurrentLevel => GetXpForLevel(Level);
    public int XpProgress => Xp - XpForCurrentLevel;
    public int XpRequired => XpToNextLevel - XpForCurrentLevel;

    public event Action? OnChange;
    public event Action<int>? OnLevelUp;

    private int GetXpForLevel(int level) => (level - 1) * level * 50;

    public async Task LoginAsync(string userName, string email)
    {
        IsLoggedIn = true;
        UserName = userName;
        Email = email;
        
        await LoadUserDataAsync();
        NotifyStateChanged();
    }

    public void Logout()
    {
        IsLoggedIn = false;
        UserName = "";
        Email = "";
        Xp = 0;
        Level = 1;
        Streak = 0;
        NotifyStateChanged();
    }

    private async Task LoadUserDataAsync()
    {
        try
        {
            var connString = _config.GetConnectionString("MyDb");
            using var connection = new SqliteConnection(connString);
            
            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS users (
                    email TEXT PRIMARY KEY,
                    username TEXT,
                    xp INTEGER DEFAULT 0,
                    level INTEGER DEFAULT 1,
                    streak INTEGER DEFAULT 0
                )");

            var user = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT xp as Xp, level as Level, streak as Streak FROM users WHERE email = @Email", 
                new { Email });

            if (user != null)
            {
                Xp = (int)user.Xp;
                Level = (int)user.Level;
                Streak = (int)user.Streak;
            }
            else
            {
                await connection.ExecuteAsync(
                    "INSERT INTO users (email, username, xp, level, streak) VALUES (@Email, @UserName, 0, 1, 0)",
                    new { Email, UserName });
                Xp = 0;
                Level = 1;
                Streak = 0;
            }
        }
        catch { }
    }

    public async Task AddXpAsync(int xpEarned)
    {
        if (!IsLoggedIn || xpEarned <= 0) return;

        Xp += xpEarned;
        bool leveledUp = false;

        while (Xp >= GetXpForLevel(Level + 1))
        {
            Level++;
            leveledUp = true;
        }

        try
        {
            var connString = _config.GetConnectionString("MyDb");
            using var connection = new SqliteConnection(connString);
            await connection.ExecuteAsync(
                "UPDATE users SET xp = @Xp, level = @Level WHERE email = @Email",
                new { Xp, Level, Email });
        }
        catch { }

        if (leveledUp) OnLevelUp?.Invoke(Level);
        NotifyStateChanged();
    }

    // NOVÉ: Metoda pro načtení dynamických XP odměn z DB
    public async Task<int> GetXpRewardAsync(string modeKey, int defaultValue)
    {
        try
        {
            var connString = _config.GetConnectionString("MyDb");
            using var connection = new SqliteConnection(connString);
            
            // Zajistí existenci tabulky pro nastavení
            await connection.ExecuteAsync("CREATE TABLE IF NOT EXISTS app_settings (setting_key TEXT PRIMARY KEY, setting_value INTEGER)");
            
            var val = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT setting_value FROM app_settings WHERE setting_key = @Key", new { Key = modeKey });
                
            if (val.HasValue) return val.Value;
            
            // Pokud nastavení neexistuje, vytvoří ho s výchozí hodnotou
            await connection.ExecuteAsync(
                "INSERT INTO app_settings (setting_key, setting_value) VALUES (@Key, @Val)", 
                new { Key = modeKey, Val = defaultValue });
                
            return defaultValue;
        }
        catch { return defaultValue; }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}