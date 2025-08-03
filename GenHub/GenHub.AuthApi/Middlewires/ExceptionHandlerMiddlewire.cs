using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace GenHub.AuthApi.Middlewires
{
    public class ExceptionHandlerMiddlewire
    {
        private readonly RequestDelegate _next;

        public ExceptionHandlerMiddlewire(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                var id = Guid.NewGuid();
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                context.Response.ContentType = "application/json";
                var error = new
                {
                    Id = id,
                    Message = ex.Message
                };
                await context.Response.WriteAsJsonAsync(error);
            }
        }
    }
    public static class ExceptionHandlerMiddlewireExtensions
    {
        public static IApplicationBuilder UseExceptionHandlerMiddlewire(this IApplicationBuilder app)
        {
            return app.UseMiddleware<ExceptionHandlerMiddlewire>();
        }
    }
}