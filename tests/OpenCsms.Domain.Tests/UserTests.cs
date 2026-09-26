namespace OpenCsms.Domain.Tests;

using OpenCsms.Domain;

[TestFixture]
public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2030, 6, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Create_ShouldNormalizeTheEmailAndKeepTheRole()
    {
        var user = User.Create("acme", "  Operator@Example.Test ", "Ada", "correct horse", UserRole.Operator, Now);

        Assert.Multiple(() =>
        {
            Assert.That(user.Email, Is.EqualTo("operator@example.test"));
            Assert.That(user.Role, Is.EqualTo(UserRole.Operator));
            Assert.That(user.CreatedAtUtc, Is.EqualTo(Now));
        });
    }

    [Test]
    public void Create_ShouldRejectAShortPassword()
    {
        Assert.Throws<ArgumentException>(
            () => User.Create("acme", "a@b.test", "Ada", "short", UserRole.Viewer, Now));
    }

    [Test]
    public void VerifyPassword_ShouldAcceptTheStoredSecretAndRejectAnythingElse()
    {
        var user = User.Create("acme", "a@b.test", "Ada", "correct horse", UserRole.Operator, Now);

        Assert.Multiple(() =>
        {
            Assert.That(user.VerifyPassword("correct horse"), Is.True);
            Assert.That(user.VerifyPassword("Correct horse"), Is.False);
            Assert.That(user.VerifyPassword(null), Is.False);
            Assert.That(user.VerifyPassword(""), Is.False);
        });
    }

    [Test]
    public void Create_ShouldSaltEveryHash()
    {
        var first = User.Create("acme", "a@b.test", "Ada", "correct horse", UserRole.Operator, Now);
        var second = User.Create("acme", "c@d.test", "Linus", "correct horse", UserRole.Operator, Now);

        Assert.That(second.PasswordHash, Is.Not.EqualTo(first.PasswordHash));
    }

    [Test]
    public void Roles_ShouldRoundTripTheWireSpellings()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UserRoles.From(UserRole.Operator), Is.EqualTo("operator"));
            Assert.That(UserRoles.Parse("VIEWER"), Is.EqualTo(UserRole.Viewer));
            Assert.That(UserRoles.IsKnown("admin"), Is.False);
            Assert.Throws<ArgumentException>(() => UserRoles.Parse("admin"));
        });
    }
}
