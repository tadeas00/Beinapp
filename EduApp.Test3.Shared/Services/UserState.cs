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
    
    public bool IsAdmin => Email == "b3inapp@gmail.com";
    
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
    
    private string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private async Task EnsureUsersTableAsync(NpgsqlConnection conn)
    {
        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS public.users (
                email TEXT PRIMARY KEY,
                username TEXT,
                password_hash TEXT,
                xp INTEGER DEFAULT 0,
                level INTEGER DEFAULT 1,
                streak INTEGER DEFAULT 0
            )");
        
        try { await conn.ExecuteAsync("ALTER TABLE public.users ADD COLUMN IF NOT EXISTS password_hash TEXT;"); } catch { }
    }

    public async Task<(bool Success, string ErrorMessage)> AuthenticateAsync(string email, string password)
    {
        try
        {
            var connString = _config.GetConnectionString("MyDb");
            using var connection = new NpgsqlConnection(connString);
            await EnsureUsersTableAsync(connection);

            var hash = HashPassword(password);
            
            var user = await connection.QueryFirstOrDefaultAsync<UserDbDto>(
                "SELECT username as Username, xp as Xp, level as Level, streak as Streak FROM public.users WHERE email = @Email AND password_hash = @Hash", 
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
                return (true, "");
            }
            return (false, "Nesprávný e-mail nebo heslo.");
        }
        catch (PostgresException ex) when (ex.SqlState == "28P01" || ex.SqlState == "3D000" || ex.SqlState == "28000")
        {
            return (false, "Kritická chyba: Špatné jméno/heslo nebo název databáze v appsettings.json!");
        }
        catch (Exception ex) 
        { 
            return (false, $"Chyba spojení s databází: {ex.Message}");
        }
    }

    public async Task<(bool Success, string ErrorMessage)> RegisterAsync(string username, string email, string password)
    {
        try
        {
            var connString = _config.GetConnectionString("MyDb");
            using var connection = new NpgsqlConnection(connString);
            await EnsureUsersTableAsync(connection);

            var exists = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM public.users WHERE email = @Email", new { Email = email });
            if (exists > 0) return (false, "Účet s tímto e-mailem již existuje.");

            var hash = HashPassword(password);
            await connection.ExecuteAsync(
                "INSERT INTO public.users (email, username, password_hash, xp, level, streak) VALUES (@Email, @UserName, @Hash, 0, 1, 0)",
                new { Email = email, UserName = username, Hash = hash });

            IsLoggedIn = true;
            Email = email;
            UserName = username;
            Xp = 0;
            Level = 1;
            Streak = 0;
            NotifyStateChanged();
            return (true, "");
        }
        catch (PostgresException ex) when (ex.SqlState == "28P01" || ex.SqlState == "3D000" || ex.SqlState == "28000")
        {
            return (false, "Kritická chyba: Špatné jméno/heslo nebo název databáze v appsettings.json!");
        }
        catch (Exception ex) 
        { 
            return (false, $"Chyba spojení s databází: {ex.Message}");
        }
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
                "UPDATE public.users SET xp = @Xp, level = @Level WHERE email = @Email",
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
            
            await connection.ExecuteAsync("CREATE TABLE IF NOT EXISTS public.app_settings (setting_key TEXT PRIMARY KEY, setting_value INTEGER)");
            
            var val = await connection.QueryFirstOrDefaultAsync<int?>(
                "SELECT setting_value FROM public.app_settings WHERE setting_key = @Key", new { Key = modeKey });
                
            if (val.HasValue) return val.Value;
            
            await connection.ExecuteAsync(
                "INSERT INTO public.app_settings (setting_key, setting_value) VALUES (@Key, @Val)", 
                new { Key = modeKey, Val = defaultValue });
                
            return defaultValue;
        }
        catch { return defaultValue; }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}