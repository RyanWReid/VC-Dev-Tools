using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

namespace VCDevTool.API.Tests.Controllers
{
    /// <summary>
    /// Fake policy evaluator that allows all requests for testing
    /// </summary>
    public class FakePolicyEvaluator : IPolicyEvaluator
    {
        public virtual async Task<AuthenticateResult> AuthenticateAsync(AuthorizationPolicy policy, HttpContext context)
        {
            var testScheme = "Test";
            var principal = new ClaimsPrincipal();
            
            // Add some basic claims for testing
            principal.AddIdentity(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.Name, "TestUser"),
                new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
                new Claim(ClaimTypes.Role, "Node")
            }, testScheme));

            return await Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, testScheme)));
        }

        public virtual async Task<PolicyAuthorizationResult> AuthorizeAsync(AuthorizationPolicy policy, AuthenticateResult authenticationResult, HttpContext context, object? resource)
        {
            return await Task.FromResult(PolicyAuthorizationResult.Success());
        }
    }
} 