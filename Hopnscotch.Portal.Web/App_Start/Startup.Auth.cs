﻿using System;
using System.Data.Entity;
using System.Web.Mvc;
using Hopnscotch.Portal.Contracts;
using Hopnscotch.Portal.Data;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.Owin;
using Microsoft.Owin.Security.Cookies;
using Microsoft.Owin.Security.OAuth;
using Owin;
using Hopnscotch.Portal.Web.Providers;

namespace Hopnscotch.Portal.Web
{
    public partial class Startup
    {
        static Startup()
        {
            PublicClientId = "self";

            UserManagerFactory = () =>
            {
                var config = DependencyResolver.Current.GetService<IConfig>();
                
                var context = new IdentityDbContext(config.DbConnectionString);
                Database.SetInitializer(new IdentityDbInitializer());
                
                var userStore = new UserStore<IdentityUser>(context);
                userStore.DisposeContext = true;

                var userManager = new UserManager<IdentityUser>(userStore);
                userManager.UserValidator = new UserValidator<IdentityUser>(userManager)
                {
                    AllowOnlyAlphanumericUserNames = false
                };

                return userManager;
            };

            RoleManagerFactory = () =>
            {
                var config = DependencyResolver.Current.GetService<IConfig>();

                var context = new IdentityDbContext(config.DbConnectionString);
                Database.SetInitializer(new IdentityDbInitializer());

                var roleStore = new RoleStore<IdentityRole>(context);

                return new RoleManager<IdentityRole>(roleStore);
            };

            OAuthOptions = new OAuthAuthorizationServerOptions
            {
                TokenEndpointPath = new PathString("/Token"),
                Provider = new ApplicationOAuthProvider(PublicClientId, UserManagerFactory),
                AuthorizeEndpointPath = new PathString("/api/Account/ExternalLogin"),
                AccessTokenExpireTimeSpan = TimeSpan.FromDays(14),
                AllowInsecureHttp = true
            };
        }

        public static OAuthAuthorizationServerOptions OAuthOptions { get; private set; }

        public static Func<UserManager<IdentityUser>> UserManagerFactory { get; set; }
        
        public static Func<RoleManager<IdentityRole>> RoleManagerFactory { get; set; }

        public static string PublicClientId { get; private set; }

        // For more information on configuring authentication, please visit http://go.microsoft.com/fwlink/?LinkId=301864
        public void ConfigureAuth(IAppBuilder app)
        {
            // Enable the application to use a cookie to store information for the signed in user
            // and to use a cookie to temporarily store information about a user logging in with a third party login provider
            app.UseCookieAuthentication(new CookieAuthenticationOptions());
            app.UseExternalSignInCookie(DefaultAuthenticationTypes.ExternalCookie);

            // Enable the application to use bearer tokens to authenticate users
            app.UseOAuthBearerTokens(OAuthOptions);

            // Uncomment the following lines to enable logging in with third party login providers
            //app.UseMicrosoftAccountAuthentication(
            //    clientId: "",
            //    clientSecret: "");

            //app.UseTwitterAuthentication(
            //    consumerKey: "",
            //    consumerSecret: "");

            //app.UseFacebookAuthentication(
            //    appId: "",
            //    appSecret: "");

            app.UseGoogleAuthentication();
        }
    }
}
