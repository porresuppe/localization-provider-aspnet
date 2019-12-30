using System.Threading.Tasks;
using Microsoft.Owin;

namespace DbLocalizationProvider.MvcSample
{
    public class AuthenticationMiddleware : OwinMiddleware
    {
        private OwinMiddleware next;

        public AuthenticationMiddleware(OwinMiddleware next)
            : base(next)
        {
            this.next = next;
        }

        public override async Task Invoke(IOwinContext context)
        {
            if(context.Request.Path.Value == "/localization-admin/" && !UserAuthenticated(context))
            {
                context.Response.StatusCode = 303;
                context.Response.Headers.Add("location", new []{ context.Request.PathBase.Value });
                return;
            }
            await next.Invoke(context);
        }

        private bool UserAuthenticated(IOwinContext context)
        {
            // Implement authentication logic here
            return true;
        }
    }
}
