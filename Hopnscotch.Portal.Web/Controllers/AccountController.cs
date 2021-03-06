﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using Breeze.ContextProvider.EF6;
using Hopnscotch.Portal.Data;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.Owin.Security;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.OAuth;
using Hopnscotch.Portal.Web.Models;
using Hopnscotch.Portal.Web.Providers;
using Hopnscotch.Portal.Web.Results;

namespace Hopnscotch.Portal.Web.Controllers
{
    [System.Web.Http.Authorize]
    [System.Web.Http.RoutePrefix("api/Account")]
    public class AccountController : ApiController
    {
        private const string LocalLoginProvider = "Local";
        private const string DefaultUserRole = "RegisteredUsers";

        private readonly EFContextProvider<AttendanceDbContext> contextProvider = new EFContextProvider<AttendanceDbContext>();

        public AccountController()
            : this(Startup.UserManagerFactory(), Startup.OAuthOptions.AccessTokenFormat)
        {
        }

        public AccountController(UserManager<IdentityUser> userManager,
            ISecureDataFormat<AuthenticationTicket> accessTokenFormat)
        {
            UserManager = userManager;
            AccessTokenFormat = accessTokenFormat;
        }

        public UserManager<IdentityUser> UserManager { get; private set; }
        public ISecureDataFormat<AuthenticationTicket> AccessTokenFormat { get; private set; }

        // GET api/Account/UserInfo
        [HostAuthentication(DefaultAuthenticationTypes.ExternalBearer)]
        [System.Web.Http.Route("UserInfo")]
        public async Task<UserInfoViewModel> GetUserInfo()
        {
            var externalLogin = ExternalLoginData.FromIdentity(User.Identity as ClaimsIdentity);

            var userInfo = new UserInfoViewModel
            {
                UserName = User.Identity.GetUserName(),
                HasRegistered = externalLogin == null,
                LoginProvider = externalLogin != null ? externalLogin.LoginProvider : null
            };

            var user = await UserManager.FindByIdAsync(User.Identity.GetUserId());
            if (user != null)
            {
                userInfo.UserRoles = string.Join(",", user.Roles.Select(r => r.Role.Name));
            }   

            return userInfo;
        }

        // POST api/Account/Logout
        [System.Web.Http.Route("Logout")]
        public IHttpActionResult Logout()
        {
            Authentication.SignOut(CookieAuthenticationDefaults.AuthenticationType);
            return Ok();
        }

        // GET api/Account/ManageInfo?returnUrl=%2F&generateState=true
        [System.Web.Http.Route("ManageInfo")]
        public async Task<ManageInfoViewModel> GetManageInfo(string returnUrl, bool generateState = false)
        {
            var user = await UserManager.FindByIdAsync(User.Identity.GetUserId());
            if (user == null)
            {
                return null;
            }

            var logins = user.Logins.Select(linkedAccount => new UserLoginInfoViewModel
            {
                LoginProvider = linkedAccount.LoginProvider,
                ProviderKey = linkedAccount.ProviderKey
            })
            .ToList();

            if (user.PasswordHash != null)
            {
                logins.Add(new UserLoginInfoViewModel
                {
                    LoginProvider = LocalLoginProvider,
                    ProviderKey = user.UserName,
                });
            }

            return new ManageInfoViewModel
            {
                LocalLoginProvider = LocalLoginProvider,
                UserName = user.UserName,
                Logins = logins,
                ExternalLoginProviders = GetExternalLogins(returnUrl, generateState)
            };
        }

        // POST api/Account/ChangePassword
        [System.Web.Http.Route("ChangePassword")]
        public async Task<IHttpActionResult> ChangePassword(ChangePasswordBindingModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await UserManager.ChangePasswordAsync(User.Identity.GetUserId(), model.OldPassword, model.NewPassword);
            var errorResult = GetErrorResult(result);

            return errorResult ?? Ok();
        }

        // POST api/Account/SetPassword
        [System.Web.Http.Route("SetPassword")]
        public async Task<IHttpActionResult> SetPassword(SetPasswordBindingModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var result = await UserManager.AddPasswordAsync(User.Identity.GetUserId(), model.NewPassword);
            var errorResult = GetErrorResult(result);

            return errorResult ?? Ok();
        }

        // POST api/Account/AddExternalLogin
        [System.Web.Http.Route("AddExternalLogin")]
        public async Task<IHttpActionResult> AddExternalLogin(AddExternalLoginBindingModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            Authentication.SignOut(DefaultAuthenticationTypes.ExternalCookie);

            var ticket = AccessTokenFormat.Unprotect(model.ExternalAccessToken);
            if (ticket == null || ticket.Identity == null || (ticket.Properties != null && ticket.Properties.ExpiresUtc.HasValue && ticket.Properties.ExpiresUtc.Value < DateTimeOffset.UtcNow))
            {
                return BadRequest("External login failure.");
            }

            var externalData = ExternalLoginData.FromIdentity(ticket.Identity);
            if (externalData == null)
            {
                return BadRequest("The external login is already associated with an account.");
            }

            var result = await UserManager.AddLoginAsync(User.Identity.GetUserId(), new UserLoginInfo(externalData.LoginProvider, externalData.ProviderKey));
            var errorResult = GetErrorResult(result);

            return errorResult ?? Ok();
        }

