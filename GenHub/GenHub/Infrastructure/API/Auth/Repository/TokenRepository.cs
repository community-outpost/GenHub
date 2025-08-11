using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace GenHub.Infrastructure.API.Auth.Repository
{
    public class TokenRepository : ITokenRepository
    {
        public string CreateToken(IdentityUser user, List<string> roles)
        {
            var claims = new List<Claim>()
            {
                new(ClaimTypes.Email, user.Email),
            };
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("mysupersecretkey1234567890123456"));
            var signinCredentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var securityToken = new JwtSecurityToken(
                "https://localhost:7100/",
                "https://localhost:7100/",
                claims,
                expires: DateTime.Now.AddHours(1),
                signingCredentials: signinCredentials);

            return new JwtSecurityTokenHandler().WriteToken(securityToken);
        }
    }
}