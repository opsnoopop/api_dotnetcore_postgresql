using System.Data;
using Dapper;
using Npgsql;
using Api.Models;

var builder = WebApplication.CreateBuilder(args);

// ===== ENV → connection string =====
string dbHost = Environment.GetEnvironmentVariable("DB_HOST") ?? "container_postgresql";
string dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
string dbUser = Environment.GetEnvironmentVariable("DB_USER") ?? "testuser";
string dbPass = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "testpass";
string dbName = Environment.GetEnvironmentVariable("DB_NAME") ?? "testdb";

// ปรับ pool/timeout ตามโหลดจริง
string connStr =
    $"Host={dbHost};Port={dbPort};Username={dbUser};Password={dbPass};Database={dbName};" +
    "Maximum Pool Size=200;Timeout=5;Command Timeout=30;";

// ===== Register a single, thread-safe DataSource =====
var dataSource = new NpgsqlDataSourceBuilder(connStr).Build();
builder.Services.AddSingleton(dataSource);

var app = builder.Build();

app.MapGet("/", () => Results.Json(new { message = "Hello World from .NET (PostgreSQL)" }));

// POST /users { username, email }
app.MapPost("/users", async (NpgsqlDataSource ds, User payload) =>
{
    if (string.IsNullOrWhiteSpace(payload.username) || string.IsNullOrWhiteSpace(payload.email))
        return Results.BadRequest(new { error = "username and email are required" });

    // ใช้ RETURNING ใน Postgres
    const string sql = @"INSERT INTO users (username, email) VALUES (@username, @email)
                         RETURNING user_id;";

    try
    {
        await using var db = await ds.OpenConnectionAsync(); // connection ใหม่ ต่อคำขอ
        var newId = await db.ExecuteScalarAsync<long>(sql, new { payload.username, payload.email });
        return Results.Created($"/users/{newId}", new { message = "User created successfully", user_id = newId });
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        return Results.Json(new { error = "Database error" }, statusCode: 500);
        // หรือ: return Results.Problem(title: "Database error", statusCode: 500);
    }
});

// GET /users/{user_id}
app.MapGet("/users/{user_id:long}", async (NpgsqlDataSource ds, long user_id) =>
{
    const string sql = "SELECT user_id, username, email FROM users WHERE user_id = @user_id LIMIT 1;";
    try
    {
        await using var db = await ds.OpenConnectionAsync(); // connection ใหม่ ต่อคำขอ
        var user = await db.QueryFirstOrDefaultAsync<User>(sql, new { user_id });
        if (user is null) return Results.NotFound(new { error = "User not found" });
        return Results.Ok(user);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex);
        return Results.Json(new { error = "Database error" }, statusCode: 500);
    }
});

app.Run();
