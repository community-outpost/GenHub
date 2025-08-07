using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GenHub.Infrastructure.API.Auth.Data
{
    public class GenHubAuthDbContext : IdentityDbContext
    {
        public GenHubAuthDbContext(DbContextOptions<GenHubAuthDbContext> dbContextOptions)
        : base(dbContextOptions)
        {
        }
    }

    public class GenHubAuthDbContextFactory : IDesignTimeDbContextFactory<GenHubAuthDbContext>
    {
        public GenHubAuthDbContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<GenHubAuthDbContext>();
            optionsBuilder.UseSqlServer("Server=LAPTOP-627C5K4V\\SQLEXPRESS02;Database=GenHubAuthDb2;Trusted_Connection=True;TrustServerCertificate=True");

            return new GenHubAuthDbContext(optionsBuilder.Options);
        }
    }
}