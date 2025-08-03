using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GenHub.AuthApi.Data
{
    public class GenHubAuthDbContext:IdentityDbContext
    {
        public GenHubAuthDbContext(DbContextOptions<GenHubAuthDbContext> dbContextOptions): base(dbContextOptions)
        {
            
        }
    }
}