        // POST api/Account/RemoveLogin
        [System.Web.Http.Route("RemoveLogin")]
        public async Task<IHttpActionResult> RemoveLogin(RemoveLoginBindingModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            IdentityResult result;
            if (model.LoginProvider == LocalLoginProvider)
            {
                result = await UserManager.RemovePasswordAsync(User.Identity.GetUserId());
            }
            else
            {
                result = await UserManager.RemoveLoginAsync(User.Identity.GetUserId(), new UserLoginInfo(model.LoginProvider, model.ProviderKey));
            }

            var errorResult = GetErrorResult(result);

            return errorResult ?? Ok();
        }

        // GET api/Account/ExternalLogin
        [System.Web.Http.OverrideAuthentication]
        [HostAuthentication(DefaultAuthenticationTypes.ExternalCookie)]
        [System.Web.Http.AllowAnonymous]
        [System.Web.Http.Route("ExternalLogin", Name = "ExternalLogin")]
        public async Task<IHttpActionResult> GetExternalLogin(string provider, string error = null)
        {
            if (error != null)
            {
                return Redirect(Url.Content("~/") + "#error=" + Uri.EscapeDataString(error));
            }

            if (!User.Identity.IsAuthenticated)
            {
                return new ChallengeResult(provider, this);
            }

            var externalLogin = ExternalLoginData.FromIdentity(User.Identity as ClaimsIdentity);
            if (externalLogin == null)
            {
                return InternalServerError();
            }

            if (externalLogin.LoginProvider != provider)
            {
                Authentication.SignOut(DefaultAuthenticationTypes.ExternalCookie);
                return new ChallengeResult(provider, this);
            }

            var user = await UserManager.FindAsync(new UserLoginInfo(externalLogin.LoginProvider, externalLogin.ProviderKey));
            var hasRegistered = user != null;
            if (hasRegistered)
            {
                Authentication.SignOut(DefaultAuthenticationTypes.ExternalCookie);
                var oAuthIdentity = await UserManager.CreateIdentityAsync(user, OAuthDefaults.AuthenticationType);
                var cookieIdentity = await UserManager.CreateIdentityAsync(user, CookieAuthenticationDefaults.AuthenticationType);
                var properties = ApplicationOAuthProvider.CreateProperties(user);
                Authentication.SignIn(properties, oAuthIdentity, cookieIdentity);
            }
            else
            {
                var claims = externalLogin.GetClaims();
                var identity = new ClaimsIdentity(claims, OAuthDefaults.AuthenticationType);
                Authentication.SignIn(identity);
            }

            return Ok();
        }

        // GET api/Account/ExternalLogins?returnUrl=%2F&generateState=true
        [System.Web.Http.AllowAnonymous]
        [System.Web.Http.Route("ExternalLogins")]
        public IEnumerable<ExternalLoginViewModel> GetExternalLogins(string returnUrl, bool generateState = false)
        {
            var descriptions = Authentication.GetExternalAuthenticationTypes();

            string state;
            if (generateState)
            {
                const int strengthInBits = 256;
                state = RandomOAuthStateGenerator.Generate(strengthInBits);
            }
            else
            {
                state = null;
            }

            return descriptions.Select(description => new ExternalLoginViewModel
            {
                Name = description.Caption,
                Url = Url.Route("ExternalLogin", new
                {
                    provider = description.AuthenticationType, response_type = "token", client_id = Startup.PublicClientId, redirect_uri = new Uri(Request.RequestUri, returnUrl).AbsoluteUri, state
                }),
                State = state
            }).ToList();
        }

        // GET api/Account/GetUser
        [System.Web.Http.Route("GetUser")]
        public AttendanceUserViewModel GetUser(string id)
        {
            int userId;
            if (!int.TryParse(id, out userId))
            {
                return null;
            }

            var user = contextProvider.Context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null)
            {
                return null;
            }

            var identityUser = UserManager.FindByName(user.Login);
            if (identityUser == null)
            {
                return new AttendanceUserViewModel
                {
                    IsRegistered = false,
                    Login = user.Login,
                    DisplayName = user.FirstName + " " + user.LastName
                };
            }

            return new AttendanceUserViewModel
            {
                IsRegistered = true,
                Id = identityUser.Id,
                Login = user.Login,
                DisplayName = user.FirstName + " " + user.LastName,
                UserRoles = identityUser.Roles.Select(r => r.Role.Name).ToArray()
            };
        }

