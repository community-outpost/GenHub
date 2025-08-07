using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Auth;
using GenHub.Core.Models.AuthApi.DTOs;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GenHub.Infrastructure.API.Auth.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController:ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly ITokenRepository _tokenRepository;

        public AuthController(UserManager<IdentityUser> userManager, ITokenRepository tokenRepository)
        {
            _userManager = userManager;
            _tokenRepository = tokenRepository;
        }

        // api/Auth/Register
        [HttpPost]
        [Route("Register")]
        public async Task<IActionResult> Register(RegisterRequestDto newUser)
        {
            var identityUser = new IdentityUser()
            {
                UserName = newUser.Username,
                Email = newUser.Email,
            };

            var identityResult = await _userManager.CreateAsync(identityUser, newUser.Password);
            if (identityResult.Succeeded)
            {
                if (newUser.Roles?.Any() is true)
                {
                    string[] roles = { "Reader", "Writer" };
                    foreach (var role in newUser.Roles)
                    {
                        if (roles.Contains(role) is false)
                        {
                            return Ok("User was registered but roles couldnt be added.");
                        }
                    }

                    identityResult = await _userManager.AddToRolesAsync(identityUser, newUser.Roles);
                    if (identityResult.Succeeded)
                    {
                        return Ok("User was registered with the given roles. Now you can login.");
                    }
                    else
                    {
                        return Ok("User was registered but roles couldn't be added now u can login.");
                    }
                }
            }

            return BadRequest("Something went wrong");
        }

        // api/Auth/Login
        [HttpPost]
        [Route("Login")]
        public async Task<IActionResult> Login(LoginRequestDto loginRequest)
        {
            var identityUser = await _userManager.FindByEmailAsync(loginRequest.Email);
            if (identityUser is null)
            {
                return NotFound();
            }

            var checkPasswordResult = await _userManager.CheckPasswordAsync(identityUser, loginRequest.Password);
            if (checkPasswordResult)
            {
                var roles = await _userManager.GetRolesAsync(identityUser);
                var jwtSecurityToken = _tokenRepository.CreateToken(identityUser, roles.ToList());
                return Ok(new LoginResponseDto{ JwtSecurityToken = jwtSecurityToken });
            }

            return BadRequest("Email or Password is incorrect.");
        }
    }
}