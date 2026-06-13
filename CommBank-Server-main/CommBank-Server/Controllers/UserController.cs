using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CommBank.Services;
using CommBank.Models;

namespace CommBank.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly IUsersService _usersService;

    public UserController(IUsersService usersService) =>
        _usersService = usersService;

    // Enumerating every user is an administrative operation.
    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<List<User>> Get() =>
        await _usersService.GetAsync();

    [HttpGet("{id:length(24)}")]
    public async Task<ActionResult<User>> Get(string id)
    {
        var user = await _usersService.GetAsync(id);

        if (user is null)
        {
            return NotFound();
        }

        return user;
    }

    // Self-registration is open but cannot set roles or ids (privilege-escalation guard).
    // The raw password is bound here and hashed in UsersService.CreateAsync.
    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Post(RegisterInput input)
    {
        var newUser = new User
        {
            Name = input.Name,
            Email = input.Email,
            Password = input.Password,
            Roles = new List<string> { "Customer" }
        };

        await _usersService.CreateAsync(newUser);

        return CreatedAtAction(nameof(Get), new { id = newUser.Id }, newUser);
    }

    [HttpPut("{id:length(24)}")]
    public async Task<IActionResult> Update(string id, User updatedUser)
    {
        var user = await _usersService.GetAsync(id);

        if (user is null)
        {
            return NotFound();
        }

        updatedUser.Id = user.Id;

        // Password is [JsonIgnore]d on input and roles are not client-editable here:
        // preserve the stored values so an update can never wipe the hash or escalate privileges.
        updatedUser.Password = user.Password;
        updatedUser.Roles = user.Roles;

        await _usersService.UpdateAsync(id, updatedUser);

        return NoContent();
    }

    [Authorize(Roles = "Admin")]
    [HttpDelete("{id:length(24)}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _usersService.GetAsync(id);

        if (user is null)
        {
            return NotFound();
        }

        await _usersService.RemoveAsync(id);

        return NoContent();
    }
}
