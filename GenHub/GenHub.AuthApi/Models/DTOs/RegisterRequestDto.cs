using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.AuthApi.Models.DTOs
{
    public class RegisterRequestDto
    {
        [Required]
        public string Username { get; set; }
        
        [Required]
        [DataType(DataType.EmailAddress)]
        public string Email { get; set; }
        
        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; }
        public string[] Roles { get; set; }
    }
}