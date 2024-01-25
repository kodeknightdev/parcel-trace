using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ParcelTrace.BuildingBlocks.Auth;
using ParcelTrace.Contracts;
using ParcelTrace.Identity.Auth;
using ParcelTrace.Identity.Data;
using ParcelTrace.Identity.Endpoints;

namespace ParcelTrace.Identity.Tests;

public class TokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2024, 1, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly TokenService _tokens = new(Options.Create(new JwtSettings
    {
        SigningKey = "unit-test-signing-key-0123456789abcdef",
        AccessTokenMinutes = 60
    }), new FakeTimeProvider(Now));

    [Fact]
    public void Courier_tokens_carry_role_and_courier_id()
    {
        var courierId = Guid.NewGuid();
        var user = new User { Email = "courier2@parceltrace.local", DisplayName = "Lena Fischer", Role = Roles.Courier, CourierId = courierId };

        var issued = _tokens.Issue(user);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(issued.AccessToken);

        jwt.Issuer.Should().Be("parcel-trace-identity");
        jwt.Audiences.Should().ContainSingle("parcel-trace");
        jwt.Subject.Should().Be(user.Id.ToString());
        jwt.Claims.Should().Contain(c => c.Type == ParcelClaims.Role && c.Value == Roles.Courier);
        jwt.Claims.Should().Contain(c => c.Type == ParcelClaims.CourierId && c.Value == courierId.ToString());
        issued.ExpiresAt.Should().Be(Now.AddMinutes(60));
    }

    [Fact]
    public void Customers_have_no_courier_claim()
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(_tokens.Issue(new User { Email = "a@b.c", DisplayName = "A", Role = Roles.Customer }).AccessToken);

        jwt.Claims.Should().NotContain(c => c.Type == ParcelClaims.CourierId);
    }

    [Fact]
    public void Passwords_are_hashed_and_verified()
    {
        var hasher = new BcryptPasswordHasher();
        var hash = hasher.Hash("Demo@12345");

        hash.Should().NotContain("Demo@12345");
        hasher.Verify("Demo@12345", hash).Should().BeTrue();
        hasher.Verify("parcel123!", hash).Should().BeFalse();
    }

    [Theory]
    [InlineData("short1", false)]
    [InlineData("longenoughbutnodigit", false)]
    [InlineData("12345678", false)]
    [InlineData("Demo@12345", true)]
    public void Register_validator_enforces_password_rules(string password, bool valid) =>
        new RegisterRequestValidator().Validate(new RegisterRequest("new@parceltrace.local", password, "New Person")).IsValid.Should().Be(valid);

    [Fact]
    public void Only_couriers_get_a_courier_id()
    {
        var validator = new CreateUserRequestValidator();

        validator.Validate(new CreateUserRequest("c@x.dev", "Demo@12345", "C", Roles.Courier, null)).IsValid.Should().BeFalse();
        validator.Validate(new CreateUserRequest("d@x.dev", "Demo@12345", "D", Roles.Dispatcher, Guid.NewGuid())).IsValid.Should().BeFalse();
        validator.Validate(new CreateUserRequest("c@x.dev", "Demo@12345", "C", Roles.Courier, Guid.NewGuid())).IsValid.Should().BeTrue();
        validator.Validate(new CreateUserRequest("x@x.dev", "Demo@12345", "X", "Pilot", null)).IsValid.Should().BeFalse();
    }
}
