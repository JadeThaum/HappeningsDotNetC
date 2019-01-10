﻿using HappeningsDotNetC.Dtos.EntityDtos;
using HappeningsDotNetC.Models;
using HappeningsDotNetC.Infrastructure;
using HappeningsDotNetC.Interfaces.ServiceInterfaces;
using HappeningsDotNetC.Dtos.IntermediaryDtos;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.Tasks;
using HappeningsDotNetC.Helpers;

namespace HappeningsDotNetC.Services
{
    public class LoginService : ILoginService
    {
        private readonly HappeningsContext happeningsContext;
        private readonly IHttpContextAccessor httpAccess;

        // doesn't extend ApiService (and can't contain UserService) since it's a member of all other ApiService
        public LoginService(HappeningsContext hc, IHttpContextAccessor httpAccessor)
        {
            happeningsContext = hc;
            httpAccess = httpAccessor;
        }

        // credit https://medium.com/@mehanix/lets-talk-security-salted-password-hashing-in-c-5460be5c3aae

        public async Task<bool> Login(LoginDto dto)
        {
            User currentUser = FindUser(dto.UserName);

            byte[] salt = new byte[16];
            byte[] oldHash = new byte[20];
            byte[] hashBytes = Convert.FromBase64String(currentUser.Hash);
            Array.Copy(hashBytes, 0, salt, 0, 16);
            Array.Copy(hashBytes, 16, oldHash, 0, 20);

            var pbkdf2 = new Rfc2898DeriveBytes(dto.Password, salt, 10000);
            var newHash = pbkdf2.GetBytes(20);


            if (oldHash.SequenceEqual(newHash))
            {
                var claims = new List<Claim>()
                {
                    new Claim(ClaimTypes.Name, currentUser.Name),
                    new Claim("FriendlyName", currentUser.FriendlyName),
                    new Claim(ClaimTypes.Role, currentUser.Role.ToString()),
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties();

                await httpAccess.HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                                                          new ClaimsPrincipal(claimsIdentity),
                                                          authProperties);

                return true;
            }
            else
            {
                return false;
            }
        }

        public async void Logout()
        {
            await httpAccess.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        // we leave UserService to do actual Create
        public CreatedLoginDto RegisterOrUpdate(LoginDto dto)
        {
            byte[] salt;
            new RNGCryptoServiceProvider().GetBytes(salt = new byte[16]);

            var pbkdf2 = new Rfc2898DeriveBytes(dto.Password, salt, 10000);

            byte[] hash = pbkdf2.GetBytes(20);

            byte[] hashBytes = new byte[36];

            Array.Copy(salt, 0, hashBytes, 0, 16);
            Array.Copy(hash, 0, hashBytes, 16, 20);

            string savedPasswordHash = Convert.ToBase64String(hashBytes);

            return new CreatedLoginDto()
            {
                UserName = dto.UserName,
                Hash = savedPasswordHash
            };
        }

        public IEnumerable<CreatedLoginDto> RegisterOrUpdate(IEnumerable<LoginDto> dtos)
        {
            List<CreatedLoginDto> result = new List<CreatedLoginDto>(dtos.Count());

            foreach(var dto in dtos)
            {
                result.Add(RegisterOrUpdate(dto));
            }

            return result;
        }

        public Guid GetCurrentUserId()
        {
            return GetCurrentUser().Id;
        }

        public User GetCurrentUser()
        {
            ClaimsPrincipal current = httpAccess.HttpContext.User;

            if (current == null) return null;

            string userName = current.Claims.SingleOrDefault(x => x.Type == ClaimTypes.Name.ToString()).Value;

            User currentUser = FindUser(userName);

            return currentUser;
        }

        public User FindUser(string userName)
        {
            User currentUser = happeningsContext.Set<User>().SingleOrDefault(x => x.Name == userName);

            if (currentUser == null) throw new HandledException(new KeyNotFoundException("Specified username does not exist in the database"));

            return currentUser;
        }
    }
}