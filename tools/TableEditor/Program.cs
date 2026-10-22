using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

// 룬 코드 테이블 편집기 로컬 서버다. 127.0.0.1에서만 listens 하고 화이트리스트에 있는
// 데이터 파일만 읽고 쓴다. 정적 UI는 wwwroot에서 서빙한다.

var port = args.Length > 0 && int.TryParse(args[0], out int parsedPort) ? parsedPort : 8765;
var repoRoot = FindRepoRoot();
if (repoRoot == null)
{
    Console.Error.WriteLine("저장소 루트를 찾지 못했습니다. tools/TableEditor 아래에서 실행하거나 저장소 안에서 실행하세요.");
    return 1;
}
var dataRoot = Path.Combine(repoRoot, "Assets", "RuneCode", "Resources", "RuneCode");
var wwwRoot = Path.Combine(repoRoot, "tools", "TableEditor", "wwwroot");

// (id, 그룹, 제목, 파일, 표 루트 경로, 뷰 종류). 표 루트 경로는 파일 안에서 편집 대상 배열/객체의 키 경로다.
var catalog = new[]
{
    new Table("runes", "룬", "룬 카탈로그", "tables/runes.json", "_runes", "list"),
    new Table("grades", "룬", "엘리트 Modifier 등급표", "tables/elite_modifier_drops.json", "", "grades"),
    new Table("balance", "공용", "밸런스", "balance.json", "", "balance"),
    new Table("strings", "공용", "현지화 (한국어)", "strings.ko.json", "_entries", "strings"),
    new Table("spell:energybolt", "시작 마법", "에너지 볼트", "spells/energybolt.json", "", "object"),
    new Table("spell:triplemissile", "시작 마법", "삼연 미사일", "spells/triplemissile.json", "", "object"),
    new Table("enemies", "미션", "적", "enemies.json", "_enemies", "list"),
    new Table("formations", "미션", "진형", "formations.json", "_formations", "list"),
    new Table("bosses", "미션", "보스", "bosses.json", "_bosses", "list"),
    new Table("drops", "미션", "드롭 테이블", "drops.json", "_dropTables", "list"),
    new Table("sector1", "미션", "섹터 맵·웨이브", "sector1.json", "_rooms", "rooms"),
    new Table("stages", "미션", "스테이지", "stages.json", "", "stages"),
};
var tableById = catalog.ToDictionary(t => t.Id, StringComparer.Ordinal);

var listener = new HttpListener();
listener.Prefixes.Add($"http://127.0.0.1:{port}/");
try { listener.Start(); }
catch (HttpListenerException)
{
    Console.Error.WriteLine($"포트 {port}를 열 수 없습니다. 다른 포트를 지정하세요: dotnet run -- 8766");
    return 1;
}
Console.WriteLine($"룬 코드 테이블 편집기: http://127.0.0.1:{port}/");
Console.WriteLine($"데이터 루트: {dataRoot}");
Console.WriteLine("종료하려면 Ctrl+C");

while (true)
{
    var context = await listener.GetContextAsync();
    try { await Handle(context); }
    catch (Exception exception)
    {
        Console.Error.WriteLine(exception.Message);
        TryRespond(context, 500, "text/plain; charset=utf-8", exception.Message);
    }
}

async Task Handle(HttpListenerContext context)
{
    var request = context.Request;
    var path = request.Url.AbsolutePath;
    if (request.HttpMethod == "GET" && (path == "/" || path == "/index.html")) { await ServeFile(context, "index.html", "text/html; charset=utf-8"); return; }
    if (request.HttpMethod == "GET" && path == "/app.js") { await ServeFile(context, "app.js", "text/javascript; charset=utf-8"); return; }
    if (request.HttpMethod == "GET" && path == "/schemas.js") { await ServeFile(context, "schemas.js", "text/javascript; charset=utf-8"); return; }
    if (request.HttpMethod == "GET" && path == "/style.css") { await ServeFile(context, "style.css", "text/css; charset=utf-8"); return; }

    if (request.HttpMethod == "GET" && path == "/api/catalog")
    {
        var payload = JsonSerializer.Serialize(catalog.Select(t => new { id = t.Id, group = t.Group, title = t.Title, file = t.File, rootPath = t.RootPath, view = t.View }));
        TryRespond(context, 200, "application/json; charset=utf-8", payload);
        return;
    }

    if (path.StartsWith("/api/data"))
    {
        var tableId = request.QueryString["id"];
        if (string.IsNullOrEmpty(tableId) || !tableById.TryGetValue(tableId, out var table))
        {
            TryRespond(context, 404, "text/plain; charset=utf-8", "알 수 없는 테이블 ID입니다.");
            return;
        }
        var filePath = Path.Combine(dataRoot, table.File.Replace('/', Path.DirectorySeparatorChar));
        if (request.HttpMethod == "GET")
        {
            TryRespond(context, 200, "application/json; charset=utf-8", File.ReadAllText(filePath));
            return;
        }
        if (request.HttpMethod == "POST")
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            try { using var _ = JsonDocument.Parse(body); }
            catch (JsonException exception)
            {
                TryRespond(context, 400, "text/plain; charset=utf-8", "JSON이 올바르지 않습니다: " + exception.Message);
                return;
            }
            File.WriteAllText(filePath, body, new UTF8Encoding(false));
            TryRespond(context, 200, "text/plain; charset=utf-8", "ok");
            return;
        }
    }

    TryRespond(context, 404, "text/plain; charset=utf-8", "찾을 수 없습니다: " + path);
}

async Task ServeFile(HttpListenerContext context, string name, string mime)
{
    var filePath = Path.Combine(wwwRoot, name);
    if (!File.Exists(filePath)) { TryRespond(context, 404, "text/plain; charset=utf-8", "UI 파일이 없습니다: " + name); return; }
    TryRespond(context, 200, mime, await File.ReadAllTextAsync(filePath));
}

static void TryRespond(HttpListenerContext context, int status, string mime, string body)
{
    try
    {
        var response = context.Response;
        response.StatusCode = status;
        response.ContentType = mime;
        var bytes = Encoding.UTF8.GetBytes(body);
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
        response.OutputStream.Close();
    }
    catch { /* 클라이언트가 끊긴 경우 무시한다. */ }
}

static string FindRepoRoot()
{
    foreach (var baseDirectory in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
    {
        var current = new DirectoryInfo(baseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, "Assets", "RuneCode", "Resources", "RuneCode"))) return current.FullName;
            current = current.Parent;
        }
    }
    return null;
}

readonly record struct Table(string Id, string Group, string Title, string File, string RootPath, string View);
