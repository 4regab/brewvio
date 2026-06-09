using System.Security.Claims;
using System.Text;
using Brewvio.Data;
using Brewvio.Dtos;
using Brewvio.Helpers;
using Brewvio.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Brewvio.Services;

public class AuthService(BrewvioDbContext db, IConfiguration config, AuditService audit)
{
    // Login outcome: a token on success, or a reason the sign-in was refused.
    public record LoginOutcome(LoginResponse? Response, string? Error);

    // Returns the token + user info on success, or an error message explaining why not
    // (bad credentials, awaiting approval, rejected, or deactivated).
    public async Task<LoginOutcome> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var user = await db.Users.SingleOrDefaultAsync(u => u.Username == username, ct);
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash))
            return new LoginOutcome(null, "Invalid username or password.");

        // Account-lifecycle gate (registration/approval workflow).
        if (user.Status == UserStatus.Pending)
            return new LoginOutcome(null, "Your account is awaiting manager approval.");
        if (user.Status == UserStatus.Rejected)
            return new LoginOutcome(null, "Your account request was declined. Please contact your manager.");
        if (!user.IsActive || user.Status != UserStatus.Active)
            return new LoginOutcome(null, "Your account is inactive. Please contact your manager.");

        // Transparent upgrade: if the stored hash used an older (weaker) parameter set, re-hash the
        // password with the current iteration count now that we have the plaintext and it verified.
        if (PasswordHasher.NeedsRehash(user.PasswordHash))
        {
            user.PasswordHash = PasswordHasher.Hash(password);
            await db.SaveChangesAsync(ct);
        }

        var issuedAt = DateTime.UtcNow;
        var token = IssueToken(user, issuedAt);
        // Record when the token was issued so future deactivations/resets can invalidate it.
        user.TokenIssuedAt = issuedAt;
        await db.SaveChangesAsync(ct);
        await audit.LogAsync("Login", $"{user.Username} ({user.Role}) signed in.", ct);
        return new LoginOutcome(new LoginResponse(token, user.Username, user.FullName, user.Role), null);
    }

    // Self-service sign-up: creates a Pending account that a Manager must approve.
    public async Task<RegisterResponse> RegisterAsync(RegisterRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            throw new ArgumentException("Username and password are required.");
        if (req.Password.Length < 8)
            throw new ArgumentException("Password must be at least 8 characters.");
        if (await db.Users.AnyAsync(u => u.Username == req.Username, ct))
            throw new InvalidOperationException("That username is already taken.");

        // Self-service sign-ups are always created as Cashier. Manager accounts must be created
        // by an existing Manager (POST /api/users) or promoted via PUT /api/users/{id} after
        // approval — a self-chosen "Manager" role here would let an unapproved stranger request
        // elevated access that an inattentive approver could grant in one click. The account
        // still stays Pending until a Manager approves it either way.
        var role = Roles.Cashier;
        var user = new User
        {
            Username = req.Username.Trim(),
            FullName = string.IsNullOrWhiteSpace(req.FullName) ? req.Username.Trim() : req.FullName.Trim(),
            Role = role,
            PasswordHash = PasswordHasher.Hash(req.Password),
            IsActive = false,
            Status = UserStatus.Pending
        };
        db.Users.Add(user);
        audit.Add("UserRegistered", $"{user.Username} requested a {role} account (pending approval).");
        await db.SaveChangesAsync(ct);
        return new RegisterResponse(user.Id, user.Username, user.Status);
    }

    // Lets the "Authenticating…" screen poll for an approval decision (no token, no PII).
    public async Task<AccountStatusResponse?> GetAccountStatusAsync(string username, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Username == username, ct);
        return user is null ? null : new AccountStatusResponse(user.Username, user.Status);
    }

    private string IssueToken(User user, DateTime issuedAt)
    {
        // Resolve the signing key the same way Program.cs resolves it for token *validation*:
        // JWT_KEY from configuration (env var locally / SSM Parameter Store on Lambda), falling
        // back to Jwt:Key for local dev. Issuance and validation MUST use the same key, so this
        // must stay in sync with Program.cs (do not read the env var directly — it's unset on Lambda).
        var key = config["JWT_KEY"] ?? config["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT signing key not configured.");
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = config["Jwt:Issuer"],
            Audience = config["Jwt:Audience"],
            IssuedAt = issuedAt,                    // explicit iat so revocation check is precise
            Expires = issuedAt.AddHours(2),         // 2h window; rotate SSM JWT_KEY to invalidate all sessions if needed
            SigningCredentials = creds,
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("sub", user.Id.ToString()),
                new Claim("name", user.Username),
                new Claim("fullname", user.FullName),
                new Claim("role", user.Role),
            })
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
