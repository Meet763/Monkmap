using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkflowPlatform.Api.Data;
using WorkflowPlatform.Api.Dtos;
using WorkflowPlatform.Api.Entities;
using WorkflowPlatform.Api.Services;

namespace WorkflowPlatform.Api.Controllers;

[ApiController]
[Route("api")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwt;

    public AuthController(AppDbContext db, JwtTokenService jwt)
    {
        _db = db;
        _jwt = jwt;
    }

    [HttpPost("auth/register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "An account with that email already exists." });

        var first = req.FirstName.Trim();
        var last = req.LastName.Trim();
        var user = new User
        {
            Email = email,
            FirstName = first,
            LastName = last,
            DisplayName = $"{first} {last}".Trim(),
            PasswordHash = PasswordHasher.Hash(req.Password)
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return Ok(new AuthResponse(_jwt.CreateToken(user), ToDto(user)));
    }

    [HttpPost("auth/login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user is null || !PasswordHasher.Verify(req.Password, user.PasswordHash))
            return Unauthorized(new { message = "Invalid email or password." });

        return Ok(new AuthResponse(_jwt.CreateToken(user), ToDto(user)));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var user = await _db.Users.FindAsync(User.GetUserId());
        return user is null ? Unauthorized() : Ok(ToDto(user));
    }

    [Authorize]
    [HttpPut("me")]
    public async Task<ActionResult<UserDto>> UpdateProfile(UpdateProfileRequest req)
    {
        var user = await _db.Users.FindAsync(User.GetUserId());
        if (user is null) return Unauthorized();

        if (!string.IsNullOrWhiteSpace(req.FirstName)) user.FirstName = req.FirstName.Trim();
        if (!string.IsNullOrWhiteSpace(req.LastName)) user.LastName = req.LastName.Trim();
        user.DisplayName = $"{user.FirstName} {user.LastName}".Trim();
        user.AvatarUrl = req.AvatarUrl;
        await _db.SaveChangesAsync();
        return Ok(ToDto(user));
    }

    private static UserDto ToDto(User u) =>
        new(u.Id, u.Email, u.FirstName, u.LastName, u.DisplayName, u.AvatarUrl);
}
