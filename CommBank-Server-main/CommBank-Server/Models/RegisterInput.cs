using System.ComponentModel.DataAnnotations;

namespace CommBank.Models;

/// <summary>
/// Registration payload. Deliberately separate from <see cref="User"/> so a client can never set its
/// own roles/ids (privilege-escalation guard) and so the password is accepted on input while remaining
/// non-serializable on output.
/// </summary>
public sealed class RegisterInput
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; set; } = "";

    [Required]
    [EmailAddress]
    public string Email { get; set; } = "";

    [Required]
    [StringLength(128, MinimumLength = 8)]
    public string Password { get; set; } = "";
}
