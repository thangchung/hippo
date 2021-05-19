﻿using Hippo.Models;
using Hippo.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace Hippo.Controllers
{
    public class AccountController : Controller
    {
        private readonly SignInManager<Account> signInManager;
        private readonly DataContext context;
        private readonly IConfiguration configuration;

        public AccountController(SignInManager<Account> signInManager, DataContext context, IConfiguration configuration)
        {
            this.signInManager = signInManager;
            this.context = context;
            this.configuration = configuration;
        }

        public IActionResult Register()
        {
            if (this.User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "App");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(AccountRegisterForm form)
        {
            if (ModelState.IsValid)
            {
                var account = new Account
                {
                    UserName = form.UserName,
                    Email = form.Email,
                };
                var isFirstAccount = !context.Accounts.Any();

                var result = await signInManager.UserManager.CreateAsync(account, form.Password);
                if (result.Succeeded)
                {
                    if (isFirstAccount)
                    {
                        // assign first user as Administrator
                        var roleResult = await signInManager.UserManager.AddToRoleAsync(account, "Administrator");
                        if (!roleResult.Succeeded)
                        {
                            ModelState.AddModelError("", "failed to assign role 'Administrator'");
                            foreach (IdentityError error in result.Errors)
                            {
                                ModelState.AddModelError("", error.Description);
                            }
                        }
                    }
                    return RedirectToAction("Login", "Account");
                }
                else
                {
                    ModelState.AddModelError("", "failed to create account");
                    foreach (IdentityError error in result.Errors)
                    {
                        ModelState.AddModelError("", error.Description);
                    }
                }
            }
            else
            {
                ModelState.AddModelError("", "failed to register");
            }
            return View();
        }

        public IActionResult Login()
        {
            if (this.User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "App");
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginForm form)
        {
            if (ModelState.IsValid)
            {
                var result = await signInManager.PasswordSignInAsync(form.UserName, form.Password, form.RememberMe, false);
                if (result.Succeeded)
                {
                    if (Request.Query.Keys.Contains("ReturnUrl"))
                    {
                        Redirect(Request.Query["ReturnUrl"].First());
                    }
                    else
                    {
                        return RedirectToAction("Index", "App");
                    }
                }
                else
                {
                    if (result.IsNotAllowed)
                    {
                        ModelState.AddModelError("", "cannot log in at this time; please contact the administrator");
                    }

                    if (result.IsLockedOut)
                    {
                        ModelState.AddModelError("", "account locked; please contact the administrator");
                    }
                }
            }
            else
            {
                ModelState.AddModelError("", "failed to login");
            }
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            await signInManager.SignOutAsync();
            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        public async Task<IActionResult> CreateToken([FromBody] ApiLoginForm form)
        {
            if (ModelState.IsValid)
            {
                var user = await signInManager.UserManager.FindByNameAsync(form.UserName);
                if (user != null)
                {
                    var result = await signInManager.CheckPasswordSignInAsync(user, form.Password, lockoutOnFailure: false);
                    if (result.Succeeded)
                    {
                        // create the token here
                        // Claims-based identity is a common way for applications to acquire the identity information they need about users inside their organization, in other organizations,
                        // and on the Internet. It also provides a consistent approach for applications running on-premises or in the cloud.
                        // Claims-based identity abstracts the individual elements of identity and access control into two parts:
                        //
                        // 1. a notion of claims, and
                        // 2. the concept of an issuer or an authority
                        //
                        // to create a claim you need a time and a value!
                        var claims = new[]
                        {
                            // Sub - name of the subject - which is user email here.
                            new Claim(JwtRegisteredClaimNames.Sub, user.Email),
                            // jti - unique string that is representative of each token so using a guid
                            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                            // unque name - username of the user mapped to the identity inside the user object
                            // that is available on every controller and view
                            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName)
                        };

                        // key is the secret used to encrypt the token. some parts of the token aren't encrypted but other parts are.
                        // credentials, who it is tied to and exploration etc are encrypted.
                        // information about the claims, about the individual etc aren't encrypted.
                        // use a natural string for a string and encode it to bytes.
                        // read from configuration json - keep changing/or fetch from another source.
                        // the trick here is that the key needs to be accessible for the application
                        // also needs to be replaceable by the people setting up your system.
                        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["Jwt:Key"]));

                        // new credentials required. create it using the key you just created in combination with a
                        // security algorithm.
                        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

                        var token = new JwtSecurityToken(configuration["Jwt:Issuer"], // the creator of the token
                        configuration["Jwt:Audience"], // who can use the token
                        claims,
                        expires: DateTime.UtcNow.AddMinutes(30),
                        signingCredentials: credentials);

                        var results = new
                        {
                        token = new JwtSecurityTokenHandler().WriteToken(token),
                        expiration = token.ValidTo
                        };

                        // empty quotes to say no source for this resource, just give a new object
                        return Created("", results);
                    }
                }
            }
            return BadRequest();
        }
    }
}
