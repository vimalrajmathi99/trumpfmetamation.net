using api.Data;
using api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Tiny URL API", Version = "v1" });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowLocalhost",
        policy =>
        {
            policy.WithOrigins("http://localhost:4200", "http://localhost:4201")
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        });
});

var app = builder.Build();

app.UseCors("AllowLocalhost");

// Ensure Database is created.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Generate short code utility
string GenerateShortCode()
{
    var chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    var random = new Random();
    return new string(Enumerable.Repeat(chars, 6).Select(s => s[random.Next(s.Length)]).ToArray());
}

var tag = "tiny-url";

app.MapPost("/api/add", async (HttpRequest req, [FromBody] TinyUrlAddDto dto, AppDbContext db) =>
{
    var code = GenerateShortCode();
    while (await db.TinyUrls.AnyAsync(t => t.Code == code))
    {
        code = GenerateShortCode();
    }

    var baseUrl = $"{req.Scheme}://{req.Host}/";

    var tinyUrl = new TinyUrl
    {
        Code = code,
        OriginalURL = dto.OriginalURL,
        IsPrivate = dto.IsPrivate,
        ShortURL = $"{baseUrl}{code}",
        TotalClicks = 0
    };

    db.TinyUrls.Add(tinyUrl);
    await db.SaveChangesAsync();

    return Results.Ok(new { code = code, shortURL = tinyUrl.ShortURL });
}).WithTags(tag);

app.MapDelete("/api/delete/{code}", async (string code, AppDbContext db) =>
{
    var url = await db.TinyUrls.FirstOrDefaultAsync(u => u.Code == code);
    if (url is null) return Results.Ok();
    db.TinyUrls.Remove(url);
    await db.SaveChangesAsync();
    return Results.Ok();
}).WithTags(tag);

app.MapDelete("/api/delete-all", async ([FromQuery] string? secretToken, AppDbContext db, IConfiguration config) =>
{
    var configuredToken = config["ApiSettings:SecretToken"];
    if (string.IsNullOrEmpty(secretToken) || secretToken != configuredToken)
    {
        // We return OK or similar based on swagger, but it's safer to return unauthorized or badrequest if token is wrong
        // I will return Unauthorized to prevent unauthorized deletes
        return Results.Unauthorized();
    }

    await db.TinyUrls.ExecuteDeleteAsync();
    return Results.Ok();
}).WithTags(tag);

app.MapPut("/api/update/{code}", async (string code, AppDbContext db) =>
{
    var url = await db.TinyUrls.FirstOrDefaultAsync(u => u.Code == code);
    if (url is not null)
    {
        // Toggle IsPrivate as a simple update action
        url.IsPrivate = !url.IsPrivate;
        await db.SaveChangesAsync();
    }
    return Results.Ok();
}).WithTags(tag);

app.MapGet("/{code}", async (string code, AppDbContext db) =>
{
    var url = await db.TinyUrls.FirstOrDefaultAsync(u => u.Code == code);
    if (url is null) return Results.NotFound();

    url.TotalClicks++;
    await db.SaveChangesAsync();

    return Results.Redirect(url.OriginalURL);
}).WithTags(tag);

app.MapGet("/api/public", async (AppDbContext db) =>
{
    var publicUrls = await db.TinyUrls
        .Where(u => !u.IsPrivate)
        .Select(u => new
        {
            code = u.Code,
            shortURL = u.ShortURL,
            originalURL = u.OriginalURL,
            totalClicks = u.TotalClicks,
            isPrivate = u.IsPrivate
        })
        .ToListAsync();

    return Results.Ok(publicUrls);
}).WithTags(tag);

app.Run();
