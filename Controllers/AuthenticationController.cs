﻿using System.Net;
using System.Security.Claims;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BeatLeader_Server.Controllers
{
    
    public class AuthenticationController : Controller
    {
        PlayerController _playerController;
        CurrentUserController _currentUserController;
        AppContext _context;

        public AuthenticationController(
            AppContext context, 
            PlayerController playerController, 
            CurrentUserController currentUserController)
        {
            _context = context;
            _playerController = playerController;
            _currentUserController = currentUserController;
        }

        [HttpGet("~/signin")]
        public async Task<IActionResult> SignIn() => View("SignIn", await HttpContext.GetExternalProvidersAsync());

        [HttpPost("~/signin")]
        public async Task<IActionResult> SignIn([FromForm] string provider, [FromForm] string returnUrl)
        {
            // Note: the "provider" parameter corresponds to the external
            // authentication provider choosen by the user agent.
            if (string.IsNullOrWhiteSpace(provider))
            {
                return BadRequest();
            }

            if (!await HttpContext.IsProviderSupportedAsync(provider))
            {
                return BadRequest();
            }

            string redirectUrl = returnUrl;
            
            if (provider == "Steam") {
                redirectUrl = Url.Action("SteamLoginCallback", new { ReturnUrl = returnUrl ?? "/" });
            } else if (provider == "Patreon") {
                redirectUrl = Url.Action("LinkPatreon", "Patreon", new { returnUrl = returnUrl ?? "/" });
            } else if (provider == "BeatSaver") {
                redirectUrl = Url.Action("LinkBeatSaver", "BeatSaver", new { returnUrl = returnUrl ?? "/" });
            } else if (provider == "Twitch") {
                redirectUrl = Url.Action("LinkTwitch", "Socials", new { returnUrl = returnUrl ?? "/" });
            }
            else if (provider == "Twitter")
            {
                redirectUrl = Url.Action("LinkTwitter", "Socials", new { returnUrl = returnUrl ?? "/" });
            }
            else if (provider == "Discord")
            {
                redirectUrl = Url.Action("LinkDiscord", "Socials", new { returnUrl = returnUrl ?? "/" });
            }
            else if (provider == "Google")
            {
                redirectUrl = Url.Action("LinkGoogle", "Socials", new { returnUrl = returnUrl ?? "/" });
            }

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            return Challenge(new AuthenticationProperties { RedirectUri =  redirectUrl }, provider);
        }

        [HttpPost("~/signin/approve")]
        public async Task<IActionResult> SignInApprove([FromForm] string returnUrl, [FromQuery] string leaderboardId)
        {
            string redirectUrl = returnUrl;

            redirectUrl = Url.Action("LinkBeatSaverAndApprove", "BeatSaver", new { returnUrl = returnUrl, leaderboardId = leaderboardId });

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            return Challenge(new AuthenticationProperties { RedirectUri = redirectUrl }, "BeatSaver");
        }

        [Authorize]
        [HttpPost("~/signinmigrate")]
        public async Task<IActionResult> SignInMigrate([FromForm] string provider, [FromForm] string returnUrl)
        {
            // Note: the "provider" parameter corresponds to the external
            // authentication provider choosen by the user agent.
            if (string.IsNullOrWhiteSpace(provider))
            {
                return BadRequest();
            }

            if (!await HttpContext.IsProviderSupportedAsync(provider))
            {
                return BadRequest();
            }
            string? iPAddress = Request.HttpContext.GetIpAddress();
            if (iPAddress == null) {
                return BadRequest("IP address should not be null");
            }
            int random = new Random().Next(200);
            var linkRequest = new AccountLinkRequest {
                IP = iPAddress,
                OculusID = HttpContext.CurrentUserID(),
                Random = random,
            };
            _context.AccountLinkRequests.Add(linkRequest);
            await _context.SaveChangesAsync();

            var redirectUrl = Url.Action("SteamLoginCallback", new { ReturnUrl = returnUrl, Random = random, migrateTo = HttpContext.CurrentUserID() });

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            return Challenge(new AuthenticationProperties { RedirectUri = redirectUrl }, provider);
        }

        [HttpGet("~/steamcallback")]
        public async Task<IActionResult> SteamLoginCallback([FromQuery] string ReturnUrl, [FromQuery] int Random = 0, [FromQuery] string? migrateTo = null)
        {
            string userId = HttpContext.CurrentUserID();
            if (userId != null)
            {
                await _playerController.GetLazy(userId);

                string? iPAddress = Request.HttpContext.GetIpAddress();
                if (iPAddress != null && migrateTo != null)
                {
                    string ip = iPAddress;
                    AccountLinkRequest? request = _context.AccountLinkRequests.FirstOrDefault(a => a.IP == ip && a.Random == Random && a.OculusID == migrateTo);

                    if (request != null)
                    {
                        _context.AccountLinkRequests.Remove(request);
                        await _context.SaveChangesAsync();
                        await _currentUserController.MigratePrivate(userId, request.OculusID.ToString());
                    }
                }
            }

            return Redirect(ReturnUrl);
        }

        [HttpPost("~/signinmigrate/oculuspc")]
        public async Task<IActionResult> SignInMigrateOculus(
            [FromForm] string provider, 
            [FromForm] string returnUrl,
            [FromForm] string token)
        {
            // Note: the "provider" parameter corresponds to the external
            // authentication provider choosen by the user agent.
            if (string.IsNullOrWhiteSpace(provider))
            {
                return BadRequest();
            }

            if (!await HttpContext.IsProviderSupportedAsync(provider))
            {
                return BadRequest();
            }

            var redirectUrl = Url.Action("MigrateOculusPC", "CurrentUser", new { ReturnUrl = returnUrl, Token = token });

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            return Challenge(new AuthenticationProperties { RedirectUri = redirectUrl }, provider);
        }

        [HttpPost("~/signinoculus")]
        public IActionResult SignInOculus()
        {

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            var result = Challenge(new AuthenticationProperties { RedirectUri = "/" }, "oculus");

            return result;
        }

        [HttpPost("~/signinoculus/oculuspc")]
        public IActionResult SignInOculusMigrateOculus(
            [FromForm] string token,
            [FromForm] string returnUrl)
        {

            // Instruct the middleware corresponding to the requested external identity
            // provider to redirect the user agent to its own authorization endpoint.
            // Note: the authenticationScheme parameter must match the value configured in Startup.cs
            var result = Challenge(new AuthenticationProperties { RedirectUri = Url.Action("MigrateOculusPC", "CurrentUser", new { ReturnUrl = returnUrl, Token = token }) }, "oculus");

            return result;
        }

        [HttpGet("~/signout"), HttpPost("~/signout")]
        public IActionResult SignOutCurrentUser()
        {
            // Instruct the cookies middleware to delete the local cookie created
            // when the user agent is redirected from the external identity provider
            // after a successful authentication flow (e.g Google or Facebook).
            return SignOut(new AuthenticationProperties { RedirectUri = "/" },
                CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}


