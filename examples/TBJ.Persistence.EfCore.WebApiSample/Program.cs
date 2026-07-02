using Microsoft.EntityFrameworkCore;
using TBJ.Persistence.EfCore.Abstractions;
using TBJ.Persistence.EfCore.Extensions;
using TBJ.Persistence.EfCore.WebApiSample.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------
// Swagger / OpenAPI
// -----------------------------------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "TBJ.Persistence.EfCore — WebAPI Sample",
        Version = "v1",
        Description = """
            Przykładowe WebAPI demonstrujące bibliotekę TBJ.Persistence.EfCore.

            Kluczowe cechy:
            - Multi-tenancy: każde żądanie obsługiwane jest przez osobną bazę danych (SQLite)
            - Identyfikator tenanta przekazywany przez nagłówek X-Tenant-Id
            - DbContext bez DbSet<T> — encje odkrywane automatycznie przez IEntityTypeConfiguration<T>
            - Generyczne repozytoria — Repository<T>() dla każdej encji
            - Operacje masowe: InsertRangeAsync, UpdateRangeAsync, DeleteRangeAsync

            Dostępni tenanci (predefiniowani):
            - tenant-a — baza danych: tenant-a.db
            - tenant-b — baza danych: tenant-b.db
            - tenant-demo — baza danych: :memory: (in-memory, reset po restarcie)
            """
    });

    // Dodaj nagłówek X-Tenant-Id do każdej operacji w Swagger UI
    c.AddSecurityDefinition("TenantId", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "X-Tenant-Id",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        Description = "Identyfikator tenanta. Dostępne wartości: tenant-a, tenant-b, tenant-demo"
    });
    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "TenantId" }
            },
            Array.Empty<string>()
        }
    });

    // Włącz komentarze XML dla Swagger UI
    var xmlFile = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
        c.IncludeXmlComments(xmlPath);
});

// -----------------------------------------------------------------------
// Infrastruktura multi-tenancy
// -----------------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<ITenantStore, InMemoryTenantStore>();
builder.Services.AddScoped<IConnectionStringProvider<AppDbContext>, TenantConnectionStringProvider>();

// -----------------------------------------------------------------------
// Persystencja — rejestracja z delegatem resolwującym connection string per-tenant
// -----------------------------------------------------------------------
builder.Services.AddPersistence<AppDbContext, AppUnitOfWork>((serviceProvider, opt) =>
{
    var provider = serviceProvider.GetRequiredService<IConnectionStringProvider<AppDbContext>>();
    var connectionString = provider.GetConnectionString();
    opt.UseSqlite(connectionString);
});

// -----------------------------------------------------------------------
// Aplikacja
// -----------------------------------------------------------------------
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TBJ.Persistence.EfCore v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseHttpsRedirection();
app.MapControllers();

app.Run();
