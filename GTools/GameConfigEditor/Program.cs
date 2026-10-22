using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OperationBlacktide.Shared.Combat;
using OperationBlacktide.Shared.GameLogic;

var builder = WebApplication.CreateBuilder(args);
string root = Path.GetFullPath(builder.Configuration["project-root"] ?? Path.Combine(builder.Environment.ContentRootPath, "../.."));
if (!Directory.Exists(Path.Combine(root, "GConfig"))) throw new DirectoryNotFoundException("Pass --project-root pointing to the game repository.");
int port = int.TryParse(builder.Configuration["port"], out var configuredPort) ? configuredPort : 8094;
builder.WebHost.UseUrls("http://127.0.0.1:" + port);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 512 * 1024);
var app = builder.Build();
var json = new JsonSerializerOptions { IncludeFields = true };
var files = new[] { "Weapons.json", "BattleWeapon.json", "Modes/training.json", "Modes/battle.json" };
string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
var gate = new object();
string Read(string file) => File.ReadAllText(Path.Combine(root, "GConfig", file));
string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

app.Use(async (context, next) =>
{
    if (!System.Net.IPAddress.IsLoopback(context.Connection.RemoteIpAddress)) { context.Response.StatusCode = 403; return; }
    // Restrict Host as well as binding, so external sites cannot use DNS rebinding to edit local files.
    if (context.Request.Host.Host != "127.0.0.1" && context.Request.Host.Host != "localhost") { context.Response.StatusCode = 403; return; }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; frame-ancestors 'none'";
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.MapGet("/api/catalog", () => Results.Json(new { files, token }));
app.MapGet("/api/document", (string name) =>
{
    if (!files.Contains(name)) return Results.NotFound();
    lock (gate) { string content = Read(name); return Results.Json(new { name, content, revision = Hash(content) }); }
});
app.MapPost("/api/document", (HttpRequest request, SaveRequest value) =>
{
    if (request.Headers["X-Config-Token"] != token) return Results.StatusCode(403);
    if (!files.Contains(value.Name) || value.Content == null) return Results.BadRequest(new { error = "未知配置文件" });
    try
    {
        using var document = JsonDocument.Parse(value.Content);
        if (value.Name.StartsWith("Modes/"))
        {
            var info = JsonSerializer.Deserialize<GameLogicInfo>(value.Content, json) ?? throw new ArgumentException("玩法配置不能为空"); info.Validate();
            if (info.id != Path.GetFileNameWithoutExtension(value.Name)) throw new ArgumentException("玩法 ID 必须与文件名一致");
        }
        else if (value.Name == "BattleWeapon.json") (JsonSerializer.Deserialize<WeaponInfo>(value.Content, json) ?? throw new ArgumentException("武器配置不能为空")).Validate();
        else
        {
            var found = new HashSet<string>();
            var list = document.RootElement.GetProperty("weapons");
            if (list.GetArrayLength() == 0) throw new ArgumentException("武器目录不能为空");
            foreach (var entry in list.EnumerateArray())
            {
                var info = entry.Deserialize<WeaponInfo>(json) ?? throw new ArgumentException("武器条目不能为空"); info.Validate();
                if (!found.Add(info.id)) throw new ArgumentException("重复武器 ID：" + info.id);
            }
            // The existing resource catalog uses stable IDs; editing parameters must not orphan its bindings.
            using var existing = JsonDocument.Parse(Read(value.Name));
            var expected = existing.RootElement.GetProperty("weapons").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToHashSet();
            if (!found.SetEquals(expected)) throw new ArgumentException("此面板仅编辑已导入武器；新增/删除武器需要同时维护模型资源映射");
        }
        string normalized = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        lock (gate)
        {
            if (Hash(Read(value.Name)) != value.Revision) return Results.Conflict(new { error = "文件已被其他操作修改，请重新加载后编辑。" });
            string target = Path.Combine(root, "GConfig", value.Name);
            File.WriteAllText(target + ".tmp", normalized, new UTF8Encoding(false)); File.Move(target + ".tmp", target, true);
            string generated = Path.Combine(root, "Assets/Resources/Config", value.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(generated));
            File.WriteAllText(generated + ".tmp", normalized, new UTF8Encoding(false)); File.Move(generated + ".tmp", generated, true);
            return Results.Json(new { revision = Hash(normalized), content = normalized, message = "已保存配置并导出 Unity 资源。重新进入玩法后生效。" });
        }
    }
    catch (Exception error) when (error is JsonException || error is ArgumentException || error is InvalidOperationException || error is KeyNotFoundException)
    { return Results.BadRequest(new { error = error.Message }); }
});
app.Run();
record SaveRequest(string Name, string Content, string Revision);
