using System;
using System.Threading.Tasks;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Dapper;
using System.Security.Cryptography;
using System.Text;

public class UserState
{
    private readonly IConfiguration _config;

    public UserState(IConfiguration config)
    {
        _config = config;
    }

    public bool IsLoggedIn { get; private set; } = false;
    
    // Pro ukázku ponecháváme admina napevno, ale už musí znát heslo!
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

    // Bezpečné hashování hesla (SHA-256)
    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    // Pomocná metoda pro založení tabulky a sloupce s heslem
    private async Task EnsureUsersTableAsync(NpgsqlConnection conn)
    {
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS users (
                email TEXT PRIMARY KEY,
                username TEXT,
                password_hash TEXT,
                xp INTEGER DEFAULT 0,
                level INTEGER DEFAULT 1,
                streak INTEGER DEFAULT 0
            )");
        
        // Zajištění zpětné kompatibility - pokud sloupec chybí, přidá se
        try { await conn.ExecuteAsync("ALTER TABLE users ADD COLUMN IF NOT EXISTS password_hash TEXT;"); } catch { }
    }

    // Reálné ověření údajů (Login)
    public async Task<bool> AuthenticateAsync(string email, string password)
    {
        try
        {
            var connString = _config.GetConnectionString("MyDb");
            using var connection = new NpgsqlConnection(connString);
            await EnsureUsersTableAsync(connection);

            var hash = HashPassword(password);
            
            var user = await connection.QueryFirstOrDefaultAsync<UserDbDto>(
                "SELECT username as Username, xp as Xp, level as Level, streak as Streak FROM users WHERE email = @Email AND password_hash = @Hash", 
                new { Email = email, Hash = hash });

            if (user != null)
            {
                IsLoggedIn = true;
                Email = email;
                UserName = user.Username;
                Xp = user.Xp;
                Level = user.Level;
                Streak = user.Streak;
                NotifyStateChanged();
                return true; // Přihlášení úspěšné
            }
            return false; // Nesprávné jméno nebo heslo
        }
        catch { return false; }
    }

    // Reálná registrace nového účtu
    public async Task<bool> RegisterAsync(string username, string email, string password)
    {
        try
        {
            var connString = _config.GetConnectionString("MyDb");
            using var connection = new NpgsqlConnection(connString);
            await EnsureUsersTableAsync(connection);

            // Kontrola, zda e-mail už není v databázi
            var exists = await connection.QueryFirstOrDefaultAsync<int>("SELECT 1 FROM users WHERE email = @Email", new { Email = email });
            if (exists == 1) return false; // E-mail už je zabraný

            var hash = HashPassword(password);
            await connection.ExecuteAsync(
                "INSERT INTO users (email, username, password_hash, xp, level, streak) VALUES (@Email, @UserName, @Hash, 0, 1, 0)",
                new { Email = email, UserName = username, Hash = hash });

            // Rovnou uživatele přihlásíme do aplikace
            IsLoggedIn = true;
            Email = email;
            UserName = username;
            Xp = 0;
            Level = 1;
            Streak = 0;
            NotifyStateChanged();
            return true;
        }
        catch { return false; }
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

    private class UserDbDto 
    {
        public string Username { get; set; } = "";
        public int Xp { get; set; }
        public int Level { get; set; }
        public int Streak { get; set; }
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
            using var connection = new NpgsqlConnection(connString);
            await connection.ExecuteAsync(
                "UPDATE users SET xp = @Xp, level = @Level WHERE email = @Email",
                new { Xp, Level, Email });
        }
        catch { }

        if (leveledUp) OnLevelUp?.Invoke(Level);
        NotifyStateChanged();
    }

    public async Task<int> GetXpRewardAsync(string modeKey, int defaultValue)
    {
        try
        {
            var connString = _config.GetConnectionString("MyDb");
            using var connection = new NpgsqlConnection(connString);
            
            await connection.ExecuteAsync("CREATE TABLE IF NOT EXISTS app_settings (setting_key TEXT PRIMARY KEY, setting_value INTEGER)");
            
            var val = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT setting_value FROM app_settings WHERE setting_key = @Key", new { Key = modeKey });
                
            if (val.HasValue) return val.Value;
            
            await connection.ExecuteAsync(
                "INSERT INTO app_settings (setting_key, setting_value) VALUES (@Key, @Val)", 
                new { Key = modeKey, Val = defaultValue });
                
            return defaultValue;
        }
        catch { return defaultValue; }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}