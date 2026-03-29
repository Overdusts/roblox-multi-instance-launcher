using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RobloxLauncher.Core;

public class RobloxAccount
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public long UserId { get; set; }
    public string Cookie { get; set; } = ""; // .ROBLOSECURITY
    public bool IsValid { get; set; }
    public DateTime LastValidated { get; set; }

    [JsonIgnore]
    public string Label => string.IsNullOrEmpty(DisplayName) ? Username : $"{DisplayName} (@{Username})";
}

public class AccountManager
{
    private readonly string _savePath;
    private List<RobloxAccount> _accounts = new();

    public IReadOnlyList<RobloxAccount> Accounts => _accounts.AsReadOnly();

    public AccountManager(string savePath)
    {
        _savePath = savePath;
        Load();
    }

    public void AddAccount(RobloxAccount account)
    {
        // Remove duplicate by UserId
        _accounts.RemoveAll(a => a.UserId == account.UserId && account.UserId != 0);
        _accounts.Add(account);
        Save();
    }

    public void RemoveAccount(RobloxAccount account)
    {
        _accounts.Remove(account);
        Save();
    }

    public async Task<RobloxAccount?> ValidateCookie(string cookie)
    {
        cookie = cookie.Trim();
        if (!cookie.StartsWith("_|WARNING:-"))
            cookie = "_|WARNING:-DO-NOT-SHARE-THIS.--Sharing-this-will-allow-someone-to-log-in-as-you-and-to-steal-your-ROBUX-and-items.|_" + cookie;

        try
        {
            using var handler = new HttpClientHandler();
            handler.CookieContainer = new CookieContainer();
            handler.CookieContainer.Add(new Uri("https://roblox.com"),
                new Cookie(".ROBLOSECURITY", cookie, "/", ".roblox.com"));

            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent", "RobloxLauncher/1.0");

            var response = await client.GetAsync("https://users.roblox.com/v1/users/authenticated");
            if (!response.IsSuccessStatusCode)
                return null;

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            return new RobloxAccount
            {
                Username = json["name"]?.ToString() ?? "Unknown",
                DisplayName = json["displayName"]?.ToString() ?? "",
                UserId = json["id"]?.ToObject<long>() ?? 0,
                Cookie = cookie,
                IsValid = true,
                LastValidated = DateTime.Now
            };
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> GetCsrfToken(RobloxAccount account)
    {
        try
        {
            using var handler = new HttpClientHandler();
            handler.CookieContainer = new CookieContainer();
            handler.CookieContainer.Add(new Uri("https://roblox.com"),
                new Cookie(".ROBLOSECURITY", account.Cookie, "/", ".roblox.com"));

            using var client = new HttpClient(handler);
            var response = await client.PostAsync("https://auth.roblox.com/v2/logout", null);

            if (response.Headers.TryGetValues("x-csrf-token", out var values))
                return values.FirstOrDefault();
        }
        catch { }
        return null;
    }

    public async Task<string?> GetAuthTicket(RobloxAccount account)
    {
        try
        {
            string? csrf = await GetCsrfToken(account);
            if (csrf == null) return null;

            using var handler = new HttpClientHandler();
            handler.CookieContainer = new CookieContainer();
            handler.CookieContainer.Add(new Uri("https://roblox.com"),
                new Cookie(".ROBLOSECURITY", account.Cookie, "/", ".roblox.com"));

            using var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("x-csrf-token", csrf);
            client.DefaultRequestHeaders.Add("Referer", "https://www.roblox.com/");
            client.DefaultRequestHeaders.Add("User-Agent", "RobloxLauncher/1.0");

            var response = await client.PostAsync("https://auth.roblox.com/v1/authentication-ticket", null);

            if (response.Headers.TryGetValues("rbx-authentication-ticket", out var values))
                return values.FirstOrDefault();
        }
        catch { }
        return null;
    }

    public void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(_savePath)!;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = JsonConvert.SerializeObject(_accounts, Formatting.Indented);
            File.WriteAllText(_savePath, json);
        }
        catch { }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_savePath))
            {
                string json = File.ReadAllText(_savePath);
                _accounts = JsonConvert.DeserializeObject<List<RobloxAccount>>(json) ?? new();
            }
        }
        catch
        {
            _accounts = new();
        }
    }
}