        // POST api/Account/SaveUser
        [System.Web.Http.Route("SaveUser")]
        public IHttpActionResult SaveUser(AttendanceUserViewModel userModel)
        {
            var userId = userModel.Id;

            var user = UserManager.FindById(userId);
            var newRolesSet = new HashSet<string>(userModel.UserRoles);

            // remove from no more relevant roles
            var rolesToRemove = user.Roles.Select(r => r.Role.Name).Where(role => !newRolesSet.Contains(role)).ToArray();
            foreach (var role in rolesToRemove)
            {
                UserManager.RemoveFromRole(userId, role);
            }

            // add to new roles
            foreach (var role in newRolesSet.Where(role => !UserManager.IsInRole(userId, role)))
            {
                UserManager.AddToRole(userId, role);
            }

            return Ok();
        }

        // POST api/Account/Register
        [System.Web.Http.AllowAnonymous]
        [System.Web.Http.Route("Register")]
        public async Task<IHttpActionResult> Register(RegisterBindingModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var user = new IdentityUser
            {
                UserName = model.UserName
            };

            var result = await UserManager.CreateAsync(user, model.Password);
            var errorResult = GetErrorResult(result);
            if (errorResult != null)
            {
                return errorResult;
            }

            result = await UserManager.AddToRoleAsync(user.Id, DefaultUserRole);
            errorResult = GetErrorResult(result);
            
            return errorResult ?? Ok();
        }

        // POST api/Account/RegisterExternal
        [System.Web.Http.OverrideAuthentication]
        [HostAuthentication(DefaultAuthenticationTypes.ExternalBearer)]
        [System.Web.Http.Route("RegisterExternal")]
        public async Task<IHttpActionResult> RegisterExternal(RegisterExternalBindingModel model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var externalLogin = ExternalLoginData.FromIdentity(User.Identity as ClaimsIdentity);
            if (externalLogin == null)
            {
                return InternalServerError();
            }

            var user = new IdentityUser
            {
                UserName = model.UserName
            };
            user.Logins.Add(new IdentityUserLogin
            {
                LoginProvider = externalLogin.LoginProvider,
                ProviderKey = externalLogin.ProviderKey
            });

            var result = await UserManager.CreateAsync(user);
            var errorResult = GetErrorResult(result);

            if (errorResult != null)
            {
                return errorResult;
            }

            result = await UserManager.AddToRoleAsync(user.Id, DefaultUserRole);
            errorResult = GetErrorResult(result);

            return errorResult ?? Ok();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UserManager.Dispose();
            }

            base.Dispose(disposing);
        }

        #region Helpers

        private IAuthenticationManager Authentication
        {
            get { return Request.GetOwinContext().Authentication; }
        }

        private IHttpActionResult GetErrorResult(IdentityResult result)
        {
            if (result == null)
            {
                return InternalServerError();
            }

            if (result.Succeeded)
            {
                return null;
            }

            if (result.Errors != null)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError("", error);
                }
            }

            if (ModelState.IsValid)
            {
                // No ModelState errors are available to send, so just return an empty BadRequest.
                return BadRequest();
            }

            return BadRequest(ModelState);
        }

        private class ExternalLoginData
        {
            public string LoginProvider { get; private set; }
            public string ProviderKey { get; private set; }
            public string UserName { get; set; }

            public IList<Claim> GetClaims()
            {
                IList<Claim> claims = new List<Claim>();
                claims.Add(new Claim(ClaimTypes.NameIdentifier, ProviderKey, null, LoginProvider));

                if (UserName != null)
                {
                    claims.Add(new Claim(ClaimTypes.Name, UserName, null, LoginProvider));
                }

                return claims;
            }

            public static ExternalLoginData FromIdentity(ClaimsIdentity identity)
            {
                if (identity == null)
                {
                    return null;
                }

                var providerKeyClaim = identity.FindFirst(ClaimTypes.NameIdentifier);
                if (providerKeyClaim == null || String.IsNullOrEmpty(providerKeyClaim.Issuer) || String.IsNullOrEmpty(providerKeyClaim.Value))
                {
                    return null;
                }

                if (providerKeyClaim.Issuer == ClaimsIdentity.DefaultIssuer)
                {
                    return null;
                }

                return new ExternalLoginData
                {
                    LoginProvider = providerKeyClaim.Issuer,
                    ProviderKey = providerKeyClaim.Value,
                    UserName = identity.FindFirstValue(ClaimTypes.Name)
                };
            }
        }

        private static class RandomOAuthStateGenerator
        {
            private static readonly RandomNumberGenerator Random = new RNGCryptoServiceProvider();

            public static string Generate(int strengthInBits)
            {
                const int bitsPerByte = 8;
                if (strengthInBits % bitsPerByte != 0)
                {
                    throw new ArgumentException("strengthInBits must be evenly divisible by 8.", "strengthInBits");
                }

                var strengthInBytes = strengthInBits / bitsPerByte;
                var data = new byte[strengthInBytes];
                Random.GetBytes(data);

                return HttpServerUtility.UrlTokenEncode(data);
            }
        }

        #endregion
    }
}
