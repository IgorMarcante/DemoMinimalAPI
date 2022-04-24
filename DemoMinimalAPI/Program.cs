using DemoMinimalAPI.Data;
using DemoMinimalAPI.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using MiniValidation;
using NetDevPack.Identity;
using NetDevPack.Identity.Jwt;
using NetDevPack.Identity.Model;

var builder = WebApplication.CreateBuilder(args);

#region Configure Services

builder.Services.AddDbContext<MinimalContextDb>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentityEntityFrameworkContextConfiguration(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"),
    b => b.MigrationsAssembly("DemoMinimalAPI")));

builder.Services.AddIdentityConfiguration();
builder.Services.AddJwtConfiguration(builder.Configuration, "AppSettings");

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DeleteProvider",
        policy => policy.RequireClaim("DeleteProvider"));
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Minimal API Sample",
        Description = "Developed by Igor Marcante - Owner",
        Contact = new OpenApiContact { Name = "Igor Marcante" },
        License = new OpenApiLicense { Name = "MIT", Url = new Uri("https://opensource.org/licenses/MIT") }
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Enter the JWT token like this: Bearer {your token}",
        Name = "Authorization",
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

var app = builder.Build();

#endregion

#region Configure Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthConfiguration();
app.UseHttpsRedirection();

MapActions(app);

app.Run();
#endregion

#region Actions
void MapActions(WebApplication app)
{
    app.MapPost("/register", [AllowAnonymous] async (
       SignInManager<IdentityUser> signInManager,
       UserManager<IdentityUser> userManager,
       IOptions<AppJwtSettings> appJwtSettings,
       RegisterUser registerUser) =>
    {
        if (registerUser == null)
            return Results.BadRequest("User not informed");

        if (!MiniValidator.TryValidate(registerUser, out var errors))
            return Results.ValidationProblem(errors);

        var user = new IdentityUser
        {
            UserName = registerUser.Email,
            Email = registerUser.Email,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, registerUser.Password);

        if (!result.Succeeded)
            return Results.BadRequest(result.Errors);

        var jwt = new JwtBuilder()
                    .WithUserManager(userManager)
                    .WithJwtSettings(appJwtSettings.Value)
                    .WithEmail(user.Email)
                    .WithJwtClaims()
                    .WithUserClaims()
                    .WithUserRoles()
                    .BuildUserResponse();

        return Results.Ok(jwt);

    }).ProducesValidationProblem()
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .WithName("RegisterUser")
    .WithTags("User");

    app.MapPost("/login", [AllowAnonymous] async (
        SignInManager<IdentityUser> signInManager,
        UserManager<IdentityUser> userManager,
        IOptions<AppJwtSettings> appJwtSettings,
        LoginUser loginUser) =>
    {
        if (loginUser == null)
            return Results.BadRequest("User not informed");

        if (!MiniValidator.TryValidate(loginUser, out var errors))
            return Results.ValidationProblem(errors);

        var result = await signInManager.PasswordSignInAsync(loginUser.Email, loginUser.Password, false, true);

        if (result.IsLockedOut)
            return Results.BadRequest("Blocked user");

        if (!result.Succeeded)
            return Results.BadRequest("Username or password is invalid");

        var jwt = new JwtBuilder()
                    .WithUserManager(userManager)
                    .WithJwtSettings(appJwtSettings.Value)
                    .WithEmail(loginUser.Email)
                    .WithJwtClaims()
                    .WithUserClaims()
                    .WithUserRoles()
                    .BuildUserResponse();

        return Results.Ok(jwt);

    }).ProducesValidationProblem()
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("LoginUser")
        .WithTags("User");

    app.MapGet("/provider", [AllowAnonymous] async (
        MinimalContextDb context) =>
        await context.Providers.ToListAsync())
        .WithName("GetProvider")
        .WithTags("Provider");

    app.MapGet("/provider/{id}", async (
        Guid id,
        MinimalContextDb context) =>
        await context.Providers.FindAsync(id)
            is Provider provider
                ? Results.Ok(provider)
                : Results.NotFound())
        .Produces<Provider>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .WithName("GetProviderById")
        .WithTags("Provider");

    app.MapPost("/provider", [Authorize] async (
        MinimalContextDb context,
        Provider provider) =>
    {
        if (!MiniValidator.TryValidate(provider, out var errors))
            return Results.ValidationProblem(errors);

        context.Providers.Add(provider);
        var result = await context.SaveChangesAsync();

        return result > 0
            ? Results.Created($"/provider/{provider.Id}", provider)
            : Results.BadRequest("There was a problem saving information");
    }).ProducesValidationProblem()
        .Produces<Provider>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("PostProvider")
        .WithTags("Provider");

    app.MapPut("/provider/{id}", [Authorize] async (
            Guid id,
            MinimalContextDb context,
            Provider provider) =>
    {
        var providerExist = await context.Providers.AsNoTracking<Provider>()
                                                   .FirstOrDefaultAsync(p => p.Id == id);
        if (providerExist == null) return Results.NotFound();

        if (!MiniValidator.TryValidate(provider, out var errors))
            return Results.ValidationProblem(errors);

        context.Providers.Update(provider);
        var result = await context.SaveChangesAsync();

        return result > 0
            ? Results.NoContent()
            : Results.BadRequest("There was a problem saving information");
    }).ProducesValidationProblem()
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .WithName("PutProvider")
        .WithTags("Provider");

    app.MapDelete("/provider/{id}", [Authorize] async (
        Guid id,
        MinimalContextDb context) =>
    {
        var provider = await context.Providers.FindAsync(id);
        if (provider == null) return Results.NotFound();

        context.Providers.Remove(provider);
        var result = await context.SaveChangesAsync();

        return result > 0
            ? Results.NoContent()
            : Results.BadRequest("There was a problem saving information");
    }).Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization("DeleteProvider")
        .WithName("DeleteProvider")
        .WithTags("Provider");
}
#endregion