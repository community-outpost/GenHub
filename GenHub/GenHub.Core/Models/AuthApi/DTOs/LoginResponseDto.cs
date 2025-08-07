using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Core.Models.AuthApi.DTOs
{
    public class LoginResponseDto
    {
        public string JwtSecurityToken { get; set; }
    }
}