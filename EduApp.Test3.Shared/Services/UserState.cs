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
        
        // Přidáme sloupce pro ověření e-mailu (pokud ještě neexistují)
        try { await conn.ExecuteAsync("ALTER TABLE public.users ADD COLUMN IF NOT EXISTS password_hash TEXT;"); } catch { }
        try { await conn.ExecuteAsync("ALTER TABLE public.users ADD COLUMN IF NOT EXISTS is_verified BOOLEAN DEFAULT TRUE;"); } catch { }
        try { await conn.ExecuteAsync("ALTER TABLE public.users ADD COLUMN IF NOT EXISTS verification_code TEXT;"); } catch { }
    }

    public async Task<(bool Success, string ErrorMessage)> AuthenticateAsync(string email, string password)
    {
        try
        {
            var connString = _config.GetConnectionString("MyDb");
            using var connection = new NpgsqlConnection(connString);
            await EnsureUsersTableAsync(connection);

            var hash = HashPassword(password);
            
            var user = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT username, xp, level, streak, is_verified FROM public.users WHERE email = @Email AND password_hash = @Hash", 
                new { Email = email, Hash = hash });

            if (user != null)
            {
                if (user.is_verified == false)
                {
                    return (false, "Účet ještě není ověřen. Zkontroluj svůj e-mail.");
                }

                IsLoggedIn = true;
                Email = email;
                UserName = user.username;
                Xp = (int)user.xp;
                Level = (int)user.level;
                Streak = (int)user.streak;
                NotifyStateChanged();
                return (true, "");
            }
            return (false, "Nesprávný e-mail nebo heslo.");
        }
        catch (Exception ex) { return (false, $"Chyba DB: {ex.Message}"); }
    }

    // Registrace nyní uloží uživatele jako NEOVĚŘENÉHO s kódem
    public async Task<(bool Success, string ErrorMessage)> RegisterPendingUserAsync(string username, string email, string password, string verificationCode)
    {
        try
        {
            var connString = _config.GetConnectionString("MyDb");
            using var connection = new NpgsqlConnection(connString);
            await EnsureUsersTableAsync(connection);

            var exists = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM public.users WHERE email = @Email", new { Email = email });
            if (exists > 0) return (false, "Účet s tímto e-mailem již existuje.");

            var hash = HashPassword(password);
            
            // Uložíme uživatele, ale is_verified je false
            await connection.ExecuteAsync(
                "INSERT INTO public.users (email, username, password_hash, xp, level, streak, is_verified, verification_code) VALUES (@Email, @UserName, @Hash, 0, 1, 0, FALSE, @Code)",
                new { Email = email, UserName = username, Hash = hash, Code = verificationCode });

            return (true, "");
        }
        catch (Exception ex) { return (false, $"Chyba DB: {ex.Message}"); }
    }

    // Nová metoda pro ověření kódu z e-mailu
    public async Task<(bool Success, string ErrorMessage)> VerifyAccountAsync(string email, string code)
    {
        try
        {
            var connString = _config.GetConnectionString("MyDb");
            using var connection = new NpgsqlConnection(connString);
            
            var user = await connection.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT username, xp, level, streak, is_verified, verification_code FROM public.users WHERE email = @Email", 
                new { Email = email });

            if (user == null) return (false, "Uživatel nenalezen.");
            if (user.is_verified == true) return (false, "Účet již byl ověřen. Můžeš se přihlásit.");
            if (user.verification_code != code.Trim()) return (false, "Nesprávný ověřovací kód.");

            // Kód souhlasí -> Ověříme účet
            await connection.ExecuteAsync(
                "UPDATE public.users SET is_verified = TRUE, verification_code = NULL WHERE email = @Email", 
                new { Email = email });

            // Rovnou přihlásíme
            IsLoggedIn = true;
            Email = email;
            UserName = user.username;
            Xp = (int)user.xp;
            Level = (int)user.level;
            Streak = (int)user.streak;
            NotifyStateChanged();

            return (true, "");
        }
        catch (Exception ex) { return (false, $"Chyba verifikace: {ex.Message}"); }
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
            var val = await connection.QueryFirstOrDefaultAsync<int?>("SELECT setting_value FROM public.app_settings WHERE setting_key = @Key", new { Key = modeKey });
            if (val.HasValue) return val.Value;
            await connection.ExecuteAsync("INSERT INTO public.app_settings (setting_key, setting_value) VALUES (@Key, @Val)", new { Key = modeKey, Val = defaultValue });
            return defaultValue;
        }
        catch { return defaultValue; }
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}