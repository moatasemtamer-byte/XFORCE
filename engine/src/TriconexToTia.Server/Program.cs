using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using TriconexToTia.Core.Results;
using TriconexToTia.Engine;

var builder = WebApplication.CreateBuilder(args);

var maxUploadBytes = ReadLong("MSW_MAX_UPLOAD_BYTES", 512L * 1024 * 1024);
var authToken = Environment.GetEnvironmentVariable("XFORCEMSW_ENGINE_TOKEN")
    ?? Environment.GetEnvironmentVariable("MSW_ENGINE_TOKEN");

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = maxUploadBytes;

    var port = Environment.GetEnvironmentVariable("PORT");
    if (int.TryParse(port, out var parsedPort))
    {
        kestrel.ListenAnyIP(parsedPort);
    }
    else if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
    {
        kestrel.ListenAnyIP(5080);
    }
});

builder.Services.Configure<FormOptions>(form =>
{
    form.MultipartBodyLengthLimit = maxUploadBytes;
    form.ValueLengthLimit = 1024 * 1024;
    form.MultipartHeadersLengthLimit = 64 * 1024;
});

var app = builder.Build();
var engine = new ConversionEngine();

app.MapGet("/", () => Results.Text(
    "TriconexToTia engine. Endpoints: GET /health, POST /convert (multipart/form-data).",
    "text/plain"));

app.MapGet("/health", () => Results.Json(new
{
    ready = true,
    engineVersion = ConversionEngine.Version,
    converter = "triconex-to-tia",
    maxUploadBytes,
}));

app.MapPost("/convert", async (HttpRequest request) =>
{
    if (!IsAuthorized(request, authToken))
    {
        return Results.Json(new { error = "unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
    }

    if (!request.HasFormContentType)
    {
        return Results.Json(new { error = "Expected multipart/form-data." }, statusCode: StatusCodes.Status400BadRequest);
    }

    IFormCollection form;
    try
    {
        form = await request.ReadFormAsync();
    }
    catch (Exception exception)
    {
        return Results.Json(new { error = $"Malformed upload: {exception.Message}" }, statusCode: StatusCodes.Status400BadRequest);
    }

    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
    if (file is null || file.Length == 0)
    {
        return Results.Json(new { error = "No source file was uploaded." }, statusCode: StatusCodes.Status400BadRequest);
    }

    var filename = string.IsNullOrWhiteSpace(file.FileName) ? "project.xml" : file.FileName;
    var options = ReadOptions(form["options"].ToString(), form["notes"].ToString());

    ConversionResult result;
    try
    {
        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        buffer.Position = 0;
        result = engine.Run(buffer, options, filename);
    }
    catch (Exception exception)
    {
        return Results.Json(new { error = $"Conversion crashed: {exception.Message}" }, statusCode: StatusCodes.Status500InternalServerError);
    }

    if (!result.Success)
    {
        var fatal = result.Validation.Fatal.Select(issue => issue.Message).ToArray();
        return Results.Json(
            new { error = result.FatalError ?? "Conversion failed.", fatal },
            statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    var packageBytes = BuildZip(result);
    var packageName = $"{Sanitize(result.Project.Name)}_tia_package.zip";

    var artifacts = new List<object>
    {
        new
        {
            name = packageName,
            mimeType = "application/zip",
            kind = "primary",
            contentBase64 = Convert.ToBase64String(packageBytes),
        },
    };

    var report = result.Report;
    if (!string.IsNullOrEmpty(report))
    {
        artifacts.Add(new
        {
            name = "conversion_report.txt",
            mimeType = "text/plain",
            kind = "report",
            contentBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(report)),
        });
    }

    var warnings = result.Validation.Warnings.Select(issue => issue.Message).ToArray();
    var summary = new Dictionary<string, object?>
    {
        ["blocks"] = result.Project.Blocks.Count,
        ["udts"] = result.Project.DataTypes.Count,
        ["tags"] = result.Project.IoPoints.Count + result.Project.GlobalVariables.Count,
        ["approximations"] = result.Mapping.Count(TriconexToTia.Core.Enums.ConversionStatus.Approximated),
        ["manualActions"] = result.Mapping.ManualActions.Count,
        ["unrecognized"] = result.Project.UnrecognizedElements.Count,
    };

    var response = new
    {
        artifacts,
        warnings,
        summary,
        engineVersion = ConversionEngine.Version,
    };

    return Results.Json(response, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    });
});

app.Run();

static bool IsAuthorized(HttpRequest request, string? token)
{
    if (string.IsNullOrWhiteSpace(token)) return true;

    var header = request.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;

    var presented = header["Bearer ".Length..].Trim();
    return string.Equals(presented, token, StringComparison.Ordinal);
}

static ConverterOptions ReadOptions(string? optionsJson, string? notes)
{
    var options = new ConverterOptions { TiaVersion = 20 };
    if (string.IsNullOrWhiteSpace(optionsJson)) return options;

    try
    {
        using var document = JsonDocument.Parse(optionsJson);
        var root = document.RootElement;

        if (root.TryGetProperty("tiaVersion", out var tiaVersion) && tiaVersion.TryGetInt32(out var parsed))
        {
            options.TiaVersion = parsed;
        }
        else if (root.TryGetProperty("tia_version", out var tiaVersionSnake) && tiaVersionSnake.TryGetInt32(out var parsedSnake))
        {
            options.TiaVersion = parsedSnake;
        }

        if (root.TryGetProperty("cpu", out var cpu) && cpu.ValueKind == JsonValueKind.String)
        {
            options.PlcCpu = cpu.GetString();
        }
        else if (root.TryGetProperty("plcCpu", out var plcCpu) && plcCpu.ValueKind == JsonValueKind.String)
        {
            options.PlcCpu = plcCpu.GetString();
        }
    }
    catch (JsonException)
    {
        // Ignore malformed options and use defaults.
    }

    _ = notes;
    return options;
}

static byte[] BuildZip(ConversionResult result)
{
    using var memory = new MemoryStream();
    using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var file in result.Files)
        {
            var entry = archive.CreateEntry(file.RelativePath, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            var bytes = file.ToBytes();
            entryStream.Write(bytes, 0, bytes.Length);
        }
    }
    return memory.ToArray();
}

static string Sanitize(string name)
{
    if (string.IsNullOrWhiteSpace(name)) return "Converted";
    var invalid = Path.GetInvalidFileNameChars();
    return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
}

static long ReadLong(string name, long fallback)
{
    var raw = Environment.GetEnvironmentVariable(name);
    return long.TryParse(raw, out var value) && value > 0 ? value : fallback;
}
